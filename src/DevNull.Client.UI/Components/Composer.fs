module DevNull.Client.Components.Composer

open System
open System.IO
open System.Runtime
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Interactivity
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open IcedTasks
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Commit
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types
open DevNull.Client.Errors
open DevNull.Client.Env
open Microsoft.Extensions.Logging
open DevNull.Client.Icons
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Theme

/// What the composer answers to: a fresh commit, a reply, or a quote.
[<Struct>]
type Target =
  | Fresh
  | ReplyTo of parentId: string
  | Quoting of quotedId: string

/// One poll being built: two to four options and the time it stays
/// open. A poll rides a fresh commit only, never beside images.
type PollDraft = {
  Options: string list
  Duration: PollDuration
}

/// Where a host whose row recycles keeps the draft alive: the text the
/// composer starts from, and the sink every change reaches.
type DraftSource = {
  Seed: string
  Changed: string -> unit
}

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Target: Target
  OnCommitted: Commit -> unit
  OnScheduled: (ScheduledCommit -> unit) voption
  OnOpenQueue: (unit -> unit) voption
  Register: (IDisposable -> unit) voption
  Draft: DraftSource voption
}

/// The composer's state logic: model, messages, and update.
module State =

  /// The moment the person picked, as the panel takes it: a day offset
  /// from today, an hour, and a five-minute step.
  [<Struct>]
  type Pick = { Day: int; Hour: int; Minute: int }

  type Model = {
    Busy: bool
    Error: AppError option
    Committed: int
    Body: string
    Attachments: ImageAttachment list
    /// One description per staged image, empty where undescribed; kept
    /// parallel to the attachments by index.
    Descriptions: string list
    Poll: PollDraft option
    /// The schedule mode: on when the next submit queues instead of
    /// posting.
    Scheduling: bool
    /// The picked moment; none until a day is chosen.
    Picked: Pick voption
  }

  type Msg =
    | Submitted of body: string
    | BodyChanged of text: string
    | AttachRequested
    | ImagesAdded of ImageAttachment list
    | ImageRemoved of index: int
    | DescriptionChanged of index: int * text: string
    | PollToggled
    | PollOptionChanged of index: int * text: string
    | PollOptionAdded
    | PollOptionRemoved of index: int
    | PollDurationPicked of PollDuration
    | ScheduleToggled
    | ScheduleDayPicked of day: int
    | ScheduleHourPicked of hour: int
    | ScheduleMinutePicked of minute: int
    | Committed of Result<Commit, AppError>
    | Scheduled of Result<ScheduledCommit, AppError>

  let initial = {
    Busy = false
    Error = None
    Committed = 0
    Body = ""
    Attachments = []
    Descriptions = []
    Poll = None
    Scheduling = false
    Picked = ValueNone
  }

  /// The staging path allocates large-object arrays an idle composer
  /// never generates enough churn to collect: the picker's transients
  /// and every staged copy wait for a generation the resting app does
  /// not run. Each transition below is where that memory dies. The
  /// collection posts at background priority, after the new model
  /// published and the bindings dropped the old lists. Aggressive is
  /// the mode that returns the emptied regions to the OS: a Forced
  /// compaction reclaims the arrays but leaves their committed regions
  /// mapped, so the resting app keeps their footprint.
  let collectStaged() : unit =
    Dispatcher.UIThread.Post(
      (fun () ->
        GCSettings.LargeObjectHeapCompactionMode <-
          GCLargeObjectHeapCompactionMode.CompactOnce

        GC.Collect(
          2,
          GCCollectionMode.Aggressive,
          blocking = true,
          compacting = true
        )),
      DispatcherPriority.Background
    )

  let apiTarget(target: Target) : Commits.Target =
    match target with
    | Fresh -> Commits.New
    | ReplyTo parentId -> Commits.ReplyTo parentId
    | Quoting quotedId -> Commits.Quote quotedId

  /// The local now, from the env's clock.
  let localNow(props: Props<'env>) : DateTimeOffset =
    DateTimeOffset.FromUnixTimeSeconds(props.Env.Clock.UnixNow()).ToLocalTime()

  /// The picked moment in local time: the day offset lands on the
  /// calendar date, the hour and minute set the time, and the offset is
  /// today's.
  let pickedMoment(now: DateTimeOffset, pick: Pick) : DateTimeOffset =
    let date = now.DateTime.AddDays(float pick.Day)

    let withTime =
      DateTime(date.Year, date.Month, date.Day, pick.Hour, pick.Minute, 0)

    DateTimeOffset(withTime, now.Offset)

  let init (props: Props<'env>) () =
    let seeded =
      match props.Draft with
      | ValueSome draft -> draft.Seed
      | ValueNone -> ""

    { initial with Body = seeded }, Cmd.none

  let update (props: Props<'env>) msg (model: Model) =
    let log =
      props.Env.LoggerFactory.CreateLogger "DevNull.Client.Components.Composer"

    match msg with
    | BodyChanged text ->
      let changed =
        match props.Draft with
        | ValueSome draft -> Cmd.ofEffect(fun _ -> draft.Changed text)
        | ValueNone -> Cmd.none

      { model with Body = text }, changed
    | AttachRequested ->
      // The picker is modal; the answer arrives as one message.
      model, Cmd.OfAsync.perform props.Env.Attachments.PickFiles () ImagesAdded
    | ImagesAdded staged ->
      let combined = model.Attachments @ staged
      let reclaim = Cmd.ofEffect(fun _ -> collectStaged())

      // The same parse the post runs, at stage time: the count, the
      // size, and the content sniff speak before the post.
      match CommitImages.parse combined with
      | Ok _ ->
        {
          model with
              Attachments = combined
              Descriptions =
                model.Descriptions @ List.replicate (List.length staged) ""
        },
        reclaim
      | Error error ->
        { model with Error = Some error },
        Cmd.batch [
          reclaim

          Cmd.ofAction(fun () ->
            log.LogWarning(
              "The staged images were refused: {Error}",
              bounded 300 (describe error)
            ))
        ]
    | ImageRemoved index ->
      let attachments =
        model.Attachments
        |> List.indexed
        |> List.filter(fun (at, _) -> at <> index)
        |> List.map snd

      let descriptions =
        model.Descriptions
        |> List.indexed
        |> List.filter(fun (at, _) -> at <> index)
        |> List.map snd

      {
        model with
            Attachments = attachments
            Descriptions = descriptions
      },
      Cmd.ofEffect(fun _ -> collectStaged())
    | DescriptionChanged(index, text) ->
      let changed =
        model.Descriptions
        |> List.indexed
        |> List.map(fun (at, current) -> if at = index then text else current)

      { model with Descriptions = changed }, Cmd.none
    | PollToggled ->
      // A poll rides a fresh commit with no images; opening one puts
      // the staged images back.
      let poll =
        match model.Poll with
        | Some _ -> None
        | None ->
          Some {
            Options = [ ""; "" ]
            Duration = PollDuration.OneDay
          }

      // Opening a poll drops staged images, so the transition collects
      // only when images were staged.
      let reclaim =
        match model.Attachments with
        | [] -> Cmd.none
        | _ -> Cmd.ofEffect(fun _ -> collectStaged())

      {
        model with
            Poll = poll
            Attachments = []
            Descriptions = []
      },
      reclaim
    | PollOptionChanged(index, text) ->
      let changed =
        model.Poll
        |> Option.map(fun poll ->
          let options =
            poll.Options
            |> List.indexed
            |> List.map(fun (at, optionText) ->
              if at = index then text else optionText)

          { poll with Options = options })

      { model with Poll = changed }, Cmd.none
    | PollOptionAdded ->
      let changed =
        model.Poll
        |> Option.bind(fun poll ->
          if List.length poll.Options >= 4 then
            None
          else
            Some {
              poll with
                  Options = poll.Options @ [ "" ]
            })

      { model with Poll = changed }, Cmd.none
    | PollOptionRemoved index ->
      let changed =
        model.Poll
        |> Option.bind(fun poll ->
          if List.length poll.Options <= 2 then
            None
          else
            Some {
              poll with
                  Options =
                    poll.Options
                    |> List.indexed
                    |> List.filter(fun (at, _) -> at <> index)
                    |> List.map snd
            })

      { model with Poll = changed }, Cmd.none
    | PollDurationPicked duration ->
      let changed =
        model.Poll |> Option.map(fun poll -> { poll with Duration = duration })

      { model with Poll = changed }, Cmd.none
    | ScheduleToggled ->
      // The toggle resets the pick, as the site's picker does.
      {
        model with
            Scheduling = not model.Scheduling
            Picked = ValueNone
            Error = None
      },
      Cmd.none
    | ScheduleDayPicked day ->
      let picked =
        match model.Picked with
        | ValueSome pick -> ValueSome { pick with Day = day }
        | ValueNone ->
          // The site's defaults: the next hour today, nine on any other
          // day.
          let now = localNow props
          let hour = if day = 0 then min (now.Hour + 1) 23 else 9

          ValueSome { Day = day; Hour = hour; Minute = 0 }

      { model with Picked = picked }, Cmd.none
    | ScheduleHourPicked hour ->
      let picked =
        model.Picked |> ValueOption.map(fun pick -> { pick with Hour = hour })

      { model with Picked = picked }, Cmd.none
    | ScheduleMinutePicked minute ->
      let picked =
        model.Picked
        |> ValueOption.map(fun pick -> { pick with Minute = minute })

      { model with Picked = picked }, Cmd.none
    | Submitted body ->
      // The poll speaks at the post: the same parse the server runs,
      // before the request leaves.
      let pollSpec: Result<PollSpec option, AppError> =
        match model.Poll with
        | None -> Ok None
        | Some draft ->
          let trimmed =
            draft.Options
            |> List.map _.Trim()
            |> List.filter(fun option -> option <> "")

          PollSpecs.parse(trimmed, draft.Duration) |> Result.map Some

      match pollSpec with
      | Error error ->
        // The refusal keeps the box open with what was typed.
        { model with Error = Some error },
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The poll was refused: {Error}",
            bounded 300 (describe error)
          ))
      | Ok spec ->
        let descriptions =
          model.Descriptions
          |> List.map(fun text -> if text = "" then None else Some text)

        if model.Scheduling then
          // The window speaks before the request: the same bounds the
          // server runs, against the clock the env carries.
          let moment =
            model.Picked
            |> ValueOption.map(fun pick ->
              let now = localNow props
              ScheduledMoments.parse(now, pickedMoment(now, pick)))

          match moment with
          | ValueSome(Error error) ->
            { model with Error = Some error },
            Cmd.ofAction(fun () ->
              log.LogWarning(
                "The moment was refused: {Error}",
                bounded 300 (describe error)
              ))
          | ValueSome(Ok at) ->
            { model with Busy = true; Error = None },
            Cmd.OfAsync.perform
              ScheduledCommits.create
              (props.Env,
               {
                 KeyFile = props.Session.KeyFile
                 Body = body
                 Images = model.Attachments
                 ImageDescriptions = descriptions
                 Poll = spec
                 Card = FirstLink
                 ScheduledAt = at
               })
              Scheduled
          | ValueNone ->
            // No moment picked: the button stays off; a stray submit
            // leaves the panel as it is.
            model, Cmd.none
        else
          { model with Busy = true; Error = None },
          Cmd.OfAsync.perform
            Commits.create
            (props.Env,
             {
               KeyFile = props.Session.KeyFile
               Body = body
               Target = apiTarget props.Target
               Images = model.Attachments
               ImageDescriptions = descriptions
               Poll = spec
             })
            Committed
    | Committed result ->
      match result with
      | Ok commit ->
        // The host's callback (often a navigation) is a command, never
        // an inline effect: running it inside update can dispose this
        // very program before the loop publishes the model.
        {
          initial with
              Committed = model.Committed + 1
        },
        Cmd.batch [
          Cmd.ofEffect(fun _ ->
            props.OnCommitted commit
            collectStaged())

          Cmd.ofAction(fun () ->
            log.LogInformation("The commit {Hash} was posted.", commit.Id))
        ]
      | Error error ->
        {
          model with
              Busy = false
              Error = Some error
        },
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The commit was not posted: {Error}",
            bounded 300 (describe error)
          ))
    | Scheduled result ->
      match result with
      | Ok queued ->
        {
          initial with
              Committed = model.Committed + 1
        },
        Cmd.batch [
          match props.OnScheduled with
          | ValueSome on -> Cmd.ofEffect(fun _ -> on queued)
          | ValueNone -> ()

          Cmd.ofEffect(fun _ -> collectStaged())

          Cmd.ofAction(fun () ->
            log.LogInformation(
              "The commit {Id} was scheduled for {Moment}.",
              queued.Id,
              queued.ScheduledAt
            ))
        ]
      | Error error ->
        {
          model with
              Busy = false
              Error = Some error
        },
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The commit was not scheduled: {Error}",
            bounded 300 (describe error)
          ))

