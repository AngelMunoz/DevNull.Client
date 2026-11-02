module DevNull.Client.Components.CompletionLane

open System
open Avalonia.Controls
open DevNull.Client.Core.Completion
open DevNull.Client.ImageCache
open DevNull.Client.Search

/// What a pick settled: the handle or the issue number the completion
/// carried into the box.
[<Struct>]
type Choice =
  | PersonChosen of handle: string
  | IssueChosen of number: int

/// What the lane reports: the key walk, the close, the text and caret
/// it applied, and the pick it settled.
type Event =
  | Stepped of step: int
  | Hovered of index: int
  | Closed
  | Applied of text: string * caret: int
  | Picked of choice: Choice

/// The lane's inputs: its session, the composer's completion slices,
/// the box it completes into, the event sink, and the avatar cache.
[<Struct>]
type Props = {
  Session: CompletionSession
  Trigger: IObservable<Trigger voption>
  Flyout: IObservable<DevNull.Client.Components.Completion.View>
  Open: IObservable<bool>
  Body: TextBox
  Events: Event -> unit
  Images: ImageCacheService
}

/// The completion lane over the body: the site's own suggestions, on
/// the same gated pipes the search palette runs. The lane feeds the
/// session from the trigger, arms the flyout, walks the keys through
/// the armed row, and carries the completion into the box.
val view: props: Props -> Control
