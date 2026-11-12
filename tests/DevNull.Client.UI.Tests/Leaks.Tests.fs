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

open Expecto
open FSharp.Control.Reactive

open DevNull.Client.Components
open DevNull.Client.Core.Commit
open DevNull.Client.Core.Http
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
            (fun () -> window),
            fun root -> hasTextFragment(root, "Search · manual refresh")
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
  ]
