module DevNull.Client.UI.Tests.EditorGates

open System
open Avalonia.Controls
open Avalonia.VisualTree
open NXUI.Extensions
open Expecto
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let private plainRow(index: int) : Control =
  Border().Height(18.0).Child(TextBlock().Text($"Row {index}"))

let private firstOfType<'T when 'T :> Control>(root: Control) =
  root.GetVisualDescendants()
  |> Seq.tryPick(function
    | :? 'T as control -> Some control
    | _ -> None)

let private noImages: ImageCacheService = {
  new ImageCacheService with
    member _.Get(_: string) = async { return None }
}

let tests =
  testList "editor gates" [
    testCaseAsync
      "gate EG1: the feed fills a 1200 px host"
      (async {
        let! window =
          probeUiAsync(fun () ->
            let feed =
              Feed.view {
                Items = Observable.single [ 0..60 ]
                KeyOf = string
                Row = plainRow
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

        let! realized =
          waitForAsync(
            5000,
            (fun () -> firstOfType<ItemsControl> window),
            (fun control ->
              control
              |> Option.map(fun found -> found.Bounds.Width > 0.0)
              |> Option.defaultValue false)
          )

        Expect.isTrue realized "the feed never laid out its rows"

        do! pumpAsync()

        let! width =
          probeUiAsync(fun () ->
            firstOfType<ItemsControl> window
            |> Option.map(fun control -> control.Bounds.Width)
            |> Option.defaultValue 0.0)

        Expect.isTrue
          (width >= 1100.0)
          $"the feed's rows stayed narrow: {width}"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "gate EG2: the dense row mounts with no menu and no translation"
      (async {
        let page =
          match LogPage.decode logPageJson with
          | Ok page -> page
          | Error failure ->
            failwith $"the canned page never decoded: {failure.Message}"

        let node = page.Page.Items[0]

        let props: RowProps = {
          OnEvent = ignore
          OnOpenExternal = ignore
          ViewerId = "viewer"
          Pasteis = Observable.single false
          Now = Observable.single DateTimeOffset.UtcNow
          PollChoices = Observable.single(Map.empty<string, string>)
          Dense = true
          Images = noImages
          Flyout = ValueNone
          Pin = ValueNone
          Star = ValueNone
          Stars = ValueNone
          Vote = ValueNone
          Pastel = ValueNone
          Menu = ValueNone
          Translation = Observable.single(fun _ -> ValueNone)
          Zone = {
            Info = TimeZoneInfo.Utc
            Label = "UTC"
          }
        }

        let! window =
          probeUiAsync(fun () ->
            let window =
              Window(
                Width = 900.0,
                Height = 600.0,
                Content = feedRow props node
              )

            window.Show()
            window)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "It was DNS."))
          )

        Expect.isTrue shown "the dense row never rendered its body"

        let! row, menu =
          probeUiAsync(fun () ->
            let row =
              window.GetVisualDescendants()
              |> Seq.tryPick(fun visual ->
                match visual with
                | :? Button as button when
                  button.Classes.Contains(Theme.name Theme.CommitRow)
                  ->
                  Some button
                | _ -> None)

            let menu =
              window.GetVisualDescendants()
              |> Seq.exists(fun visual -> visual.Name = "CommitMenuButton")

            row, menu)

        Expect.isSome row "the dense row's own control never stood"
        Expect.isFalse menu "the dense row drew a menu trigger"

        do! onUiAsync(fun () -> window.Close())
      })
  ]