/// The composer's semantic regions; the view only composes these.
module Parts =

  /// Sets one control's tooltip and returns it, so the composition stays
  /// one expression.
  let withTip (text: string) (control: Control) : Control =
    ToolTip.SetTip(control, text)
    control

  /// The input: one line to start, the site's textarea rhythm; the
  /// inner presenter takes over past the cap. The text rides the
  /// model: the one-way seed answers a recycled row's rebuild, and
  /// every change reports back, so the draft outlives the box.
  let body
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : TextBox =
    let placeholder =
      match props.Target with
      | Fresh -> "What did you ship?"
      | ReplyTo _ -> "Write your reply…"
      | Quoting _ -> "Add your take"

    TextBox()
      .Name("ComposerBody")
      .AcceptsReturn(true)
      .MinHeight(44.0)
      .MaxHeight(300.0)
      .TextWrapping(TextWrapping.Wrap)
      .PlaceholderText(
        model
        |> Observable.map(fun current ->
          match props.Target, current.Poll with
          | Fresh, Some _ -> "Ask a question"
          | _ -> placeholder)
        |> Observable.distinctUntilChanged
      )
      .Text(
        model
        |> Observable.map _.Body
        |> Observable.distinctUntilChanged
        |> _.ToBinding()
      )
      .OnTextChangedHandler(fun box _ ->
        box.Text
        |> Option.ofObj
        |> Option.defaultValue ""
        |> State.BodyChanged
        |> dispatch)

  /// The box's key lane. The platform's paste gesture asks the
  /// attachment sources first — a clipboard image stages, clipboard
  /// text lands in the box — and the platform's commit pair
  /// (Ctrl/Cmd+Enter) posts the body, the way the site's composer
  /// does. Both are always handled, so the box's own text handling
  /// never races them.
  let keyLane
    (props: Props<'env>, dispatch: Dispatch<State.Msg>, body: TextBox)
    : unit =
    let pasteMatches(args: KeyEventArgs) =
      match Application.Current with
      | null -> false
      | app ->
        match app.PlatformSettings with
        | null -> false
        | settings ->
          settings.HotkeyConfiguration.Paste
          |> Seq.exists(fun gesture -> gesture.Matches args)

    let commitPair(args: KeyEventArgs) =
      args.Key = Key.Enter
      && args.KeyModifiers &&& (KeyModifiers.Control ||| KeyModifiers.Meta)
         <> KeyModifiers.None

    body.AddHandler(
      InputElement.KeyDownEvent,
      (fun _ args ->
        if commitPair args then
          args.Handled <- true

          let text = body.Text |> Option.ofObj |> Option.defaultValue ""

          dispatch(State.Submitted text)
        elif pasteMatches args then
          args.Handled <- true

          asyncEx {
            let! image = props.Env.Attachments.ReadClipboardImage()

            match image with
            | Some staged ->
              Dispatcher.UIThread.Post(fun () ->
                dispatch(State.ImagesAdded [ staged ]))
            | None ->
              let! text = props.Env.Attachments.ReadClipboardText()

              match text with
              | Some pasted ->
                Dispatcher.UIThread.Post(fun () -> body.SelectedText <- pasted)
              | None -> ()
          }
          |> Async.StartImmediate),
      RoutingStrategies.Tunnel
    )

  /// Every landed commit leaves the box empty for the next one: the
  /// model resets its body, and the box's one-way text binding follows.
  /// The error line under the input.
  let errorLine(model: IObservable<State.Model>) : Control =
    TextBlock()
      .TextWrapping(TextWrapping.Wrap)
      .Classes(Theme.name Theme.FgDanger)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Error |> Option.map describe |> Option.defaultValue "")
      )
      .IsVisible(model |> Observable.map _.Error.IsSome)

  /// One staged image chip: the bytes decoded small, the file name,
  /// its remove button, and the optional description the post carries
  /// for screen readers, with the server's own bound counted live.
  let chip
    (
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      index: int,
      staged: ImageAttachment
    ) : Control =
    // The preview decodes to the chip's own width: staged photos run
    // phone-camera resolutions, and one full decode costs tens of
    // megabytes of native pixels for 48 on-screen ones. The decoded
    // bitmap is native memory, so the chip image disposes it when the
    // strip rebuilds and drops it.
    let bitmap =
      use stream = new MemoryStream(staged.Bytes)
      Bitmap.DecodeToWidth(stream, 96)

    let preview =
      Avalonia.Controls
        .Image()
        .Source(bitmap)
        .Width(48.0)
        .Height(48.0)
        .Stretch(Stretch.UniformToFill)

    preview.DetachedFromVisualTree.Add(fun _ -> bitmap.Dispose())

    let descriptionText(current: State.Model) =
      current.Descriptions |> List.tryItem index |> Option.defaultValue ""

    let described =
      TextBox()
        .Name($"ImageDescription{index}")
        .Width(190.0)
        .PlaceholderText("Describe this image")
        .Text(
          model
          |> Observable.map descriptionText
          |> Observable.distinctUntilChanged
          |> _.ToBinding()
        )
        .OnTextChangedHandler(fun box _ ->
          let text = box.Text |> Option.ofObj |> Option.defaultValue ""

          dispatch(State.DescriptionChanged(index, text)))

    let counter =
      TextBlock()
        .Classes(Theme.name Theme.Mono, Theme.name Theme.Muted)
        .FontSize(11.0)
        .HorizontalAlignment(HorizontalAlignment.Right)
        .Text(
          model
          |> Observable.map(fun current ->
            let length = (descriptionText current).Length

            if length = 0 then "" else $"{length}/1000")
          |> Observable.distinctUntilChanged
        )

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
                  .Text(staged.FileName)
                  .MaxWidth(140.0)
                  .TextTrimming(TextTrimming.CharacterEllipsis),
                Button()
                  .Name($"RemoveImage{index}")
                  .Classes(Theme.name Theme.GhostButton)
                  .Padding(4.0, 0.0, 4.0, 0.0)
                  .Content("remove")
                  .OnClickHandler(fun _ _ -> dispatch(State.ImageRemoved index)),
                described,
                counter
              )
          )
      )

  /// The staged strip above the action row. It rebuilds only when the
  /// attachment list itself moves: every keystroke changes the model,
  /// and re-decoding every chip per keystroke was tens of megabytes of
  /// native churn per key.
  let attachmentStrip
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    ContentControl()
      .Content(
        model
        |> Observable.map _.Attachments
        |> Observable.distinctUntilChanged
        |> Observable.map(function
          | [] -> Panel() :> Control
          | staged ->
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(6.0)
              .Children(
                [|
                  for index, image in List.indexed staged ->
                    chip(model, dispatch, index, image)
                |]
              )
            :> Control)
        |> _.ToBinding()
      )

  /// The attach button: the image glyph, a live count, disabled past
  /// three staged files and while a poll is open — the server takes a
  /// poll or images, never both. A quote target hides it.
  let attachButton
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let count =
      model |> Observable.map(fun current -> $"{current.Attachments.Length}/3")

    match props.Target with
    | Quoting _ -> Panel() :> Control
    | _ ->
      Button()
        .Name("AttachButton")
        .Classes(Theme.name Theme.GhostButton)
        .Padding(6.0, 2.0, 6.0, 2.0)
        .Content(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(4.0)
            .Children(
              Icons.iconAt(Glyph.Image, 16.0),
              TextBlock().Classes(Theme.name Theme.Mono).Text(count)
            )
        )
        .IsEnabled(
          model
          |> Observable.map(fun current ->
            current.Attachments.Length < 3 && current.Poll.IsNone)
        )
        .OnClickHandler(fun _ _ -> dispatch State.AttachRequested)

  /// The poll button: the site's icon-only entry that opens the builder.
  /// A poll rides a fresh commit with no images, so images disable it
  /// and a reply or quote hides it.
  let pollButton
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let offered =
      match props.Target with
      | Fresh -> true
      | _ -> false

    Button()
      .Name("PollButton")
      .Classes(Theme.name Theme.GhostButton)
      .Width(32.0)
      .Height(32.0)
      .Padding(Thickness(0.0))
      .Content(Icons.iconAt(Glyph.Poll, 20.0))
      .IsVisible(offered)
      .IsEnabled(
        model
        |> Observable.map(fun current ->
          current.Attachments.IsEmpty && current.Poll.IsNone)
      )
      .OnClickHandler(fun _ _ -> dispatch State.PollToggled)
    |> withTip "Add poll"

  /// The schedule button: the clock entry beside the poll button. A
  /// reply or quote hides it — the server takes a scheduled commit as
  /// neither.
  let scheduleButton
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let offered =
      match props.Target with
      | Fresh -> true
      | _ -> false

    Button()
      .Name("ScheduleButton")
      .Classes(Theme.name Theme.GhostButton)
      .Width(32.0)
      .Height(32.0)
      .Padding(Thickness(0.0))
      .Content(Icons.iconAt(Glyph.Clock, 20.0))
      .IsVisible(offered)
      .IsEnabled(model |> Observable.map(fun current -> not current.Busy))
      .OnClickHandler(fun _ _ -> dispatch State.ScheduleToggled)
    |> withTip "Schedule for later"

  /// One option box of the poll builder: the text rides the model the
  /// way the body does, so the structure rebuilds only when the option
  /// count moves. The site caps an option at 50 characters.
  let pollOptionBox
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>, index: int)
    : Control =
    let optionText(current: State.Model) : string =
      current.Poll
      |> Option.bind(fun poll -> List.tryItem index poll.Options)
      |> Option.defaultValue ""

    TextBox()
      .Name($"PollOption{index}")
      .PlaceholderText($"Option {index + 1}")
      .MinHeight(36.0)
      .FontSize(15.0)
      .MaxLength(50)
      .Text(
        model
        |> Observable.map optionText
        |> Observable.distinctUntilChanged
        |> _.ToBinding()
      )
      .OnTextChangedHandler(fun box _ ->
        let text = box.Text |> Option.ofObj |> Option.defaultValue ""

        dispatch(State.PollOptionChanged(index, text)))

  /// The row of one poll option: its box and, past the first two, the
  /// remove button the site draws as an X.
  let pollOptionRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>, index: int)
    : Control =
    let remove =
      Button()
        .Name($"PollOptionRemove{index}")
        .Classes(Theme.name Theme.GhostButton)
        .Width(28.0)
        .Height(28.0)
        .Padding(Thickness(0.0))
        .Margin(8.0, 0.0, 0.0, 0.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Content(Icons.iconAt(Glyph.Close, 16.0))
        .IsVisible(
          model
          |> Observable.map(fun current ->
            current.Poll
            |> Option.map(fun poll -> List.length poll.Options > 2)
            |> Option.defaultValue false)
        )
        .DockRight()
        .OnClickHandler(fun _ _ -> dispatch(State.PollOptionRemoved index))
      |> withTip $"Remove option {index + 1}"

    DockPanel()
      .LastChildFill(true)
      .Children(remove, pollOptionBox(model, dispatch, index))

  /// The poll length picker: the site's select, with its four
  /// durations, and the model's pick as the selected item.
  let pollLength
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let labels = [| "1 hour"; "1 day"; "3 days"; "7 days" |]

    let indexOf(duration: PollDuration) : int =
      match duration with
      | OneHour -> 0
      | OneDay -> 1
      | ThreeDays -> 2
      | SevenDays -> 3

    let durationAt(index: int) : PollDuration =
      match index with
      | 0 -> OneHour
      | 1 -> OneDay
      | 2 -> ThreeDays
      | _ -> SevenDays

    ComboBox()
      .Name("PollLength")
      .Width(128.0)
      .MinHeight(32.0)
      .ItemsSource(labels)
      .SelectedIndex(
        model
        |> Observable.map(fun current ->
          current.Poll
          |> Option.map(fun poll -> indexOf poll.Duration)
          |> Option.defaultValue 1)
        |> Observable.distinctUntilChanged
      )
      .OnSelectionChangedHandler(fun picker _ ->
        if picker.SelectedIndex >= 0 then
          dispatch(State.PollDurationPicked(durationAt picker.SelectedIndex)))

  /// The poll builder under the body: the bordered fieldset of the
  /// site, its option rows, the add button, and the footer with the
  /// length select and the remove action. The structure rebuilds only
  /// when the option count moves, so the boxes keep their carets while
  /// typing.
  let pollPanel
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let built(count: int) : Control =
      match count with
      | 0 -> Panel() :> Control
      | _ ->
        let optionRows: Control array = [|
          for index in 0 .. count - 1 do
            pollOptionRow(model, dispatch, index)
        |]

        let add =
          Button()
            .Name("PollOptionAdd")
            .Classes(Theme.name Theme.GhostButton, Theme.name Theme.FgAccent)
            .Padding(6.0, 2.0, 6.0, 2.0)
            .HorizontalAlignment(HorizontalAlignment.Left)
            .Content(
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(6.0)
                .Children(
                  Icons.iconAt(Glyph.Plus, 16.0),
                  TextBlock().Text("Add option")
                )
            )
            .IsVisible(
              model
              |> Observable.map(fun current ->
                current.Poll
                |> Option.map(fun poll -> List.length poll.Options < 4)
                |> Option.defaultValue false)
            )
            .OnClickHandler(fun _ _ -> dispatch State.PollOptionAdded)

        let length =
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(
              TextBlock()
                .Classes(Theme.name Theme.Muted)
                .Text("Poll length")
                .VerticalAlignment(VerticalAlignment.Center),
              pollLength(model, dispatch)
            )

        let remove =
          Button()
            .Name("PollClose")
            .Classes(Theme.name Theme.GhostButton, Theme.name Theme.PollRemove)
            .Padding(6.0, 2.0, 6.0, 2.0)
            .Content("Remove poll")
            .DockRight()
            .OnClickHandler(fun _ _ -> dispatch State.PollToggled)

        let footer =
          Border()
            .Classes(Theme.name Theme.Hairline)
            .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
            .Padding(Thickness(0.0, 12.0, 0.0, 0.0))
            .Child(DockPanel().LastChildFill(true).Children(remove, length))

        let children: Control array = [| yield! optionRows; add; footer |]

        Border()
          .Classes(Theme.name Theme.Hairline)
          .BorderThickness(Thickness(1.0))
          .CornerRadius(8.0)
          .Padding(12.0)
          .Child(StackPanel().Spacing(8.0).Children(children))
        :> Control

    ContentControl()
      .Content(
        model
        |> Observable.map(fun current ->
          current.Poll
          |> Option.map(fun poll -> List.length poll.Options)
          |> Option.defaultValue 0)
        |> Observable.distinctUntilChanged
        |> Observable.map built
        |> _.ToBinding()
      )

  /// The labels of the day list: today, tomorrow, then the calendar
  /// dates of the window the server allows.
  let dayLabels(now: DateTimeOffset) : string array = [|
    for day in 0 .. ScheduledMoments.maxDays ->
      match day with
      | 0 -> "Today"
      | 1 -> "Tomorrow"
      | _ -> now.DateTime.AddDays(float day).ToString("MMM d")
  |]

  /// The hour labels, two digits, twenty-four hours.
  let hourLabels: string array = [| for hour in 0..23 -> hour.ToString("00") |]

  /// The minute labels, one per five-minute step.
  let minuteLabels: string array = [|
    for step in 0..11 -> (step * 5).ToString("00")
  |]

  /// The schedule panel under the poll builder: the site's picker as
  /// three selects — a day of the window, an hour, a five-minute step —
  /// the window's help line, and the link into the queue. The hour and
  /// minute stay off until a day is picked, the way the site's picker
  /// does.
  let schedulePanel
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let now = State.localNow props

    let dayCombo =
      ComboBox()
        .Name("ScheduleDay")
        .MinWidth(128.0)
        .MinHeight(32.0)
        .ItemsSource(dayLabels now)
        .SelectedIndex(
          model
          |> Observable.map(fun current ->
            current.Picked
            |> ValueOption.map _.Day
            |> ValueOption.defaultValue -1)
          |> Observable.distinctUntilChanged
        )
        .OnSelectionChangedHandler(fun picker _ ->
          if picker.SelectedIndex >= 0 then
            dispatch(State.ScheduleDayPicked picker.SelectedIndex))

    let hourCombo =
      ComboBox()
        .Name("ScheduleHour")
        .MinWidth(72.0)
        .MinHeight(32.0)
        .ItemsSource(hourLabels)
        .IsEnabled(
          model |> Observable.map(fun current -> current.Picked.IsSome)
        )
        .SelectedIndex(
          model
          |> Observable.map(fun current ->
            current.Picked
            |> ValueOption.map _.Hour
            |> ValueOption.defaultValue -1)
          |> Observable.distinctUntilChanged
        )
        .OnSelectionChangedHandler(fun picker _ ->
          if picker.SelectedIndex >= 0 then
            dispatch(State.ScheduleHourPicked picker.SelectedIndex))

    let minuteCombo =
      ComboBox()
        .Name("ScheduleMinute")
        .MinWidth(72.0)
        .MinHeight(32.0)
        .ItemsSource(minuteLabels)
        .IsEnabled(
          model |> Observable.map(fun current -> current.Picked.IsSome)
        )
        .SelectedIndex(
          model
          |> Observable.map(fun current ->
            current.Picked
            |> ValueOption.map(fun pick -> pick.Minute / 5)
            |> ValueOption.defaultValue -1)
          |> Observable.distinctUntilChanged
        )
        .OnSelectionChangedHandler(fun picker _ ->
          if picker.SelectedIndex >= 0 then
            dispatch(State.ScheduleMinutePicked(picker.SelectedIndex * 5)))

    let pickerRow =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Muted)
            .Text("Submit at")
            .VerticalAlignment(VerticalAlignment.Center),
          dayCombo,
          hourCombo,
          TextBlock().Classes(Theme.name Theme.Muted).Text(":"),
          minuteCombo
        )

    let help =
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .FontSize(12.0)
        .TextWrapping(TextWrapping.Wrap)
        .Text(
          $"Within {ScheduledMoments.maxDays} days. Private until submitted; then submitted for review, not published automatically."
        )

    let queueLink =
      match props.OnOpenQueue with
      | ValueSome openQueue ->
        Button()
          .Name("ScheduledCommitsLink")
          .Classes(Theme.name Theme.GhostButton, Theme.name Theme.FgAccent)
          .Padding(6.0, 2.0, 6.0, 2.0)
          .HorizontalAlignment(HorizontalAlignment.Left)
          .Content("Scheduled commits →")
          .OnClickHandler(fun _ _ -> openQueue())
        :> Control
      | ValueNone -> Panel() :> Control

    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .Padding(12.0)
      .Child(StackPanel().Spacing(8.0).Children(pickerRow, help, queueLink))
      .IsVisible(model |> Observable.map _.Scheduling)

  /// The action row: the attach, poll, and schedule buttons and the
  /// commit button docked right, the error line filling the rest.
  let actionRow
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      body: TextBox
    ) : Control =
    let commitLabel(current: State.Model) =
      if current.Scheduling then "Schedule commit" else "Commit"

    DockPanel()
      .LastChildFill(true)
      .Children(
        Button()
          .Classes(Theme.name Theme.AccentButton)
          .Content(
            TextBlock()
              .Text(
                model
                |> Observable.map commitLabel
                |> Observable.distinctUntilChanged
              )
          )
          .IsEnabled(
            model
            |> Observable.map(fun current ->
              not current.Busy
              && (not current.Scheduling || current.Picked.IsSome))
          )
          .OnClickHandler(fun _ _ ->
            dispatch(
              State.Submitted(
                body.Text |> Option.ofObj |> Option.defaultValue ""
              )
            ))
          .DockRight(),
        attachButton(props, model, dispatch),
        pollButton(props, model, dispatch),
        scheduleButton(props, model, dispatch),
        errorLine model
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let body = Parts.body(props, model, dispatch)

  Parts.keyLane(props, dispatch, body)

  // No box: the composer rides the rail plain, the way the site's does
  // on the log and the thread; a host that wants a card wraps it.
  StackPanel()
    .Margin(0.0, 0.0, 0.0, 12.0)
    .Spacing(8.0)
    .Children(
      body,
      Parts.attachmentStrip(model, dispatch),
      Parts.pollPanel(model, dispatch),
      Parts.schedulePanel(props, model, dispatch),
      Parts.actionRow(props, model, dispatch, body)
    )

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)

    match props.Register with
    | ValueNone ->
      let root, _ = Program.runWithLifetime this () program
      this.Content <- root
    | ValueSome register ->
      let root, lifetime = Program.runWithLifecycle () program
      this.Content <- root

      // A cached page ends without touching the composer's branches
      // above, so the lifetime's end is the one transition that covers
      // a page left with staged images.
      register
        {
          new IDisposable with
            member _.Dispose() =
              lifetime.Dispose()
              State.collectStaged()
        }
