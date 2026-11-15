module DevNull.Client.UI.Tests.Leaks

// The retention checks: a themed control releases with its tree, a feed
// row releases when its page ends, and a scroll soak stays inside a
// managed-memory budget. The first three fail while the app roots
// controls from app-lifetime observables; they prove both directions of
// the fix.

open System
open System.IO
open System.Reflection
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Media.Imaging
open Avalonia.VisualTree

open System.Reactive.Subjects
open Expecto
open FSharp.Control.Reactive

open DevNull.Client.Components
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Commit
open DevNull.Client.Core.Http
open DevNull.Client.Editor
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.Workbench
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

/// The api of the soak: every log read answers a many-card page.
let soakApi(cardCount: int) : IHttp =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok(manyCardsPageJson cardCount)
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))
  :> IHttp

/// Builds and shows the shell over one test env.
let startShell(store: SessionStore, http: IHttp) : Async<Window> =
  let env = TestEnv(store, http)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

/// The first text block under the root carrying the text.
let textBlockWith(root: Control, text: string) : TextBlock option =
  root.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? TextBlock as block when block.Text = text -> Some block
    | _ -> None)

/// The first control under the root carrying the name.
let controlByName(root: Control, name: string) : Control option =
  root.GetVisualDescendants()
  |> Seq.tryFind(fun visual -> visual.Name = name)
  |> Option.map(fun visual -> visual :?> Control)

/// Clicks one button by its text once it shows up.
let clickWhenShown(window: Window, text: string) : Async<unit> = async {
  let! appeared =
    waitForAsync(5000, (fun () -> buttonWithText(window, text)), Option.isSome)

  Expect.isTrue appeared $"the {text} button never appeared"

  do! onUiAsync(fun () -> buttonWithText(window, text) |> Option.iter click)
}

/// A forced full collection: blocking, compacting, with finalizers
/// drained between the passes.
let collect() : unit =
  for _ in 1..2 do
    GC.Collect()
    GC.WaitForPendingFinalizers()

  GC.Collect()

/// Shrinks the window so a many-card feed overflows the viewport plus
/// its cache band and recycles rows while scrolling.
let shrinkAsync(window: Window) : Async<unit> =
  onUiAsync(fun () ->
    window.Width <- 420.0
    window.Height <- 480.0)

/// Whether the framework still holds the bitmap's platform image
/// alive; disposal is the drop path a canceled load owes it.
let bitmapAlive(bitmap: Bitmap) : bool =
  let flags = BindingFlags.Instance ||| BindingFlags.NonPublic

  typeof<Bitmap>.GetProperty("PlatformImpl", flags)
  |> Option.ofObj
  |> Option.bind(fun platform -> platform.GetValue bitmap |> Option.ofObj)
  |> Option.bind(fun impl ->
    impl.GetType().GetProperty("IsAlive")
    |> Option.ofObj
    |> Option.map(fun alive -> alive.GetValue impl :?> bool))
  |> Option.defaultValue false

/// Presses one bare key through the routed event.
let pressKey(source: Interactive, key: Key) : unit =
  source.RaiseEvent(
    KeyEventArgs(RoutedEvent = InputElement.KeyDownEvent, Key = key)
  )

let workbenchNow = DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)

let workbenchCheckoutA = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

let workbenchCheckoutB = "018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d"

let workbenchFollowing =
  WorkbenchSettings.parse "following" |> WorkbenchState.refOf

let workbenchLatest = WorkbenchSettings.parse "latest" |> WorkbenchState.refOf

let workbenchTrending =
  WorkbenchSettings.parse "trending" |> WorkbenchState.refOf

let workbenchImages = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let workbenchProps(env: TestEnv) : WorkbenchHost.Props<TestEnv> = {
  Env = env
  Initial = Build.state(workbenchFollowing, Some workbenchCheckoutA)
  Images = workbenchImages
  Now = Observable.single workbenchNow
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  OnOpenAuthor = ignore
  OnOpenExternal = ignore
  OnStateChanged = ignore
  OnUseDefaultLayout = ignore
}

let hasTextFragment(root: Control, fragment: string) : bool =
  root.GetVisualDescendants()
  |> Seq.exists(function
    | :? TextBlock as block ->
      block.Text <> null && block.Text.Contains fragment
    | _ -> false)

/// The weak reference to one region's current child root. The control
/// never leaves the probe, so only the host can keep it alive.
let regionRootWeak
  (window: Window, region: string)
  : Async<WeakReference option> =
  probeUiAsync(fun () ->
    controlByName(window, region)
    |> Option.bind(function
      | :? ContentControl as host -> Option.ofObj host.Content
      | _ -> None)
    |> Option.map WeakReference)

/// The weak reference to the checked-out commit's inspector header.
let inspectorRootWeak(window: Window) : Async<WeakReference option> =
  probeUiAsync(fun () ->
    controlByName(window, "InspectorRegion")
    |> Option.bind(fun region ->
      region.GetVisualDescendants()
      |> Seq.tryPick(function
        | :? TextBlock as block when
          block.Text <> null && block.Text.StartsWith "CHECKOUT"
          ->
          Some(WeakReference block)
        | _ -> None)))

let private sendGrown
  (mounted: WorkbenchHost.MountedHost, msg: WorkbenchHost.Msg)
  : Async<unit> = async {
  let before = mounted.Created.Length
  do! onUiAsync(fun () -> mounted.Send msg)

  let! grown =
    waitForAsync(
      8000,
      (fun () -> mounted.Created.Length),
      fun count -> count > before
    )

  Expect.isTrue grown "a new child handle never reached the mount"
  do! settleAsync()
}

