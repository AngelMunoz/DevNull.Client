namespace DevNull.Client.Views

open System
open System.IO
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Shapes
open Avalonia.Controls.Templates
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Threading
open Avalonia.VisualTree
open NXUI.Extensions
open FSharp.Control.Reactive
open FSharp.UMX
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open IcedTasks
open Navs
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types
open DevNull.Client.Errors
open DevNull.Client.Env
open Microsoft.Extensions.Logging
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.UiClock

/// The open thread: the detail column's reading pane. The pane itself
/// rebuilds only when the checked-out target moves; everything inside
/// follows the model stream, so a poll refresh never rebuilds a row.
module Thread =

  /// Only one note sounds at a time, the site's own rule: each claim
  /// stops the playback that held the stage before it. One stage,
  /// swapped whole under a lock — no fields to mutate, one record.
  type Solo() =
    let gate = obj()
    let stage: IAudioPlayback option ref = ref None

    member _.Claim(playback: IAudioPlayback) =
      lock gate (fun () ->
        match !stage with
        | Some playing when not(obj.ReferenceEquals(playing, playback)) ->
          playing.Stop()
        | _ -> ()

        stage := Some playback)

  /// Where the thread list's scroll stands, swapped whole on each
  /// scroll: whether the reader rides the bottom edge, the extent the
  /// last scroll saw, and where a remembered place has landed.
  type ScrollState(target: float) =
    let place: Ref<ScrollPlace> =
      ref {
        Riding = true
        Extent = 0.0
        Target = target
        Landed = target <= 0.0
        Asked = false
      }

    member _.Current = !place

    member _.Move(next: ScrollPlace) = place := next

  and ScrollPlace = {
    Riding: bool
    Extent: float
    Target: float
    Landed: bool
    Asked: bool
  }

  /// Everything one row of the thread reads: the dispatch, the model,
  /// the image cache, the viewer, and whether the thread is an org's.
  type RowCtx = {
    Dispatch: Dispatch<State.Msg>
    Model: IObservable<State.Model>
    Images: ImageCacheService
    ViewerId: string
    IsOrg: bool
    Nav: INavigable
    Audio: IAudioEngine voption
    Http: IHttp
    Settings: AppSettingsStore
    /// The one note that may sound at a time.
    Solo: Solo
    Log: ILogger
    /// How wide the reading pane stands, so bubbles take their
    /// fraction of it the way the site caps theirs.
    PaneWidth: IObservable<float>
  }

  /// One author's name as the receipt and ghost lines know it.
  let authorName(ctx: RowCtx, who: User option, authorId: string) : string =
    match who with
    | Some user -> user.Name
    | None -> if authorId = ctx.ViewerId then "You" else "Someone"

  /// The reply ghost: the message a bubble answers, peeking out above
  /// it. A click walks the thread to the original.
  let ghost
    (ctx: RowCtx, reply: MessageParent, mine: bool, author: string)
    : Control =
    let snippet =
      let body = reply.Body.Replace("\r", " ").Replace("\n", " ")

      // The site's snippet: links fall away, so the words carry it.
      let words =
        body.Split(' ')
        |> Array.filter(fun word ->
          not(word.StartsWith "http://" || word.StartsWith "https://"))

      let trimmed = String.concat " " words |> _.Trim()

      if trimmed <> "" then
        trimmed
      elif reply.HasVoice then
        "voice note"
      elif reply.ImagesCount > 1 then
        $"{reply.ImagesCount} images"
      else
        "image"

    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(8.0, 3.0, 8.0, 3.0)
      .FontSize(12.0)
      .Opacity(0.9)
      .HorizontalAlignment(
        if mine then
          HorizontalAlignment.Right
        else
          HorizontalAlignment.Left
      )
      .Content(
        StackPanel()
          .Spacing(1.0)
          .Children(
            TextBlock()
              .Classes(Theme.name Theme.Mono)
              .FontSize(11.0)
              .Text(author),
            TextBlock()
              .Classes(Theme.name Theme.Muted)
              .FontSize(12.0)
              .Text(snippet)
              .TextTrimming(TextTrimming.CharacterEllipsis)
              .MaxWidth(260.0)
          )
      )
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        ctx.Dispatch(State.GhostWanted reply.Id))

  /// The images of one message: fixed boxes on the signed URLs, the
  /// click opening the gallery.
  let images(ctx: RowCtx, who: Message) : Control voption =
    if who.Images.IsEmpty then
      ValueNone
    else
      let boxes: Control array = [|
        for image in who.Images ->
          AsyncImage
            .view(
              {
                Cache = ctx.Images
                Url = image.Url
                DecodeWidth = 512
                DecodeHeight = ValueSome 320
                Stretch = Stretch.UniformToFill
                Placeholder = ValueNone
                ToolTip = image.Description |> Option.toValueOption
              }
            )
            .Width(232.0)
            .Height(150.0)
      |]

      let gallery = WrapPanel().Children(boxes)

      let open' =
        Button()
          .Classes(Theme.name Theme.CommitRow)
          .Padding(0.0)
          .CornerRadius(10.0)
          .Content(gallery)
          .OnClickHandler(fun sender _ ->
            match TopLevel.GetTopLevel sender with
            | :? Window as owner ->
              ImageViewer.show {
                Owner = owner
                Cache = ctx.Images
                Photos = who.Images
                Start = 0
              }
            | _ -> ())

      ValueSome open'

  /// One message's link card, live from the thread: the card once the
  /// server read it, the quiet word while it reads.
  let linkCard
    (ctx: RowCtx, key: string, messageId: int, mine: bool)
    : Control voption =
    let cardOf(preview: LinkPreview) : Control =
      if preview.IsPending then
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(12.0)
          .Margin(0.0, 4.0, 0.0, 0.0)
          .Text("reading the link…")
      else
        Border()
          .CornerRadius(10.0)
          .BorderThickness(1.0)
          .Classes(Theme.name Theme.Hairline)
          .Padding(10.0, 8.0, 10.0, 8.0)
          .Margin(0.0, 6.0, 0.0, 0.0)
          .MaxWidth(280.0)
          .Child(
            StackPanel()
              .Spacing(2.0)
              .Children(
                TextBlock()
                  .Classes(
                    if mine then
                      Theme.name Theme.FgOnAccent
                    else
                      Theme.name Theme.Subtitle
                  )
                  .Text(preview.Title |> Option.defaultValue preview.Url)
                  .TextWrapping(TextWrapping.Wrap),
                TextBlock()
                  .Classes(
                    if mine then
                      Theme.name Theme.FgOnAccent
                    else
                      Theme.name Theme.Mono
                  )
                  .FontSize(11.0)
                  .Text(preview.Url)
                  .TextTrimming(TextTrimming.CharacterEllipsis)
              )
          )

    let card =
      ctx.Model
      |> Observable.map(fun current ->
        current.State.Threads
        |> Map.tryFind key
        |> Option.bind(fun thread ->
          thread.Messages
          |> List.tryFind(fun message -> message.Id = messageId)
          |> Option.bind _.LinkPreview))
      |> Observable.distinctUntilChanged

    let host = ContentControl()

    host.Content(
      card
      |> Observable.map(function
        | Some preview -> cardOf preview
        | None -> null)
      |> _.ToBinding()
    )
    |> ignore

    host.IsVisible(card |> Observable.map Option.isSome) |> ignore

    ValueSome host

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
                  (match resume with
                   | Some at when at > TimeSpan.Zero -> at
                   | _ -> playback.Position)
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
        (match model.Phase with
         | Sounding playback -> playback.Seek(at)
         | _ -> ())

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
            |> Observable.map(fun current ->
              Icons.iconAt(playIcon current, 14.0))
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
        for value in
          (match voice.Source with
           | Remote clip -> clip.Waveform
           | Staged(_, bars') -> DevNull.Client.Core.Commit.Voices.bars bars') ->
          Rectangle()
            .Classes(Theme.name Theme.FgAccent)
            .Height(barHeight value)
            .Width(1.5)
            .Opacity(fill)
            .VerticalAlignment(VerticalAlignment.Bottom)
          :> Control
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

  /// The word and shape one message's meta line carries: `pushed`
  /// until the other side pulled, then `pulled` with its own clock
  /// time in a direct thread, or the org's pull count.
  let receiptWord
    (isOrg: bool, thread: ThreadState, message: Message)
    : (bool * string) option =
    if isOrg then
      let others =
        thread.Members
        |> List.filter(fun member' -> member'.User.Id <> message.UserId)

      let pulled =
        others
        |> List.filter(fun member' -> member'.ReadThroughId >= message.Id)

      if List.isEmpty others then
        None
      elif
        List.length pulled = List.length others && List.length others > 3
      then
        Some(true, "pulled by everyone")
      else
        Some(true, $"pulled by {List.length pulled} of {List.length others}")
    elif
      thread.PartnerReadThroughId >= message.Id
      && thread.PartnerReadThroughId > 0
    then
      let pulledAt = message.CreatedAt.LocalDateTime.ToString("h:mm tt")

      Some(true, $"pulled {pulledAt}")
    else
      Some(false, "pushed")

  /// The receipts under one message, the site's own meta line: the
  /// status glyph — a filled dot once pushed, two dots joined by a
  /// rail once pulled — beside the word. Live from the thread,
  /// because the read position moves under a built row.
  let receipts
    (ctx: RowCtx, key: string, message: Message, mine: bool)
    : Control voption =
    if not mine then
      ValueNone
    else
      let partnerLane = message.User |> Option.bind _.Lane

      // The glyph: my filled dot while only pushed; once pulled, my
      // dot joined by the rail to the partner's lane-colored one.
      let glyph(pulled: bool) : Control =
        let mine = Ellipse().Classes(Theme.name Theme.AccentDot)

        mine.Width <- 8.0
        mine.Height <- 8.0

        if pulled then
          let theirs = Ellipse()

          theirs.Width <- 8.0
          theirs.Height <- 8.0
          Theme.laneFill(theirs, partnerLane)

          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(3.0)
            .VerticalAlignment(VerticalAlignment.Center)
            .Children(
              mine,
              Border()
                .Classes(Theme.name Theme.Hairline)
                .Width(8.0)
                .Height(1.5)
                .VerticalAlignment(VerticalAlignment.Center),
              theirs
            )
        else
          mine

      let line =
        ctx.Model
        |> Observable.map(fun current ->
          current.State.Threads
          |> Map.tryFind key
          |> Option.bind(fun thread -> receiptWord(ctx.IsOrg, thread, message)))
        |> Observable.distinctUntilChanged

      let host = ContentControl()

      host.Content(
        line
        |> Observable.map(function
          | Some(pulled, word) ->
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(5.0)
              .VerticalAlignment(VerticalAlignment.Center)
              .Children(
                glyph pulled,
                TextBlock()
                  .Classes(Theme.name Theme.Mono)
                  .FontSize(11.0)
                  .Text(word)
              )
            :> Control
          | None -> null)
        |> _.ToBinding()
      )
      |> ignore

      host.Margin <- Thickness(4.0, 2.0, 4.0, 0.0)
      host.HorizontalAlignment <- HorizontalAlignment.Right

      host.IsVisible(line |> Observable.map Option.isSome) |> ignore

      ValueSome host

  /// The who-reacted line: the viewer, the partner, a member the
  /// thread carries — each as their handle — or someone the payload
  /// did not name.
  let whoNames
    (thread: ThreadState option, viewerId: string, who: string list)
    : string =
    who
    |> List.map(fun userId ->
      if userId = viewerId then
        "you"
      else
        let named =
          match thread with
          | Some thread ->
            match thread.Target with
            | Partner user when user.Id = userId -> Some("@" + user.Handle)
            | _ ->
              thread.Members
              |> List.tryFind(fun member' -> member'.User.Id = userId)
              |> Option.map(fun member' -> "@" + member'.User.Handle)
          | None -> None

        named |> Option.defaultValue "someone")
    |> String.concat ", "

  /// One reaction chip: the emoji and its count in the site's own
  /// shape — no button chrome, just the bordered count a pointer
  /// press toggles. Worn on the mine wash when the viewer's reaction
  /// is among them; the tip names who left it.
  let reactionChip
    (ctx: RowCtx, messageId: int, summary: State.ReactionSummary, who: string)
    : Control =
    let chip =
      Border()
        .Classes(
          Theme.name(
            if summary.IsMine then
              Theme.ReactionChipMine
            else
              Theme.ReactionChip
          )
        )
        .Child(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(4.0)
            .Children(
              TextBlock().Text(summary.Emoji),
              TextBlock()
                .Classes(Theme.name Theme.Mono)
                .FontSize(11.0)
                .Text(string summary.Count)
            )
        )

    ToolTip.SetTip(chip, who)

    chip.PointerPressed.Add(fun args ->
      args.Handled <- true
      ctx.Dispatch(State.ReactWanted(messageId, summary.Emoji)))

    chip :> Control

  /// The chips row under one message: one chip per emoji the message
  /// carries, live from the thread. The message carries none until it
  /// does, and the row hides then.
  let reactionChips(ctx: RowCtx, key: string, messageId: int) : Control =
    let summaries =
      ctx.Model
      |> Observable.map(fun current ->
        let thread = current.State.Threads |> Map.tryFind key

        let chips =
          thread
          |> Option.bind(fun thread ->
            thread.Messages
            |> List.tryFind(fun message -> message.Id = messageId)
            |> Option.map(fun message ->
              State.summarizeReactions(message.Reactions, ctx.ViewerId)))

        (chips |> Option.defaultValue [], thread))
      |> Observable.distinctUntilChanged

    let host = ContentControl()

    host.Content(
      summaries
      |> Observable.map(fun (rows, thread) ->
        let chips: Control array = [|
          for summary in rows ->
            reactionChip(
              ctx,
              messageId,
              summary,
              whoNames(thread, ctx.ViewerId, summary.Who)
            )
        |]

        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(4.0)
          .Margin(2.0, 2.0, 0.0, 0.0)
          .Children(chips))
      |> _.ToBinding()
    )
    |> ignore

    host.IsVisible(
      summaries |> Observable.map(fun (rows, _) -> not rows.IsEmpty)
    )
    |> ignore

    host

  /// The reaction tray the hover react button opens: the quick picks
  /// on top, the ranked rest under a divider — the site's toolbar
  /// shape, one flyout, no nested picker. A pick closes it, the
  /// site's own tray rule.
  let reactionTray(ctx: RowCtx, messageId: int) : Popup =
    let tray = Popup()

    let ranked =
      ctx.Model
      |> Observable.map(fun current ->
        let usage = current.State.ReactionUsage |> Option.defaultValue []

        let quick = State.quickReactions usage

        quick,
        quick @ (usage |> State.frequentReactions |> List.except quick)
        |> List.distinct)
      |> Observable.distinctUntilChanged

    let host = ContentControl()

    host.Content(
      ranked
      |> Observable.map(fun (quick, all) ->
        let pick(size: float, mark: string) : Control =
          Button()
            .Classes(Theme.name Theme.GhostButton)
            .Padding(4.0)
            .FontSize(size)
            .Content(mark)
            .OnClickHandler(fun _ args ->
              args.Handled <- true
              tray.IsOpen <- false
              ctx.Dispatch(State.ReactWanted(messageId, mark)))
          :> Control

        let quickRow: Control array = [|
          for mark in quick -> pick(17.0, mark)
        |]

        let rest: Control array = [|
          for mark in List.skip (List.length quick) all -> pick(15.0, mark)
        |]

        StackPanel()
          .Spacing(4.0)
          .Children(
            WrapPanel().Children(quickRow),
            Border()
              .Classes(Theme.name Theme.Hairline)
              .Height(1.0)
              .HorizontalAlignment(HorizontalAlignment.Stretch),
            WrapPanel().MaxWidth(220.0).Children(rest)
          ))
      |> _.ToBinding()
    )
    |> ignore

    tray.Child <-
      Border()
        .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
        .BorderThickness(1.0)
        .CornerRadius(8.0)
        .Padding(6.0)
        .Child(host)

    tray

  /// Everything one bubble's build reads: the row context, the run's
  /// key, the message, whose side it sits on, and whether the receipts
  /// line stands under it.
  [<Struct>]
  type BubbleAsk = {
    Ctx: RowCtx
    Key: string
    Message: Message
    Mine: bool
    ShowsStatus: IObservable<bool>
  }

  /// The hover-revealed actions beside one bubble: reply and react,
  /// 28px icons outside the bubble edge, vertically centered. They
  /// take no room in the thread; the pointer over the message row is
  /// what shows them, the site's group-hover rule.
  let hoverActions(ask: BubbleAsk, tray: Popup) : Control =
    let react =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(0.0)
        .Width(28.0)
        .Height(28.0)
        .Content(Icons.iconAt(Glyph.Reaction, 16.0))
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          tray.IsOpen <- not tray.IsOpen)

    ToolTip.SetTip(react, "Add reaction")

    let reply =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(0.0)
        .Width(28.0)
        .Height(28.0)
        .Content(Icons.iconAt(Glyph.Reply, 16.0))
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          ask.Ctx.Dispatch(State.ReplyWanted ask.Message))

    ToolTip.SetTip(reply, "Reply")

    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(2.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Classes(Theme.name Theme.HoverActions)
      .Children(reply, react)
      .Name(
        (if ask.Mine then
           "BubbleActionsMine"
         else
           "BubbleActionsTheirs")
      )

  /// One bubble: the message body in a rounded fill, mine on the
  /// accent, theirs on the card surface. A voice note rides in a box
  /// of its own, the way the site gives it one, and the flash target
  /// is whichever box stands alone at the message's edge.
  let bubble(ask: BubbleAsk) : Control =
    let {
          Ctx = ctx
          Key = key
          Message = who
          Mine = mine
          ShowsStatus = showsStatus
        } =
      ask

    let body =
      CommitCard.body(
        ValueNone,
        who.Body,
        who.BodySegments,
        who.Mentions,
        (if mine then CommitCard.OnFill else CommitCard.Accent)
      )

    let tray = reactionTray(ctx, who.Id)

    let shown = images(ctx, who)
    let card = linkCard(ctx, key, who.Id, mine)

    let voiceBox =
      who.Voice
      |> Option.map(fun clip ->
        Border()
          .Classes(
            Theme.name(if mine then Theme.AccentBack else Theme.SurfaceBack)
          )
          .CornerRadius(12.0)
          .Padding(8.0)
          .Margin(0.0, 4.0, 0.0, 0.0)
          .Child(
            voiceChip(
              {
                Audio = ctx.Audio
                Http = ctx.Http
                Settings = ctx.Settings
                Solo = ctx.Solo
                Log = ctx.Log
                Source = Remote clip
              }
            )
          ))

    // The card host binds to the model stream and is always built, so
    // the body box's gate reads the message itself: a voice-only note
    // carries nothing beside the note, and an empty body box would
    // render as a bare padding pill above the player.
    let carriesMore = who.Body <> "" || shown.IsSome || who.LinkPreview.IsSome

    let bodyBox =
      Border()
        .Name(
          (if carriesMore || voiceBox.IsNone then
             $"ChatMessage{who.Id}"
           else
             "ChatMessageBody")
        )
        .Classes(
          Theme.name(if mine then Theme.AccentBack else Theme.SurfaceBack)
        )
        .CornerRadius(12.0)
        .Padding(10.0, 6.0, 10.0, 6.0)
        .MaxWidth(ctx.PaneWidth |> Observable.map(fun w -> w * 0.85))
        .Child(
          StackPanel()
            .Spacing(4.0)
            .Children(
              [|
                if who.Body <> "" then
                  yield body

                match shown with
                | ValueSome gallery -> yield gallery
                | ValueNone -> ()

                match card with
                | ValueSome preview -> yield preview
                | ValueNone -> ()
              |]
            )
        )

    (match voiceBox with
     | Some box when not carriesMore ->
       box.Name($"ChatMessage{who.Id}") |> ignore
     | _ -> ())

    // The double-click heart: the site's double-tap, on the desktop's
    // own pair of clicks.
    let heart(args: RoutedEventArgs) =
      args.Handled <- true
      ctx.Dispatch(State.ReactWanted(who.Id, "❤️"))

    let actions = hoverActions(ask, tray)

    let column =
      StackPanel().Spacing(2.0).Children [|
        match who.ReplyTo with
        | Some reply ->
          let author =
            authorName(
              ctx,
              (match who.User with
               | Some user when reply.UserId <> ctx.ViewerId -> Some user
               | _ -> None),
              reply.UserId
            )

          yield ghost(ctx, reply, mine, author)
        | None -> ()

        if carriesMore || voiceBox.IsNone then
          yield bodyBox

        match voiceBox with
        | Some box -> yield box
        | None -> ()

        yield
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(4.0)
            .Margin(2.0, 2.0, 0.0, 0.0)
            .Children(reactionChips(ctx, key, who.Id))
      |]

    column.DoubleTapped.Add heart

    // The row's whole lane must hold the pointer: a backgroundless
    // panel passes the samples between the action buttons through to
    // the page behind, and the pointer-over state drops mid-crossing
    // before the far icon is reached.
    let row =
      DockPanel()
        .Classes(Theme.name Theme.MsgRow)
        .Background(Brushes.Transparent)
        .LastChildFill(true)
        .HorizontalAlignment(
          if mine then
            HorizontalAlignment.Right
          else
            HorizontalAlignment.Left
        )

    // The actions hang outside the bubble edge: on the far side for
    // mine, on the near side for theirs, the way the site floats
    // them off the bubble. The HoverActions style carries the
    // hide-and-show on the row's own pointer-over state.
    (if mine then
       actions.Dock(Dock.Left)
     else
       actions.Dock(Dock.Right))
    |> ignore

    column.Dock(Dock.Left) |> ignore
    row.Children(actions, column) |> ignore
    row.Children.Add(tray)

    match receipts(ctx, key, who, mine) with
    | ValueSome line ->
      line.IsVisible(showsStatus) |> ignore

      StackPanel()
        .Spacing(2.0)
        .HorizontalAlignment(
          if mine then
            HorizontalAlignment.Right
          else
            HorizontalAlignment.Left
        )
        .Children(row, line)
      :> Control
    | ValueNone -> row :> Control

  /// One staged bubble: the message on its way in, or one the server
  /// refused — refused once more with its retry. The staged shape is
  /// hollow with a dashed accent edge, the refused one a solid danger
  /// edge.
  let stagedBubble(ctx: RowCtx, pending: PendingSend) : Control =
    let children: Control array = [|
      match pending.Message.Body with
      | Some text ->
        yield CommitCard.body(ValueNone, text, [], [], CommitCard.Accent)
      | None -> ()

      yield
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            TextBlock()
              .Classes(
                Theme.name(
                  if pending.Rejected then Theme.FgDanger else Theme.Muted
                )
              )
              .FontSize(11.0)
              .Text((if pending.Rejected then "rejected · retry" else "staged")),
            (if pending.Rejected then
               Button()
                 .Classes(Theme.name Theme.GhostButton)
                 .Padding(4.0, 0.0, 4.0, 2.0)
                 .FontSize(11.0)
                 .Content("retry")
                 .OnClickHandler(fun _ args ->
                   args.Handled <- true
                   ctx.Dispatch(State.RetryWanted pending.ClientId))
               :> Control
             else
               TextBlock() :> Control)
          )
    |]

    let inner =
      StackPanel().Spacing(4.0).Margin(10.0, 6.0, 10.0, 6.0).Children(children)

    if pending.Rejected then
      Border()
        .Classes(Theme.name Theme.RejectedBubble)
        .CornerRadius(12.0)
        .Padding(0.0)
        .Child(inner)
        .HorizontalAlignment(HorizontalAlignment.Right)
    else
      // The dashed edge the site draws around a message on its way
      // in: a rectangle under the content, the one dashed line the
      // chat carries.
      let edge = Rectangle().Classes(Theme.name Theme.StagedEdge)

      edge.RadiusX <- 11.0
      edge.RadiusY <- 11.0

      let dashes = Avalonia.Collections.AvaloniaList<float>()

      dashes.Add 2.0
      dashes.Add 2.0
      edge.StrokeDashArray <- dashes

      Grid()
        .Children(edge, inner)
        .HorizontalAlignment(HorizontalAlignment.Right)

  /// One author's run: the org label, the bubbles stacked, and under
  /// the last bubble the clock time the site prints for the other
  /// side's runs.
  let run(ctx: RowCtx, key: string, ofRun: State.Run) : Control =
    let statusMessage =
      ctx.Model
      |> Observable.map(fun current ->
        current.State.Threads
        |> Map.tryFind key
        |> Option.bind(fun thread ->
          State.statusOf(ctx.IsOrg, ctx.ViewerId, thread)))

    let children: Control array = [|
      if ctx.IsOrg && not ofRun.IsMine then
        let label: Control =
          match ofRun.Author with
          | Some user ->
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(4.0)
              .Children(
                Avatar.view {
                  Name = user.Name
                  Url = user.AvatarUrl
                  Lane = user.Lane
                  Size = 16.0
                  Cache = ctx.Images
                },
                TextBlock()
                  .Classes(Theme.name Theme.Mono)
                  .FontSize(11.0)
                  .Text(authorName(ctx, ofRun.Author, ofRun.AuthorId))
                  .VerticalAlignment(VerticalAlignment.Center)
              )
            :> Control
          | None ->
            TextBlock()
              .Classes(Theme.name Theme.Mono)
              .FontSize(11.0)
              .Text(authorName(ctx, ofRun.Author, ofRun.AuthorId))
            :> Control

        yield
          label
            .Margin(4.0, 6.0, 4.0, 0.0)
            .HorizontalAlignment(HorizontalAlignment.Left)

      for carried in ofRun.Bubbles ->
        match carried with
        | State.Sent message ->
          bubble {
            Ctx = ctx
            Key = key
            Message = message
            Mine = ofRun.IsMine
            ShowsStatus =
              (statusMessage
               |> Observable.map(fun status ->
                 status |> Option.map _.Id |> Option.contains message.Id))
          }
        | State.Staged pending -> stagedBubble(ctx, pending)

      // Every run ends in its clock time, the site's own meta line —
      // mine's too, right-aligned, except the one run whose line is
      // the receipt instead (the newest message of the conversation).
      match
        ofRun.Bubbles
        |> List.tryFindBack(function
          | State.Sent _ -> true
          | State.Staged _ -> false)
      with
      | Some(State.Sent last) ->
        let at = last.CreatedAt.LocalDateTime.ToString("h:mm tt")

        let line =
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .FontSize(11.0)
            .Margin(4.0, 0.0, 4.0, 0.0)
            .Text(at)
            .HorizontalAlignment(
              if ofRun.IsMine then
                HorizontalAlignment.Right
              else
                HorizontalAlignment.Left
            )

        line.IsVisible(
          statusMessage
          |> Observable.map(fun status ->
            not(
              ofRun.IsMine
              && status |> Option.map _.Id |> Option.contains last.Id
            ))
        )
        |> ignore

        yield line
      | _ -> ()
    |]

    StackPanel()
      .Spacing(2.0)
      .Margin(0.0, 6.0, 0.0, 0.0)
      .HorizontalAlignment(
        if ofRun.IsMine then
          HorizontalAlignment.Right
        else
          HorizontalAlignment.Left
      )
      .Children(children)

  /// One quiet org event line.
  let eventRow(ctx: RowCtx, message: Message) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .FontSize(12.0)
      .Margin(0.0, 8.0, 0.0, 0.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Text(State.eventText(ctx.ViewerId, message))

  /// One calendar-day break: the centered quiet pill with its leading
  /// dot, and no rule.
  let dayRow(label: string) : Control =
    Border()
      .Classes(Theme.name Theme.DayMarker)
      .Margin(0.0, 12.0, 0.0, 0.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Child(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            Ellipse().Classes(Theme.name Theme.DayMarker),
            TextBlock().Text(label)
          )
      )

  /// The unread divider: the "HEAD · N new" line the head wears.
  /// The unread divider: the "HEAD · N new" word on the left with the
  /// hairline running off after it, the way the site's head rule
  /// reads.
  let headRow(count: int) : Control =
    DockPanel()
      .Margin(0.0, 10.0, 0.0, 0.0)
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.HeadRule)
          .Dock(Dock.Left)
          .Text($"HEAD · {count} new"),
        Border()
          .Classes(Theme.name Theme.HeadRule)
          .Height(1.0)
          .VerticalAlignment(VerticalAlignment.Center)
      )

  /// The message list: virtualized, keyed, following its tail.
  let list
    (ctx: RowCtx, key: string, restore: float, thread: IObservable<ThreadState>)
    : Control =
    let scroll = ScrollState(restore)

    let rowItems =
      thread
      |> Observable.map(fun current ->
        State.threadRows {
          State.ViewerId = ctx.ViewerId
          IsOrg = ctx.IsOrg
          Now = DateTimeOffset.UtcNow
          Thread = current
        })

    let template =
      FuncDataTemplate<State.ThreadRow>(
        (fun item _ ->
          if isNull(box item) then
            Panel() :> Control
          else
            match item with
            | State.ThreadRow.Day(_, label) -> dayRow label
            | State.ThreadRow.Head count -> headRow count
            | State.ThreadRow.Event message -> eventRow(ctx, message)
            | State.ThreadRow.Run ofRun -> run(ctx, key, ofRun)),
        false
      )

    let items =
      ItemsControl()
        .ItemTemplate(template)
        .ItemsPanel(FuncTemplate<Panel>(fun () -> VirtualizingStackPanel()))
        .BindItemsSource(rowItems, State.threadRowKey)

    let scroller = ScrollViewer().Name("RemotesThread").Content(items)

    scroller.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled

    // The scroll model in one handler: the top asks for older pages,
    // the bottom edge is the tail a riding reader keeps, a remembered
    // place lands once the extent can hold it, and every settled place
    // parks itself in the store. Each pass swaps one whole place.
    scroller.ScrollChanged.Add(fun _ ->
      let extent = scroller.Extent.Height
      let room = extent - scroller.Viewport.Height
      let place = scroll.Current

      if scroller.Offset.Y < 240.0 && room > 0.0 then
        ctx.Dispatch State.OlderWanted

      let fromEnd = room - scroller.Offset.Y

      let riding =
        if fromEnd < 48.0 then
          true
        elif extent > place.Extent + 0.5 && place.Riding then
          scroller.ScrollToEnd()
          place.Riding
        else
          false

      // A whole viewport below the reading is the site's own bar for
      // "far from the present edge".
      let behind = fromEnd > scroller.Viewport.Height

      ctx.Dispatch(State.RideMoved(riding, behind))

      let settled =
        if not place.Landed then
          if room >= place.Target then
            scroller.Offset <- Vector(0.0, place.Target)
            { place with Landed = true }
          elif not place.Asked then
            ctx.Dispatch State.OlderWanted
            { place with Asked = true }
          elif extent > place.Extent then
            // The ask grew nothing the first time; land where the
            // list ends rather than chase forever.
            { place with Landed = true }
          else
            place
        else
          place

      scroll.Move {
        settled with
            Riding = riding
            Extent = extent
      }

      ctx.Dispatch(State.ThreadScrolled scroller.Offset.Y))

    // The landing asks: one message a ghost named, or the present
    // edge. The listener ends with the list's detach.
    let landings =
      ctx.Model |> Observable.map _.Landing |> Observable.distinctUntilChanged

    let land =
      Sub.ofId "landing" (fun _ ->
        landings.Subscribe(function
          | Some(State.Message id) ->
            scroller.Offset <- Vector(0.0, 0.0)

            scroller.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? Border as border when visual.Name = $"ChatMessage{id}" ->
                Some border
              | _ -> None)
            |> Option.iter(fun border ->
              border.Classes.Add(Theme.name Theme.FlashRing) |> ignore

              let timer = DispatcherTimer()
              timer.Interval <- TimeSpan.FromMilliseconds 1200.0

              let stop() =
                timer.Stop()
                border.Classes.Remove(Theme.name Theme.FlashRing) |> ignore

              timer.Tick.Add(fun _ -> stop())
              border.DetachedFromVisualTree.Add(fun _ -> stop())
              timer.Start())

            ctx.Dispatch State.Landed
          | Some State.Present ->
            scroller.ScrollToEnd()
            ctx.Dispatch State.Landed
          | None -> ()))
      |> Sub.untilDetached scroller
      |> List.exactlyOne
      |> snd

    land ignore |> ignore
    scroller

  /// The composer: the meta row with the stash chip and the counter,
  /// the reply bar, the staged images, and the input row. Ctrl+Enter
  /// The floating return to the thread's present edge; while messages
  /// arrived unseen, it doubles as the "N new" pill.
  let present
    (
      dispatch: Dispatch<State.Msg>,
      visible: IObservable<bool>,
      label: IObservable<string>
    ) : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(10.0, 4.0, 10.0, 4.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Bottom)
      .Margin(0.0, 0.0, 0.0, 8.0)
      .Content(label |> _.ToBinding())
      .IsVisible(visible)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        dispatch State.PresentWanted)

  /// One staged image chip, the commit composer's own shape: the bytes
  /// decoded small, the file name, and its remove. The decoded bitmap
  /// is native memory, so the preview disposes it when the strip
  /// rebuilds and drops it.
  let imageChip
    (dispatch: Dispatch<State.Msg>, image: ImageAttachment, index: int)
    : Control =
    let bitmap =
      use stream = new MemoryStream(image.Bytes)
      Bitmap.DecodeToWidth(stream, 96)

    let preview =
      Avalonia.Controls
        .Image()
        .Source(bitmap)
        .Width(48.0)
        .Height(48.0)
        .Stretch(Stretch.UniformToFill)

    preview.DetachedFromVisualTree.Add(fun _ -> bitmap.Dispose())

    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .Padding(6.0)
      .Child(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            preview,
            StackPanel()
              .VerticalAlignment(VerticalAlignment.Center)
              .Children(
                TextBlock()
                  .Classes(Theme.name Theme.Mono)
                  .FontSize(12.0)
                  .Text(image.FileName)
                  .MaxWidth(140.0)
                  .TextTrimming(TextTrimming.CharacterEllipsis),
                Button()
                  .Name($"RemoveImage{index}")
                  .Classes(Theme.name Theme.GhostButton)
                  .Padding(4.0, 0.0, 4.0, 0.0)
                  .FontSize(12.0)
                  .Content("remove")
                  .OnClickHandler(fun _ args ->
                    args.Handled <- true
                    dispatch(State.ImageRemoved index))
              )
          )
      )

  /// The link-card picker: a quiet button whose flyout lists the
  /// draft's links, the first one, or none. The button itself shows
  /// only while the draft carries a link. The flyout is
  /// control-owned: it dies with the pane.
  let linkPicker
    (
      dispatch: Dispatch<State.Msg>,
      draft: IObservable<string>,
      choice: IObservable<LinkCard>
    ) : Control =
    let links = draft |> Observable.map State.linksOf

    let label =
      Observable.combineLatest links choice
      |> Observable.map(fun (found, chosen) ->
        match chosen with
        | LinkCard.CardOf url when found |> List.contains url ->
          State.plainLink url
        | LinkCard.NoCard -> "no card"
        | _ -> if found.IsEmpty then "no links" else "first link")

    let rows =
      Observable.combineLatest links choice
      |> Observable.map(fun (found, chosen) -> [
        yield ("first link", LinkCard.FirstLink)

        for url in found -> State.plainLink url, LinkCard.CardOf url
        yield ("no card", LinkCard.NoCard)
      ])
      |> Observable.distinctUntilChanged

    let host = ContentControl()

    host.Content(
      rows
      |> Observable.map(fun picks ->
        StackPanel().Children [|
          for label', pick in picks ->
            let choice: Control =
              Button()
                .Classes(Theme.name Theme.GhostButton)
                .Padding(6.0, 2.0, 6.0, 2.0)
                .FontSize(12.0)
                .Content(label')
                .OnClickHandler(fun _ args ->
                  args.Handled <- true
                  dispatch(State.LinkChosen pick))

            choice
        |])
      |> _.ToBinding()
    )
    |> ignore

    let flyout =
      Popup()
        .Child(
          Border()
            .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
            .BorderThickness(1.0)
            .CornerRadius(6.0)
            .Padding(4.0)
            .Child(host)
        )

    let quiet =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(6.0, 0.0, 6.0, 0.0)
        .FontSize(11.0)
        .Content(label |> Observable.distinctUntilChanged |> _.ToBinding())
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          flyout.IsOpen <- not flyout.IsOpen)

    ToolTip.SetTip(quiet, "Choose which link gets a card")

    Panel()
      .Children(quiet, flyout)
      .HorizontalAlignment(HorizontalAlignment.Right)
      .IsVisible(links |> Observable.map(fun found -> not found.IsEmpty))

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
      let phase =
        model |> Observable.map _.Phase |> Observable.distinctUntilChanged

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
                2.0
                + (List.tryItem at current |> Option.defaultValue 0.0) * 20.0)
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

    let capture: IMicrophoneCapture option ref = ref None

    let release() =
      match !capture with
      | Some held ->
        held.Cancel()
        capture.Value <- None
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
          capture.Value <- Some live

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
        match !capture, model.Phase with
        | Some held, Recorder.Recording started ->
          let taken = DateTimeOffset.UtcNow - started

          capture.Value <- None

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
          taken |> Result.mapError CaptureFailed |> Result.bind State.stageVoice

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
              !capture |> Option.map _.Level() |> Option.defaultValue 0.0,
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

  /// The composer: the meta row with the typing line and the counter,
  /// the reply bar, the staged images, and the input row. Enter
  /// pushes, Esc cancels the reply.
  let composer
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      solo: Solo,
      target: ChatTarget,
      thread: IObservable<ThreadState>
    ) : Control =
    let isOrg =
      match target with
      | Org _ -> true
      | Partner _ -> false

    let placeholder =
      match target with
      | Partner partner -> $"Message @{partner.Handle}"
      | Org org ->
        let orgName = org.Name |> Option.defaultValue "the org"
        $"Message {orgName}"

    let draft = model |> Observable.map _.Draft
    let images = model |> Observable.map _.Images
    let choice = model |> Observable.map _.LinkChoice

    let voiceRides =
      model |> Observable.map(fun current -> current.Voice.IsSome)

    let reply = thread |> Observable.map(fun current -> current.ReplyingTo)

    let counter = draft |> Observable.map(fun text -> text.TrimEnd().Length)

    // The site's own word: quiet until 80%, amber from 95%, danger at
    // the limit, thousands grouped the way the counter reads them.
    let counterBlock =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .FontSize(11.0)
        .Text(
          counter
          |> Observable.map(fun length ->
            FormattableString.Invariant $"""{length:N0}/2,000""")
        )
        .IsVisible(counter |> Observable.map(fun length -> length >= 1600))

    Theme.bindClass(
      counterBlock,
      Theme.FgWarning,
      counter |> Observable.map(fun length -> length >= 1900 && length < 2000)
    )

    // The counter goes quiet-danger at the limit; the class carries
    // the color, never a brush observable.
    Theme.bindClass(
      counterBlock,
      Theme.FgDanger,
      counter |> Observable.map(fun length -> length >= 2000)
    )

    // The typing line, the site's own slot: the meta row's leading
    // word while the partner or the org types, its three dots chasing
    // each other on the theme's own beat.
    let typingWord =
      let names =
        thread
        |> Observable.map(fun current -> State.typingNames(isOrg, current))
        |> Observable.distinctUntilChanged

      let dot(klass: string) =
        Ellipse()
          .Width(3.0)
          .Height(3.0)
          .Classes(klass)
          .VerticalAlignment(VerticalAlignment.Center)

      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(4.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Children(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(2.0)
            .VerticalAlignment(VerticalAlignment.Center)
            .Children(
              dot(Theme.name Theme.TypingDot1),
              dot(Theme.name Theme.TypingDot2),
              dot(Theme.name Theme.TypingDot3)
            ),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .FontSize(11.0)
            .Text(names |> Observable.map State.typingText)
        )
        .IsVisible(names |> Observable.map(List.isEmpty >> not))

    let metaRow =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8.0)
        .Children(typingWord, linkPicker(dispatch, draft, choice), counterBlock)

    let replyBar =
      ContentControl()
        .Content(
          reply
          |> Observable.map(function
            | Some parent ->
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(8.0)
                .Children(
                  TextBlock()
                    .Classes(Theme.name Theme.Muted)
                    .FontSize(12.0)
                    .Text($"in reply to: {parent.Body}")
                    .TextTrimming(TextTrimming.CharacterEllipsis),
                  Button()
                    .Classes(Theme.name Theme.GhostButton)
                    .Padding(4.0, 0.0, 4.0, 2.0)
                    .FontSize(11.0)
                    .Content("cancel")
                    .OnClickHandler(fun _ args ->
                      args.Handled <- true
                      dispatch State.ReplyCancelled)
                )
              :> Control
            | None -> null)
          |> _.ToBinding()
        )

    // The staged images: the chips rebuild only when the image list
    // itself moves, so typing never re-decodes a preview under the
    // caret.
    let staged =
      ContentControl()
        .Content(
          images
          |> Observable.distinctUntilChanged
          |> Observable.map(fun staged' ->
            let chips: Control array = [|
              for index, image in List.indexed staged' ->
                imageChip(dispatch, image, index)
            |]

            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(8.0)
              .Children(chips)
            :> Control)
          |> _.ToBinding()
        )
        .IsVisible(images |> Observable.map(fun staged' -> not staged'.IsEmpty))

    let input =
      TextBox()
        .Name("RemotesComposer")
        .PlaceholderText(placeholder)
        .MinHeight(40.0)
        .MaxHeight(160.0)
        .AcceptsReturn(true)
        .TextWrapping(TextWrapping.Wrap)
        // The site's own rule: a voice note rides alone, so its draft
        // is closed to typing while one is staged.
        .IsEnabled(voiceRides |> Observable.map not)
        .Text(draft |> Observable.distinctUntilChanged |> _.ToBinding())
        .OnTextChangedHandler(fun box _ ->
          let text = box.Text |> Option.ofObj |> Option.defaultValue ""

          dispatch(State.DraftChanged(text, box.CaretIndex)))
        // The caret is the mention's own place: a move inside the text
        // re-reads what stands at the caret, the way typing does.
        .OnCaretIndexChanged(fun box _ ->
          let text = box.Text |> Option.ofObj |> Option.defaultValue ""

          dispatch(State.DraftChanged(text, box.CaretIndex)))

    // The site's fine-pointer rule, through the tunnel so the box's
    // own key handling never eats it: Enter pushes, Shift+Enter makes
    // the newline, Esc cancels the reply or discards a live recording,
    // and the platform's paste gesture asks the attachment sources
    // first — a clipboard image stages, clipboard text lands in the
    // box.
    input.AddHandler(
      InputElement.KeyDownEvent,
      (fun _ args ->
        let pasteMatches(args: KeyEventArgs) =
          match Application.Current with
          | null -> false
          | app ->
            match app.PlatformSettings with
            | null -> false
            | settings ->
              settings.HotkeyConfiguration.Paste
              |> Seq.exists(fun gesture -> gesture.Matches args)

        let plainEnter =
          args.Key = Key.Enter && args.KeyModifiers = KeyModifiers.None

        let controlEnter =
          args.Key = Key.Enter
          && args.KeyModifiers.HasFlag(KeyModifiers.Control)

        if plainEnter || controlEnter then
          args.Handled <- true
          dispatch State.SendWanted
        elif args.Key = Key.Escape then
          args.Handled <- true
          dispatch State.ReplyCancelled
          dispatch State.RecordCancelled
        elif pasteMatches args then
          args.Handled <- true

          asyncEx {
            let! image = props.Env.Attachments.ReadClipboardImage()

            match image with
            | Some staged ->
              Dispatcher.UIThread.Post(fun () ->
                dispatch(State.Picked [ staged ]))
            | None ->
              let! text = props.Env.Attachments.ReadClipboardText()

              match text with
              | Some pasted ->
                Dispatcher.UIThread.Post(fun () ->
                  input.SelectedText <- pasted)
              | None -> ()
          }
          |> Async.StartImmediate
        else
          ()),
      RoutingStrategies.Tunnel
    )

    // Who a mention can name here: the partner alone, or the org's
    // members beside the viewer.
    let mentionables(current: ThreadState) : User list =
      match target with
      | Partner partner -> [ partner ]
      | Org _ ->
        current.Members
        |> List.map _.User
        |> List.filter(fun user -> user.Id <> props.Session.Session.User.Id)

    let mentionPicks =
      let word = model |> Observable.map _.Mention

      Observable.combineLatest word thread
      |> Observable.map(fun (typed, current) ->
        match typed with
        | Some prefix ->
          mentionables current
          |> List.filter(fun user ->
            user.Handle.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
          |> List.truncate 6
        | None -> [])
      |> Observable.distinctUntilChanged

    // The completion lands in the box itself, so the change rides the
    // same TextChanged path typing does.
    let applyMention(handle: string) =
      let typed = input.Text |> Option.ofObj |> Option.defaultValue ""

      let next, caret = State.mentionCompleted(typed, input.CaretIndex, handle)

      input.Text <- next
      input.CaretIndex <- caret
      dispatch(State.DraftChanged(next, caret))

    let mentions =
      let pick(user: User) : Control =
        Button()
          .Name("MentionPick")
          .Classes(Theme.name Theme.GhostButton)
          .Padding(8.0, 4.0, 8.0, 4.0)
          .FontSize(12.0)
          .HorizontalContentAlignment(HorizontalAlignment.Left)
          .Content(
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(6.0)
              .Children(
                TextBlock()
                  .Classes(Theme.name Theme.Mono)
                  .FontSize(12.0)
                  .Text("@" + user.Handle),
                TextBlock()
                  .Classes(Theme.name Theme.Muted)
                  .FontSize(12.0)
                  .Text(user.Name)
              )
          )
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            applyMention user.Handle)
        :> Control

      ContentControl()
        .Content(
          mentionPicks
          |> Observable.map(fun people ->
            let picks: Control array = [| for user in people -> pick user |]

            Border()
              .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
              .BorderThickness(1.0)
              .CornerRadius(6.0)
              .Padding(4.0)
              .MaxWidth(280.0)
              .HorizontalAlignment(HorizontalAlignment.Left)
              .Child(StackPanel().Children(picks))
            :> Control)
          |> _.ToBinding()
        )
        .IsVisible(mentionPicks |> Observable.map(List.isEmpty >> not))

    let pick =
      let button =
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0)
          .Content(Icons.iconAt(Glyph.Image, 16.0))
          // Images never ride beside a staged voice note either.
          .IsEnabled(voiceRides |> Observable.map not)
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            dispatch State.PickWanted)

      ToolTip.SetTip(button, "Attach images")
      button

    let send =
      Button()
        .Classes(Theme.name Theme.AccentButton)
        .MinWidth(72.0)
        .Content("Push")
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          dispatch State.SendWanted)

    // The staged voice note: the full player over the take just made,
    // so it can be heard before it rides, with its discard beside it.
    // The take is keyed by its length and name, never by comparing
    // the bytes themselves.
    let stagedVoice =
      let host = ContentControl()

      host.Content(
        model
        |> Observable.map(fun current ->
          current.Voice
          |> Option.map(fun (attachment, bars) ->
            (attachment.FileName, attachment.Bytes.Length), (attachment, bars)))
        |> Observable.distinctUntilChanged
        |> Observable.map(function
          | Some(_, (attachment, bars)) ->
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(8.0)
              .Children(
                voiceChip {
                  Audio = props.Env.Natives.Audio
                  Http = props.Env.Http
                  Settings = props.Env.Settings
                  Solo = solo
                  Log =
                    props.Env.LoggerFactory.CreateLogger
                      "DevNull.Client.Views.RemotesVoice"
                  Source = Staged(attachment.Bytes, bars)
                },
                Button()
                  .Classes(Theme.name Theme.GhostButton)
                  .Padding(4.0)
                  .Content(Icons.iconAt(Glyph.Trash, 14.0))
                  .VerticalAlignment(VerticalAlignment.Center)
                  .OnClickHandler(fun _ args ->
                    args.Handled <- true
                    dispatch State.VoiceDiscarded)
              )
            :> Control
          | None -> TextBlock() :> Control)
        |> _.ToBinding()
      )
      |> ignore

      host.IsVisible(
        model |> Observable.map(fun current -> current.Voice.IsSome)
      )
      |> ignore

      host

    // The mic button: the site's own rule — it stays in place and
    // dims while a draft or images ride the push, saying why.
    let mic =
      let button =
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0)
          .Content(Icons.iconAt(Glyph.Mic, 16.0))
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            dispatch State.RecordWanted)

      let free =
        model
        |> Observable.map(fun current ->
          current.Voice.IsNone && current.Images.IsEmpty)

      button.IsEnabled(
        Observable.combineLatest
          free
          (model |> Observable.map(fun current -> current.Draft.Trim() = ""))
        |> Observable.map(fun (free', quiet) -> free' && quiet)
      )
      |> ignore

      ToolTip.SetTip(button, "Record a voice note (max. 1 min)")

      button.IsVisible(
        model
        |> Observable.map(fun _ -> props.Env.Natives.Audio.IsSome)
        |> Observable.distinctUntilChanged
      )
      |> ignore

      button

    // The ping button: a quiet nudge for the partner or the org.
    let ping =
      let button =
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0)
          .Content(Icons.iconAt(Glyph.Vibrate, 16.0))
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            dispatch State.PingWanted)

      let label =
        match target with
        | Partner partner -> $"Ping @{partner.Handle}"
        | Org org ->
          let orgName = org.Name |> Option.defaultValue "the org"

          $"Ping {orgName}"

      ToolTip.SetTip(button, label)
      button

    // The recording lane: the bordered box that stands in for the
    // input row while the take is live. One lane per take — the
    // content is projected when the recording starts, so the program
    // (and the microphone with it) lives only while the take does.
    // The lane reports through the page's own messages.
    let recordLane =
      let report(outcome: Recorder.Report) =
        match outcome with
        | Recorder.Staged staged -> dispatch(State.VoiceStaged staged)
        | Recorder.Cancel -> dispatch State.RecordCancelled

      ContentControl()
        .Name("RemotesRecord")
        .Content(
          model
          |> Observable.map(fun current -> current.Recording)
          |> Observable.distinctUntilChanged
          |> Observable.map(fun recording ->
            if recording then
              RecordLane(props.Env.Natives.Audio, report) :> Control
            else
              null)
          |> _.ToBinding()
        )
        .IsVisible(model |> Observable.map(fun current -> current.Recording))

    // The input row: the tool buttons docked left, Push right, and the
    // box filling every pixel between them. A StackPanel here measured
    // the box to its typed length and pushed Push off the pane's edge;
    // a DockPanel lays no gaps of its own, so each docked edge carries
    // its own.
    let inputRow =
      DockPanel()
        .LastChildFill(true)
        .Children(
          pick
            .Dock(Dock.Left)
            .Margin(0.0, 0.0, 8.0, 0.0)
            .VerticalAlignment(VerticalAlignment.Center),
          mic
            .Dock(Dock.Left)
            .Margin(0.0, 0.0, 8.0, 0.0)
            .VerticalAlignment(VerticalAlignment.Center),
          ping
            .Dock(Dock.Left)
            .Margin(0.0, 0.0, 8.0, 0.0)
            .VerticalAlignment(VerticalAlignment.Center),
          send
            .Dock(Dock.Right)
            .Margin(8.0, 0.0, 0.0, 0.0)
            .VerticalAlignment(VerticalAlignment.Center),
          input
        )

    inputRow.IsVisible(
      model |> Observable.map(fun current -> not current.Recording)
    )
    |> ignore

    StackPanel()
      .Spacing(6.0)
      .Margin(0.0, 8.0, 0.0, 8.0)
      .Children(
        mentions,
        metaRow,
        replyBar,
        staged,
        stagedVoice,
        recordLane,
        inputRow
      )

  /// The bar that takes the composer's place when the viewer cannot
  /// push: a fetched request to settle, or the word on why. The
  /// server's copy verbatim.
  let accessBar
    (dispatch: Dispatch<State.Msg>, partner: User, access: PushAccess)
    : Control =
    let bar =
      match access with
      | PushRequest ->
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .Children(
            TextBlock()
              .Text(
                $"@{partner.Handle} wants to push to you. Merge to reply, or drop it. They won't be told."
              )
              .TextWrapping(TextWrapping.Wrap)
              .Classes(Theme.name Theme.Muted),
            Button()
              .Classes(Theme.name Theme.GhostButton)
              .Content("Drop")
              .OnClickHandler(fun _ args ->
                args.Handled <- true
                dispatch(State.SettleWanted(partner, false))),
            Button()
              .Classes(Theme.name Theme.AccentButton)
              .Content("Merge")
              .OnClickHandler(fun _ args ->
                args.Handled <- true
                dispatch(State.SettleWanted(partner, true)))
          )
        :> Control
      | PushWaiting ->
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(13.0)
          .Text($"pushed. waiting for @{partner.Handle} to merge your request")
          .TextWrapping(TextWrapping.Wrap)
        :> Control
      | PushRestricted ->
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(13.0)
          .Text(
            $"@{partner.Handle} doesn't follow you, so you can't push to them yet"
          )
          .TextWrapping(TextWrapping.Wrap)
        :> Control
      | PushClosed
      | UnknownAccess _ ->
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(13.0)
          .Text($"@{partner.Handle} isn't taking remotes")
          .TextWrapping(TextWrapping.Wrap)
        :> Control
      | PushOpen -> TextBlock() :> Control

    bar

  /// One quiet line the menus say when the server refuses, shown under
  /// the pane's header.
  let notice(model: IObservable<State.Model>) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .FontSize(11.0)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Notice |> Option.defaultValue "")
      )
      .TextWrapping(TextWrapping.Wrap)
      .IsVisible(model |> Observable.map(fun current -> current.Notice.IsSome))

  /// One menu pick as a button row.
  let menuPick
    (dispatch: Dispatch<State.Msg>, label: string, msg: State.Msg)
    : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(6.0, 2.0, 6.0, 2.0)
      .FontSize(12.0)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .HorizontalContentAlignment(HorizontalAlignment.Left)
      .Content(label)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        dispatch msg)

  /// One destructive pick: the site's own word first, so one click
  /// never lands it. The flyout carries the question, the
  /// consequences, and the Cancel and destructive buttons.
  let confirmPick
    (
      dispatch: Dispatch<State.Msg>,
      label: string,
      title: string,
      body: string,
      verb: string,
      msg: State.Msg
    ) : Control =
    let flyout = Popup()

    flyout.Child <-
      Border()
        .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
        .BorderThickness(1.0)
        .CornerRadius(6.0)
        .Padding(10.0)
        .MaxWidth(300.0)
        .Child(
          StackPanel()
            .Spacing(6.0)
            .Children(
              TextBlock()
                .Classes(Theme.name Theme.Subtitle)
                .FontSize(13.0)
                .Text(title),
              TextBlock()
                .Classes(Theme.name Theme.Muted)
                .FontSize(12.0)
                .TextWrapping(TextWrapping.Wrap)
                .Text(body),
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(8.0)
                .HorizontalAlignment(HorizontalAlignment.Right)
                .Children(
                  Button()
                    .Classes(Theme.name Theme.GhostButton)
                    .Padding(8.0, 2.0, 8.0, 2.0)
                    .FontSize(12.0)
                    .Content("Cancel")
                    .OnClickHandler(fun _ args ->
                      args.Handled <- true
                      flyout.IsOpen <- false),
                  Button()
                    .Classes(Theme.name Theme.DangerButton)
                    .Padding(8.0, 2.0, 8.0, 2.0)
                    .FontSize(12.0)
                    .Content(verb)
                    .OnClickHandler(fun _ args ->
                      args.Handled <- true
                      flyout.IsOpen <- false
                      dispatch msg)
                )
            )
        )

    Panel()
      .Children(
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0, 2.0, 6.0, 2.0)
          .FontSize(12.0)
          .HorizontalAlignment(HorizontalAlignment.Stretch)
          .HorizontalContentAlignment(HorizontalAlignment.Left)
          .Content(label)
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            flyout.IsOpen <- not flyout.IsOpen),
        flyout
      )

  /// The conversation's own menu: view profile, pin, stash, mutes,
  /// block or leave. The flyout is control-owned, the way the link
  /// picker's is.
  let actionsMenu
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      target: ChatTarget,
      thread: IObservable<ThreadState>
    ) : Control =
    let title =
      match target with
      | Partner partner -> partner.Name
      | Org org -> org.Name |> Option.defaultValue "the org"

    let pinned =
      thread |> Observable.map(fun current -> current.PinnedAt.IsSome)

    let archived = thread |> Observable.map _.IsArchived

    let pick(label: string, msg: State.Msg) : Control =
      menuPick(dispatch, label, msg)

    let stateful
      (label: string, msg: State.Msg, shown: IObservable<bool>)
      : Control =
      let control = pick(label, msg)
      control.IsVisible(shown) |> ignore
      control

    let rows: Control array = [|
      match target with
      | Partner partner ->
        yield
          Button()
            .Classes(Theme.name Theme.GhostButton)
            .Padding(6.0, 2.0, 6.0, 2.0)
            .FontSize(12.0)
            .HorizontalContentAlignment(HorizontalAlignment.Left)
            .Content("view profile")
            .OnClickHandler(fun _ args ->
              args.Handled <- true
              Navigation.go(props.Nav, "/profile/" + partner.Handle, ignore))
      | Org _ -> ()

      yield stateful("pin", State.PinWanted true, pinned |> Observable.map not)

      yield stateful("unpin", State.PinWanted false, pinned)

      yield
        stateful(
          "stash",
          State.ArchiveWanted true,
          archived |> Observable.map not
        )

      yield stateful("pop", State.ArchiveWanted false, archived)

      yield!
        [
          "mute 1 hour", MuteHour
          "mute until tomorrow", MuteTomorrow
          "mute 1 week", MuteWeek
          "mute forever", MuteForever
          "unmute", MuteOff
        ]
        |> List.map(fun (label, how) -> pick(label, State.MuteWanted how))

      match target with
      | Partner partner ->
        yield
          confirmPick(
            dispatch,
            $"block @{partner.Handle}",
            $"Block {partner.Name}?",
            "This conversation goes away for both of you, and neither of you can message, follow, reply to, or star the other. They won't be told. You can unblock them any time from your settings.",
            "Block",
            State.BlockWanted partner
          )
      | Org _ ->
        yield
          confirmPick(
            dispatch,
            $"leave {title}",
            $"Leave {title}?",
            "You stop seeing the org and its history. A maintainer would have to add you back. If you are its only maintainer, the longest-standing member takes over.",
            "Leave",
            State.OrgRemoveWanted(props.Session.Session.User.Handle, true)
          )
    |]

    let flyout =
      Popup()
        .Child(
          Border()
            .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
            .BorderThickness(1.0)
            .CornerRadius(6.0)
            .Padding(4.0)
            .Child(StackPanel().Children(rows))
        )

    let more =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(6.0)
        .Content("···")
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          flyout.IsOpen <- not flyout.IsOpen)

    ToolTip.SetTip(more, $"More options for {title}")

    Panel()
      .Children(more, flyout)
      .HorizontalAlignment(HorizontalAlignment.Right)

  /// The org header's member stack: the org's members, ringed and
  /// overlapped, two rows deep when the org is wide.
  let memberStack(ctx: RowCtx, members: ThreadMember list) : Control =
    StackPanel().Orientation(Orientation.Horizontal).Spacing(0.0).Children [|
      for member' in members ->
        (Avatar.view {
          Name = member'.User.Name
          Url = member'.User.AvatarUrl
          Lane = member'.User.Lane
          Size = 26.0
          Cache = ctx.Images
        })
          .Margin(-6.0, 0.0, 0.0, 0.0)
    |]

  /// The maintainer's menu on an org header: rename, add members with
  /// the share-history choice, a role change per member, remove, and
  /// leave. Control-owned flyout, the house pattern.
  let orgMenu
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      org: OrgRef,
      thread: IObservable<ThreadState>
    ) : Control =
    let viewer = props.Session.Session.User

    let members = thread |> Observable.map(fun current -> current.Members)

    let role = thread |> Observable.map(fun current -> current.Role)

    let renameBox =
      TextBox()
        .Name("OrgRename")
        .PlaceholderText("name")
        .MaxLength(60)
        .MinHeight(30.0)
        .FontSize(12.0)
        .MaxWidth(180.0)
        .Text(
          model
          |> Observable.map _.OrgRename
          |> Observable.distinctUntilChanged
          |> _.ToBinding()
        )
        .OnTextChangedHandler(fun box _ ->
          box.Text
          |> Option.ofObj
          |> Option.defaultValue ""
          |> State.OrgRenameChanged
          |> dispatch)

    let memberBox =
      TextBox()
        .Name("OrgMember")
        .PlaceholderText("@handle")
        .MinHeight(30.0)
        .FontSize(12.0)
        .MaxWidth(180.0)
        .OnKeyDownHandler(fun box args ->
          if args.Key = Key.Enter then
            args.Handled <- true

            let typed =
              box.Text |> Option.ofObj |> Option.defaultValue "" |> _.Trim()

            dispatch(State.OrgMemberChanged typed))

    // The add-section's own count: everyone the org would hold once
    // the picked names land.
    let addCount =
      Observable.combineLatest members (model |> Observable.map _.OrgMembers)
      |> Observable.map(fun (current, picked) ->
        let others =
          current |> List.filter(fun member' -> member'.User.Id <> viewer.Id)

        List.length others + 1 + List.length picked)
      |> Observable.distinctUntilChanged

    let memberHost = ContentControl()

    memberHost.Content(
      members
      |> Observable.map(fun current ->
        current |> List.filter(fun member' -> member'.User.Id <> viewer.Id))
      |> Observable.distinctUntilChanged
      |> Observable.map(fun others ->
        let rows: Control array = [|
          for member' in others ->
            let flipping =
              match member'.Role with
              | Some Maintainer
              | Some(UnknownRole _) -> "make contributor", Contributor
              | Some Contributor
              | None -> "make maintainer", Maintainer

            let label, role = flipping

            StackPanel()
              .Spacing(2.0)
              .Children(
                StackPanel()
                  .Orientation(Orientation.Horizontal)
                  .Spacing(4.0)
                  .Children(
                    TextBlock()
                      .Classes(Theme.name Theme.Mono)
                      .FontSize(11.0)
                      .Text("@" + member'.User.Handle)
                      .VerticalAlignment(VerticalAlignment.Center),
                    menuPick(
                      dispatch,
                      label,
                      State.OrgRoleWanted(member'.User.Handle, role)
                    )
                  ),
                menuPick(
                  dispatch,
                  "Remove from org",
                  State.OrgRemoveWanted(member'.User.Handle, false)
                )
              )
              .Margin(0.0, 2.0, 0.0, 0.0)
        |]

        StackPanel().Children rows)
      |> _.ToBinding()
    )
    |> ignore

    let rows: Control array = [|
      yield
        TextBlock().Classes(Theme.name Theme.Mono).FontSize(11.0).Text("rename")

      yield
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(4.0)
          .Children(
            renameBox,
            Button()
              .Classes(Theme.name Theme.GhostButton)
              .Padding(6.0, 2.0, 6.0, 2.0)
              .FontSize(11.0)
              .Content("save")
              .OnClickHandler(fun _ args ->
                args.Handled <- true
                dispatch State.OrgRenameWanted)
          )

      yield
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(11.0)
          .Text("add members")
          .Margin(0.0, 6.0, 0.0, 0.0)

      yield
        ContentControl()
          .Content(
            addCount
            |> Observable.map(fun count ->
              TextBlock()
                .Classes(Theme.name Theme.Mono)
                .FontSize(11.0)
                .Text($"{count}/100")
              :> Control)
            |> _.ToBinding()
          )

      yield memberBox

      yield
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(4.0)
          .Children(
            TextBlock()
              .Classes(Theme.name Theme.Mono)
              .FontSize(11.0)
              .Text(
                "Share history — they can read everything pushed before they joined"
              )
              .TextWrapping(TextWrapping.Wrap)
              .VerticalAlignment(VerticalAlignment.Center),
            CheckBox()
              .IsChecked(
                model
                |> Observable.map(fun current -> current.OrgShare)
                |> _.ToBinding()
              )
              .OnIsCheckedChangedHandler(fun box _ ->
                let shared =
                  match box.IsChecked with
                  | v when v.HasValue && v.Value -> true
                  | _ -> false

                dispatch(State.OrgShareChanged shared))
          )

      yield memberHost

      yield
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0, 2.0, 6.0, 2.0)
          .FontSize(12.0)
          .HorizontalContentAlignment(HorizontalAlignment.Left)
          .Content("add")
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            dispatch State.OrgAddWanted)

      yield
        (let orgName = org.Name |> Option.defaultValue "the org"

         confirmPick(
           dispatch,
           $"leave {orgName}",
           $"Leave {orgName}?",
           "You stop seeing the org and its history. A maintainer would have to add you back. If you are its only maintainer, the longest-standing member takes over.",
           "Leave",
           State.OrgRemoveWanted(viewer.Handle, true)
         ))
    |]

    let flyout =
      Popup()
        .Child(
          Border()
            .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
            .BorderThickness(1.0)
            .CornerRadius(6.0)
            .Padding(6.0)
            .MaxWidth(280.0)
            .Child(ScrollViewer().Content(StackPanel().Children(rows)))
        )

    Panel()
      .Children(
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0)
          .Content("members")
          .IsVisible(
            role |> Observable.map(fun current -> current = Some Maintainer)
          )
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            flyout.IsOpen <- not flyout.IsOpen),
        flyout
      )
      .HorizontalAlignment(HorizontalAlignment.Right)

  /// The pane's footer: the composer while the thread takes pushes,
  /// the access bar when a direct thread does not, nothing for a
  /// closed org. The content rebuilds only when the access itself
  /// moves: the composer's own bindings must survive every poll.
  let footer
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      solo: Solo,
      target: ChatTarget,
      thread: IObservable<ThreadState>
    ) : Control =
    let access =
      thread |> Observable.map _.Access |> Observable.distinctUntilChanged

    ContentControl()
      .Content(
        access
        |> Observable.map(function
          | PushOpen -> composer(props, model, dispatch, solo, target, thread)
          | PushRequest
          | PushWaiting
          | PushRestricted
          | PushClosed
          | UnknownAccess _ as refused ->
            match target with
            | Partner partner -> accessBar(dispatch, partner, refused)
            | Org _ -> null)
        |> _.ToBinding()
      )

  /// The thread pane for one target: the identity header, the message
  /// list over its overlays, and the typing line.
  let pane
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      solo: Solo,
      target: ChatTarget,
      key: string
    ) : Control =
    let isOrg =
      match target with
      | Org _ -> true
      | Partner _ -> false

    // The reading pane's own width, watched before anything reads it:
    // bubbles cap themselves at a fraction of it, the way the site
    // caps theirs at a fraction of the pane.
    let rowsGrid = Grid()

    let paneWidth =
      rowsGrid.GetObservable(Visual.BoundsProperty)
      |> Observable.map(fun bounds -> bounds.Width)

    let ctx: RowCtx = {
      Dispatch = dispatch
      Model = model
      Images = props.Env.ImageCache
      ViewerId = props.Session.Session.User.Id
      IsOrg = isOrg
      Nav = props.Nav
      Audio = props.Env.Natives.Audio
      Http = props.Env.Http
      Settings = props.Env.Settings
      Solo = solo
      Log =
        props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.RemotesVoice"
      PaneWidth = paneWidth
    }

    let thread =
      model
      |> Observable.map(fun current ->
        current.State.Threads
        |> Map.tryFind key
        |> Option.defaultValue(RemotesState.emptyThread target))

    let restore =
      props.Env.Remotes.ScrollPlaceOf key |> ValueOption.defaultValue 0.0

    let header =
      let identity: Control =
        match target with
        | Partner partner ->
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(10.0)
            .Children(
              Avatar.view {
                Name = partner.Name
                Url = partner.AvatarUrl
                Lane = partner.Lane
                Size = 36.0
                Cache = ctx.Images
              },
              StackPanel()
                .VerticalAlignment(VerticalAlignment.Center)
                .Children(
                  TextBlock()
                    .Classes(Theme.name Theme.Subtitle)
                    .Text(partner.Name),
                  TextBlock()
                    .Classes(Theme.name Theme.Mono)
                    .Text("@" + partner.Handle)
                )
            )
          :> Control
        | Org org ->
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(10.0)
            .Children(
              TextBlock()
                .Classes(Theme.name Theme.Subtitle)
                .Text(org.Name |> Option.defaultValue "org")
                .VerticalAlignment(VerticalAlignment.Center),
              ContentControl()
                .Content(
                  thread
                  |> Observable.map(fun current ->
                    memberStack(ctx, current.Members))
                  |> _.ToBinding()
                )
            )
          :> Control

      let menu: Control =
        match target with
        | Org org ->
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Children(
              orgMenu(props, model, dispatch, org, thread),
              actionsMenu(props, model, dispatch, target, thread)
            )
          :> Control
        | Partner _ -> actionsMenu(props, model, dispatch, target, thread)

      DockPanel().LastChildFill(true).Children(menu.Dock(Dock.Right), identity)

    let loadingOlder = thread |> Observable.map _.IsLoadingOlder

    let loadingLine =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text("loading older…")
        .HorizontalAlignment(HorizontalAlignment.Center)
        .Margin(0.0, 8.0, 0.0, 0.0)
        .VerticalAlignment(VerticalAlignment.Top)
        .IsVisible(loadingOlder)

    // The site's own bar: the pill shows when the reading sits a
    // viewport or more behind the present edge, or when messages
    // arrived the reader has not seen; unseen messages name their
    // count.
    let awayFromPresent =
      model
      |> Observable.map(fun current ->
        (not current.Riding && current.Behind) || current.Unseen > 0)
      |> Observable.distinctUntilChanged

    let presentLabel =
      model
      |> Observable.map(fun current ->
        if current.Unseen > 0 then
          $"{current.Unseen} new"
        else
          "back to present")
      |> Observable.distinctUntilChanged

    let presentButton = present(dispatch, awayFromPresent, presentLabel)

    let listHost =
      Panel()
        .Children(list(ctx, key, restore, thread), loadingLine, presentButton)

    let rows =
      rowsGrid.RowDefinitions <- RowDefinitions("Auto,Auto,*,Auto")

      rowsGrid.Children(
        header.Row(0).Margin(0.0, 0.0, 0.0, 8.0),
        notice(model).Row(1),
        listHost.Row(2),
        footer(props, model, dispatch, solo, target, thread).Row(3)
      )
      |> ignore

      rowsGrid

    let quietWord =
      match target with
      | Partner partner -> $"empty history. say hi to @{partner.Handle}"
      | Org _ -> "empty history. say hi to the org"

    let quiet =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text(quietWord)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Center)
        .IsVisible(
          thread
          |> Observable.map(fun current ->
            current.IsLoaded
            && current.Messages.IsEmpty
            && current.Pending.IsEmpty)
        )

    Panel().Children(rows, quiet)

  /// The detail column: the pane of the checked-out target — rebuilt
  /// only when the target moves — or the quiet invitation.
  let column
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      solo: Solo
    ) : Control =
    // The pane rebuilds only when the checked-out target moves. The
    // guard keys on the store's key, never the whole target: a refresh
    // that rebuilds the target's user payload must not rebuild the
    // pane under it.
    let checkedOut =
      model
      |> Observable.map _.State.OpenTarget
      |> Observable.distinctUntilChangedKey(Option.map ChatTargets.key)

    ContentControl()
      .Content(
        checkedOut
        |> Observable.map(function
          | Some target ->
            pane(props, model, dispatch, solo, target, ChatTargets.key target)
          | None -> Parts.noThread())
        |> _.ToBinding()
      )
