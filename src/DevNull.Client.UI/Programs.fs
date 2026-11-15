module DevNull.Client.Programs

open System
open System.Reactive.Disposables
open Avalonia
open Avalonia.Controls
open Avalonia.Threading
open MVUish.Avalonia

let runPage
  (dispatcher: IDispatcher, program: Program<unit, 'model, 'msg, 'view>)
  =
  Program.runWithLifecycle () (program |> Program.withDispatcher dispatcher)

let runComponent
  (
    scope: Visual,
    dispatcher: IDispatcher,
    program: Program<unit, 'model, 'msg, 'view>
  ) =
  Program.runWithLifetime
    scope
    ()
    (program |> Program.withDispatcher dispatcher)

/// The one-shot start state of an attach-gated component. A class with
/// member vals carries the guard; the signature file hides the type by
/// omitting it.
type AttachGate() =
  member val Started = false with get, set
  member val Disposed = false with get, set
  member val Handle: IDisposable = Disposable.Empty with get, set

let runComponentOnAttach
  (
    scope: Visual,
    dispatcher: IDispatcher,
    program: Program<unit, 'model, 'msg, 'view>,
    setView: 'view -> unit
  ) : IDisposable =
  let gate = AttachGate()

  let start() : unit =
    if not gate.Started && not gate.Disposed then
      gate.Started <- true
      let view, handle = runComponent(scope, dispatcher, program)
      gate.Handle <- handle

      if dispatcher.CheckAccess() then
        setView view
      else
        dispatcher.Post(
          Action(fun () -> setView view),
          DispatcherPriority.Default
        )

  if TopLevel.GetTopLevel scope |> isNull |> not then
    start()
  else
    scope.AttachedToVisualTree.Add(fun _ -> start())

  {
    new IDisposable with
      member _.Dispose() =
        if not gate.Disposed then
          gate.Disposed <- true
          gate.Handle.Dispose()
  }