/// The largest realized band over every virtualizing panel under the
/// root: the page's own feed dominates, and chrome panels realize only
/// a few rows each. Runs on the UI thread.
let realizedBand(window: Window) : int =
  window.GetVisualDescendants()
  |> Seq.choose(function
    | :? VirtualizingStackPanel as panel -> Some panel.Children.Count
    | _ -> None)
  |> Seq.fold (fun best count -> max best count) 0

/// The weak references to the currently realized rows of the largest
/// virtualizing panel: the page's own feed dominates, and chrome
/// panels realize only a few rows each. Each probe builds its own
/// list, so no test binding keeps a row alive. Runs on the UI thread.
let realizedWeaks(window: Window) : WeakReference list =
  let children =
    window.GetVisualDescendants()
    |> Seq.choose(function
      | :? VirtualizingStackPanel as panel ->
        Some(panel.Children :> Control seq)
      | _ -> None)
    |> Seq.sortByDescending Seq.length
    |> Seq.tryHead
    |> Option.defaultValue(Seq.empty<Control>)

  children |> Seq.map WeakReference |> List.ofSeq

/// The main feed's offset: the scrollable viewer with the largest
/// extent. Small chrome viewers never qualify. Runs on the UI thread.
let mainOffset(window: Window) : float =
  window.GetVisualDescendants()
  |> Seq.choose(function
    | :? ScrollViewer as scroller -> Some scroller
    | _ -> None)
  |> Seq.filter(fun scroller ->
    scroller.Extent.Height > scroller.Viewport.Height + 1.0)
  |> Seq.sortByDescending(fun scroller -> scroller.Extent.Height)
  |> Seq.tryHead
  |> Option.map(fun scroller -> scroller.Offset.Y)
  |> Option.defaultValue -2.0

/// Scrolls the main feed toward its end until the offset stops growing:
/// a virtualized list estimates its extent from the realized band, so
/// one jump lands short of the true end and each jump converges it.
let rec private convergeScroll
  (window: Window, previous: float, attempts: int)
  : Async<unit> = async {
  if attempts <= 0 then
    return ()
  else
    do! scrollFeedToEndAsync window
    do! settleAsync()

    let! offset = probeUiAsync(fun () -> mainOffset window)

    if offset = previous then
      return ()
    else
      return! convergeScroll(window, offset, attempts - 1)
}

/// Scrolls the main feed to its converged end.
let scrollToConvergedEndAsync(window: Window) : Async<unit> =
  convergeScroll(window, -1.0, 12)

/// Every text the tree shows: a block's own text plus its inline runs,
/// so a line built of runs counts the way the search identity lines
/// do. Runs on the UI thread.
let treeTexts(window: Window) : Set<string> =
  window.GetVisualDescendants()
  |> Seq.choose(function
    | :? TextBlock as block ->
      let main = if block.Text <> null then block.Text else ""

      let runs =
        if block.Inlines <> null then
          block.Inlines
          |> Seq.choose(function
            | :? Avalonia.Controls.Documents.Run as run -> Option.ofObj run.Text
            | _ -> None)
          |> String.concat ""
        else
          ""

      let text = main + runs

      if text = "" then None else Some text
    | _ -> None)
  |> Set.ofSeq

/// Waits until the tree's text set differs from the given one: turning
/// a page replaces the realized rows while the chrome stays put, so
/// the set turns over without naming any row.
let waitForTextChangeAsync
  (window: Window, previous: Set<string>)
  : Async<bool> =
  waitForAsync(
    8000,
    (fun () -> treeTexts window),
    fun current -> current <> previous
  )

/// Waits until the tree's text set stops changing between beats: the
/// page's reads have landed and the rows it shows hold still, without
/// naming any row.
let rec private settleTexts
  (window: Window, previous: Set<string>, attempts: int)
  : Async<Set<string>> = async {
  if attempts <= 0 then
    return previous
  else
    do! settleAsync()

    let! current = probeUiAsync(fun () -> treeTexts window)

    if current = previous then
      return current
    else
      return! settleTexts(window, current, attempts - 1)
}

/// The tree's text set once it holds still across two beats.
let waitForQuiescentTextsAsync(window: Window) : Async<Set<string>> = async {
  let! first = probeUiAsync(fun () -> treeTexts window)
  return! settleTexts(window, first, 15)
}

/// Waits until scrolling replaces the realized rows in both
/// directions: some text leaves the tree and new text arrives, without
/// naming any row.
let waitForRowTurnoverAsync
  (window: Window, previous: Set<string>)
  : Async<bool> =
  waitForAsync(
    8000,
    (fun () -> treeTexts window),
    fun current ->
      not(Set.isEmpty(Set.difference previous current))
      && not(Set.isEmpty(Set.difference current previous))
  )

/// Waits until the tree shares text with the given set again: scrolling
/// back restores the first rows, without naming any row.
let waitForRowReturnAsync(window: Window, first: Set<string>) : Async<bool> =
  waitForAsync(
    8000,
    (fun () -> treeTexts window),
    fun current -> not(Set.isEmpty(Set.intersect current first))
  )

/// One reply shaped like `replyJson`, with its own id and body, so a
/// hundred-reply page carries a hundred distinct rows.
let replyAt(index: int) : string =
  replyJson
    .Replace(
      "018f4c1e-7777-7c3a-9b2d-5e6f7a8b9c0d",
      sprintf "018f4c1e-%04d-7c3a-9b2d-5e6f7a8b9c0d" index
    )
    .Replace(
      "\"short_hash\":\"d1e2f3a\"",
      sprintf "\"short_hash\":\"r%05d\"" index
    )
    .Replace("\"body\":\"A reply.\"", sprintf "\"body\":\"Reply %d.\"" index)

