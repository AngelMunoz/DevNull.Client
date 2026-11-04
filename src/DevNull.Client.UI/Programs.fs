module DevNull.Client.Programs

open System
open Avalonia
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
