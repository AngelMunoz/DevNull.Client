module DevNull.Client.Programs

open System
open Avalonia
open Avalonia.Threading
open MVUish.Avalonia

/// Starts a page's program with its update loop bound to the given
/// dispatcher: the one the head captured at startup, or the headless
/// session's. The loop keeps that dispatcher for its whole lifetime.
val runPage:
  dispatcher: IDispatcher * program: Program<unit, 'model, 'msg, 'view> ->
    'view * IDisposable

/// Starts a component's program with its loop bound to the given
/// dispatcher and its lifetime tied to the first detach of the given
/// visual.
val runComponent:
  scope: Visual *
  dispatcher: IDispatcher *
  program: Program<unit, 'model, 'msg, 'view> ->
    'view * IDisposable
