module DevNull.Client.Components.ThreadVoice

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Shapes
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Commit
open DevNull.Client.Core.Http
open DevNull.Client.Icons
open DevNull.Client.Natives
open DevNull.Client.SettingsStore
open Microsoft.Extensions.Logging

/// Only one note sounds at a time, the site's own rule: each claim
/// stops the playback that held the stage before it. One stage,
/// swapped whole under a lock — no fields to mutate, one record.
type Solo() =
  let gate = obj()
  let mutable stage: IAudioPlayback option = None

  member _.Claim(playback: IAudioPlayback) =
    lock gate (fun () ->
      match stage with
      | Some playing when not(obj.ReferenceEquals(playing, playback)) ->
        playing.Stop()
      | _ -> ()

      stage <- Some playback)

/// Where one player's note comes from: a received clip the signed
/// URL downloads, or the staged take the recorder already holds.
type VoiceSource =
  | Remote of clip: VoiceClip
  | Staged of media: byte array * waveform: Waveform

/// Everything the voice player reads: the engine the platform head
/// passed, the transport the bytes download through, the settings
/// the volume lives in, the one-note stage, the note itself, and the
/// logger of its failures.
[<Struct>]
type VoiceCtx = {
  Audio: IAudioEngine voption
  Http: IHttp
  Settings: AppSettingsStore
  Solo: Solo
  Log: ILogger
  Source: VoiceSource
}

/// The voice player's speeds, in the order the button cycles them.
let voiceSpeeds = [| 1.0; 1.5; 2.0 |]
let voiceSpeedLabels = [| "1×"; "1.5×"; "2×" |]

let waveWidth = 132.0
let waveHeight = 24.0

let barHeight(value: int) =
  max (waveHeight * 0.1) (float value / 100.0 * waveHeight)

/// How long one source's note runs, in the note's own milliseconds.
let durationOf(source: VoiceSource) =
  match source with
  | Remote clip -> clip.DurationMs
  | Staged(media, _) -> (media.Length - 44) * 1000 / 32000

/// The clock word one position reads as, `m:ss`.
let clockWord (durationMs: int) (position: TimeSpan) =
  let playedMs = min durationMs (int position.TotalMilliseconds) |> max 0

  let minutes = playedMs / 60000
  let seconds = (playedMs % 60000) / 1000

  $"{minutes}:{seconds:D2}"

/// The played share of one note, 0 to 1.
let playedFraction (durationMs: int) (position: TimeSpan) =
  let playedMs = min durationMs (int position.TotalMilliseconds) |> max 0

  min 1.0 (float playedMs / float(max 1 durationMs))

