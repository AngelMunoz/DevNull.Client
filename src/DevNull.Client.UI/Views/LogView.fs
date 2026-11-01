module DevNull.Client.Views.LogView

open System
open System.Collections.Generic
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open IcedTasks
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open Navs
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Env
open Microsoft.Extensions.Logging
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.Navigation
open DevNull.Client.Profiles
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.Timeline
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore
open DevNull.Client.TranslationCache

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLastVisit
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideSshKeys
  and 'env :> IProvideUserSettings
  and 'env :> IProvideTranslations
  and 'env :> IProvideLogging> = {
  Env: 'env
  Log: LogKind
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

/// One row of the log feed: a day separator, a commit on the rail, a
/// deletion marker, or the last-visit separator.
type LogRow =
  | Day of string
  | Node of Commit
  | TombstoneRow of Tombstone
  | LastVisitRow

/// The visit the marker reads: when it was and who is reading.
[<Struct>]
type VisitMarker = {
  VisitedAt: DateTimeOffset
  ViewerId: string
}

/// The page's state logic: model, messages, update, and the
/// subscriptions that feed it from outside.
module State =

  /// One thread preview the page grew past the payload slice: the
  /// replies shown and the cursor the next page asks for.
  [<Struct>]
  type PreviewState = {
    Shown: CommitNode list
    Cursor: string option
  }

  type Model = {
    Loading: bool
    Error: AppError option
    Items: CommitNode list
    NextCursor: string option
    Head: string option
    NewCount: int option
    Ranked: bool
    Attached: bool
    Now: DateTimeOffset
    Pasteis: bool
    ReplyingTo: string option
    /// The commit whose poll vote is in flight, so its buttons disable.
    Voting: string option
    /// The option a viewer picked in each open poll, by commit id.
    PollChoices: Map<string, string>
    /// The visit captured when the page mounted; the rendered marker
    /// keeps pointing there for the session even as the store advances.
    Marker: DateTimeOffset option
    /// The thread previews the page grew past the payload slice, by
    /// parent id.
    Previews: Map<string, PreviewState>
    /// Whether translations show by themselves.
    Automatic: bool
    /// Every commit's translation on this page.
    Translations: Translation.Book
    /// Whether the page already asked for its automatic translations.
    AutoRan: bool
  }

  type Msg =
    | Fetched of Result<LogPage, AppError>
    | ReloadRequested
    | Appended of Result<LogPage, AppError>
    | MoreRequested
    | StarToggled of Commit
    | StarDone of Result<Commit, AppError>
    | PastelGiven of Commit
    | PastelDone of Result<Commit, AppError>
    | CheckNew
    | NewChecked of Result<LogPage, AppError>
    | NewRequested
    | NewFetched of Result<LogPage, AppError>
    | AttachedChanged of attached: bool
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState
    | ReplyToggled of Commit
    | ReplyPosted of commitId: string
    | PollChoiceChanged of Commit * PollOption * chosen: bool
    | PollVoteRequested of Commit
    | PollVotedDone of Result<Commit, AppError>
    | PreviewMoreRequested of Commit
    | PreviewLoaded of commitId: string * Result<Page<CommitNode>, AppError>
    | TranslateRequested of Commit
    | OriginalRequested of Commit
    | TranslationDone of Commit * Result<TranslationPayload, AppError>
    | TranslationsAuto of (Commit * Result<TranslationPayload, AppError>) list
    | TranslationRecalled of Commit * Translation.Book option
    | PrefsArrived of settings: UserSettings option

  let initial = {
    Loading = true
    Error = None
    Items = []
    NextCursor = None
    Head = None
    NewCount = None
    Ranked = false
    Attached = false
    Now = DateTimeOffset.UtcNow
    Pasteis = false
    ReplyingTo = None
    Voting = None
    PollChoices = Map.empty
    Marker = None
    Previews = Map.empty
    Automatic = false
    Translations = Translation.initial
    AutoRan = false
  }

  /// The id of the newest commit in a page: the head the since-poll
  /// checks against.
  let headOf(items: CommitNode list) : string option =
    items
    |> List.tryPick(function
      | LiveCommit commit -> Some commit.Id
      | Tombstone _ -> None)

  let fetch(props: Props<'env>, cursor: string option) =
    Logs.get(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        Log = props.Log
        Since = None
        Cursor = cursor
      }
    )

  let checkNew(props: Props<'env>, head: string) =
    Logs.get(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        Log = props.Log
        Since = Some head
        Cursor = None
      }
    )

  let init (props: Props<'env>) () =
    // The visit is read once per mount, the way the site holds the
    // marker's position for the whole session.
    let marker =
      props.Env.LastVisit.LastVisit(
        props.Session.Session.User.Id,
        LogKinds.wire props.Log
      )

    {
      initial with
          Pasteis = props.Session.Session.PasteisMode
          Automatic = props.Session.Session.AutomaticTranslation
          Translations =
            Translation.seed(
              (props.Env :> IProvideTranslations).Translations.Snapshot()
            )
          Marker = marker
    },
    Cmd.OfAsync.perform fetch (props, None) Fetched

  /// The bare translate call for one commit, in the language the settings
  /// choose.
  let translateCall(props: Props<'env>, commit: Commit) =
    let language =
      Translation.languageFor(
        (props.Env :> IProvideUserSettings).UserSettings.Current,
        commit
      )

    Commits.translate(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        CommitId = commit.Id
        Language = language
      }
    )

  /// Runs one commit's translate call: the language its own translation or
  /// the settings pick, and the 503 retries.
  let translate(props: Props<'env>, commit: Commit) =
    Translation.execute(
      Translation.retryDelays,
      fun () -> translateCall(props, commit)
    )

  /// Asks for the page's unknown translations, one at a time, up to the
  /// budget a first screen needs.
  let autoTranslations(props: Props<'env>, commits: Commit list) =
    Translation.auto((fun commit -> translateCall(props, commit)), 4, commits)

  /// Records this visit's newest commit as the next marker, once the
  /// feed has items to take it from.
  let private markVisit(props: Props<'env>, items: CommitNode list) : Cmd<Msg> =
    match items with
    | [] -> Cmd.none
    | first :: _ ->
      let newest =
        match first with
        | LiveCommit commit -> commit.CreatedAt
        | Tombstone _ -> DateTimeOffset.UtcNow

      Cmd.ofEffect(fun _ ->
        props.Env.LastVisit.Mark(
          props.Session.Session.User.Id,
          LogKinds.wire props.Log,
          newest
        ))

  let replaceNode(items: CommitNode list, updated: Commit) : CommitNode list =
    items
    |> List.map(function
      | LiveCommit commit when commit.Id = updated.Id -> LiveCommit updated
      | node -> node)

  /// The batch that asks for the page's unknown translations, once the
  /// settings say translations show by themselves. The batch's commits go
  /// busy at once, so a click cannot double a running ask.
  let autoBatch(props: Props<'env>, model: Model) : Model * Cmd<Msg> =
    let automatic =
      (props.Env :> IProvideUserSettings).UserSettings.Current
      |> Option.exists(fun settings -> settings.AutomaticTranslation)

    let pending =
      if automatic && not model.AutoRan then
        Translation.pending(
          model.Translations,
          model.Items
          |> List.choose(function
            | LiveCommit commit -> Some commit
            | Tombstone _ -> None)
        )
      else
        []

    if pending.IsEmpty then
      model, Cmd.none
    else
      let marked =
        pending
        |> List.fold
          (fun book commit -> Translation.request(book, commit.Id))
          model.Translations

      {
        model with
            Translations = marked
            AutoRan = true
      },
      Cmd.OfAsync.perform autoTranslations (props, pending) TranslationsAuto

  let update (props: Props<'env>) msg model =
    let log = props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Log"
    let kind = LogKinds.wire props.Log

    // Every log line is an effect: update returns it as a command,
    // and the loop writes it after the computation.
    let failure = CommitActions.failure log

    let failed what error =
      Cmd.ofAction(fun () -> failure what error)

    match msg with
    | Fetched result ->
      match result with
      | Ok page ->
        let next, batch =
          autoBatch(
            props,
            {
              model with
                  Loading = false
                  Error = None
                  Items = page.Page.Items
                  NextCursor = page.Page.NextCursor
                  Head = headOf page.Page.Items
                  NewCount = None
                  Ranked = page.IsRanked
            }
          )

        next,
        Cmd.batch [
          markVisit(props, page.Page.Items)

          Cmd.ofAction(fun () ->
            log.LogDebug(
              "The {Log} log fetched {Count} commits.",
              kind,
              page.Page.Items.Length
            ))

          batch
        ]
      | Error error ->
        {
          model with
              Loading = false
              Error = Some error
        },
        failed $"the {kind} log fetch" error
    | CheckNew ->
      // A ranked log is score ordered, so "newer than head" means
      // nothing; the site skips the poll there too.
      match model.Head, model.Ranked with
      | Some head, false ->
        model, Cmd.OfAsync.perform checkNew (props, head) NewChecked
      | _ -> model, Cmd.none
    | NewChecked result ->
      match result with
      | Ok page ->
        // The since read answers the same first page; only its count is
        // news. The commits themselves come with the pill's refresh.
        {
          model with
              NewCount =
                page.NewCommitsCount |> Option.filter(fun count -> count > 0)
        },
        Cmd.none
      | Error error -> model, failed "the new-commit check" error
    | NewRequested ->
      { model with NewCount = None },
      Cmd.OfAsync.perform fetch (props, None) NewFetched
    | NewFetched result ->
      match result with
      | Ok page ->
        // The pill's refresh replaces the feed with the fresh first
        // page, the way the site reloads its log; no duplicate can
        // survive. The reader lands at the top with the new commits:
        // the keyed diff keeps the scroller's old place, so the page
        // itself must ask for the scroll.
        let replaced = {
          model with
              Items = page.Page.Items
              NextCursor = page.Page.NextCursor
              Head = headOf page.Page.Items
              Ranked = page.IsRanked
        }

        replaced,
        Cmd.batch [
          Cmd.ofEffect(fun _ -> props.Env.Timeline.ScrollToTop())

          Cmd.ofAction(fun () ->
            log.LogDebug(
              "The {Log} log refreshed with {Count} commits.",
              kind,
              page.Page.Items.Length
            ))
        ]
      | Error error ->
        { model with Error = Some error },
        failed $"the {kind} log refresh" error
    | ReloadRequested ->
      {
        model with
            Loading = true
            Error = None
      },
      Cmd.OfAsync.perform fetch (props, None) Fetched
    | ReplyToggled commit ->
      let replyingTo =
        if model.ReplyingTo = Some commit.Id then
          None
        else
          Some commit.Id

      { model with ReplyingTo = replyingTo }, Cmd.none
    | ReplyPosted commitId ->
      // A landed reply closes its composer and refreshes the feed, so
      // the new reply arrives through the same read as everything
      // else. Its draft is spent.
      { model with ReplyingTo = None },
      Cmd.ofEffect(fun dispatch' -> dispatch' ReloadRequested)
    | PollChoiceChanged(commit, option, chosen) ->
      {
        model with
            PollChoices =
              CommitCard.choose {
                Choices = model.PollChoices
                CommitId = commit.Id
                OptionId = option.Id
                Chosen = chosen
              }
      },
      Cmd.none
    | PollVoteRequested commit ->
      match CommitCard.selectedOption(model.PollChoices, commit) with
      | None -> model, Cmd.none
      | Some option ->
        {
          model with
              Error = None
              Voting = Some commit.Id
        },
        Cmd.OfAsync.perform
          CommitActions.vote
          (props.Env,
           {
             Session = props.Session
             Commit = commit
             Option = option
           })
          PollVotedDone
    | PollVotedDone result ->
      match result with
      | Ok updated ->
        {
          model with
              Items = replaceNode(model.Items, updated)
              Voting = None
              PollChoices = model.PollChoices |> Map.remove updated.Id
        },
        Cmd.none
      | Error error ->
        {
          model with
              Error = Some error
              Voting = None
        },
        failed "the poll vote" error
    | PreviewMoreRequested commit ->
      // The first ask reads the newest page whole, the way the site's
      // preview does; the next ones walk the cursor it left.
      let cursor =
        model.Previews |> Map.tryFind commit.Id |> Option.bind _.Cursor

      model,
      Cmd.OfAsync.perform
        Commits.replies
        (props.Env,
         {
           KeyFile = props.Session.KeyFile
           CommitId = commit.Id
           Newest = true
           Cursor = cursor
         })
        (fun page -> PreviewLoaded(commit.Id, page))
    | PreviewLoaded(commitId, result) ->
      match result with
      | Ok page ->
        let known =
          model.Previews
          |> Map.tryFind commitId
          |> Option.map _.Shown
          |> Option.defaultValue []

        let seen = known |> List.map FeedParts.nodeKey |> Set.ofList

        let fresh =
          page.Items
          |> List.filter(fun node ->
            seen |> Set.contains(FeedParts.nodeKey node) |> not)

        let grown = {
          Shown = known @ fresh
          Cursor = page.NextCursor
        }

        {
          model with
              Previews = model.Previews |> Map.add commitId grown
        },
        Cmd.none
      | Error error ->
        { model with Error = Some error }, failed "the thread preview" error
    | MoreRequested ->
      // A cursor means the next page; no cursor means the feed ended,
      // so a restore chase past the end asks for nothing.
      match model.NextCursor with
      | Some _ ->
        model,
        Cmd.batch [
          Cmd.OfAsync.perform fetch (props, model.NextCursor) Appended

          Cmd.ofAction(fun () ->
            log.LogTrace("The {Log} log asks for its next page.", kind))
        ]
      | None -> model, Cmd.none
    | Appended result ->
      match result with
      | Ok page ->
        {
          model with
              Items = model.Items @ page.Page.Items
              NextCursor = page.Page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The {Log} log grew by {Count} commits.",
            kind,
            page.Page.Items.Length
          ))
      | Error error ->
        { model with Error = Some error }, failed "the next page" error
    | StarToggled commit ->
      { model with Error = None },
      Cmd.OfAsync.perform
        CommitActions.toggleStar
        (props.Env, props.Session, commit)
        StarDone
    | StarDone result ->
      match result with
      | Ok updated ->
        {
          model with
              Items = replaceNode(model.Items, updated)
        },
        Cmd.none
      | Error error ->
        { model with Error = Some error }, failed "the star" error
    | PastelGiven commit ->
      { model with Error = None },
      Cmd.OfAsync.perform
        CommitActions.pastel
        (props.Env,
         {
           Session = props.Session
           Commit = commit
         })
        PastelDone
    | PastelDone result ->
      match result with
      | Ok updated ->
        {
          model with
              Items = replaceNode(model.Items, updated)
        },
        Cmd.none
      | Error error ->
        { model with Error = Some error }, failed "the pastel" error
    | AttachedChanged attached -> { model with Attached = attached }, Cmd.none
    | ClockTicked moment -> { model with Now = moment }, Cmd.none
    | SessionChanged state ->
      let pasteis, automatic =
        match state with
        | SignedIn entry ->
          entry.Session.PasteisMode, entry.Session.AutomaticTranslation
        | SignedOut -> model.Pasteis, model.Automatic

      {
        model with
            Pasteis = pasteis
            Automatic = automatic
      },
      Cmd.none
    | TranslateRequested commit when
      Translation.canReuse(model.Translations, commit)
      ->
      {
        model with
            Translations =
              Translation.showTranslated(model.Translations, commit.Id)
      },
      Cmd.none
    | TranslateRequested commit ->
      model,
      Cmd.OfAsync.perform
        Translation.recall
        ((props.Env :> IProvideTranslations).Translations,
         model.Translations,
         commit.Id)
        (fun book -> TranslationRecalled(commit, book))
    | TranslationRecalled(commit, book) ->
      match book with
      | Some book -> { model with Translations = book }, Cmd.none
      | None when Set.contains commit.Id model.Translations.Busy ->
        model, Cmd.none
      | None ->
        {
          model with
              Translations = Translation.request(model.Translations, commit.Id)
        },
        Cmd.OfAsync.perform translate (props, commit) (fun result ->
          TranslationDone(commit, result))
    | OriginalRequested commit ->
      {
        model with
            Translations =
              Translation.showOriginal(model.Translations, commit.Id)
      },
      Cmd.none
    | PrefsArrived settings ->
      if
        settings |> Option.exists(fun prefs -> prefs.AutomaticTranslation)
        && not model.Loading
      then
        autoBatch(props, model)
      else
        model, Cmd.none
    | TranslationsAuto results ->
      let cache = (props.Env :> IProvideTranslations).Translations

      let book, stores =
        results
        |> List.fold
          (fun (book, stores) (commit, result) ->
            match result with
            | Error(Refused(429, _, _)) -> book, stores
            | _ ->
              let args: Translation.Finish = {
                Book = book
                Now = model.Now
                CommitId = commit.Id
                Result = result
              }

              Translation.finish args,
              Cmd.ofAction(fun () ->
                Async.Start(Translation.store(cache, args)))
              :: stores)
          (model.Translations, [])

      { model with Translations = book }, Cmd.batch stores
    | TranslationDone(commit, result) ->
      let args: Translation.Finish = {
        Book = model.Translations
        Now = model.Now
        CommitId = commit.Id
        Result = result
      }

      {
        model with
            Translations = Translation.finish args
      },
      Cmd.ofAction(fun () ->
        Async.Start(
          Translation.store(
            (props.Env :> IProvideTranslations).Translations,
            args
          )
        ))

  /// The page's inlets from outside: the shared clock, the session
  /// store, and — while the page sits in the tree — the since-poll and
  /// the shared chrome's requests. The keyed engine stops and starts
  /// sources as the model moves.
  let subscribe (props: Props<'env>) (model: Model) : Sub<Msg> =
    let chromeRequests =
      props.Env.Timeline.Requests
      |> Observable.choose(function
        | Timeline.Reload
        | Timeline.CommitLanded -> Some ReloadRequested
        | Timeline.ScrollToTop -> None)

    [
      Sub.fromObservable
        "clock"
        (props.Env.UiClock.Minute |> Observable.map ClockTicked)

      Sub.fromObservable
        "user-settings"
        ((props.Env :> IProvideUserSettings).UserSettings.Observe
         |> Observable.map PrefsArrived)

      Sub.fromObservable
        "session"
        (props.Env.Session.Observe |> Observable.map SessionChanged)

      if model.Attached then
        Sub.interval "since-poll" (TimeSpan.FromMinutes 1.0) (fun () ->
          CheckNew)

      if model.Attached then
        Sub.fromObservable "chrome" chromeRequests
    ]
    |> Sub.batch

  /// Interleaves day separators between the commits of different days,
  /// carrying the day across tombstones. A ranked log is score ordered,
  /// its days interleave, so it rides without separators the way the
  /// site's ranked feed does. The last-visit marker rides above the
  /// first already-seen commit, and only when someone else's newer
  /// commit sits above it — the site's own rules.
  let toRows
    (items: CommitNode list, ranked: bool, marker: VisitMarker option)
    : LogRow list =
    let now = DateTimeOffset.UtcNow

    let nodeRow node =
      match node with
      | LiveCommit commit -> LogRow.Node commit
      | Tombstone tombstone -> LogRow.TombstoneRow tombstone

    let baseRows =
      if ranked then
        [ for node in items -> nodeRow node ]
      else
        let dayOf node =
          match node with
          | LiveCommit commit -> Some(dayLabel(now, commit.CreatedAt))
          | Tombstone _ -> None

        // The day seen before each item, carried across tombstones: one
        // carried state per item, seeded with the empty one.
        let carry =
          items
          |> List.scan (fun day node -> dayOf node |> Option.orElse day) None
          |> List.truncate items.Length

        [
          for prevDay, node in List.zip carry items do
            match node with
            | LiveCommit commit ->
              let day = dayLabel(now, commit.CreatedAt)

              if prevDay <> Some day then
                LogRow.Day day
                LogRow.Node commit
              else
                LogRow.Node commit
            | Tombstone tombstone -> LogRow.TombstoneRow tombstone
        ]

    let markerTarget =
      match ranked, marker with
      | false, Some visit ->
        items
        |> List.tryFindIndex(function
          | LiveCommit commit -> commit.CreatedAt <= visit.VisitedAt
          | Tombstone _ -> false)
        |> Option.bind(fun seenAt ->
          let newerFromOthers =
            items
            |> List.truncate seenAt
            |> List.exists(function
              | LiveCommit commit -> commit.Author.Id <> visit.ViewerId
              | Tombstone _ -> false)

          match newerFromOthers with
          | true ->
            (match List.item seenAt items with
             | LiveCommit commit -> Some commit.Id
             | Tombstone _ -> None)
          | false -> None)
      | _ -> None

    match markerTarget with
    | None -> baseRows
    | Some seenId ->
      baseRows
      |> List.collect(fun row ->
        match row with
        | LogRow.Node commit when commit.Id = seenId -> [
            LogRow.LastVisitRow
            row
          ]
        | _ -> [ row ])

  let keyOf(logRow: LogRow) : string =
    match logRow with
    | LogRow.Day label -> "day:" + label
    | LogRow.Node commit -> commit.Id
    | LogRow.TombstoneRow tombstone -> tombstone.Id
    | LogRow.LastVisitRow -> "last-visit"

/// The page's semantic regions; the view only composes these.
module Parts =

  /// The hover card source every log card shares.
  let flyoutOf(props: Props<'env>) : UserFlyout.Source voption =
    ValueSome {
      UserFlyout.Fetch = props.Env.Profiles.Fetch
      OnOpen =
        fun username -> Navigation.go(props.Nav, "/profile/" + username, ignore)
      OnOpenOrg =
        fun handle -> Navigation.go(props.Nav, "/org/" + handle, ignore)
      Cache = props.Env.ImageCache
    }

  /// The row context every log card shares: navigation for opens and
  /// quotes, the live flags, and the model slices the card follows.
  let commitCtx
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : CommitCard.RowProps = {
    OnEvent =
      function
      | CommitCard.Opened c ->
        Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
      | CommitCard.ThreadRequested c ->
        Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
      | CommitCard.MoreRepliesRequested c ->
        dispatch(State.PreviewMoreRequested c)
      | CommitCard.QuoteRequested c ->
        Navigation.go(props.Nav, "/quote/" + c.Id, ignore)
      | CommitCard.ReplyToggled c -> dispatch(State.ReplyToggled c)
      | CommitCard.PollChoiceChanged(commit, option, chosen) ->
        dispatch(State.PollChoiceChanged(commit, option, chosen))
      | CommitCard.TranslateRequested commit ->
        dispatch(State.TranslateRequested commit)
      | CommitCard.OriginalRequested commit ->
        dispatch(State.OriginalRequested commit)
      | CommitCard.StarToggled _
      | CommitCard.PastelGiven _
      | CommitCard.PollVoteRequested _
      | CommitCard.PinToggled _ -> ()
    Pasteis = model |> Observable.map _.Pasteis
    ViewerId = props.Session.Session.User.Id
    Now = model |> Observable.map _.Now
    Voting = model |> Observable.map _.Voting
    PollChoices = model |> Observable.map _.PollChoices
    Dense = false
    Images = props.Env.ImageCache
    Flyout = flyoutOf props
    Pin = ValueNone
    Zone = zoneOf((props.Env :> IProvideUserSettings).UserSettings.Current)
    Translation =
      model
      |> Observable.map(fun model ->
        fun commit ->
          Translation.view {
            Automatic = model.Automatic
            Book = model.Translations
            Now = model.Now
            Commit = commit
          })
  }

  /// The card's host-run actions: every command dispatches its page
  /// message, and every busy stream is the page's own in-flight vote
  /// slice.
  let actionSlots
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : CommitCard.ActionSlots =
    let busy = model |> Observable.map _.Voting

    let slot(command: Commit -> unit) : CommitCard.ActionSlot voption =
      ValueSome { Command = command; Busy = busy }

    {
      Star = slot(fun commit -> dispatch(State.StarToggled commit))
      Vote = slot(fun commit -> dispatch(State.PollVoteRequested commit))
      Pastel = slot(fun commit -> dispatch(State.PastelGiven commit))
    }

  /// One feed row: a separator, a commit card with its revealed reply
  /// composer, or a deletion marker.
  let row
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    )
    (logRow: LogRow)
    : Control =
    let ctx = commitCtx(props, dispatch, model)
    let slots = actionSlots(model, dispatch)

    match logRow with
    | LogRow.Day label -> FeedParts.dayRow label
    | LogRow.LastVisitRow -> FeedParts.lastVisitRow()
    | LogRow.Node commit ->
      // The reply composer exists only while its row is the answering
      // one: constructing one per realized row kept a hidden MVU
      // program alive for every row in the band, when at most one is
      // ever open. It seeds from the timeline service's parked draft,
      // so a recycled row rebuilds with the typed text, and the draft
      // outlives the page the same way.
      let reply =
        ContentControl()
          .Content(
            model
            |> Observable.map(fun current ->
              current.ReplyingTo = Some commit.Id)
            |> Observable.distinctUntilChanged
            |> Observable.map(fun answering ->
              if answering then
                Composer.Page {
                  Env = props.Env
                  Session = props.Session
                  Target = Composer.ReplyTo commit.Id
                  OnCommitted =
                    fun _ ->
                      props.Env.Timeline.ForgetDraft commit.Id
                      dispatch(State.ReplyPosted commit.Id)
                  OnScheduled = ValueNone
                  OnOpenQueue = ValueNone
                  Register = ValueNone
                  Draft =
                    ValueSome {
                      Seed = props.Env.Timeline.DraftOf commit.Id
                      Changed =
                        fun text ->
                          props.Env.Timeline.RememberDraft(commit.Id, text)
                    }
                }
                :> Control
              else
                Panel())
            |> _.ToBinding()
          )

      // The thread preview rides under the card: the payload slice,
      // or the list the page grew. It rebuilds only when the shown
      // list itself changed.
      let preview =
        match commit.ReplyPreview with
        | None -> Panel() :> Control
        | Some _ ->
          ContentControl()
            .Content(
              model
              |> Observable.map(fun current ->
                current.Previews |> Map.tryFind commit.Id)
              |> Observable.distinctUntilChanged
              |> Observable.map(function
                | Some grown ->
                  CommitCard.replyBlock(ctx, commit, ValueSome grown.Shown)
                | None -> CommitCard.replyBlock(ctx, commit, ValueNone))
              |> _.ToBinding()
            )

      StackPanel()
        .Children(
          CommitCard.feedRowWith (ctx, slots) (LiveCommit commit),
          preview,
          reply
        )

    | LogRow.TombstoneRow tombstone ->
      CommitCard.feedRowWith (ctx, slots) (Tombstone tombstone)

  /// The new-commits pill: a count over the feed, one click to take it.
  let newPill
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    Button()
      .Classes(Theme.name Theme.AccentButton)
      .CornerRadius(999.0)
      .Padding(16.0, 6.0, 16.0, 6.0)
      .Margin(0.0, 8.0, 0.0, 0.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .HorizontalContentAlignment(HorizontalAlignment.Center)
      .Content(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            Icons.icon Glyph.ArrowUp,
            TextBlock()
              .Text(
                model
                |> Observable.map(fun current ->
                  match current.NewCount with
                  | Some count -> $"{count} new commits"
                  | None -> "")
              )
          )
      )
      .IsVisible(model |> Observable.map _.NewCount.IsSome)
      .OnClickHandler(fun _ _ -> dispatch State.NewRequested)

  /// The timeline itself: the virtualized feed over the page's rows.
  /// The shared chrome's return-to-top signal reaches the feed's own
  /// scroller, and the scroller's place parks in the timeline service,
  /// so the next page of this log picks it back up.
  let feed
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    let place = props.Env.Timeline.OffsetOf props.Log

    Feed.view {
      Items =
        model
        |> Observable.map(fun current ->
          State.toRows(
            current.Items,
            current.Ranked,
            current.Marker
            |> Option.map(fun visitedAt -> {
              VisitedAt = visitedAt
              ViewerId = props.Session.Session.User.Id
            })
          ))
      KeyOf = State.keyOf
      Row = row(props, dispatch, model)
      Footer =
        ValueSome(
          PageChrome.loadMore(
            model |> Observable.map _.NextCursor.IsSome,
            fun () -> dispatch State.MoreRequested
          )
        )
      ScrollTop =
        ValueSome(
          props.Env.Timeline.Requests
          |> Observable.choose(function
            | Timeline.ScrollToTop -> Some()
            | _ -> None)
        )
      Restore = (if place > 0.0 then ValueSome place else ValueNone)
      OnScroll =
        ValueSome(fun offset -> props.Env.Timeline.Remember(props.Log, offset))
      OnNeedMore = ValueSome(fun () -> dispatch State.MoreRequested)
    }

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  // The pinned area takes no horizontal inset: the shared chrome above
  // the outlet carries the band and the composer rail, so the page
  // starts a hair under it, on the reading column's left edge.
  let pinnedPage =
    PageChrome.page {
      Model = model
      TitleOf = fun _ -> ""
      Padding = Thickness(0.0, 8.0, 0.0, 0.0)
      ErrorOf = _.Error
      Busy = fun current -> current.Loading, "Loading the log…"
      Empty =
        fun current ->
          not current.Loading && current.Error.IsNone && current.Items.IsEmpty,
          "Nothing here yet. Commit something."
      Header = [ Parts.newPill(model, dispatch) ]
      Body = Parts.feed(props, dispatch, model)
    }

  // The cached log page reports its tree presence, so the since-poll
  // and the chrome subscription pause while the page is stashed and
  // resume on the way back.
  pinnedPage.AttachedToVisualTree.Add(fun _ ->
    dispatch(State.AttachedChanged true))

  pinnedPage.DetachedFromVisualTree.Add(fun _ ->
    dispatch(State.AttachedChanged false))

  pinnedPage

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLastVisit
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideSshKeys
  and 'env :> IProvideUserSettings
  and 'env :> IProvideTranslations
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root
    props.Context.AddDisposable lifetime
