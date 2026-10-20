module DevNull.Client.UI.Tests.Harness

// The headless harness, proven in Briple.Trainer: one session for the
// whole assembly in PerAssembly isolation (PerTest resets the global UI
// dispatcher, which strands the MVUish update loop on a ghost
// dispatcher), every control touch through onUi, and a deterministic
// Background-priority frame pump for dispatcher work queued from other
// threads.

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

/// Runs a unit-of-work on the UI thread and waits for it.
let onUi(test: unit -> unit) : unit =
  session.Dispatch(Action test, CancellationToken.None).GetAwaiter().GetResult()

/// Runs a probe on the UI thread and brings its value back.
let probeUi<'T>(probe: unit -> 'T) : 'T =
  let cell = ref ValueNone

  session
    .Dispatch(
      Action(fun () -> cell.Value <- ValueSome(probe())),
      CancellationToken.None
    )
    .GetAwaiter()
    .GetResult()

  match cell.Value with
  | ValueSome value -> value
  | ValueNone -> failwith "the dispatch never ran"

/// Drains every queued dispatcher job, including Background priority
/// (RunJobs clamps to the minimum active priority and never reaches it).
let pump() =
  onUi(fun () ->
    let frame = DispatcherFrame()

    Dispatcher.UIThread.InvokeAsync(
      Action(fun () -> frame.Continue <- false),
      DispatcherPriority.Background
    )
    |> ignore

    Dispatcher.UIThread.PushFrame frame)

/// Gives cross-thread producers a beat, then drains.
let settle() =
  Thread.Sleep 50
  pump()

/// Polls until the predicate holds or the timeout elapses; each probe
/// runs on the UI thread and each miss pumps, so the MVUish update loop
/// and the render pass advance between attempts.
let waitFor(ms: int, probe: unit -> 'T, predicate: 'T -> bool) : bool =
  let watch = Diagnostics.Stopwatch.StartNew()
  let found = ref false

  while (not found.Value) && watch.ElapsedMilliseconds < int64 ms do
    if probeUi(fun () -> predicate(probe())) then
      found.Value <- true
    else
      Thread.Sleep 100
      pump()

  found.Value

/// Shows one window on the UI thread and returns it, so the visual
/// tree (and therefore GetVisualDescendants) exists for the probes.
let show(window: Window) : Window =
  probeUi(fun () ->
    window.Show()
    window)

/// Builds and shows one control inside a fresh window.
let mount(control: Control) : Window =
  probeUi(fun () ->
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

/// Clicks through the routed event, the way MigrondiUI's tests do.
let click(button: Button) : unit =
  button.RaiseEvent(Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent))
