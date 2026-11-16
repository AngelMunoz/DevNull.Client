module DevNull.Client.UI.Tests.FeedTests

// The realized-container count: the feed renders its rows through a
// VirtualizingStackPanel, and this test measures how many containers
// that panel holds for a feed far taller than its viewport.

open System
open System.Reactive.Subjects
open Avalonia
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

let private itemHost(window: Window) : ItemsControl option =
  window.GetVisualDescendants()
  |> Seq.choose(function
    | :? ItemsControl as control -> Some control
    | _ -> None)
  |> Seq.tryHead

let private rowTop
  (window: Window, scroller: ScrollViewer, index: int)
  : float option =
  itemHost window
  |> Option.bind(fun host ->
    match host.ContainerFromIndex index with
    | null -> None
    | container ->
      let point = container.TranslatePoint(Point(0.0, 0.0), scroller)

      if point.HasValue then Some point.Value.Y else None)

let private variableRow(index: int) : Control =
  let label = TextBlock()
  label.Text <- $"Row {index}"

  let cell = Border()
  cell.Height <- 20.0 + float(index % 3) * 10.0
  cell.Child <- label
  cell

/// A forced full collection with finalizers drained between the passes.
let private collect() : unit =
  for _ in 1..2 do
    GC.Collect()
    GC.WaitForPendingFinalizers()

  GC.Collect()

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
    testCaseAsync
      "growing past the short-page band releases the plain rows"
      (async {
        let items = new BehaviorSubject<int list>([ 0; 1; 2 ])
        let later = ResizeArray<WeakReference>()

        let trackingRow(index: int) : Control =
          let control = row index
          later.Add(WeakReference(control))
          control

        let! window =
          probeUiAsync(fun () ->
            let feed =
              Feed.view {
                Header = ValueNone
                Items = items :> IObservable<int list>
                KeyOf = string
                Row = trackingRow
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
            fun root -> hasText(root, "Row 2")
          )

        Expect.isTrue appeared "the short page never realized its rows"

        let plainRows = later |> Seq.toList
        later.Clear()

        do! onUiAsync(fun () -> items.OnNext [ 0..39 ])

        let! switched =
          waitForAsync(
            5000,
            (fun () -> realized window),
            fun count -> count > 0
          )

        Expect.isTrue
          switched
          "the long page never switched to the virtual panel"

        do! pumpAsync()
        do! settleAsync()
        collect()
        do! settleAsync()

        let! alive =
          probeUiAsync(fun () ->
            plainRows
            |> List.filter(fun reference -> reference.IsAlive)
            |> List.length)

        Expect.equal alive 0 "the retired plain rows stayed alive"

        do! onUiAsync(fun () -> window.Close())
      })
    testCaseAsync
      "an anchor restore lands the row at the stored intra offset"
      (async {
        let items = new BehaviorSubject<int list>([ 0..59 ])
        let mutable scroller = Unchecked.defaultof<ScrollViewer>

        let! window =
          probeUiAsync(fun () ->
            let header = Border()
            header.Height <- 80.0

            let feed =
              Feed.viewWith(
                {
                  Header = ValueSome header
                  Items = items :> IObservable<int list>
                  KeyOf = string
                  Row = variableRow
                  Footer = ValueNone
                  HasMore = Observable.single false
                  ScrollTo = ValueNone
                  Ride = false
                  Restore = ValueNone
                  OnScroll = ValueNone
                  OnRide = ValueNone
                  OnNeedMore = ValueNone
                },
                {
                  Feed.Advanced.none() with
                      RestoreAnchor =
                        ValueSome {
                          Key = "10"
                          Offset = 5.0
                          Entry = ValueNone
                        }
                }
              )

            scroller <- feed
            let window = Window(Width = 480.0, Height = 600.0, Content = feed)

            window.Show()
            window)

        let! landed =
          waitForAsync(
            5000,
            (fun () -> rowTop(window, scroller, 10)),
            fun top -> top |> Option.exists(fun value -> abs(value + 5.0) < 1.0)
          )

        Expect.isTrue landed "the anchor row never reached its stored offset"

        do! onUiAsync(fun () -> window.Close())
      })
    testCaseAsync
      "an anchor restore waits for rows that arrive after the fetch"
      (async {
        // The returning page mounts empty: the init fetch is still in
        // flight, so the anchor row is not in the loaded rows yet.
        let items = new BehaviorSubject<int list>([])
        let mutable scroller = Unchecked.defaultof<ScrollViewer>

        let! window =
          probeUiAsync(fun () ->
            let header = Border()
            header.Height <- 80.0

            let feed =
              Feed.viewWith(
                {
                  Header = ValueSome header
                  Items = items :> IObservable<int list>
                  KeyOf = string
                  Row = variableRow
                  Footer = ValueNone
                  HasMore = Observable.single false
                  ScrollTo = ValueNone
                  Ride = false
                  Restore = ValueNone
                  OnScroll = ValueNone
                  OnRide = ValueNone
                  OnNeedMore = ValueNone
                },
                {
                  Feed.Advanced.none() with
                      RestoreAnchor =
                        ValueSome {
                          Key = "10"
                          Offset = 5.0
                          Entry = ValueNone
                        }
                }
              )

            scroller <- feed
            let window = Window(Width = 480.0, Height = 600.0, Content = feed)

            window.Show()
            window)

        // Dispatcher passes run while the fetch is in flight: the
        // restore gets its chances against the empty rows and must
        // still hold its place for the rows to come.
        let mutable passes = 0

        let! waited =
          waitForAsync(
            5000,
            (fun () ->
              passes <- passes + 1
              passes),
            fun count -> count >= 20
          )

        Expect.isTrue waited "the dispatcher passes never ran"

        do! onUiAsync(fun () -> items.OnNext [ 0..59 ])

        let! landed =
          waitForAsync(
            5000,
            (fun () -> rowTop(window, scroller, 10)),
            fun top -> top |> Option.exists(fun value -> abs(value + 5.0) < 1.0)
          )

        Expect.isTrue landed "the anchor row never reached its stored offset"

        do! onUiAsync(fun () -> window.Close())
      })
    testCaseAsync
      "a keyed prepend holds the reader on the same row"
      (async {
        let items = new BehaviorSubject<int list>([ 0..39 ])
        let mutable scroller = Unchecked.defaultof<ScrollViewer>
        let mutable anchor: DevNull.Client.FeedWindow.Anchor voption = ValueNone

        let! window =
          probeUiAsync(fun () ->
            let feed =
              Feed.viewWith(
                {
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
                },
                {
                  Feed.Advanced.none() with
                      OnAnchor =
                        ValueSome(fun place -> anchor <- ValueSome place)
                }
              )

            scroller <- feed
            let window = Window(Width = 480.0, Height = 600.0, Content = feed)

            window.Show()
            window)

        let! appeared =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "Row 0")
          )

        Expect.isTrue appeared "the feed never realized its first row"

        do! onUiAsync(fun () -> scroller.Offset <- Vector(0.0, 200.0))

        let! heldBefore =
          waitForAsync(
            5000,
            (fun () -> anchor),
            fun place ->
              place |> ValueOption.exists(fun current -> current.Key = "10")
          )

        Expect.isTrue heldBefore "the feed never parked the row at the top"

        do! onUiAsync(fun () -> items.OnNext([ -5 .. -1 ] @ [ 0..39 ]))

        let! heldAfter =
          waitForAsync(
            5000,
            (fun () -> anchor),
            fun place ->
              place
              |> ValueOption.exists(fun current ->
                current.Key = "10" && abs current.Offset < 1.0)
          )

        Expect.isTrue heldAfter "the prepended rows moved the reader"

        do! onUiAsync(fun () -> window.Close())
      })
    testCaseAsync
      "scrolling to the older edge asks for each page as it lands"
      (async {
        let items = new BehaviorSubject<int list>([ 0..59 ])
        let older = new BehaviorSubject<bool>(true)
        let mutable calls = 0
        let mutable scroller = Unchecked.defaultof<ScrollViewer>

        let! window =
          probeUiAsync(fun () ->
            let feed =
              Feed.viewWith(
                {
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
                },
                {
                  Feed.Advanced.none() with
                      Paging =
                        ValueSome {
                          Order = DevNull.Client.FeedWindow.NewestFirst
                          HasOlder = older
                          HasNewer = Observable.single false
                          OnOlder = (fun () -> calls <- calls + 1)
                          OnNewer = ignore
                        }
                }
              )

            scroller <- feed
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

        do!
          onUiAsync(fun () ->
            scroller.Offset <- Vector(0.0, scroller.Extent.Height))

        let! asked =
          waitForAsync(5000, (fun () -> calls), fun count -> count >= 1)

        Expect.isTrue asked "the near-end probe never asked for the older page"

        // The settle pump drains a duplicate probe when one exists; the
        // counter read runs on the UI thread, where the probe writes it.
        do! settleAsync()

        let! once = probeUiAsync(fun () -> calls)

        Expect.equal once 1 "the probe asked more than once for one page"

        // The page lands the way LogView lands it: the rows grow and
        // HasOlder stays true. No availability toggle reaches the feed.
        do! onUiAsync(fun () -> items.OnNext [ 0..79 ])
        do! settleAsync()

        do! onUiAsync(fun () -> scroller.Offset <- Vector(0.0, 0.0))

        do!
          onUiAsync(fun () ->
            scroller.Offset <- Vector(0.0, scroller.Extent.Height))

        let! askedAgain =
          waitForAsync(5000, (fun () -> calls), fun count -> count >= 2)

        Expect.isTrue askedAgain "the probe never asked after the page landed"

        do! onUiAsync(fun () -> items.OnNext [ 0..99 ])
        do! settleAsync()

        do! onUiAsync(fun () -> scroller.Offset <- Vector(0.0, 0.0))

        do!
          onUiAsync(fun () ->
            scroller.Offset <- Vector(0.0, scroller.Extent.Height))

        let! askedThird =
          waitForAsync(5000, (fun () -> calls), fun count -> count >= 3)

        Expect.isTrue askedThird "the probe never asked for the third page"

        do! onUiAsync(fun () -> window.Close())
      })
    testCaseAsync
      "an under-filled cursor feed asks for the next page without a scroll"
      (async {
        let items = new BehaviorSubject<int list>([ 0; 1; 2 ])
        let older = new BehaviorSubject<bool>(true)
        let mutable calls = 0

        let! window =
          probeUiAsync(fun () ->
            let feed =
              Feed.viewWith(
                {
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
                },
                {
                  Feed.Advanced.none() with
                      Paging =
                        ValueSome {
                          Order = DevNull.Client.FeedWindow.NewestFirst
                          HasOlder = older
                          HasNewer = Observable.single false
                          OnOlder = (fun () -> calls <- calls + 1)
                          OnNewer = ignore
                        }
                }
              )

            let window = Window(Width = 480.0, Height = 600.0, Content = feed)

            window.Show()
            window)

        let! asked =
          waitForAsync(5000, (fun () -> calls), fun count -> count >= 1)

        Expect.isTrue asked "the under-filled feed never asked for a page"

        do! onUiAsync(fun () -> window.Close())
      })
  ]