/// A replies page with a hundred rows and no next cursor.
let hundredRepliesJson =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":100,\"next_cursor\":null,\"prev_cursor\":null}}"
    ([ 0..99 ] |> List.map replyAt |> String.concat ",")

/// The thread api with a hundred replies on the thread's replies
/// endpoint. Every other branch matches `threadApi`.
let hundredRepliesApi() =
  FakeApi(fun request ->
    if
      request.Method = "GET"
      && request.Target.StartsWith(
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/replies"
      )
    then
      ok hundredRepliesJson
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif request.Method = "POST" && request.Target = "/api/v1/commits" then
      ok(sprintf "{\"data\":%s}" replyJson)
    elif
      request.Method = "POST"
      && request.Target =
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d/pasteis"
    then
      ok pastelJson
    elif
      request.Target.StartsWith
        "/api/v1/commits/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
    then
      ok threadPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The thread tab over the canned commit, reading its thread and
/// replies through the env's api.
let threadPropsOf(env: TestEnv) : ThreadTab.Props<TestEnv> = {
  Env = env
  Session = signedInSession()
  CommitId = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  Now = fun () -> DateTimeOffset.UtcNow
  OnSnapshot = ignore
  OnFetch = ignore
  OnQuote = ignore
  OnReplyRequested = ignore
  OnOpenRepo = ignore
  OnShared = ignore
}

/// Mounts the thread tab with a hundred replies in a small window, so
/// the viewport plus the overscan band covers only a fraction of the
/// hundred rows.
let mountHundredReplyThread() : Async<Window * IDisposable> = async {
  let env = TestEnv(signedInStore(), hundredRepliesApi() :> IHttp)

  let! window, handle =
    probeUiAsync(fun () ->
      let control, handle = ThreadTab.mount(ui, threadPropsOf env)

      let window = Window(Width = 480.0, Height = 600.0, Content = control)
      window.Show()
      window, handle)

  return window, handle
}

/// One commit shaped like `commitJson`, with its own id and body, so a
/// hundred-commit answer carries a hundred distinct rows.
let searchCommitAt(index: int) : string =
  commitJson
    .Replace(
      "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d",
      sprintf "018f4c1e-%04d-7c3a-9b2d-5e6f7a8b9c0d" index
    )
    .Replace(
      "\"short_hash\":\"b8b9c0d\"",
      sprintf "\"short_hash\":\"s%05d\"" index
    )
    .Replace(
      "\"body\":\"It was DNS.\"",
      sprintf "\"body\":\"Budget search %d.\"" index
    )

/// The search api with a hundred commits on the commit search
/// endpoint. Every other branch matches the search tests' api.
let hundredSearchApi() =
  let commits = [ 0..99 ] |> List.map searchCommitAt |> String.concat ","

  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/search/profiles" then
      ok searchProfilesJson
    elif request.Target.StartsWith "/api/v1/search/commits" then
      ok(sprintf "{\"data\":[%s]}" commits)
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif request.Target.Contains "/replies" then
      ok repliesPageJson
    elif request.Target.StartsWith "/api/v1/commits/" then
      ok threadPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// One scheduled queue entry with its own id and body, so a
/// hundred-row queue carries a hundred distinct rows.
let queuedAt(index: int) : string =
  sprintf
    """{"id":"018f5a1e-%04d-7c3a-9b2d-5e6f7a8b9c0d","body":"Queued body %d.","body_segments":[{"type":"text","text":"Queued body %d."}],"preview_segments":null,"poll_options":null,"image_count":0,"scheduled_at":"2026-10-15T13:00:00+00:00","submitted_at":null,"status":"scheduled"}"""
    index
    index
    index

/// The queue api with a hundred rows on the scheduled-commits
/// endpoint. Every other branch matches the log tests' api.
let hundredQueueApi() =
  let rows = [ 0..99 ] |> List.map queuedAt |> String.concat ","

  let page =
    sprintf
      """{"data":[%s],"links":{"first":null,"last":null,"prev":null,"next":null},"meta":{"current_page":1,"from":1,"last_page":1,"path":"","per_page":100,"to":100,"total":100}}"""
      rows

  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/scheduled-commits" then
      ok page
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// Opens the composer's schedule panel and picks the second day, so the
/// panel's queue link appears.
let pickTomorrowAsync(window: Window) : Async<unit> = async {
  let! button =
    waitForAsync(
      5000,
      (fun () -> controlByName(window, "ScheduleButton")),
      Option.isSome
    )

  Expect.isTrue button "the schedule button never appeared"

  do!
    onUiAsync(fun () ->
      controlByName(window, "ScheduleButton")
      |> Option.iter(fun visual -> click(visual :?> Button)))

  let! panel =
    waitForAsync(
      5000,
      (fun () ->
        window.GetVisualDescendants()
        |> Seq.tryFind(fun visual -> visual.Name = "ScheduleDay")),
      Option.isSome
    )

  Expect.isTrue panel "the schedule panel never appeared"

  do!
    onUiAsync(fun () ->
      window.GetVisualDescendants()
      |> Seq.iter(function
        | :? ComboBox as picker when picker.Name = "ScheduleDay" ->
          picker.SelectedIndex <- 1
        | _ -> ()))
}

/// A history column model with a hundred distinct rows from the canned
/// log page, so the column carries a hundred distinct keys.
let hundredHistoryModel() : HistoryColumn.Model =
  let page =
    match LogPage.decode(manyCardsPageJson 100) with
    | Ok page -> page
    | Error failure ->
      failwith $"the canned log page never decoded: {failure.Message}"

  {
    Loading = false
    Error = None
    Items = page.Page.Items
    NextCursor = None
    Head = Some "018f4c1e-6f8a-7c3a-9b2d-0001"
    NewCount = None
    Ranked = false
    Polling = false
    Now = workbenchNow
    FetchedAt = Some(workbenchNow.AddMinutes -12.0)
    Busy = None
    PollChoices = Map.empty
    Pasteis = false
    HeardIds = []
    Offset = 0.0
  }

/// The history column over a throwaway api: the model already carries
/// its hundred rows, so the api never serves.
let historyPropsOf(env: TestEnv) : HistoryColumn.Props<TestEnv> = {
  Env = env
  Session = None
  Source = LogSource Following
  KeyFile = "/tmp/devnull-test-id_ed25519"
  ViewerId = "01a151db-7fee-727b-a7ce-c911a7556242"
  Pasteis = false
  Images = {
    new ImageCacheService with
      member _.Get(_url: string) = async { return None }
  }
  Flyout = ValueNone
  Now = Observable.single workbenchNow
  Refresh = Observable.empty
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  LastVisit = None
  OnOpenExternal = ignore
  OnOpened = ignore
  OnReply = ignore
  OnQuote = ignore
  Restore = None
  OnReport = ignore
  OnDialog = ignore
}

/// Mounts the workbench history column with a hundred rows in a small
/// window, so the viewport plus the overscan band covers only a
/// fraction of the rows.
let mountHundredHistory
  ()
  : Async<Window * BehaviorSubject<HistoryColumn.Model>> = async {
  let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
  let subject = new BehaviorSubject<HistoryColumn.Model>(hundredHistoryModel())

  let! window =
    probeUiAsync(fun () ->
      let view =
        HistoryColumn.view
          (historyPropsOf env)
          (subject :> IObservable<HistoryColumn.Model>)
          (fun (_: HistoryColumn.Msg) -> ())

      let window = Window(Width = 480.0, Height = 600.0, Content = view)
      window.Show()
      window)

  return window, subject
}

/// A hundred distinct notifications from the canned inbox page, with
/// rising event counts so the last row names its own count.
let hundredNotificationItems() : Notification list =
  let page =
    match NotificationPage.decode notificationsJson with
    | Ok page -> page
    | Error failure ->
      failwith $"the canned inbox never decoded: {failure.Message}"

  let first =
    match page.Page.Items with
    | head :: _ -> head
    | [] -> failwith "the canned inbox carries no notification"

  [
    for index in 0..99 ->
      {
        first with
            Id = sprintf "01a15752-%04d-7065-b076-99e5bdad596d" index
            EventCount = index + 1
      }
  ]

/// The notification center over a throwaway api: the model already
/// carries its hundred rows, so the api never serves.
let notificationsPropsOf(env: TestEnv) : NotificationsCenter.Props<TestEnv> = {
  Env = env
  KeyFile = "/tmp/devnull-test-id_ed25519"
  Images = {
    new ImageCacheService with
      member _.Get(_url: string) = async { return None }
  }
  Now = Observable.single workbenchNow
  OnCheckout = ignore
}

/// Mounts the notification center with a hundred rows in a small
/// window, so the viewport plus the overscan band covers only a
/// fraction of the rows.
let mountHundredNotifications
  ()
  : Async<Window * BehaviorSubject<NotificationsCenter.Model>> = async {
  let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)

  let model: NotificationsCenter.Model = {
    Loading = false
    More = false
    Error = None
    Items = hundredNotificationItems()
    Cursor = None
    Now = workbenchNow
  }

  let subject = new BehaviorSubject<NotificationsCenter.Model>(model)

  let! window =
    probeUiAsync(fun () ->
      let view =
        NotificationsCenter.view
          (notificationsPropsOf env)
          (subject :> IObservable<NotificationsCenter.Model>)
          (fun (_: NotificationsCenter.Msg) -> ())

      let window = Window(Width = 480.0, Height = 600.0, Content = view)
      window.Show()
      window)

  return window, subject
}

