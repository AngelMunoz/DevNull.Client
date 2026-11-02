module DevNull.Client.Components.ThreadRecorder

open System
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Core.Commit
open DevNull.Client.Errors
open DevNull.Client.Icons
open DevNull.Client.Natives

/// The recording lane's own bounds, the web's own: a half second
/// before a recording counts, a minute before it stops itself, and
/// the warning color through its last ten seconds.
let minRecording = TimeSpan.FromMilliseconds 500.0
let maxRecording = TimeSpan.FromSeconds 60.0
let warnRecording = TimeSpan.FromSeconds 50.0

/// The recording lane's own MVU program: the phase, the take's age,
/// and the rolling 48 levels ride the model; the engine rides
/// commands; the meter and the control's own attach and detach ride
/// subscriptions. The lane reports outward through two projections —
/// one for the staged take, one for a cancelled one — instead of
/// knowing the page's messages.
module Recorder =

  /// Where one take stands: waiting for the microphone, recording,
  /// stopped while the microphone was still opening, encoding the
  /// take into its staged form, or done.
  type Phase =
    | Opening
    | Stopping
    | Recording of started: DateTimeOffset
    | Encoding
    | Done

  type Model = { Phase: Phase; Levels: float list }

  type Msg =
    | Opened of Result<IMicrophoneCapture, AudioFailure>
    | Ticked of level: float * at: TimeSpan
    | StopPressed
    | DiscardPressed
    | TakeReady of Result<byte array, AudioFailure>
    | Cancelled
    | Detached

  /// What the lane tells the composer around it.
  type Report =
    | Staged of Result<(ImageAttachment * Waveform), AppError>
    | Cancel

  /// The microphone opens with the program itself; a head that
  /// carries no engine ends the lane at once.
  let init (audio: IAudioEngine voption) () : Model * Cmd<Msg> =
    let opening =
      match audio with
      | ValueSome engine ->
        Cmd.OfAsync.perform
          (fun (head: IAudioEngine) -> head.StartCapture())
          engine
          Opened
      | ValueNone -> Cmd.ofMsg Cancelled

    {
      Phase = Opening
      Levels = List.replicate 48 0.0
    },
    opening

  let view (model: IObservable<Model>) (dispatch: Dispatch<Msg>) : Control =
    let levels =
      model |> Observable.map _.Levels |> Observable.distinctUntilChanged

    let word =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .FontSize(11.0)
        .MinWidth(64.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Text(
          model
          |> Observable.map(fun current ->
            match current.Phase with
            | Opening -> "opening microphone…"
            | Stopping -> "closing…"
            | Encoding -> "encoding…"
            | Done -> ""
            | Recording _ -> "")
          |> Observable.distinctUntilChanged
        )

    // The pulsing dot, on the clock's own beat.
    let dot =
      Ellipse()
        .Width(8.0)
        .Height(8.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Opacity(
          model
          |> Observable.map(fun current ->
            match current.Phase with
            | Recording started ->
              let on =
                int(
                  (DateTimeOffset.UtcNow - started).TotalMilliseconds / 600.0
                )
                  %
                  2
                  =
                  0

              if on then 1.0 else 0.35
            | _ -> 1.0)
        )

    let bars: Control array = [|
      for at in 0..47 ->
        Border()
          .Width(2.0)
          .CornerRadius(1.0)
          .VerticalAlignment(VerticalAlignment.Bottom)
          .Height(
            levels
            |> Observable.map(fun current ->
              2.0 + (List.tryItem at current |> Option.defaultValue 0.0) * 20.0)
          )
        :> Control
    |]

    let wave =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(2.0)
        .Height(24.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Children(bars)

    let stop =
      Button()
        .Classes(Theme.name Theme.AccentButton)
        .Padding(0.0)
        .Width(28.0)
        .Height(28.0)
        .HorizontalContentAlignment(HorizontalAlignment.Center)
        .VerticalContentAlignment(VerticalAlignment.Center)
        .Content(Icons.iconAt(Glyph.Stop, 14.0))
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          dispatch StopPressed)

    ToolTip.SetTip(stop, "Stop recording")

    let box' =
      Border()
        .Classes(Theme.name Theme.SurfaceBack, Theme.name Theme.Hairline)
        .BorderThickness(1.0)
        .CornerRadius(8.0)
        .Padding(8.0, 4.0, 8.0, 4.0)
        .Child(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(dot, wave, word, stop)
        )

    let discard =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(0.0)
        .Width(28.0)
        .Height(28.0)
        .Content(Icons.iconAt(Glyph.Trash, 16.0))
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          dispatch DiscardPressed)

    ToolTip.SetTip(discard, "Discard voice note (Esc)")

    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(6.0)
      .Children(discard, box')

/// The live recording lane: the Recorder program in a control. The
/// capture itself lives in the subscription's state — one cell the
/// meter reads and the stop drains, with Stop as its only teardown —
/// and the lane talks to its composer through the two reports.
type RecordLane(audio: IAudioEngine voption, report: Recorder.Report -> unit) as this
  =
  inherit UserControl()

  let mutable capture: IMicrophoneCapture option = None

  let release() =
    match capture with
    | Some held ->
      held.Cancel()
      capture <- None
    | None -> ()

  let update
    (report: Recorder.Report -> unit)
    (msg: Recorder.Msg)
    (model: Recorder.Model)
    : Recorder.Model * Cmd<Recorder.Msg> =

    match msg with
    | Recorder.Opened opened ->
      match opened, model.Phase with
      // A stop that landed while the microphone was still opening: the
      // take is off before it ever records, instead of resurrecting a
      // live meter the stopped reader must click away again.
      | Ok live, (Recorder.Done | Recorder.Stopping | Recorder.Encoding) ->
        live.Cancel()
        report Recorder.Cancel

        { model with Phase = Recorder.Done }, Cmd.none
      // The open may beat the lane's own first layout pass; the take
      // records anyway — the arms above and the detach's own release
      // cover every teardown path.
      | Ok live, _ ->
        capture <- Some live

        {
          model with
              Phase = Recorder.Recording DateTimeOffset.UtcNow
        },
        Cmd.none
      | Error failure, _ ->
        report(Recorder.Staged(Error(CaptureFailed failure)))

        { model with Phase = Recorder.Done }, Cmd.none
    | Recorder.Ticked(level, at) ->
      let levels = List.truncate 47 model.Levels @ [ level ]

      match model.Phase with
      | Recorder.Recording _ when at >= maxRecording ->
        { model with Levels = levels }, Cmd.ofMsg Recorder.StopPressed
      | _ -> { model with Levels = levels }, Cmd.none
    | Recorder.StopPressed ->
      match capture, model.Phase with
      | Some held, Recorder.Recording started ->
        let taken = DateTimeOffset.UtcNow - started

        capture <- None

        if taken < minRecording then
          held.Cancel()
          report Recorder.Cancel

          {
            model with
                Phase = Recorder.Done
                Levels = List.replicate 48 0.0
          },
          Cmd.none
        else
          let draining =
            Cmd.OfAsync.perform
              (fun (held: IMicrophoneCapture) -> held.Stop())
              held
              Recorder.TakeReady

          { model with Phase = Recorder.Encoding }, draining
      // The stop asked before the microphone finished opening: the
      // Opened answer cancels the take the moment it lands.
      | None, Recorder.Opening ->
        { model with Phase = Recorder.Stopping }, Cmd.none
      | _ -> { model with Phase = Recorder.Done }, Cmd.none
    | Recorder.DiscardPressed ->
      release()
      report Recorder.Cancel

      { model with Phase = Recorder.Done }, Cmd.none
    | Recorder.TakeReady taken ->
      let staged =
        taken |> Result.mapError CaptureFailed |> Result.bind Voices.stageVoice

      report(Recorder.Staged staged)

      { model with Phase = Recorder.Done }, Cmd.none
    | Recorder.Cancelled ->
      report Recorder.Cancel
      { model with Phase = Recorder.Done }, Cmd.none
    | Recorder.Detached ->
      release()
      { model with Phase = Recorder.Done }, Cmd.none

  let subscribe (host: Control) (model: Recorder.Model) : Sub<Recorder.Msg> =
    match model.Phase with
    | Recorder.Recording started ->
      Sub.batch [
        Sub.interval "meter" (TimeSpan.FromMilliseconds 50.0) (fun () ->
          Recorder.Ticked(
            capture |> Option.map _.Level() |> Option.defaultValue 0.0,
            DateTimeOffset.UtcNow - started
          ))

        Sub.fromObservable
          "detach"
          (host.DetachedFromVisualTree
           |> Observable.map(fun _ -> Recorder.Detached))
      ]
    | _ -> Sub.batch []

  do
    let program =
      Program.mkProgram (Recorder.init audio) (update report) Recorder.view
      |> Program.withSubscription(subscribe this)
      // The control's detach message ends the program: the meter
      // stops, the model completes, and an open microphone cancels
      // with it.
      |> Program.withTermination
        (function
        | Recorder.Detached -> true
        | _ -> false)
        (fun _ -> ())

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root

    // The detach itself releases the microphone: the lifetime's end
    // must not race the queued Detached message for the last word.
    this.DetachedFromVisualTree.Add(fun _ ->
      release()
      lifetime.Dispose())
