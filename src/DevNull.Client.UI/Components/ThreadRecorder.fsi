module DevNull.Client.Components.ThreadRecorder

open Avalonia.Controls
open DevNull.Client.Core.Commit
open DevNull.Client.Errors
open DevNull.Client.Natives

module Recorder =

  /// What the lane tells the composer around it.
  type Report =
    | Staged of Result<(ImageAttachment * Waveform), AppError>
    | Cancel

/// The live recording lane: the Recorder program in a control. The
/// capture itself lives in the subscription's state — one cell the
/// meter reads and the stop drains, with Stop as its only teardown —
/// and the lane talks to its composer through the two reports.
type RecordLane =
  new:
    audio: IAudioEngine voption * report: (Recorder.Report -> unit) ->
      RecordLane

  inherit UserControl
