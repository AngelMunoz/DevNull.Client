module DevNull.Client.Views.RemotesView

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.UiClock

/// The width below which the page gives up its two columns: one pane
/// at a time, the way the site reads on a phone.
let onePaneBelow = 720.0

/// The page's own composition: the inbox column and the thread column
/// side by side on a wide window, one pane at a time below the
/// breakpoint — the inbox until a conversation opens, the thread with
/// its back row once one has. One solo stage sounds every note the
/// page plays, and the program drives them all.
let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  // One note sounds at a time across every pane the program builds.
  let solo = Thread.Solo()

  let columns = Grid()

  // The strip's own width picks the shape, with whether a thread
  // stands checked out: both panes on a wide window, one below the
  // breakpoint.
  let shape =
    Observable.combineLatest
      (columns.GetObservable(Visual.BoundsProperty)
       |> Observable.map(fun bounds -> bounds.Width))
      (model |> Observable.map(fun current -> current.State.OpenTarget.IsSome))

  // The inbox takes the whole strip while it stands alone, its share
  // of a wide one (the site's 320, proportionally less, floored so the
  // rows stay readable), and nothing once a thread owns a narrow one.
  let inboxWidth =
    shape
    |> Observable.map(fun (w, isOpen) ->
      if w < onePaneBelow then
        (if isOpen then 0.0 else w)
      else
        min 320.0 (max 232.0 (w * 0.42)))

  // The inbox column's right hairline rides a border around it: the
  // column itself is a plain panel.
  let inboxColumn =
    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 1.0, 0.0))
      .Width(inboxWidth)
      .IsVisible(
        shape
        |> Observable.map(fun (w, isOpen) -> w >= onePaneBelow || not isOpen)
      )
      .Child(Parts.inbox(props, model, dispatch))

  // The one-pane thread's way back to the inbox.
  let back =
    Button()
      .Name("BackToInbox")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(6.0, 2.0, 6.0, 2.0)
      .HorizontalAlignment(HorizontalAlignment.Left)
      .Content(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(4.0)
          .Children(
            Icons.iconAt(Glyph.ChevronLeft, 14.0),
            TextBlock().FontSize(12.0).Text("inbox")
          )
      )
      .IsVisible(
        shape |> Observable.map(fun (w, isOpen) -> w < onePaneBelow && isOpen)
      )
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        dispatch State.Closed)

  let threadHost = Grid()

  threadHost.RowDefinitions <- RowDefinitions("Auto,*")

  threadHost.Children(
    back.Row(0).Margin(0.0, 0.0, 0.0, 4.0),
    Thread.column(props, model, dispatch, solo).Row(1)
  )
  |> ignore

  threadHost.IsVisible(
    shape |> Observable.map(fun (w, isOpen) -> w >= onePaneBelow || isOpen)
  )
  |> ignore

  columns.ColumnDefinitions <- ColumnDefinitions("Auto,*")

  columns.Children(
    inboxColumn.Column(0),
    threadHost.Column(1).Margin(16.0, 0.0, 16.0, 0.0)
  )
  |> ignore

  // The chat fills the strip the shell gives it; its top clears the
  // drawn caption band the way every page's pinned header does.
  Grid().Margin(Thickness(0.0, Theme.bandHeight, 0.0, 0.0)).Children(columns)

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideNatives
  and 'env :> IProvideRemotes
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root
    props.Context.AddDisposable lifetime