/// The voice player's own MVU program: the phase, the bytes, the
/// position, and the volume ride the model; the engine and the
/// transport ride commands; the end, the clock, and the control's
/// own detach ride subscriptions.
module Player =

  /// Where one play stands: resting, waiting for the bytes or the
  /// engine while the button says pause (carrying where a restart
  /// picks the note back up), sounding, or failed.
  type Phase =
    | Idle
    | Wanting of resume: TimeSpan option
    | Sounding of playback: IAudioPlayback
    | Failed

  type Model = {
    Phase: Phase
    SpeedAt: int
    Media: byte array option
    Position: TimeSpan
    Volume: int
  }

  type Msg =
    | TogglePressed
    | SpeedPressed
    | VolumeMoved of percent: int
    | BytesArrived of Result<byte array, TransportFailure>
    | Sounded of Result<IAudioPlayback, AudioFailure>
    | WentIdle
    | Ticked
    | Scrubbed of at: TimeSpan
    | Detached

  let init (voice: VoiceCtx) () : Model * Cmd<Msg> =
    {
      Phase = Idle
      SpeedAt = 0
      Media =
        (match voice.Source with
         | Staged(bytes, _) -> Some bytes
         | Remote _ -> None)
      Position = TimeSpan.Zero
      Volume = int(round(voice.Settings.Current.VoiceVolume * 100.0))
    },
    Cmd.none

  let stop(model: Model) =
    match model.Phase with
    | Sounding playback -> playback.Stop()
    | _ -> ()

  /// Asks the engine to sound whatever bytes the model holds, at
  /// the model's own speed and volume.
  let ask (voice: VoiceCtx) (model: Model) : Cmd<Msg> =
    match voice.Audio, model.Media with
    | ValueSome head, Some bytes ->
      Cmd.OfAsync.perform
        (fun
             (engine: IAudioEngine,
              played: byte array,
              speed: float,
              loudness: float) -> engine.Play(played, speed, loudness))
        (head, bytes, voiceSpeeds[model.SpeedAt], float model.Volume / 100.0)
        Sounded
    | ValueSome _, None ->
      Cmd.OfAsync.perform
        (fun (http: IHttp, url: string) -> http.GetBytes(url))
        (voice.Http,
         (match voice.Source with
          | Remote clip -> clip.Url
          | Staged _ -> ""))
        BytesArrived
    | ValueNone, _ -> Cmd.none

  let update (voice: VoiceCtx) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    let durationMs = durationOf voice.Source

    match msg with
    | TogglePressed ->
      match model.Phase with
      | Idle
      | Failed ->
        if voice.Audio.IsNone then
          { model with Phase = Failed }, Cmd.none
        else
          // A pause picks the note back up where it stood; a note
          // that played through starts over.
          let through =
            model.Position >= TimeSpan.FromMilliseconds(float durationMs)

          let resume =
            if model.Position > TimeSpan.Zero && not through then
              Some model.Position
            else
              None

          { model with Phase = Wanting resume }, ask voice model
      | Wanting _ -> { model with Phase = Idle }, Cmd.none
      | Sounding _ ->
        stop model
        { model with Phase = Idle }, Cmd.none
    | SpeedPressed ->
      let next = {
        model with
            SpeedAt = (model.SpeedAt + 1) % voiceSpeeds.Length
      }

      stop model

      match model.Phase with
      | Sounding _
      | Wanting _ ->
        {
          next with
              Phase = Wanting(Some model.Position)
        },
        ask voice next
      | _ -> next, Cmd.none
    | VolumeMoved percent ->
      voice.Settings.SetVoiceVolume(float percent / 100.0)

      // The fader lands on the sounding note itself, not only on the
      // next one.
      (match model.Phase with
       | Sounding playback -> playback.SetVolume(float percent / 100.0)
       | _ -> ())

      { model with Volume = percent }, Cmd.none
    | BytesArrived arrived ->
      match arrived, model.Phase with
      | Ok bytes, Wanting _ ->
        let next = { model with Media = Some bytes }

        next,
        Cmd.batch [
          ask voice next

          Cmd.ofAction(fun () ->
            voice.Log.LogDebug(
              "The voice note's bytes arrived ({Bytes} bytes).",
              bytes.Length
            ))
        ]
      | Ok bytes, _ -> { model with Media = Some bytes }, Cmd.none
      | Error failure, _ ->
        { model with Phase = Failed },
        Cmd.ofAction(fun () ->
          voice.Log.LogWarning(
            "The voice note's bytes did not download: {Error}",
            failure.Message.Trim()
          ))
    | Sounded sounded ->
      match sounded, model.Phase with
      | Ok playback, Wanting resume ->
        stop model
        voice.Solo.Claim(playback)

        resume
        |> Option.iter(fun at ->
          if at > TimeSpan.Zero then
            playback.Seek at)

        {
          model with
              Phase = Sounding playback
              Position =
                match resume with
                | Some at when at > TimeSpan.Zero -> at
                | _ -> playback.Position
        },
        Cmd.ofAction(fun () -> voice.Log.LogDebug("The voice note sounds."))
      | Ok _, _ -> model, Cmd.none
      | Error failure, _ ->
        { model with Phase = Failed },
        Cmd.ofAction(fun () ->
          voice.Log.LogWarning(
            "The voice note did not sound: {Why}",
            match failure with
            | AudioFailed message -> message
            | NoDevice -> "no playback device"
            | Denied -> "the playback device is not allowed"
            | Busy -> "the playback device is busy"
          ))
    | WentIdle ->
      stop model

      {
        model with
            Phase = Idle
            Position = TimeSpan.FromMilliseconds(float durationMs)
      },
      Cmd.none
    | Ticked ->
      match model.Phase with
      | Sounding playback ->
        {
          model with
              Position = playback.Position
        },
        Cmd.none
      | _ -> model, Cmd.none
    | Scrubbed at ->
      match model.Phase with
      | Sounding playback -> playback.Seek(at)
      | _ -> ()

      { model with Position = at }, Cmd.none
    | Detached ->
      stop model
      model, Cmd.none

  /// The inlets: the sounding pipeline's own end and clock, and the
  /// control's detach, which stops the note and ends the program.
  let subscribe (host: Control) (model: Model) : Sub<Msg> =
    let riding =
      match model.Phase with
      | Sounding playback -> [
          Sub.fromObservable
            "ended"
            (playback.Ended |> Observable.map(fun () -> WentIdle))

          Sub.interval "tick" (TimeSpan.FromMilliseconds 100.0) (fun () ->
            Ticked)
        ]
      | _ -> []

    Sub.batch(
      riding
      @ [
        Sub.fromObservable
          "detach"
          (host.DetachedFromVisualTree |> Observable.map(fun _ -> Detached))
      ]
    )

