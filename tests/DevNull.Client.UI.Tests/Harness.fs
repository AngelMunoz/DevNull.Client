module DevNull.Client.UI.Tests.Harness

// The headless harness, proven in Briple.Trainer: one session for the
// whole assembly in PerAssembly isolation (PerTest resets the global UI
// dispatcher, which strands the MVUish update loop on a ghost
// dispatcher), every control touch through onUiAsync, and a
// deterministic Background-priority frame pump for dispatcher work
// queued from other threads.

open System
open System.Threading

open Avalonia
open Avalonia.Controls
open Avalonia.Headless
open Avalonia.Threading
open Avalonia.VisualTree

open DevNull.Client.Program

/// One headless session for the whole assembly; the app type carries
/// the Fluent theme and the app styles.
let private session =
  HeadlessUnitTestSession.StartNew(
    typeof<App>,
    AvaloniaTestIsolationLevel.PerAssembly
  )

/// The session's dispatcher. The first dispatcher created becomes
/// `Dispatcher.UIThread`, and the headless platform creates it on the
/// session thread inside the first setup. Capturing it there keeps a
/// test thread's first `UIThread` access from creating a dispatcher on
/// that thread instead.
let private ui: Dispatcher =
  session
    .Dispatch(
      Func<Dispatcher>(fun () -> Dispatcher.UIThread),
      CancellationToken.None
    )
    .GetAwaiter()
    .GetResult()

/// Runs a unit-of-work on the UI thread and waits for it. A call from
/// the UI thread runs inline: a nested dispatch queues work the blocked
/// thread can never serve. Every dispatch resumes its caller on the
/// thread pool, so a continuation that lands on the session thread never
/// turns the next call into a nested dispatch.
let onUiAsync(test: unit -> unit) : Async<unit> = async {
  if ui.CheckAccess() then
    test()
  else
    do! Async.AwaitTask(session.Dispatch(Action test, CancellationToken.None))

    do! Async.SwitchToThreadPool()
}

/// Runs a probe on the UI thread and brings its value back.
let probeUiAsync<'T>(probe: unit -> 'T) : Async<'T> = async {
  if ui.CheckAccess() then
    return probe()
  else
    let! value =
      Async.AwaitTask(session.Dispatch(Func<'T> probe, CancellationToken.None))

    do! Async.SwitchToThreadPool()
    return value
}

/// Drains every queued dispatcher job, including Background priority
/// (RunJobs clamps to the minimum active priority and never reaches it).
let pumpAsync() : Async<unit> =
  onUiAsync(fun () ->
    let frame = DispatcherFrame()

    ui.InvokeAsync(
      Action(fun () -> frame.Continue <- false),
      DispatcherPriority.Background
    )
    |> ignore

    ui.PushFrame frame)

/// Gives cross-thread producers a beat, then drains.
let settleAsync() : Async<unit> = async {
  do! Async.Sleep 50
  do! pumpAsync()
}

/// Polls until the predicate holds or the timeout elapses; each probe
/// runs on the UI thread and each miss pumps, so the MVUish update loop
/// and the render pass advance between attempts.
let waitForAsync
  (ms: int, probe: unit -> 'T, predicate: 'T -> bool)
  : Async<bool> =
  let watch = Diagnostics.Stopwatch.StartNew()

  let rec loop() = async {
    if watch.ElapsedMilliseconds >= int64 ms then
      return false
    else
      let! found = probeUiAsync(fun () -> predicate(probe()))

      if found then
        return true
      else
        do! Async.Sleep 100
        do! pumpAsync()
        return! loop()
  }

  loop()

/// Shows one window on the UI thread and returns it, so the visual
/// tree (and therefore GetVisualDescendants) exists for the probes.
let showAsync(window: Window) : Async<Window> =
  probeUiAsync(fun () ->
    window.Show()
    window)

/// Builds and shows one control inside a fresh window.
let mountAsync(control: Control) : Async<Window> =
  probeUiAsync(fun () ->
    let window = Window(Content = control)
    window.Show()
    window)

/// True when a TextBlock under the root carries the text.
let hasText(root: Control, text: string) : bool =
  match root with
  | :? TextBlock as textBlock when textBlock.Text = text -> true
  | _ ->
    root.GetVisualDescendants()
    |> Seq.exists(fun visual ->
      match visual with
      | :? TextBlock as textBlock -> textBlock.Text = text
      | _ -> false)

/// Every button whose content subtree carries the text.
let buttonsWithText(root: Control, text: string) : Button list =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? Button as button -> Some button
    | _ -> None)
  |> Seq.filter(fun button ->
    let direct = string button.Content = text

    let nested =
      match button.Content with
      | :? Control as content -> hasText(content, text)
      | _ -> false

    direct || nested)
  |> List.ofSeq

/// The first button whose content subtree carries the text, preferring
/// the innermost one: a card row is a button too, and it contains the
/// action buttons the text may point at.
let buttonWithText(root: Control, text: string) : Button option =
  let matches = buttonsWithText(root, text)

  matches
  |> List.tryFind(fun button ->
    matches
    |> List.forall(fun other ->
      not(button |> _.GetVisualDescendants() |> Seq.contains other)))

/// The first button carrying the text among the ancestors of one
/// control and their subtrees: the nearest container wins, so a page
/// button beats a sidebar button with the same label.
let buttonNear(control: Control, text: string) : Button option =
  control.GetVisualAncestors()
  |> Seq.choose(fun visual ->
    match visual with
    | :? Control as ancestor -> Some ancestor
    | _ -> None)
  |> Seq.tryPick(fun ancestor -> buttonWithText(ancestor, text))

/// The first text box whose placeholder equals the text.
let textBoxWithWatermark(root: Control, text: string) : TextBox option =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? TextBox as textBox -> Some textBox
    | _ -> None)
  |> Seq.tryFind(fun textBox -> textBox.PlaceholderText = text)

/// The main feed: the scrollable viewer with the largest extent. Small
/// chrome viewers never qualify.
let private feedScroller(window: Window) : ScrollViewer option =
  window.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? ScrollViewer as scroller -> Some scroller
    | _ -> None)
  |> Seq.filter(fun scroller ->
    scroller.Extent.Height > scroller.Viewport.Height + 1.0)
  |> Seq.sortByDescending(fun scroller -> scroller.Extent.Height)
  |> Seq.tryHead

/// Scrolls the main feed to its end, so a virtualized list realizes
/// the rows near the bottom and recycles the rows at the top.
let scrollFeedToEndAsync(window: Window) : Async<unit> =
  onUiAsync(fun () -> feedScroller window |> Option.iter _.ScrollToEnd())

/// Scrolls the main feed back to its start, so recycled rows are
/// realized again.
let scrollFeedToTopAsync(window: Window) : Async<unit> =
  onUiAsync(fun () ->
    feedScroller window
    |> Option.iter(fun scroller ->
      scroller.Offset <- Avalonia.Vector(0.0, 0.0)))

/// Clicks through the routed event, the way MigrondiUI's tests do.
let click(button: Button) : unit =
  button.RaiseEvent(Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent))
