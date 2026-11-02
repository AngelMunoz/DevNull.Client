module DevNull.Client.Components.Palette

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client.Components.PageChrome

[<Struct>]
type RenderedRow = {
  Leading: Control voption
  Control: Control
  Followed: Control voption
}

type Props<'row, 'key> = {
  Rows: IObservable<'row list>
  KeyOf: 'row -> 'key
  Render: 'row -> RenderedRow
  Active: IObservable<int>
  OnHover: (int -> unit) voption
}

[<Struct>]
type Scroll = { Name: string; MaxHeight: float }

type Built = {
  Content: Control
  Targets: Control array
}

/// Rides the active row into view: the active index names one of the
/// row controls the palette built, and the row itself finds its
/// scroller. Every index counts, including zero.
let followRows
  (active: IObservable<int>, rows: IObservable<Control array>)
  : IDisposable =
  Observable.combineLatest active rows
  |> Observable.subscribe(fun (index, controls) ->
    controls
    |> Array.tryItem index
    |> Option.iter(fun row -> row.BringIntoView()))

let view(scroll: Scroll, props: Props<'row, 'key>) : Control =
  let build(rows: 'row list) : Built =
    let rendered = rows |> List.map props.Render

    let items = [|
      for row in rendered do
        match row.Leading with
        | ValueSome leading ->
          leading
          row.Control
        | ValueNone -> row.Control
    |]

    let targets = [|
      for row in rendered do
        match row.Followed with
        | ValueSome control -> control
        | ValueNone -> ()
    |]

    match props.OnHover with
    | ValueSome hover ->
      targets
      |> Array.iteri(fun index control ->
        control.PointerEntered.Add(fun _ -> hover index))
    | ValueNone -> ()

    {
      Content = ItemsControl().ItemsSource(items)
      Targets = targets
    }

  let built =
    props.Rows
    |> Observable.distinctUntilChangedKey(List.map props.KeyOf)
    |> Observable.map build
    |> share

  let follow = followRows(props.Active, built |> Observable.map _.Targets)

  let scroller =
    ScrollViewer()
      .Name(scroll.Name)
      .MaxHeight(scroll.MaxHeight)
      .Content(
        ContentControl()
          .Content(built |> Observable.map _.Content |> _.ToBinding())
      )

  scroller.DetachedFromVisualTree.Add(fun _ -> follow.Dispose())

  scroller
