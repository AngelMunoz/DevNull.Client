module DevNull.Client.Components.PageChrome

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.Theme

/// One line of page text: off, or the words it says.
[<Struct>]
type Line =
  | Off
  | Words of string

let share(source: IObservable<'T>) : IObservable<'T> =
  source |> Observable.replayBuffer 1 |> Observable.refCount

/// One line of text in the given foreground class, following an
/// observable source.
let messagePanel(text: IObservable<string>, fg: StyleClass) : Control =
  Status.panel(text, fg)

/// The page's error card: one plain line per failure, in the danger
/// tone, shown only while the page carries an error.
let errorPanel(error: IObservable<AppError option>) : Control =
  let words =
    error
    |> Observable.map(fun current -> current |> Option.map describe)
    |> Observable.distinctUntilChanged
    |> share

  Border()
    .Classes(Theme.name Theme.Card)
    .Margin(0.0, 8.0, 0.0, 8.0)
    .Child(
      messagePanel(
        words |> Observable.map(Option.defaultValue ""),
        Theme.FgDanger
      )
    )
    .IsVisible(words |> Observable.map Option.isSome)

/// The feed's load-more button, shown while the page holds a cursor.
let loadMore(visible: IObservable<bool>, onClick: unit -> unit) : Control =
  Button()
    .Content("Load more")
    .Classes(Theme.name Theme.GhostButton)
    .HorizontalAlignment(HorizontalAlignment.Center)
    .Margin(0.0, 8.0, 0.0, 16.0)
    .IsVisible(visible)
    .OnClickHandler(fun _ _ -> onClick())

/// One quiet line of page text, shown while its slice says Words.
let linePanel(line: IObservable<Line>) : Control =
  let words =
    line
    |> Observable.map(fun current ->
      match current with
      | Off -> ""
      | Words words -> words)

  messagePanel(words, Theme.Muted)
    .IsVisible(line |> Observable.map(fun current -> current <> Off))

/// Everything one list page renders: the pinned header built from the
/// page's own pieces, and the scrolling body built once. `Padding` is
/// the pinned area's inset, so a page whose rows run edge to edge keeps
/// its own gutter.
type PageProps<'m> = {
  Model: IObservable<'m>
  TitleOf: 'm -> string
  Padding: Thickness
  ErrorOf: 'm -> AppError option
  Busy: 'm -> Line
  Empty: 'm -> Line
  /// The page's own header pieces, after the title, before the error.
  Header: Control list
  /// The scrolling body.
  Body: Control
}

/// Builds one list page: the pinned header over the body.
let page(props: PageProps<'m>) : Control =
  let model = props.Model

  let titleText =
    model
    |> Observable.map props.TitleOf
    |> Observable.distinctUntilChanged
    |> share

  let title =
    TextBlock()
      .Classes(Theme.name Theme.Title)
      .Text(titleText)
      .IsVisible(titleText |> Observable.map(fun text -> text <> ""))

  let busy =
    model
    |> Observable.map props.Busy
    |> Observable.distinctUntilChanged
    |> share

  let empty =
    model
    |> Observable.map props.Empty
    |> Observable.distinctUntilChanged
    |> share

  let header: Control array = [| for piece in props.Header -> piece |]

  let pinnedChildren: Control array = [|
    title
    yield! header
    errorPanel(model |> Observable.map props.ErrorOf)
    linePanel busy
    linePanel empty
  |]

  let pinned =
    StackPanel()
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Margin(props.Padding)
      .Children(pinnedChildren)

  let root = Grid()

  root.RowDefinitions <- RowDefinitions("Auto,*")

  root.Children(pinned.Row(0), props.Body.Row(1))
