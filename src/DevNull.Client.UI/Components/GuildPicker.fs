module DevNull.Client.Components.GuildPicker

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Components.Feed
open DevNull.Client.Components.GuildRow
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache
open DevNull.Client.Icons
open DevNull.Client.Theme

module Flyout = DevNull.Client.Components.SearchParts

[<Struct>]
type Props = {
  Rows: IObservable<Guild list>
  Query: IObservable<string>
  Images: ImageCacheService
  OnOpen: unit -> unit
  OnQuery: string -> unit
  OnPick: Guild -> unit
  OnClose: unit -> unit
}

[<Struct>]
type View = {
  Trigger: Control voption
  Layer: Control
}

module Parts =

  // The search box: every keystroke asks the host, whose reads
  // coalesce the burst.
  let search(props: Props) : Control =
    TextBox()
      .Name("GuildPickerSearch")
      .MaxLength(100)
      .PlaceholderText("Search guilds")
      .Background(Brushes.Transparent)
      .BorderThickness(Thickness(0.0))
      .VerticalAlignment(VerticalAlignment.Center)
      .FontSize(14.0)
      .Text(props.Query)
      .OnTextChangedHandler(fun box _ ->
        box.Text |> Option.ofObj |> Option.iter props.OnQuery)

  // The quiet line under an empty answer.
  let quiet() : Control =
    Flyout.statusLine "fatal: no guilds match"

  // The rows: the virtualized feed over the list the host feeds, each
  // row a guild row whose click picks it. The feed's HasMore subject
  // joins the picker's feeds, so the detach ends it with the rest.
  let rows
    (props: Props, feeds: System.Reactive.Disposables.CompositeDisposable)
    : Control =
    let empty =
      props.Rows
      |> Observable.map(fun current -> current.IsEmpty)
      |> Observable.distinctUntilChanged

    let standing = new System.Reactive.Subjects.BehaviorSubject<bool>(false)
    feeds.Add standing

    Feed.view {
      Header = ValueSome(quiet().IsVisible(empty))
      Items = props.Rows
      KeyOf = fun guild -> guild.Handle
      Row =
        fun guild ->
          GuildRow.view {
            Guild = guild
            Cache = props.Images
            OnOpen = props.OnPick
          }
      Footer = ValueNone
      HasMore = standing :> IObservable<bool>
      ScrollTo = ValueNone
      Ride = false
      Restore = ValueNone
      OnScroll = ValueNone
      OnRide = ValueNone
      OnNeedMore = ValueNone
    }

let view(props: Props) : View =
  let openLine = new System.Reactive.Subjects.BehaviorSubject<bool>(false)

  // Every feed the picker owns ends with its detach: the open signal,
  // the close path, and the rows' subjects, the way the reply picker's
  // feeds end with its own teardown.
  let feeds = new System.Reactive.Disposables.CompositeDisposable()

  // A pick ends the picker on the spot, the way a light dismiss does;
  // the host learns the choice after the card stands down.
  let standing = {
    props with
        OnPick =
          fun guild ->
            openLine.OnNext false
            props.OnPick guild
  }

  let trigger =
    Button()
      .Name("GuildPickerTrigger")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(0.0)
      .HorizontalAlignment(HorizontalAlignment.Left)
      .Content(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            Icons.iconAt(Glyph.Commit, 14.0),
            TextBlock().Text("Post to a guild")
          )
      )
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        openLine.OnNext true
        props.OnOpen())

  let card =
    Border()
      .Name("GuildPickerCard")
      .Classes(Theme.name Theme.Card)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .MaxWidth(420.0)
      .Child(
        StackPanel()
          .Margin(12.0)
          .Spacing(8.0)
          .Children(
            TextBlock().Classes(Theme.name Theme.Title).Text("Post to a guild"),
            Parts.search props,
            Border().MaxHeight(280.0).Child(Parts.rows(standing, feeds))
          )
      )

  let dialog =
    Popup()
      .Name("GuildPickerDialog")
      .PlacementTarget(trigger)
      .Placement(PlacementMode.Bottom)
      .IsLightDismissEnabled(true)
      .Child(card)

  // The card stands while the trigger's subject holds true; the light
  // dismiss ends it from outside clicks and escape, and every close
  // answers the host so its state follows.
  feeds.Add(
    openLine
    |> Observable.distinctUntilChanged
    |> Observable.subscribe(fun isOpen -> dialog.IsOpen <- isOpen)
  )

  feeds.Add(
    dialog.Closed
    |> Observable.subscribe(fun _ ->
      openLine.OnNext false
      props.OnClose())
  )

  dialog.DetachedFromVisualTree.Add(fun _ -> feeds.Dispose())

  {
    Trigger = ValueSome(trigger :> Control)
    Layer = dialog :> Control
  }
