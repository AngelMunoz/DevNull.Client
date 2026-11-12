module DevNull.Client.UI.Tests.WorkbenchGateTests

open System
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Headless
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive

open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Workbench
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let private row(index: int) : Control =
  let label = TextBlock()
  label.Text <- $"Row {index}"

  let cell = Border()
  cell.Height <- 24.0
  cell.Child <- label
  cell

let private controlByName(root: Control, name: string) : Control option =
  root.GetVisualDescendants()
  |> Seq.choose(function
    | :? Control as control when control.Name = name -> Some control
    | _ -> None)
  |> Seq.tryHead

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

let private historyProps(env: TestEnv) : HistoryColumn.Props<TestEnv> = {
  Env = env
  Log = Following
  KeyFile = "/tmp/devnull-test-id_ed25519"
  ViewerId = "01a151db-7fee-727b-a7ce-c911a7556242"
  Pasteis = false
  Images = {
    new ImageCacheService with
      member _.Get(_url: string) = async { return None }
  }
  Now = Observable.single(DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero))
  Refresh = Observable.empty
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  OnOpenExternal = ignore
  OnOpened = ignore
  OnQuote = ignore
  Restore = None
  OnReport = ignore
}

let private inspectorProps(env: TestEnv) : Inspector.Props<TestEnv> = {
  Env = env
  CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
  KeyFile = "/tmp/devnull-test-id_ed25519"
  ViewerId = "01a151db-0000-0000-0000-000000000000"
  Pasteis = true
  Images = {
    new ImageCacheService with
      member _.Get(_url: string) = async { return None }
  }
  Now = Observable.single(DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero))
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  OnCheckoutParent = ignore
  OnOpenAuthor = ignore
  OnOpenExternal = ignore
  OnQuote = ignore
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

    testCaseAsync
      "G2 the commit card carries the workbench flyout callback"
      (async {
        let commit = pageOf logPageJson |> commitOf
        let opened = ResizeArray<string>()

        let source: UserFlyout.Source = {
          Fetch = fun _ -> async { return None }
          OnOpen = fun handle -> opened.Add handle
          OnOpenOrg = ignore
          OnOpenTag = ignore
          Cache = {
            new ImageCacheService with
              member _.Get(_url: string) = async { return None }
          }
        }

        let props = {
          rowProps commit with
              Flyout = ValueSome source
        }

        let! window =
          probeUiAsync(fun () ->
            let card = feedRow props (LiveCommit commit)

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

        do!
          onUiAsync(fun () ->
            controlByName(window, "AuthorAvatar")
            |> Option.iter(fun control ->
              match control with
              | :? Button as button ->
                let center =
                  Point(button.Bounds.Width / 2.0, button.Bounds.Height / 2.0)

                let moved = button.TranslatePoint(center, window)

                if moved.HasValue then
                  window.MouseMove moved.Value
              | _ -> ()))

        let! card =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "Reading the profile…")
          )

        Expect.isTrue card "the hover card never opened"

        let! exists =
          probeUiAsync(fun () ->
            controlByName(window, "UserFlyout") |> Option.isSome)

        Expect.isTrue exists "the flyout content did not exist"

        do!
          onUiAsync(fun () ->
            controlByName(window, "UserFlyout")
            |> Option.iter(function
              | :? ContentControl as host ->
                match host.Content with
                | :? Button as openCard -> click openCard
                | _ -> ()
              | _ -> ()))

        let! heard =
          waitForAsync(5000, (fun () -> opened.Count), fun count -> count > 0)

        Expect.isTrue heard "the flyout's open callback never ran"

        Expect.equal
          (opened |> Seq.toList)
          [ "octocat" ]
          "the open callback received the author handle"

        do! onUiAsync(fun () -> window.Close())
      })
  ]

let columnTests =
  testList "workbench history column view" [
    testCaseAsync
      "the history column renders its fetched page"
      (async {
        let page = pageOf logPageJson
        let now = DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)

        let subject =
          new BehaviorSubject<HistoryColumn.Model>(
            {
              Loading = false
              Error = None
              Items = page.Page.Items
              NextCursor = page.Page.NextCursor
              Head = Some "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
              NewCount = None
              Ranked = false
              Polling = false
              Now = now
              FetchedAt = Some(now.AddMinutes -12.0)
              Busy = None
              PollChoices = Map.empty
              Pasteis = false
              HeardIds = []
              Offset = 0.0
            }
          )

        let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
        let props = historyProps env

        let! window =
          probeUiAsync(fun () ->
            let view =
              HistoryColumn.view
                props
                (subject :> IObservable<HistoryColumn.Model>)
                (fun (_: HistoryColumn.Msg) -> ())

            let window = Window(Width = 1200.0, Height = 600.0, Content = view)

            window.Show()
            window)

        let! head =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "HEAD -> Following")
          )

        Expect.isTrue head "the HEAD line never rendered"

        let! fetched =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "fetched 12m")
          )

        Expect.isTrue fetched "the fetched line never rendered"

        let! rows =
          waitForAsync(
            5000,
            (fun () -> realized window),
            fun count -> count > 0
          )

        Expect.isTrue rows "the feed never realized a row"

        let! body =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue body "the fetched commit never rendered"

        do!
          onUiAsync(fun () ->
            window.Close()
            subject.OnCompleted()
            subject.Dispose())
      })
  ]

let inspectorViewTests =
  testList "workbench inspector view" [
    testCaseAsync
      "the inspector renders the checkout, parents, and replies"
      (async {
        let thread =
          match CommitPage.decode threadPageJson with
          | Ok page -> page
          | Error failure ->
            failwith $"the canned thread never decoded: {failure.Message}"

        let replies =
          match CommitNode.decodePage repliesPageJson with
          | Ok page -> page
          | Error failure ->
            failwith $"the canned replies never decoded: {failure.Message}"

        let parent = pageOf secondLogPageJson |> commitOf
        let now = DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)

        let subject =
          new BehaviorSubject<Inspector.Model>(
            {
              CommitId = thread.Commit.Id
              Loading = false
              Error = None
              Commit = Some thread.Commit
              Ancestors = [ LiveCommit parent ]
              ReplyParticipants = thread.ReplyParticipants
              Replies = replies.Items
              RepliesCursor = Some "next"
              RepliesLoading = false
              RepliesError = None
              Busy = None
              Now = now
            }
          )

        let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
        let props = inspectorProps env

        let! window =
          probeUiAsync(fun () ->
            let view =
              Inspector.view
                props
                (subject :> IObservable<Inspector.Model>)
                (fun (_: Inspector.Msg) -> ())

            let window = Window(Width = 380.0, Height = 800.0, Content = view)

            window.Show()
            window)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root ->
              hasText(root, "CHECKOUT b8b9c0d")
              && hasText(root, "no lane")
              && hasText(root, "It was DNS.")
              && hasText(root, "PARENTS")
              && hasText(root, "Second commit.")
              && hasText(root, "REPLIES")
              && hasText(root, "A reply.")
              && hasText(root, "Load more")
          )

        Expect.isTrue shown "the inspector never rendered its regions"

        let! actions =
          probeUiAsync(fun () ->
            (controlByName(window, "StarButton") |> Option.isSome,
             controlByName(window, "PastelButton") |> Option.isSome))

        Expect.isTrue (fst actions) "the star action never rendered"
        Expect.isTrue (snd actions) "the pastel action never rendered"

        do!
          onUiAsync(fun () ->
            window.Close()
            subject.OnCompleted()
            subject.Dispose())
      })
  ]