/// One played voice note, the site's own chrome: an icon play
/// square, the two-layer waveform that scrubs under the pointer, a
/// clock that counts the note's own time, then the volume and the
/// speed cycle. An MVU control of its own: the view builds once and
/// binds only its model stream, and the program ends when the
/// control detaches.
type VoicePlayer(voice: VoiceCtx) as this =
  inherit UserControl()

  let durationMs = durationOf voice.Source

  let view
    (model: IObservable<Player.Model>)
    (dispatch: Dispatch<Player.Msg>)
    : Control =
    let playIcon(phase: Player.Phase) =
      match phase with
      | Player.Sounding _ -> Glyph.Pause
      | Player.Failed -> Glyph.Refresh
      | _ -> Glyph.Play

    let playTip(phase: Player.Phase) =
      match phase with
      | Player.Sounding _ -> "Pause the note"
      | Player.Failed -> "Playback failed"
      | _ -> "Play the note"

    let phase =
      model |> Observable.map _.Phase |> Observable.distinctUntilChanged

    let position = model |> Observable.map _.Position

    let volume =
      model |> Observable.map _.Volume |> Observable.distinctUntilChanged

    let speedAt =
      model |> Observable.map _.SpeedAt |> Observable.distinctUntilChanged

    let playButton =
      Button()
        .Name("VoicePlay")
        .Classes(Theme.name Theme.GhostButton)
        .Padding(0.0)
        .Width(28.0)
        .Height(28.0)
        .HorizontalContentAlignment(HorizontalAlignment.Center)
        .VerticalContentAlignment(VerticalAlignment.Center)
        .Content(
          phase
          |> Observable.map(fun current -> Icons.iconAt(playIcon current, 14.0))
          |> _.ToBinding()
        )
        .Tip(phase |> Observable.map(playTip >> box))
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          dispatch Player.TogglePressed)

    // Two layers of the same bars: the back one dimmed, the front
    // one clipped to the played width — the site's fill, moved by
    // the model's own position.
    let bars(fill: float) : Control array = [|
      let values =
        match voice.Source with
        | Remote clip -> clip.Waveform
        | Staged(_, bars') -> DevNull.Client.Core.Commit.Voices.bars bars'

      for value in values ->
        Rectangle()
          .Classes(Theme.name Theme.FgAccent)
          .Height(barHeight value)
          .Width(1.5)
          .Opacity(fill)
          .VerticalAlignment(VerticalAlignment.Bottom)
    |]

    let backBars =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(1.0)
        .Height(waveHeight)
        .Width(waveWidth)
        .VerticalAlignment(VerticalAlignment.Center)
        .Children(bars 0.35)

    let frontBars =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(1.0)
        .Height(waveHeight)
        .Width(waveWidth)
        .VerticalAlignment(VerticalAlignment.Center)
        .Children(bars 1.0)

    let frontClip =
      Border()
        .ClipToBounds(true)
        .Width(
          position
          |> Observable.map(
            playedFraction durationMs >> fun share -> share * waveWidth
          )
        )
        .HorizontalAlignment(HorizontalAlignment.Left)
        .VerticalAlignment(VerticalAlignment.Stretch)
        .Child(frontBars)

    let wave =
      Grid()
        .Width(waveWidth)
        .Height(waveHeight)
        .VerticalAlignment(VerticalAlignment.Center)
        .Name("VoiceWave")
        .Children(backBars, frontClip)

    // A press or a drag on the waveform scrubs the note to the
    // point under the pointer.
    let scrubAt(point: Point) =
      let played = min 1.0 (max 0.0 (point.X / waveWidth))

      dispatch(
        Player.Scrubbed(TimeSpan.FromMilliseconds(played * float durationMs))
      )

    wave.PointerPressed.Add(fun args ->
      args.Handled <- true
      args.Pointer.Capture(wave)
      scrubAt(args.GetPosition wave))

    wave.PointerMoved.Add(fun args ->
      if args.Pointer.Captured = wave then
        scrubAt(args.GetPosition wave))

    wave.PointerReleased.Add(fun args ->
      if args.Pointer.Captured = wave then
        args.Pointer.Capture(null))

    let clock =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .FontSize(11.0)
        .MinWidth(32.0)
        .TextAlignment(TextAlignment.Right)
        .VerticalAlignment(VerticalAlignment.Center)
        .Text(position |> Observable.map(clockWord durationMs))

    // The device-wide volume the site keeps: a fader in a quiet
    // flyout, quantized to whole percents, saved as it moves.
    let volumeFlyout = Popup()

    let volumeWord =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .FontSize(11.0)
        .Text(volume |> Observable.map(fun percent -> string percent + "%"))

    let fader =
      Slider(Minimum = 0.0, Maximum = 100.0, Width = 120.0)
        .Name("VoiceVolume")
        .Value(volume |> Observable.map float)

    fader.ValueChanged.Add(fun args ->
      dispatch(Player.VolumeMoved(int(round args.NewValue))))

    volumeFlyout.Child <-
      Border()
        .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
        .BorderThickness(1.0)
        .CornerRadius(6.0)
        .Padding(8.0)
        .Child(StackPanel().Spacing(4.0).Children(fader, volumeWord))

    let volumeButton =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(4.0, 2.0, 4.0, 2.0)
        .Content(Icons.iconAt(Glyph.Speaker, 14.0))
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          volumeFlyout.IsOpen <- not volumeFlyout.IsOpen)

    ToolTip.SetTip(volumeButton, "Voice volume")

    // The speed cycle: dimmed at the natural pace, the site's own
    // quiet word.
    let speedButton =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(4.0, 2.0, 4.0, 2.0)
        .FontSize(11.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Content(
          speedAt
          |> Observable.map(fun at -> voiceSpeedLabels[at])
          |> _.ToBinding()
        )
        .Opacity(
          speedAt |> Observable.map(fun at -> if at = 0 then 0.6 else 1.0)
        )
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          dispatch Player.SpeedPressed)

    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(6.0)
      .Children(
        Border()
          .Classes(Theme.name Theme.SurfaceBack)
          .CornerRadius(6.0)
          .Width(28.0)
          .Height(28.0)
          .VerticalAlignment(VerticalAlignment.Center)
          .Child(playButton),
        wave,
        clock,
        Panel().Children(volumeButton, volumeFlyout),
        speedButton
      )

  do
    let program =
      Program.mkProgram (Player.init voice) (Player.update voice) view
      |> Program.withSubscription(Player.subscribe this)
      // The control's detach message ends the program: the
      // subscriptions stop, the model completes, and whatever was
      // sounding stops with it. Nothing disposes the lifetime from
      // the detach event itself — a dispose there would kill the
      // loop before the Detached message (and its stop) could run.
      // The detach subscription is unconditional, so the message
      // always arrives.
      |> Program.withTermination
        (function
        | Player.Detached -> true
        | _ -> false)
        Player.stop

    let root, _ = Program.runWithLifecycle () program

    this.Content <- root

/// One message's voice note: the waveform player.
let voiceChip(voice: VoiceCtx) : Control = VoicePlayer(voice) :> Control
