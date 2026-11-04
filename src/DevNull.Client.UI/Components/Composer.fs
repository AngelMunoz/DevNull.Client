module DevNull.Client.Components.Composer

open System
open System.Runtime
open Avalonia
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Commit
open DevNull.Client.Core.Completion
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Env
open DevNull.Client.ImageCache
open Microsoft.Extensions.Logging
open DevNull.Client.Search
open DevNull.Client.Stores

module Flyout = DevNull.Client.Components.Completion

[<Struct>]
type Target =
  | Fresh
  | ReplyTo of parentId: string
  | Quoting of quotedId: string

type DraftSource = {
  Seed: string
  Changed: string -> unit
}

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Target: Target
  OnCommitted: Commit -> unit
  OnScheduled: (ScheduledCommit -> unit) voption
  OnOpenQueue: (unit -> unit) voption
  Draft: DraftSource voption
  OnBodyReady: (TextBox -> unit) voption
}

// The composer's state logic: model, messages, and update.
module State =

  // The completion lane over the body: the trigger the caret sits
  // in, the rows the newest answer settled into, and the row the keys
  // hold. The issue reference the person closed by hand stays closed
  // until the text moves past it.
  type CompletionState = {
    Trigger: Trigger voption
    Users: User list
    Organizations: Organization list
    Issue: Issue option
    Active: int
    Searching: bool
    Throttled: bool
    Dismissed: (int * IssueNumber) voption
  }

  let closedCompletion = {
    Trigger = ValueNone
    Users = []
    Organizations = []
    Issue = None
    Active = 0
    Searching = false
    Throttled = false
    Dismissed = ValueNone
  }

  // The query the trigger holds, empty for a bare `@`.
  let inline queryOf(completion: CompletionState) =
    match completion.Trigger with
    | ValueSome(Mention(_, query)) -> query
    | ValueSome(IssueRef(_, digits)) -> IssueNumber.value digits
    | ValueNone -> ""

  // How many rows the keys walk: the people and organizations
  // together, or the one issue card.
  let inline completionCount(completion: CompletionState) =
    match completion.Trigger with
    | ValueSome(IssueRef _) -> if completion.Issue.IsSome then 1 else 0
    | ValueSome(Mention _)
    | ValueNone ->
      List.length completion.Users + List.length completion.Organizations

  // Whether the flyout stands: a trigger the person did not close,
  // with rows to walk, an answer on its way, a slow-down to say, or —
  // for a mention — a query whose quiet states belong on screen.
  let inline completionOpen(completion: CompletionState) =
    match completion.Trigger with
    | ValueNone -> false
    | ValueSome(IssueRef _) -> completion.Issue.IsSome
    | ValueSome(Mention _) ->
      completion.Searching
      || completion.Throttled
      || completionCount completion > 0
      || queryOf completion <> ""

  // The completion state the text and caret leave: a new trigger
  // resets the rows and the armed row, and the issue reference the
  // person closed by hand stays closed while the text still holds
  // it.
  let trackCompletion(current: CompletionState, text: string, caret: int) =

    let opened(trigger: Trigger) = {
      closedCompletion with
          Trigger = ValueSome trigger
          Searching = true
          Dismissed = current.Dismissed
    }

    match triggerAt(text, caret) with
    | ValueNone -> {
        closedCompletion with
            Dismissed = current.Dismissed
      }
    | ValueSome(IssueRef(start, digits) as trigger) ->
      if current.Dismissed = ValueSome(start, digits) then
        {
          closedCompletion with
              Dismissed = current.Dismissed
        }
      else
        opened trigger
    | ValueSome trigger -> opened trigger

  type Model = {
    Busy: bool
    Error: AppError option
    Committed: int
    Body: string
    Attachments: ImageAttachment list
    // One description per staged image, empty where undescribed; kept
    // parallel to the attachments by index.
    Descriptions: string list
    Poll: PollEditor.Draft option
    // The schedule mode: on when the next submit queues instead of
    // posting.
    Scheduling: bool
    // The picked moment; none until a day is chosen.
    Picked: SchedulePicker.Pick voption
    // The completion lane over the body.
    Completion: CompletionState
  }

  type Msg =
    | Submitted of body: string
    | BodyChanged of text: string
    | CompletionTracked of text: string * caret: int
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
    | MentionsArrived of MentionsFound
    | IssueArrived of IssueFound
    | CompletionStepped of step: int
    | CompletionHovered of index: int
    | CompletionClosed
    | CompletionPicked of CompletionLane.Choice

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
    Completion = closedCompletion
  }

  // The staging path allocates large-object arrays an idle composer
  // never generates enough churn to collect: the picker's transients
  // and every staged copy wait for a generation the resting app does
  // not run. Each transition below is where that memory dies. The
  // collection posts at background priority, after the new model
  // published and the bindings dropped the old lists. Aggressive is
  // the mode that returns the emptied regions to the OS: a Forced
  // compaction reclaims the arrays but leaves their committed regions
  // mapped, so the resting app keeps their footprint.
  let collectStaged(props: Props<'env>) : unit =
    props.Env.Dispatcher.Post(
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

  // The local now, from the env's clock.
  let localNow(props: Props<'env>) : DateTimeOffset =
    DateTimeOffset.FromUnixTimeSeconds(props.Env.Clock.UnixNow()).ToLocalTime()

  // The picked moment in local time: the day offset lands on the
  // calendar date, the hour and minute set the time, and the offset is
  // today's.
  let pickedMoment
    (now: DateTimeOffset, pick: SchedulePicker.Pick)
    : DateTimeOffset =
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

  let loggerOf(props: Props<'env>) =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Components.Composer"

  let updateCore (log: ILogger, props: Props<'env>) msg (model: Model) =
    match msg with
    | BodyChanged text ->
      let changed =
        match props.Draft with
        | ValueSome draft -> Cmd.ofEffect(fun _ -> draft.Changed text)
        | ValueNone -> Cmd.none

      // The lane re-tracks one beat later, off the settled caret; until
      // it does, the rows the old trigger held cannot stand.
      {
        model with
            Body = text
            Completion = {
              closedCompletion with
                  Dismissed = model.Completion.Dismissed
            }
      },
      changed
    | CompletionTracked(text, caret) ->
      {
        model with
            Completion = trackCompletion(model.Completion, text, caret)
      },
      Cmd.none
    | MentionsArrived found ->
      match model.Completion.Trigger with
      | ValueSome(Mention(_, query)) when query = found.Query ->
        {
          model with
              Completion = {
                model.Completion with
                    Users = found.Results.Users
                    Organizations = found.Results.Organizations
                    Searching = false
                    Throttled = found.Throttled
              }
        },
        Cmd.none
      // An answer for a trigger the caret already left behind never
      // shows.
      | ValueSome(Mention _)
      | ValueSome(IssueRef _)
      | ValueNone -> model, Cmd.none
    | IssueArrived found ->
      match model.Completion.Trigger with
      | ValueSome(IssueRef(_, digits)) when
        IssueNumber.value digits = found.Digits
        ->
        {
          model with
              Completion = {
                model.Completion with
                    Issue = found.Issue
                    Searching = false
                    Throttled = found.Throttled
              }
        },
        Cmd.none
      // An answer for a trigger the caret already left behind never
      // shows.
      | ValueSome(IssueRef _)
      | ValueSome(Mention _)
      | ValueNone -> model, Cmd.none
    | CompletionStepped step ->
      let count = completionCount model.Completion

      if count = 0 then
        model, Cmd.none
      else
        let at = (model.Completion.Active + step + count) % count

        {
          model with
              Completion = { model.Completion with Active = at }
        },
        Cmd.none
    | CompletionHovered index ->
      // A hover that lands on the row the keys already armed is quiet:
      // a pointer resting over a row must not fight the keys.
      let count = completionCount model.Completion

      if count = 0 || index = model.Completion.Active then
        model, Cmd.none
      else
        let at = Math.Max(0, Math.Min(index, count - 1))

        {
          model with
              Completion = { model.Completion with Active = at }
        },
        Cmd.none
    | CompletionClosed ->
      // An issue card closed by hand stays closed until the text moves
      // past it; anything else simply closes.
      let dismissed =
        match model.Completion.Trigger with
        | ValueSome(IssueRef(start, digits)) -> ValueSome(start, digits)
        | _ -> ValueNone

      {
        model with
            Completion = {
              closedCompletion with
                  Dismissed = dismissed
            }
      },
      Cmd.none
    | CompletionPicked(CompletionLane.PersonChosen handle) ->
      // The lane carried the handle into the box; the log keeps the
      // success beside the failures.
      model,
      Cmd.ofAction(fun () ->
        log.LogDebug("The mention completion picked {Handle}.", handle))
    | CompletionPicked(CompletionLane.IssueChosen number) ->
      model,
      Cmd.ofAction(fun () ->
        log.LogDebug("The issue completion picked {Number}.", number))
    | AttachRequested ->
      // The picker is modal; the answer arrives as one message.
      model, Cmd.OfAsync.perform props.Env.Attachments.PickFiles () ImagesAdded
    | ImagesAdded staged ->
      let combined = model.Attachments @ staged
      let reclaim = Cmd.ofEffect(fun _ -> collectStaged props)

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
      Cmd.ofEffect(fun _ -> collectStaged props)
    | DescriptionChanged(index, text) ->
      let changed =
        model.Descriptions
        |> List.indexed
        |> List.map(fun (at, current) -> if at = index then text else current)

      { model with Descriptions = changed }, Cmd.none
    | PollToggled ->
      // A poll rides a fresh commit with no images; opening one puts
      // the staged images back.
      let poll: PollEditor.Draft option =
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
        | _ -> Cmd.ofEffect(fun _ -> collectStaged props)

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
            collectStaged props)

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

          Cmd.ofEffect(fun _ -> collectStaged props)

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

  let update(props: Props<'env>) = updateCore(loggerOf props, props)

  // The completion session's answers enter the program through the
  // engine's subscription set, and the session itself retires with it.
  let subscribe (session: CompletionSession) (_: Model) : Sub<Msg> =
    Sub.batch [
      Sub.fromObservable
        "completion-mentions"
        (session.Mentions |> Observable.map MentionsArrived)

      Sub.fromObservable
        "completion-issues"
        (session.Issues |> Observable.map IssueArrived)

      Sub.ofId "completion-session" (fun _ -> session :> IDisposable)
    ]

// The composer's semantic regions; the view only composes these.
module Parts =

  // The input: one line to start, the site's textarea rhythm; the
  // inner presenter takes over past the cap. The text rides the
  // model: the one-way seed answers a recycled row's rebuild, and
  // every change reports back, so the draft outlives the box.
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
        let text = box.Text |> Option.ofObj |> Option.defaultValue ""

        dispatch(State.BodyChanged text)

        // The caret rides one beat later: a text set from code reports
        // its change before the caret move that follows it, and the
        // completion reads the caret only here.
        props.Env.Dispatcher.Post(
          (fun () ->
            let caret = min box.CaretIndex text.Length

            dispatch(State.CompletionTracked(text, caret))),
          DispatcherPriority.Default
        ))

  // The composer's own completion session: the site's suggestions,
  // gated by the composer's own reply or quote context. A reply's
  // participants change, so its composer answers every query fresh;
  // every other target remembers its answers.
  let completionSession(props: Props<'env>) : CompletionSession =
    let log =
      props.Env.LoggerFactory.CreateLogger "DevNull.Client.Components.Composer"

    let reply, quote =
      match props.Target with
      | ReplyTo parentId -> ValueSome parentId, ValueNone
      | Quoting quotedId -> ValueNone, ValueSome quotedId
      | Fresh -> ValueNone, ValueNone

    new CompletionSession(
      {
        Mentions =
          fun query ->
            Suggestions.mentions(
              props.Env,
              {
                KeyFile = props.Session.KeyFile
                Query = query
                Reply = reply
                Quote = quote
              }
            )
        Issue =
          fun digits ->
            match IssueNumber.parse digits with
            | ValueSome number ->
              Suggestions.issue(
                props.Env,
                {
                  KeyFile = props.Session.KeyFile
                  Number = number
                }
              )
            | ValueNone -> async { return Ok None }
        Remember = reply |> ValueOption.isNone
      },
      log
    )

let view
  (props: Props<'env>, session: CompletionSession)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let body = Parts.body(props, model, dispatch)

  props.OnBodyReady |> ValueOption.iter(fun ready -> ready body)

  let submit() =
    dispatch(
      State.Submitted(body.Text |> Option.ofObj |> Option.defaultValue "")
    )

  ComposerKeys.attach {
    Body = body
    Attachments = props.Env.Attachments
    Commit = ComposerKeys.ControlOrMeta
    OnCommit = submit
    OnStaged = fun image -> dispatch(State.ImagesAdded [ image ])
    OnEscape = ValueNone
  }

  let flyout =
    model
    |> Observable.map(fun current ->
      let count = State.completionCount current.Completion

      let state: Flyout.View = {
        Query = State.queryOf current.Completion
        Caption =
          match props.Target with
          | Fresh -> State.queryOf current.Completion = ""
          | ReplyTo _
          | Quoting _ -> false
        Users = current.Completion.Users
        Organizations = current.Completion.Organizations
        Issue = current.Completion.Issue
        Searching = current.Completion.Searching
        Throttled = current.Completion.Throttled
        Active =
          if count = 0 then
            0
          else
            min current.Completion.Active (count - 1)
      }

      state)
    |> Observable.distinctUntilChanged

  let completion =
    CompletionLane.view {
      Session = session
      Trigger =
        model |> Observable.map(fun current -> current.Completion.Trigger)
      Flyout = flyout
      Open =
        model
        |> Observable.map(fun current ->
          State.completionOpen current.Completion)
      Body = body
      Events =
        fun event ->
          match event with
          | CompletionLane.Stepped step ->
            dispatch(State.CompletionStepped step)
          | CompletionLane.Hovered index ->
            dispatch(State.CompletionHovered index)
          | CompletionLane.Closed -> dispatch State.CompletionClosed
          | CompletionLane.Applied(text, caret) ->
            dispatch(State.CompletionTracked(text, caret))
          | CompletionLane.Picked choice ->
            dispatch(State.CompletionPicked choice)
      Images = props.Env.ImageCache
    }

  let strip =
    AttachmentStrip.view {
      Images = model |> Observable.map _.Attachments
      OnRemove = fun index -> dispatch(State.ImageRemoved index)
      MarkHandled = false
      Descriptions =
        ValueSome(fun index -> {
          Text =
            model
            |> Observable.map(fun current ->
              current.Descriptions
              |> List.tryItem index
              |> Option.defaultValue "")
            |> Observable.distinctUntilChanged
          Changed = fun text -> dispatch(State.DescriptionChanged(index, text))
        })
      Spacing = 6.0
      HideWhenEmpty = false
    }

  let poll =
    PollEditor.view {
      Draft = model |> Observable.map _.Poll
      OnOptionChanged =
        fun (index, text) -> dispatch(State.PollOptionChanged(index, text))
      OnOptionAdded = fun () -> dispatch State.PollOptionAdded
      OnOptionRemoved = fun index -> dispatch(State.PollOptionRemoved index)
      OnDurationPicked =
        fun duration -> dispatch(State.PollDurationPicked duration)
      OnClosed = fun () -> dispatch State.PollToggled
    }

  let schedule =
    SchedulePicker.view {
      Now = State.localNow props
      Scheduling = model |> Observable.map _.Scheduling
      Picked = model |> Observable.map _.Picked
      OnDayPicked = fun day -> dispatch(State.ScheduleDayPicked day)
      OnHourPicked = fun hour -> dispatch(State.ScheduleHourPicked hour)
      OnMinutePicked = fun minute -> dispatch(State.ScheduleMinutePicked minute)
      OnOpenQueue = props.OnOpenQueue
    }

  let offersExtras =
    match props.Target with
    | Fresh -> true
    | ReplyTo _
    | Quoting _ -> false

  let toolbar =
    ComposerToolbar.view {
      Attachments = model |> Observable.map _.Attachments
      Poll = model |> Observable.map _.Poll
      Busy = model |> Observable.map _.Busy
      Scheduling = model |> Observable.map _.Scheduling
      Picked = model |> Observable.map _.Picked
      Error = model |> Observable.map _.Error
      OfferAttach =
        match props.Target with
        | Quoting _ -> false
        | Fresh
        | ReplyTo _ -> true
      OfferExtras = offersExtras
      OnAttach = fun () -> dispatch State.AttachRequested
      OnPoll = fun () -> dispatch State.PollToggled
      OnSchedule = fun () -> dispatch State.ScheduleToggled
      OnSubmit = submit
    }

  // No box: the composer rides the rail plain, the way the site's does
  // on the log and the thread; a host that wants a card wraps it.
  StackPanel()
    .Margin(0.0, 0.0, 0.0, 12.0)
    .Spacing(8.0)
    .Children(body, completion, strip, poll, schedule, toolbar)

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let session = Parts.completionSession props

    let program =
      Program.mkProgram
        (State.init props)
        (State.update props)
        (view(props, session))
      |> Program.withSubscription(State.subscribe session)

    // The composer is replace-only: the first detach ends the program.
    let root, _ = Programs.runComponent(this, props.Env.Dispatcher, program)
    this.Content <- root

    // A host left with staged images collects them on the composer's
    // one live teardown.
    this.DetachedFromVisualTree.Add(fun _ -> State.collectStaged props)
