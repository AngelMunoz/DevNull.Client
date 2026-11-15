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
                Header = ValueNone
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

        // The outer ScrollViewer's viewport reaches the
        // VirtualizingStackPanel, so the feed realizes only the band
        // near it.
        Expect.isLessThan count 100 "the feed realized too many of its 200 rows"
      })
    testCaseAsync
      "a header renders above the first row and the footer below the last realized row"
      (async {
        let items = new BehaviorSubject<int list>([ 0..19 ])

        let! window =
          probeUiAsync(fun () ->
            let header = TextBlock()
            header.Text <- "Thread header"

            let footer = TextBlock()
            footer.Text <- "Load more"

            let feed =
              Feed.view {
                Header = ValueSome header
                Items = items :> IObservable<int list>
                KeyOf = string
                Row = row
                Footer = ValueSome footer
                HasMore = Observable.single false
                ScrollTo = ValueNone
                Ride = false
                Restore = ValueNone
                OnScroll = ValueNone
                OnRide = ValueNone
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

        // One more pass settles the template's follow-up layout.
        do! pumpAsync()

        let! order =
          probeUiAsync(fun () ->
            let texts =
              window.GetVisualDescendants()
              |> Seq.choose(function
                | :? TextBlock as block -> Some block.Text
                | _ -> None)
              |> List.ofSeq

            let headerAt = texts |> List.tryFindIndex((=) "Thread header")
            let firstAt = texts |> List.tryFindIndex((=) "Row 0")
            let footerAt = texts |> List.tryFindIndex((=) "Load more")

            headerAt, firstAt, footerAt)

        match order with
        | Some headerAt, Some firstAt, Some footerAt ->
          Expect.isLessThan
            headerAt
            firstAt
            "the header is not above the first row"

          Expect.isLessThan
            firstAt
            footerAt
            "the footer is not below the first row"
        | _ -> failwith "the header, first row, or footer never appeared"
      })
    testCaseAsync
      "replacing then removing a row drops the old content"
      (async {
        let items =
          new BehaviorSubject<(string * string) list>(
            [ "a", "Shipping"; "b", "Already" ]
          )

        let! window =
          probeUiAsync(fun () ->
            let header = Border()
            header.Height <- 550.0

            let feed =
              Feed.view {
                Header = ValueSome header
                Items = items :> IObservable<(string * string) list>
                KeyOf = fst
                Row =
                  fun (_, label) ->
                    row 0
                    |> fun control ->
                      match control with
                      | :? Border as border ->
                        match border.Child with
                        | :? TextBlock as block -> block.Text <- label
                        | _ -> ()
                      | _ -> ()

                      control
                Footer = ValueNone
                HasMore = Observable.single false
                ScrollTo = ValueNone
                Ride = false
                Restore = ValueNone
                OnScroll = ValueNone
                OnRide = ValueNone
                OnNeedMore = ValueNone
              }

            let window = Window(Width = 480.0, Height = 600.0, Content = feed)

            window.Show()
            window)

        let! appeared =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "Shipping")
          )

        Expect.isTrue appeared "the first row never appeared"

        do!
          onUiAsync(fun () -> items.OnNext [ "a", "Submitted"; "b", "Already" ])

        let! replaced =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "Submitted")
          )

        Expect.isTrue replaced "the row never updated"

        do! onUiAsync(fun () -> items.OnNext [ "b", "Already" ])
        do! settleAsync()
        do! pumpAsync()

        let! gone =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              not(hasText(root, "Submitted")) && not(hasText(root, "Shipping"))
          )

        Expect.isTrue gone "the removed row content stayed"
      })
    testCaseAsync
      "removing the first row under a tall header drops it"
      (async {
        let items = new BehaviorSubject<int list>([ 0; 1 ])

        let! window =
          probeUiAsync(fun () ->
            let header = Border()
            header.Height <- 550.0

            let feed =
              Feed.view {
                Header = ValueSome header
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

            let window = Window(Width = 480.0, Height = 600.0, Content = feed)

            window.Show()
            window)

        let! appeared =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "Row 0")
          )

        Expect.isTrue appeared "the first row never appeared"

        do! onUiAsync(fun () -> items.OnNext [ 1 ])
        do! settleAsync()

        let! gone =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(hasText(root, "Row 0"))
          )

        Expect.isTrue gone "the removed first row stayed"
      })
    testCaseAsync
      "removing the only realized row drops it"
      (async {
        let items = new BehaviorSubject<int list>([ 0; 1 ])

        let tallRow(index: int) : Control =
          let label = TextBlock()
          label.Text <- $"Tall {index}"
          let cell = Border()
          cell.Height <- 300.0
          cell.Child <- label
          cell

        let! window =
          probeUiAsync(fun () ->
            let header = Border()
            header.Height <- 200.0

            let feed =
              Feed.view {
                Header = ValueSome header
                Items = items :> IObservable<int list>
                KeyOf = string
                Row = tallRow
                Footer = ValueNone
                HasMore = Observable.single false
                ScrollTo = ValueNone
                Ride = false
                Restore = ValueNone
                OnScroll = ValueNone
                OnRide = ValueNone
                OnNeedMore = ValueNone
              }

            let window = Window(Width = 480.0, Height = 600.0, Content = feed)

            window.Show()
            window)

        let! appeared =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "Tall 0")
          )

        Expect.isTrue appeared "the first row never appeared"

        do! onUiAsync(fun () -> items.OnNext [ 1 ])
        do! settleAsync()
        do! pumpAsync()

        let! gone =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(hasText(root, "Tall 0"))
          )

        Expect.isTrue gone "the removed first row stayed"
      })
    testCaseAsync
      "an item removal drops its row"
      (async {
        let items = new BehaviorSubject<int list>([ 0; 1; 2 ])

        let! window =
          probeUiAsync(fun () ->
            let feed =
              Feed.view {
                Header = ValueNone
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

            let window = Window(Width = 480.0, Height = 600.0, Content = feed)

            window.Show()
            window)

        let! appeared =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "Row 0")
          )

        Expect.isTrue appeared "the first row never appeared"

        do! onUiAsync(fun () -> items.OnNext [ 1; 2 ])
        do! settleAsync()

        let! gone =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(hasText(root, "Row 0"))
          )

        Expect.isTrue gone "the removed row stayed"
      })
  ]