let tests =
  testList "memory leaks" [
    testCaseAsync
      "a canceled image load disposes its decoded bitmap"
      (async {
        // The loader decodes before it posts; a cancel between the two
        // leaves the bitmap with no control to show it.
        let! bitmap =
          probeUiAsync(fun () ->
            use stream = new MemoryStream(imageBytes)
            Bitmap.DecodeToWidth(stream, 16))

        let! aliveBefore = probeUiAsync(fun () -> bitmapAlive bitmap)

        Expect.isTrue aliveBefore "the bitmap probe saw a dead image"

        let! disposed =
          probeUiAsync(fun () ->
            DevNull.Client.Components.AsyncImage.handOver (true, ignore) bitmap

            not(bitmapAlive bitmap))

        Expect.isTrue disposed "the canceled load kept its bitmap"
      })

    testCaseAsync
      "a themed control releases with its tree"
      (async {
        // The control and its weak reference are built inside one probe,
        // so no test binding keeps the control alive.
        let! pair =
          probeUiAsync(fun () ->
            let control = CommitCard.tombstone()

            Window(Content = control), WeakReference(control))

        let window, weak = pair

        do! settleAsync()

        // The window stays alive and shown; only the themed subtree
        // leaves, so the headless session's window bookkeeping cannot
        // hold the answer.
        do! onUiAsync(fun () -> window.Content <- Panel())

        do! settleAsync()
        collect()

        Expect.isFalse weak.IsAlive "the themed control outlived its tree"
      })

    testCaseAsync
      "a feed row releases when its page ends"
      (async {
        let! window = startShell(signedInStore(), soakApi 40)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Card 1."))
          )

        Expect.isTrue shown "the first card never rendered"

        // The row's body block, held weakly before the row ever recycles.
        let! weak =
          probeUiAsync(fun () ->
            textBlockWith(window, "Card 1.")
            |> Option.map(fun block -> WeakReference(block)))

        Expect.isSome weak "the first card's body never rendered"

        do! shrinkAsync window
        do! scrollFeedToEndAsync window
        do! settleAsync()

        // Leaving the log ends its page program; the recycled row must
        // follow it out of memory.
        do! clickWhenShown(window, "Settings")
        do! settleAsync()
        collect()

        Expect.isFalse weak.Value.IsAlive "the recycled row outlived its page"
      })

    testCaseAsync
      "a scroll soak stays inside its memory budget"
      (async {
        let! window = startShell(signedInStore(), soakApi 30)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Card 1."))
          )

        Expect.isTrue shown "the feed never rendered"

        do! shrinkAsync window

        let! before = probeUiAsync(fun () -> GC.GetTotalMemory true)

        for _ in 1..3 do
          do! scrollFeedToEndAsync window
          do! settleAsync()
          do! scrollFeedToTopAsync window
          do! settleAsync()

        // The realized rows ride the open page's model; leaving the
        // log ends that page, and everything the soak realized must
        // follow it out of memory.
        do! clickWhenShown(window, "Settings")
        do! settleAsync()
        collect()

        // One more drain: CI's runners sit on a higher, noisier
        // baseline than a local run (9.6-14.0 MB observed across runs,
        // against under 8 locally), and Linux lands its finalizers
        // later. The extra beat plus collection settles that floor
        // before the delta is read.
        do! settleAsync()
        collect()

        let! after = probeUiAsync(fun () -> GC.GetTotalMemory true)

        // The budget sits above the noisiest baseline observed with the
        // rail and poll chrome (16.02 MB) and below the smallest value
        // the leak retained before the fix (16.9 MB), so both directions
        // of the gate hold on every runner.
        Expect.isLessThan
          (after - before)
          16_500_000L
          $"the soak retained {after - before} bytes"
      })

    testCaseAsync
      "the key table releases the page it leaves"
      (async {
        let! window = startShell(signedInStore(), soakApi 40)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Card 1."))
          )

        Expect.isTrue shown "the first card never rendered"

        // The row's body block, held weakly before anything can recycle
        // the row.
        let! weak =
          probeUiAsync(fun () ->
            textBlockWith(window, "Card 1.")
            |> Option.map(fun block -> WeakReference(block)))

        Expect.isSome weak "the first card's body never rendered"

        // The table walks the cards before the page ends, so a handler
        // that captured what it touched would hold it.
        do!
          onUiAsync(fun () ->
            pressKey(window, Key.J)
            pressKey(window, Key.J)
            pressKey(window, Key.K))

        do! settleAsync()

        // Leaving the log ends its page program; the walked row must
        // follow it out of memory.
        do! clickWhenShown(window, "Settings")
        do! settleAsync()
        collect()

        Expect.isFalse weak.Value.IsAlive "the walked row outlived its page"
      })

    testCaseAsync
      "the bindings list releases its content on close"
      (async {
        let! window = startShell(signedInStore(), soakApi 40)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Card 1."))
          )

        Expect.isTrue shown "the first card never rendered"

        do! onUiAsync(fun () -> pressKey(window, Key.OemQuestion))

        let! opened =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "Keyboard shortcuts"))
          )

        Expect.isTrue opened "the bindings list never opened"

        let! weak =
          probeUiAsync(fun () ->
            textBlockWith(window, "Keyboard shortcuts")
            |> Option.map(fun block -> WeakReference(block)))

        Expect.isSome weak "the bindings list never rendered"

        do! onUiAsync(fun () -> pressKey(window, Key.Escape))
        do! settleAsync()
        do! settleAsync()
        collect()

        Expect.isFalse weak.Value.IsAlive "the closed list outlived its overlay"
      })

    testCaseAsync
      "a removed staged image collects on the transition"
      (async {
        // Two-megabyte payloads cross the large-object threshold the
        // way real photos do; the real PNG head passes the sniffing
        // parser and the camera session's true decode (the trailing
        // zeros ride past IEND, where the decoder stops).
        let staged(name: string) : ImageAttachment = {
          stagedImage(name) with
              Bytes =
                Array.append imageBytes (Array.create (2 * 1024 * 1024) 0uy)
        }

        let attachments = FakeAttachments()

        attachments.PickResponse <- [ staged "leak-a.png"; staged "leak-b.png" ]

        let env = TestEnv(signedInStore(), logApi(), attachments = attachments)
        let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
        let! _ = showAsync window

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "It was DNS."))
          )

        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            controlByName(window, "AttachButton")
            |> Option.iter(fun visual ->
              match visual with
              | :? Button as button -> click button
              | _ -> ()))

        let! landed =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "leak-a.png"))
          )

        Expect.isTrue landed "the picked files never staged"

        // The weak reference is taken while the canned answer still
        // holds the payload; a WeakReference keeps no strong path, so
        // only the composer's model can hold the array alive next.
        let! weak =
          probeUiAsync(fun () ->
            attachments.PickResponse
            |> List.tryHead
            |> Option.map(fun current -> WeakReference(current.Bytes)))

        Expect.isSome weak "the staged payload never became reachable"

        // The fake's property outlives the pick, so it must stop holding
        // the arrays before the removal can be the last reference.
        attachments.PickResponse <- []

        do!
          onUiAsync(fun () ->
            controlByName(window, "RemoveImage0")
            |> Option.iter(fun visual ->
              match visual with
              | :? Button as button -> click button
              | _ -> ()))

        do! settleAsync()
        do! settleAsync()

        // No forced collection here: the removal transition itself must
        // collect the staged bytes, or the resting app never will.
        Expect.isFalse
          weak.Value.IsAlive
          "the removed image outlived its transition"
      })

    testCaseAsync
      "the workbench mount releases every child root it drops"
      (async {
        let! window, mounted =
          probeUiAsync(fun () ->
            let env = TestEnv(signedInStore(), workbenchApi() :> IHttp)
            let view, handle = WorkbenchHost.mount(ui, workbenchProps env)

            let window = Window(Width = 1600.0, Height = 900.0, Content = view)

            window.Show()
            window, handle :?> WorkbenchHost.MountedHost)

        let! firstHistory =
          waitForAsync(
            8000,
            (fun () -> mounted.LiveHistory),
            fun live -> live = 1
          )

        Expect.isTrue firstHistory "the first history program never landed"

        let! firstInspector =
          waitForAsync(
            8000,
            (fun () -> mounted.LiveInspector),
            fun live -> live = 1
          )

        Expect.isTrue firstInspector "the first inspector program never landed"

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              hasText(root, "It was DNS.") && hasTextFragment(root, "CHECKOUT")
          )

        Expect.isTrue shown "the workbench never rendered its children"
        do! settleAsync()

        let droppedHistory = ResizeArray<WeakReference>()
        let droppedInspectors = ResizeArray<WeakReference>()
        let droppedThreads = ResizeArray<WeakReference>()
        let droppedNotifications = ResizeArray<WeakReference>()
        let droppedSearch = ResizeArray<WeakReference>()

        let keep
          (target: ResizeArray<WeakReference>, weak: WeakReference option)
          =
          match weak with
          | Some reference -> target.Add reference
          | None -> failwith "a dropped root had no weak reference"

        // Ten ref switches: every history root before the last one must
        // leave with its program.
        let refs = [
          workbenchLatest
          workbenchFollowing
          workbenchTrending
          workbenchLatest
          workbenchFollowing
          workbenchTrending
          workbenchLatest
          workbenchFollowing
          workbenchTrending
          workbenchLatest
        ]

        for entry in refs do
          let! weak = regionRootWeak(window, "HistoryRegion")
          keep(droppedHistory, weak)
          do! sendGrown(mounted, WorkbenchHost.RefSelected entry)

        // Five checkouts: each inspector root is replaced by the next.
        let! initialInspector = inspectorRootWeak window
        keep(droppedInspectors, initialInspector)

        for checkout in
          [
            workbenchCheckoutB
            workbenchCheckoutA
            workbenchCheckoutB
            workbenchCheckoutA
            workbenchCheckoutB
          ] do
          let! weak = inspectorRootWeak window
          keep(droppedInspectors, weak)
          do! sendGrown(mounted, WorkbenchHost.CheckoutSet(Some checkout))

          let! live =
            waitForAsync(
              8000,
              (fun () -> mounted.LiveInspector),
              fun count -> count = 1
            )

          Expect.isTrue live "the inspector did not stay single"

        // Five thread programs, each closed before the next opens.
        for _ in 1..5 do
          do! sendGrown(mounted, WorkbenchHost.OpenThread workbenchCheckoutA)

          let! open' =
            waitForAsync(
              8000,
              (fun () -> mounted.LiveThread),
              fun count -> count = 1
            )

          Expect.isTrue open' "the thread program never started"

          let! weak = regionRootWeak(window, "ThreadRegion")
          keep(droppedThreads, weak)

          do! onUiAsync(fun () -> mounted.Send WorkbenchHost.ThreadClosed)

          let! closed =
            waitForAsync(
              8000,
              (fun () -> mounted.LiveThread),
              fun count -> count = 0
            )

          Expect.isTrue closed "the thread program outlived its close"
          do! settleAsync()

        // Staging opens and closes without starting a child program.
        let stagedBefore = mounted.Created.Length

        do! onUiAsync(fun () -> mounted.Send WorkbenchHost.StagingToggled)

        let! composer =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "What did you ship?")
          )

        Expect.isTrue composer "the staging composer never opened"

        do! onUiAsync(fun () -> mounted.Send WorkbenchHost.StagingToggled)

        let! composerGone =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> not(hasText(root, "What did you ship?"))
          )

        Expect.isTrue composerGone "the staging composer never closed"

        Expect.equal
          mounted.Created.Length
          stagedBefore
          "staging started a child program"

        // The notifications center reads once on entry, then leaves with
        // its ref.
        do!
          sendGrown(
            mounted,
            WorkbenchHost.RefSelected(
              WorkbenchSettings.parse "notifications" |> WorkbenchState.refOf
            )
          )

        let! notifications =
          waitForAsync(
            8000,
            (fun () -> mounted.LiveNotifications),
            fun count -> count = 1
          )

        Expect.isTrue notifications "the notifications program never started"

        let! notificationRows =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasTextFragment(root, "NOTIFICATIONS")
          )

        Expect.isTrue notificationRows "the notifications center never rendered"

        let! notificationWeak = regionRootWeak(window, "NotificationsRegion")
        keep(droppedNotifications, notificationWeak)

        do! sendGrown(mounted, WorkbenchHost.RefSelected workbenchFollowing)

        let! notificationsGone =
          waitForAsync(
            8000,
            (fun () -> mounted.LiveNotifications),
            fun count -> count = 0
          )

        Expect.isTrue notificationsGone "the notifications program stayed live"

        // The search center opens once, then closes back to the history.
        do! sendGrown(mounted, WorkbenchHost.SearchCommitted "dns")

        let! search =
          waitForAsync(
            8000,
            (fun () -> mounted.LiveSearch),
            fun count -> count = 1
          )

        Expect.isTrue search "the search program never started"

        let! searchRows =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, "SearchDialogCard")),
            Option.isSome
          )

        Expect.isTrue searchRows "the search center never rendered"

        let! searchWeak = regionRootWeak(window, "SearchRegion")
        keep(droppedSearch, searchWeak)

        do! sendGrown(mounted, WorkbenchHost.SearchClosed)

        let! searchGone =
          waitForAsync(
            8000,
            (fun () -> mounted.LiveSearch),
            fun count -> count = 0
          )

        Expect.isTrue searchGone "the search program stayed live"

        do! settleAsync()
        collect()

        let alive(items: ResizeArray<WeakReference>) =
          items |> Seq.filter _.IsAlive |> Seq.length

        Expect.equal (alive droppedHistory) 0 "a replaced history root survived"

        Expect.equal
          (alive droppedInspectors)
          0
          "a replaced inspector root survived"

        Expect.equal (alive droppedThreads) 0 "a closed thread root survived"

        Expect.equal
          (alive droppedNotifications)
          0
          "a closed inbox root survived"

        Expect.equal (alive droppedSearch) 0 "a closed search root survived"

        let created = mounted.Created

        do!
          onUiAsync(fun () ->
            (mounted :> IDisposable).Dispose()
            (mounted :> IDisposable).Dispose()
            window.Content <- Panel()
            window.Close())

        do! settleAsync()
        do! settleAsync()
        collect()

        Expect.isTrue
          (created |> List.forall(fun handle -> handle.Disposed))
          "a child handle the mount saw stayed live"

        Expect.isTrue
          (created
           |> List.forall(fun handle ->
             obj.ReferenceEquals(
               handle.Raw,
               System.Reactive.Disposables.Disposable.Empty
             )))
          "a child handle the mount dropped kept its wrapped handle"

        Expect.equal mounted.LiveHistory 0 "the history count stayed up"
        Expect.equal mounted.LiveInspector 0 "the inspector count stayed up"
        Expect.equal mounted.LiveThread 0 "the thread count stayed up"
        Expect.equal mounted.LiveRepository 0 "the repository count stayed up"

        Expect.equal
          mounted.LiveNotifications
          0
          "the notifications count stayed up"

        Expect.equal mounted.LiveSearchPalette 0 "the palette count stayed up"
        Expect.equal mounted.LiveSearch 0 "the search count stayed up"
        Expect.isTrue mounted.HostDisposed "the host handle stayed live"
      })

    testCaseAsync
      "a hundred-reply thread realizes fewer than thirty rows"
      (async {
        let! window, handle = mountHundredReplyThread()

        // The feed realizes its first band once the replies arrive, so
        // a nonzero band is the load signal without naming any row.
        let! loaded =
          waitForAsync(
            8000,
            (fun () -> realizedBand window),
            fun count -> count > 0
          )

        Expect.isTrue loaded "the thread never realized a row"
        do! settleAsync()

        let! count = probeUiAsync(fun () -> realizedBand window)

        Expect.isLessThan
          count
          30
          "the thread realized too many of its hundred replies"

        let! before = probeUiAsync(fun () -> treeTexts window)

        do! scrollToConvergedEndAsync window

        let! turned = waitForRowTurnoverAsync(window, before)

        Expect.isTrue
          turned
          "scrolling to the end never replaced the realized rows"

        do! scrollFeedToTopAsync window
        do! settleAsync()

        let! returned = waitForRowReturnAsync(window, before)

        Expect.isTrue returned "scrolling back never restored the first rows"

        do!
          onUiAsync(fun () ->
            handle.Dispose()
            window.Close())
      })

    testCaseAsync
      "a scrolled-past reply row releases with its thread"
      (async {
        let! window, handle = mountHundredReplyThread()

        let! loaded =
          waitForAsync(
            8000,
            (fun () -> realizedBand window),
            fun count -> count > 0
          )

        Expect.isTrue loaded "the thread never realized a row"
        do! settleAsync()

        let! weaks = probeUiAsync(fun () -> realizedWeaks window)

        Expect.isTrue (weaks.Length > 0) "the feed realized no rows"

        let! before = probeUiAsync(fun () -> treeTexts window)

        do! scrollToConvergedEndAsync window

        let! turned = waitForRowTurnoverAsync(window, before)

        Expect.isTrue
          turned
          "scrolling to the end never replaced the realized rows"

        // The rows bind the thread program's own model stream, so they
        // follow it out: ending the thread completes the stream and
        // releases every row the scroll detached.
        do!
          onUiAsync(fun () ->
            handle.Dispose()
            window.Close())

        do! settleAsync()
        collect()

        let alive = weaks |> List.filter _.IsAlive |> List.length

        Expect.equal alive 0 "a scrolled-past reply row outlived its thread"
      })

    testCaseAsync
      "a search page with a hundred rows realizes fewer than thirty"
      (async {
        let! window = startShell(signedInStore(), hundredSearchApi() :> IHttp)

        do!
          onUiAsync(fun () ->
            window.Width <- 480.0
            window.Height <- 600.0)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "The Octocat")
          )

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Search")

        let! palette =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "SearchInput")),
            Option.isSome
          )

        Expect.isTrue palette "the search palette never opened"

        let! beforeType = probeUiAsync(fun () -> treeTexts window)

        do!
          onUiAsync(fun () ->
            controlByName(window, "SearchInput")
            |> Option.iter(fun control ->
              (control :?> TextBox).Text <- "budget"))

        // The suggestion lists arrive behind the typed term, so a text
        // turnover settles the search without naming any suggestion.
        let! settled = waitForTextChangeAsync(window, beforeType)

        Expect.isTrue settled "the search never settled"

        let! beforeResults = probeUiAsync(fun () -> treeTexts window)

        do! clickWhenShown(window, "See all matching commits")

        let! arrived = waitForTextChangeAsync(window, beforeResults)

        Expect.isTrue arrived "the results page never arrived"

        // The session debounces the results read behind the navigation,
        // so a realized row is the arrival signal without naming any row.
        let! rowsLanded =
          waitForAsync(
            8000,
            (fun () -> realizedBand window),
            fun count -> count > 0
          )

        Expect.isTrue rowsLanded "the results never realized a row"

        let! before = waitForQuiescentTextsAsync window

        let! count = probeUiAsync(fun () -> realizedBand window)

        Expect.isLessThan
          count
          30
          "the search realized too many of its hundred rows"

        do! scrollToConvergedEndAsync window

        let! turned = waitForRowTurnoverAsync(window, before)

        Expect.isTrue
          turned
          "scrolling to the end never replaced the realized rows"

        do! scrollFeedToTopAsync window
        do! settleAsync()

        let! returned = waitForRowReturnAsync(window, before)

        Expect.isTrue returned "scrolling back never restored the first rows"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a scheduled queue with a hundred rows realizes fewer than thirty"
      (async {
        let! window = startShell(signedInStore(), hundredQueueApi() :> IHttp)

        do!
          onUiAsync(fun () ->
            window.Width <- 480.0
            window.Height <- 600.0)

        let! shown =
          waitForAsync(
            5000,
            (fun () -> window),
            fun root -> hasText(root, "The Octocat")
          )

        Expect.isTrue shown "the log never rendered"

        let! beforeQueue = probeUiAsync(fun () -> treeTexts window)

        do! pickTomorrowAsync window
        do! clickWhenShown(window, "Scheduled commits →")

        let! arrived = waitForTextChangeAsync(window, beforeQueue)

        Expect.isTrue arrived "the queue page never arrived"

        // The queue read rides behind the navigation, so a realized row
        // is the arrival signal without naming any row.
        let! rowsLanded =
          waitForAsync(
            8000,
            (fun () -> realizedBand window),
            fun count -> count > 0
          )

        Expect.isTrue rowsLanded "the queue never realized a row"

        let! before = waitForQuiescentTextsAsync window

        let! count = probeUiAsync(fun () -> realizedBand window)

        Expect.isLessThan
          count
          30
          "the queue realized too many of its hundred rows"

        do! scrollToConvergedEndAsync window

        let! turned = waitForRowTurnoverAsync(window, before)

        Expect.isTrue
          turned
          "scrolling to the end never replaced the realized rows"

        do! scrollFeedToTopAsync window
        do! settleAsync()

        let! returned = waitForRowReturnAsync(window, before)

        Expect.isTrue returned "scrolling back never restored the first rows"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a workbench history with a hundred rows realizes fewer than thirty"
      (async {
        let! window, subject = mountHundredHistory()

        let! loaded =
          waitForAsync(
            8000,
            (fun () -> realizedBand window),
            fun count -> count > 0
          )

        Expect.isTrue loaded "the history never realized a row"
        do! settleAsync()

        let! count = probeUiAsync(fun () -> realizedBand window)

        Expect.isLessThan
          count
          30
          "the history realized too many of its hundred rows"

        let! before = probeUiAsync(fun () -> treeTexts window)

        do! scrollToConvergedEndAsync window

        let! turned = waitForRowTurnoverAsync(window, before)

        Expect.isTrue
          turned
          "scrolling to the end never replaced the realized rows"

        do! scrollFeedToTopAsync window
        do! settleAsync()

        let! returned = waitForRowReturnAsync(window, before)

        Expect.isTrue returned "scrolling back never restored the first rows"

        do!
          onUiAsync(fun () ->
            window.Close()
            subject.OnCompleted()
            subject.Dispose())
      })

    testCaseAsync
      "a notification center with a hundred rows realizes fewer than thirty"
      (async {
        let! window, subject = mountHundredNotifications()

        let! loaded =
          waitForAsync(
            8000,
            (fun () -> realizedBand window),
            fun count -> count > 0
          )

        Expect.isTrue loaded "the notification center never realized a row"
        do! settleAsync()

        let! count = probeUiAsync(fun () -> realizedBand window)

        Expect.isLessThan
          count
          30
          "the notification center realized too many of its hundred rows"

        let! before = probeUiAsync(fun () -> treeTexts window)

        do! scrollToConvergedEndAsync window

        let! turned = waitForRowTurnoverAsync(window, before)

        Expect.isTrue
          turned
          "scrolling to the end never replaced the realized rows"

        do! scrollFeedToTopAsync window
        do! settleAsync()

        let! returned = waitForRowReturnAsync(window, before)

        Expect.isTrue returned "scrolling back never restored the first rows"

        do!
          onUiAsync(fun () ->
            window.Close()
            subject.OnCompleted()
            subject.Dispose())
      })
  ]
