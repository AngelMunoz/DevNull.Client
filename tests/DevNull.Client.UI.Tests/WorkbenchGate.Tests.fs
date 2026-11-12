module DevNull.Client.UI.Tests.WorkbenchGateTests

open System
open System.Reactive.Subjects
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive

open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let private row(index: int) : Control =
  let label = TextBlock()
  label.Text <- $"Row {index}"

  let cell = Border()
  cell.Height <- 24.0
  cell.Child <- label
  cell

let private realized(window: Window) : int =
  window.GetVisualDescendants()
  |> Seq.choose(function
    | :? VirtualizingStackPanel as panel -> Some panel.Children.Count
    | _ -> None)
  |> Seq.tryHead
  |> Option.defaultValue 0

let private pageOf(json: string) =
  match LogPage.decode json with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned page never decoded: {failure.Message}"

let private commitOf(page: LogPage) =
  match page.Page.Items with
  | LiveCommit commit :: _ -> commit
  | _ -> failwith "the canned page carries no live commit"

let private rowProps(commit: Commit) : RowProps = {
  OnEvent = ignore
  OnOpenExternal = ignore
  ViewerId = "01a151db-7fee-727b-a7ce-c911a7556242"
  Pasteis = Observable.single false
  Now = Observable.single(DateTimeOffset.UtcNow)
  PollChoices = Observable.single Map.empty
  Dense = true
  Images = {
    new ImageCacheService with
      member _.Get(_url: string) = async { return None }
  }
  Flyout = ValueNone
  Pin = ValueNone
  Star = ValueNone
  Stars = ValueNone
  Vote = ValueNone
  Pastel = ValueNone
  Menu = ValueNone
  Translation = Observable.single(fun (_: Commit) -> ValueNone)
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
}

let tests =
  testList "workbench reuse gates" [
    testCaseAsync
      "G1 the feed rows fill a 1200-px host"
      (async {
        let items = new BehaviorSubject<int list>([ 0..49 ])

        let! window =
          probeUiAsync(fun () ->
            let feed =
              Feed.view {
                Items = items :> IObservable<int list>
                KeyOf = string
                Row = row
                Footer = ValueNone
                HasMore = Observable.single false
                ScrollTo = ValueNone
                Ride = false
                Restore = ValueNone
                OnScroll = ValueNone
                OnRide = ValueNone
                OnNeedMore = ValueNone
              }

            let window = Window(Width = 1200.0, Height = 600.0, Content = feed)

            window.Show()
            window)

        let! appeared =
          waitForAsync(
            5000,
            (fun () -> realized window),
            fun count -> count > 0
          )

        Expect.isTrue appeared "the feed never realized a row"

        do! pumpAsync()

        let! width =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.choose(function
              | :? VirtualizingStackPanel as panel -> Some panel
              | _ -> None)
            |> Seq.tryHead
            |> Option.map(fun panel -> panel.Bounds.Width)
            |> Option.defaultValue 0.0)

        Expect.isGreaterThan
          width
          1100.0
          "the feed rows do not fill the free-width host"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "G2 the commit card renders dense without the More menu or translation"
      (async {
        let commit = pageOf logPageJson |> commitOf

        let! window =
          probeUiAsync(fun () ->
            let card = feedRow (rowProps commit) (LiveCommit commit)

            let window = Window(Width = 900.0, Height = 600.0, Content = card)

            window.Show()
            window)

        do! settleAsync()

        let! body =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue body "the card never rendered its body"

        let! menu =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.exists(fun visual -> visual.Name = "CommitMenuButton"))

        Expect.isFalse menu "the card drew a More menu"

        let! translate =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.exists(fun visual -> visual.Name = "TranslateButton"))

        Expect.isFalse translate "the card drew a Translate action"

        let! dense =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.choose(function
              | :? Grid as grid when grid.ColumnDefinitions.Count = 2 ->
                Some grid
              | _ -> None)
            |> Seq.exists(fun grid ->
              grid.ColumnDefinitions[0].Width.Value = 48.0))

        Expect.isTrue dense "the card did not take the dense rail width"

        do! onUiAsync(fun () -> window.Close())
      })
  ]
