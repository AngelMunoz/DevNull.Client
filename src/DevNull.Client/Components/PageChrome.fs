module DevNull.Client.Components.PageChrome

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.Theme

/// One line of text that follows an observable source.
let messagePanel
  (text: IObservable<string>, brush: IObservable<IBrush>)
  : Control =
  TextBlock()
    .TextWrapping(TextWrapping.Wrap)
    .TextAlignment(TextAlignment.Center)
    .Foreground(brush)
    .Margin(24.0)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .VerticalAlignment(VerticalAlignment.Center)
    .Text(text)

/// The page's error card: one plain line per failure, in the danger
/// tone, shown only while the page carries an error.
let errorPanel(error: IObservable<AppError option>) : Control =
  let text =
    error
    |> Observable.map(fun current ->
      current |> Option.map describe |> Option.defaultValue "")

  Border()
    .Classes(Theme.name Theme.Card)
    .Margin(0.0, 8.0, 0.0, 8.0)
    .Child(messagePanel(text, Brushes.danger))
    .IsVisible(error |> Observable.map Option.isSome)

/// The busy line of a page.
let busy(visible: IObservable<bool>, message: string) : Control =
  messagePanel(System.Reactive.Linq.Observable.Return message, Brushes.muted)
    .IsVisible(visible)

/// The empty line of a page.
let empty(visible: IObservable<bool>, message: string) : Control =
  messagePanel(System.Reactive.Linq.Observable.Return message, Brushes.muted)
    .IsVisible(visible)

/// The feed's load-more button, shown while the page holds a cursor.
let loadMore(visible: IObservable<bool>, onClick: unit -> unit) : Control =
  Button()
    .Content("Load more")
    .Classes(Theme.name Theme.GhostButton)
    .HorizontalAlignment(HorizontalAlignment.Center)
    .Margin(0.0, 8.0, 0.0, 16.0)
    .IsVisible(visible)
    .OnClickHandler(fun _ _ -> onClick())

/// Everything one list page renders: the pinned header built from the
/// page's own pieces, and the scrolling body built once. `Padding` is
/// the pinned area's inset, so a page whose rows run edge to edge keeps
/// its own gutter.
type PageProps<'m> = {
  Model: IObservable<'m>
  TitleOf: 'm -> string
  Padding: Thickness
  ErrorOf: 'm -> AppError option
  Busy: 'm -> bool * string
  Empty: 'm -> bool * string
  /// The page's own header pieces, after the title, before the error.
  Header: Control list
  /// The scrolling body.
  Body: Control
}

/// Builds one list page: the pinned header over the body.
let page(props: PageProps<'m>) : Control =
  let model = props.Model

  let titleText = model |> Observable.map props.TitleOf

  let title =
    TextBlock()
      .Classes(Theme.name Theme.Title)
      .Text(titleText)
      .IsVisible(titleText |> Observable.map(fun text -> text <> ""))

  let busyVisible =
    model |> Observable.map(fun current -> fst(props.Busy current))

  let busyText = model |> Observable.map(fun current -> snd(props.Busy current))

  let emptyVisible =
    model |> Observable.map(fun current -> fst(props.Empty current))

  let emptyText =
    model |> Observable.map(fun current -> snd(props.Empty current))

  let busyPanel = messagePanel(busyText, Brushes.muted).IsVisible(busyVisible)

  let emptyPanel =
    messagePanel(emptyText, Brushes.muted).IsVisible(emptyVisible)

  let header: Control array = [| for piece in props.Header -> piece |]

  let pinnedChildren: Control array = [|
    title
    yield! header
    errorPanel(model |> Observable.map props.ErrorOf)
    busyPanel
    emptyPanel
  |]

  let pinned =
    StackPanel()
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Margin(props.Padding)
      .Children(pinnedChildren)

  let root = Grid()

  root.RowDefinitions <- RowDefinitions("Auto,*")

  root.Children(pinned.Row(0), props.Body.Row(1))
