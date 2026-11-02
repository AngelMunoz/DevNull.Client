module DevNull.Client.UI.Tests.FeedTests

// The realized-container count: the feed renders its rows through a
// VirtualizingStackPanel, and this test measures how many containers
// that panel holds for a feed far taller than its viewport.

open System
open System.Reactive.Subjects
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive

open DevNull.Client.Components
open DevNull.Client.UI.Tests.Harness

/// One row of a measured height, so the realized count follows the
/// viewport rather than the font metrics.
let private row(index: int) : Control =
  let label = TextBlock()
  label.Text <- $"Row {index}"

  let cell = Border()
  cell.Height <- 20.0
  cell.Child <- label
  cell

/// The containers the feed's virtualizing panel holds.
let private realized(window: Window) : int =
  window.GetVisualDescendants()
  |> Seq.choose(function
    | :? VirtualizingStackPanel as panel -> Some panel.Children.Count
    | _ -> None)
  |> Seq.tryHead
  |> Option.defaultValue 0

let tests =
  testList "feed virtualization" [
    testCaseAsync
      "the feed realizes only the rows the viewport shows"
      (async {
        let items = new BehaviorSubject<int list>([ 0..199 ])

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
                OnNeedMore = ValueNone
              }

            let window = Window(Width = 480.0, Height = 600.0, Content = feed)

            window.Show()
            window)

        let! appeared =
          waitForAsync(
            5000,
            (fun () -> realized window),
            fun count -> count > 0
          )

        Expect.isTrue appeared "the feed never realized a row"

        // The dispatcher pass that realizes the first band has landed;
        // one more pass settles the template's follow-up layout.
        do! pumpAsync()

        let! count = probeUiAsync(fun () -> realized window)

        // VERIFY-18a measured 30 of 200 rows realized: the outer
        // ScrollViewer's viewport reaches the VirtualizingStackPanel,
        // so the feed realizes only the band near it.
        Expect.isLessThan count 100 "the feed realized too many of its 200 rows"
      })
  ]
