module DevNull.Client.Views.LogView

open System
open System.Collections.Generic
open System.Reactive.Subjects
open System.Runtime.CompilerServices
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open Navs
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Components.CommitActions
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.TranslationModel
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
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Timeline
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore
open DevNull.Client.TranslationCache

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLastVisit
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideSshKeys
  and 'env :> IProvideRemotes
  and 'env :> IProvideUserSettings
  and 'env :> IProvideTranslations
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging> = {
  Env: 'env
  Log: LogKind
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

type LogRow =
  | Day of string
  | Node of Commit
  | TombstoneRow of Tombstone
  | LastVisitRow

[<Struct>]
type VisitMarker = {
  VisitedAt: DateTimeOffset
  ViewerId: string
}

module State =

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
    ScrollRequests: int
    Ranked: bool
    Attached: bool
    Now: DateTimeOffset
    Pasteis: bool
    ReplyingTo: string option
    // The thread read of each parent a reply opened on, keyed by its
    // id: the answer the reply's participant rows build from. A failed
    // read never lands here, and the reply posts without the field.
    ReplyPages: Map<string, CommitPage>
    Voting: string option
    PollChoices: Map<string, string>
    Marker: DateTimeOffset option
    Previews: Map<string, PreviewState>
    Automatic: bool
    Translations: TranslationModel.Book
    AutoRan: bool
    // The commit whose share dialog stands open; the page model owns
    // it, and the dialog layer reads it.
    ShareOpen: Commit voption
    // The commit whose report dialog stands open.
    ReportOpen: Commit voption
    // The viewer's relation per author handle, as the More menu reads
    // it.
    Relations: Map<string, AuthorRelation>
    // The commit whose menu action is in flight, so its items disable.
    MenuBusy: string option
    HeardIds: string list
  }

  type Msg =
    | Fetched of Result<LogPage, AppError>
    | ReloadRequested
    | Appended of Result<LogPage, AppError>
    | MoreRequested
    | StarToggled of Commit
    | StarDone of Result<Commit, AppError>
    | PastelGiven of Commit
    | PastelDone of Result<PastelResult, AppError>
    | CheckNew
    | NewChecked of Result<LogPage, AppError>
    | NewRequested
    | NewFetched of Result<LogPage, AppError>
    | ScrollRequested
    | AttachedChanged of attached: bool
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState
    | ReplyToggled of Commit
    | ReplyPosted of commitId: string
    | ReplyPageFetched of commitId: string * Result<CommitPage, AppError>
    | PollChoiceChanged of Commit * PollOption * chosen: bool
    | PollVoteRequested of Commit
    | PollVotedDone of Result<Commit, AppError>
    | PreviewMoreRequested of Commit
    | PreviewLoaded of commitId: string * Result<Page<CommitNode>, AppError>
    | TranslateRequested of Commit
    | OriginalRequested of Commit
    | TranslationDone of Commit * Result<TranslationPayload, AppError>
    | TranslationsAuto of (Commit * Result<TranslationPayload, AppError>) list
    | TranslationRecalled of Commit * TranslationModel.Book option
    | PrefsArrived of settings: UserSettings option
    | ShareOpened of Commit
    | ShareClosed
    | Shared
    | ReportOpened of Commit
    | ReportClosed
    | MenuOpened of Commit
    | RelationLoaded of handle: string * AuthorRelation voption
    | AuthorFollowToggled of Commit
    | AuthorFollowDone of Commit * Result<unit, AppError>
    | AuthorMuteToggled of Commit
    | AuthorMuteDone of Commit * Result<MuteResult, AppError>
    | AuthorBlockRequested of Commit
    | AuthorBlockDone of Commit * Result<BlockResult, AppError>
    | DeleteRequested of Commit
    | Deleted of commitId: string * Result<unit, AppError>

  let initial = {
    Loading = true
    Error = None
    Items = []
    NextCursor = None
    Head = None
    NewCount = None
    ScrollRequests = 0
    Ranked = false
    Attached = false
    Now = DateTimeOffset.UtcNow
    Pasteis = false
    ReplyingTo = None
    ReplyPages = Map.empty
    Voting = None
    PollChoices = Map.empty
    Marker = None
    Previews = Map.empty
    Automatic = false
    Translations = TranslationModel.initial
    AutoRan = false
    ShareOpen = ValueNone
    ReportOpen = ValueNone
    Relations = Map.empty
    MenuBusy = None
    HeardIds = []
  }

  // The id of the newest commit in a page: the head the since-poll
  // checks against.
  let headOf(items: CommitNode list) : string option =
    items
    |> List.tryPick(function
      | LiveCommit commit -> Some commit.Id
      | Tombstone _ -> None)

  // One late commit lands among the loaded ones by its moment: the
  // first live row older than it. Tombstones never move.
  let insertLate(late: Commit, items: CommitNode list) : CommitNode list =
    let at =
      items
      |> List.tryFindIndex(function
        | Tombstone _ -> false
        | LiveCommit commit -> commit.CreatedAt < late.CreatedAt)

    match at with
    | Some index -> items |> List.insertAt index (LiveCommit late)
    | None -> items @ [ LiveCommit late ]

  let fetch(props: Props<'env>, cursor: string option) =
    Logs.get(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        Log = props.Log
        Since = None
        Cursor = cursor
        Arrived = []
        Pending = []
        Everything = false
        Through = []
      }
    )

  let checkNew(props: Props<'env>, model: Model, head: string) =
    let liveIds =
      model.Items
      |> List.choose(function
        | LiveCommit commit -> Some commit.Id
        | Tombstone _ -> None)

    Logs.get(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        Log = props.Log
        Since = Some head
        Cursor = None
        Arrived = if model.Ranked then [] else model.HeardIds
        Pending = liveIds |> List.truncate 100
        Everything = false
        Through = liveIds |> List.rev |> List.truncate 20
      }
    )

  // The parent's thread read, for the audience a reply's picker
  // offers: feed commits carry mentions as handle positions without
  // ids, so the answer is the only source of the participants' ids.
  let thread(props: Props<'env>, commitId: string) =
    Commits.show(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        CommitId = commitId
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
            TranslationModel.seed(
              (props.Env :> IProvideTranslations).Translations.Snapshot()
            )
          Marker = marker
    },
    Cmd.OfAsync.perform fetch (props, None) Fetched

  // The bare translate call for one commit, in the language the settings
  // choose.
  let translateCall(props: Props<'env>, commit: Commit) =
    let language =
      TranslationModel.languageFor(
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

  // Runs one commit's translate call: the language its own translation or
  // the settings pick, and the 503 retries.
  let translate(props: Props<'env>, commit: Commit) =
    TranslationModel.execute(
      TranslationModel.retryDelays,
      fun () -> translateCall(props, commit)
    )

  // Asks for the page's unknown translations, one at a time, up to the
  // budget a first screen needs.
  let autoTranslations(props: Props<'env>, commits: Commit list) =
    TranslationModel.auto(
      (fun commit -> translateCall(props, commit)),
      4,
      commits
    )

  // Records this visit's newest commit as the next marker, once the
  // feed has items to take it from.
  let markVisit(props: Props<'env>, items: CommitNode list) : Cmd<Msg> =
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

  // The marker one deleted commit folds to.
  let tombstoneOf(commit: Commit) : Tombstone = {
    Id = commit.Id
    ParentId = commit.ParentId
    DeletedAt = DateTimeOffset.UtcNow
    RepliesCount = commit.RepliesCount
  }

  // The unread state the menu's words start from.
  let unreadRelation: AuthorRelation = {
    IsFollowing = false
    IsMuting = false
    IsBlocking = false
  }

  // The relation the page holds for one author handle, defaulting to
  // the unread state the menu's words start from.
  let heldRelation(model: Model, handle: string) : AuthorRelation =
    model.Relations |> Map.tryFind handle |> Option.defaultValue unreadRelation

  // The batch that asks for the page's unknown translations, once the
  // settings say translations show by themselves. The batch's commits go
  // busy at once, so a click cannot double a running ask.
  let autoBatch(props: Props<'env>, model: Model) : Model * Cmd<Msg> =
    let automatic =
      (props.Env :> IProvideUserSettings).UserSettings.Current
      |> Option.exists(fun settings -> settings.AutomaticTranslation)

    let pending =
      if automatic && not model.AutoRan then
        TranslationModel.pending(
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
          (fun book commit -> TranslationModel.request(book, commit.Id))
          model.Translations

      {
        model with
            Translations = marked
            AutoRan = true
      },
      Cmd.OfAsync.perform autoTranslations (props, pending) TranslationsAuto

  let loggerOf(props: Props<'env>) =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Log"

  let updateCore (log: ILogger, props: Props<'env>) msg model =
    let kind = LogKinds.wire props.Log
    let translations = (props.Env :> IProvideTranslations).Translations

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
                  HeardIds = []
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
        model, Cmd.OfAsync.perform checkNew (props, model, head) NewChecked
      | _ -> model, Cmd.none
    | NewChecked result ->
      match result with
      | Ok page ->
        if model.Ranked then
          {
            model with
                NewCount =
                  page.NewCommitsCount |> Option.filter(fun count -> count > 0)
          },
          Cmd.none
        else
          let loaded =
            model.Items
            |> List.choose(function
              | LiveCommit commit -> Some commit.Id
              | Tombstone _ -> None)
            |> Set.ofList

          let headAt =
            model.Items
            |> List.tryPick(function
              | LiveCommit commit when Some commit.Id = model.Head ->
                Some commit.CreatedAt
              | _ -> None)

          let isNewer(commit: Commit) =
            match headAt with
            | Some at -> commit.CreatedAt > at
            | None -> true

          let unloaded =
            page.Page.Items
            |> List.choose(function
              | LiveCommit commit when not(Set.contains commit.Id loaded) ->
                Some commit
              | _ -> None)

          let late =
            unloaded
            |> List.filter(fun commit -> not(isNewer commit))
            |> List.sortByDescending(fun commit -> commit.CreatedAt)

          let merged =
            late
            |> List.fold
              (fun items arrival -> insertLate(arrival, items))
              model.Items

          let mergedLoaded =
            merged
            |> List.choose(function
              | LiveCommit commit -> Some commit.Id
              | Tombstone _ -> None)
            |> Set.ofList

          let heard =
            (model.HeardIds @ (unloaded |> List.filter isNewer |> List.map _.Id))
            |> List.distinct
            |> List.filter(fun id -> not(Set.contains id mergedLoaded))
            |> List.truncate 100

          {
            model with
                Items = merged
                NewCount =
                  page.NewCommitsCount |> Option.filter(fun count -> count > 0)
                HeardIds = heard
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
              HeardIds = []
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
        if Option.contains commit.Id model.ReplyingTo then
          None
        else
          Some commit.Id

      // The reply-open performs the parent's thread read: one read per
      // reply-open, the answer the picker's rows build from.
      let ask =
        match replyingTo with
        | Some parentId ->
          Cmd.OfAsync.perform thread (props, parentId) (fun answer ->
            ReplyPageFetched(parentId, answer))
        | None -> Cmd.none

      { model with ReplyingTo = replyingTo }, ask
    | ReplyPageFetched(commitId, answer) ->
      match answer with
      | Ok page ->
        {
          model with
              ReplyPages = Map.add commitId page model.ReplyPages
        },
        Cmd.none
      | Error error ->
        // The picker stays out and the reply posts without the
        // participants, the way it does without the audience at all.
        model,
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The reply's thread read for {Id} failed: {Error}",
            commitId,
            bounded 300 (describe error)
          ))
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
        Cmd.ofAction(fun () ->
          log.LogDebug("The vote on {Commit} landed.", updated.Id))
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
      Cmd.batch [
        Cmd.OfAsync.perform
          CommitActions.toggleStar
          (props.Env, props.Session, commit)
          StarDone

        Cmd.ofAction(fun () ->
          log.LogTrace("The star on {Commit} is being toggled.", commit.Id))
      ]
    | StarDone result ->
      match result with
      | Ok updated ->
        {
          model with
              Items = replaceNode(model.Items, updated)
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The star on {Commit} settled.", updated.Id))
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
      | Ok pastel ->
        {
          model with
              Items = replaceNode(model.Items, pastel.Commit)
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The pastel on {Commit} landed.", pastel.Commit.Id))
      | Error error ->
        { model with Error = Some error }, failed "the pastel" error
    | ScrollRequested ->
      {
        model with
            ScrollRequests = model.ScrollRequests + 1
      },
      Cmd.none
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
      TranslationModel.canReuse(model.Translations, commit)
      ->
      {
        model with
            Translations =
              TranslationModel.showTranslated(model.Translations, commit.Id)
      },
      Cmd.none
    | TranslateRequested commit ->
      model,
      Cmd.OfAsync.perform
        TranslationModel.recall
        (translations, model.Translations, commit.Id)
        (fun book -> TranslationRecalled(commit, book))
    | TranslationRecalled(commit, book) ->
      match book with
      | Some book -> { model with Translations = book }, Cmd.none
      | None when Set.contains commit.Id model.Translations.Busy ->
        model, Cmd.none
      | None ->
        {
          model with
              Translations =
                TranslationModel.request(model.Translations, commit.Id)
        },
        Cmd.OfAsync.perform translate (props, commit) (fun result ->
          TranslationDone(commit, result))
    | OriginalRequested commit ->
      {
        model with
            Translations =
              TranslationModel.showOriginal(model.Translations, commit.Id)
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
              let args: TranslationModel.Finish = {
                Book = book
                Now = model.Now
                CommitId = commit.Id
                Result = result
              }

              TranslationModel.finish args,
              Cmd.ofAction(fun () ->
                Async.Start(TranslationModel.store(cache, args)))
              :: stores)
          (model.Translations, [])

      { model with Translations = book }, Cmd.batch stores
    | TranslationDone(commit, result) ->
      let args: TranslationModel.Finish = {
        Book = model.Translations
        Now = model.Now
        CommitId = commit.Id
        Result = result
      }

      {
        model with
            Translations = TranslationModel.finish args
      },
      Cmd.ofAction(fun () ->
        Async.Start(TranslationModel.store(translations, args)))
    | ShareOpened commit ->
      {
        model with
            ShareOpen = ValueSome commit
      },
      Cmd.none
    | ShareClosed -> { model with ShareOpen = ValueNone }, Cmd.none
    | Shared ->
      model, Cmd.ofEffect(fun _ -> Navigation.go(props.Nav, "/remotes", ignore))
    | ReportOpened commit ->
      {
        model with
            ReportOpen = ValueSome commit
      },
      Cmd.none
    | ReportClosed -> { model with ReportOpen = ValueNone }, Cmd.none
    | MenuOpened commit ->
      // The relation the menu speaks loads once per author handle; the
      // read carries no block flag, so that flag moves only when a
      // block answers. The viewer's own commit needs no read at all.
      if
        (Author.id commit.Author) = props.Session.Session.User.Id
        || model.Relations |> Map.containsKey(Author.handle commit.Author)
      then
        model, Cmd.none
      else
        model,
        Cmd.OfAsync.perform
          props.Env.Profiles.Fetch
          (Author.handle commit.Author)
          (fun details ->
            let toRelation(loaded: ProfileDetails) : AuthorRelation = {
              IsFollowing = loaded.IsFollowing
              IsMuting = loaded.IsMuting
              IsBlocking = false
            }

            let relation =
              details |> Option.map toRelation |> ValueOption.ofOption

            RelationLoaded((Author.handle commit.Author), relation))
    | RelationLoaded(handle, relation) ->
      (match relation with
       | ValueSome current -> {
           model with
               Relations = model.Relations |> Map.add handle current
         }
       | ValueNone -> model),
      Cmd.none
    | AuthorFollowToggled commit ->
      // The word flips at once and rolls back on a refusal, the way
      // the site's toggle answers before the server does.
      let held = heldRelation(model, (Author.handle commit.Author))

      {
        model with
            MenuBusy = Some commit.Id
            Relations =
              model.Relations
              |> Map.add (Author.handle commit.Author) {
                held with
                    IsFollowing = not held.IsFollowing
              }
      },
      Cmd.OfAsync.perform
        CommitActions.toggleFollow
        (props.Env,
         {
           Session = props.Session
           Author = commit.Author
           Flag = held.IsFollowing
         })
        (fun result -> AuthorFollowDone(commit, result))
    | AuthorFollowDone(commit, result) ->
      match result with
      | Ok() ->
        { model with MenuBusy = None },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The follow of @{Author} settled.",
            (Author.handle commit.Author)
          ))
      | Error error ->
        let held = heldRelation(model, (Author.handle commit.Author))

        {
          model with
              MenuBusy = None
              Relations =
                model.Relations
                |> Map.add (Author.handle commit.Author) {
                  held with
                      IsFollowing = not held.IsFollowing
                }
        },
        failed "the follow toggle" error
    | AuthorMuteToggled commit ->
      let held = heldRelation(model, (Author.handle commit.Author))

      {
        model with
            MenuBusy = Some commit.Id
            Relations =
              model.Relations
              |> Map.add (Author.handle commit.Author) {
                held with
                    IsMuting = not held.IsMuting
              }
      },
      Cmd.OfAsync.perform
        CommitActions.toggleMute
        (props.Env,
         {
           Session = props.Session
           Author = commit.Author
           Flag = held.IsMuting
         })
        (fun result -> AuthorMuteDone(commit, result))
    | AuthorMuteDone(commit, result) ->
      match result with
      | Ok mute ->
        // The server's answer is the truth the state keeps.
        let held = heldRelation(model, (Author.handle commit.Author))

        {
          model with
              MenuBusy = None
              Relations =
                model.Relations
                |> Map.add (Author.handle commit.Author) {
                  held with
                      IsMuting = mute.IsMuting
                }
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The mute of @{Author} settled.",
            (Author.handle commit.Author)
          ))
      | Error error ->
        let held = heldRelation(model, (Author.handle commit.Author))

        {
          model with
              MenuBusy = None
              Relations =
                model.Relations
                |> Map.add (Author.handle commit.Author) {
                  held with
                      IsMuting = not held.IsMuting
                }
        },
        failed "the mute toggle" error
    | AuthorBlockRequested commit ->
      let held = heldRelation(model, (Author.handle commit.Author))

      { model with MenuBusy = Some commit.Id },
      Cmd.OfAsync.perform
        CommitActions.blockAuthor
        (props.Env,
         {
           Session = props.Session
           Author = commit.Author
           Flag = held.IsBlocking
         })
        (fun result -> AuthorBlockDone(commit, result))
    | AuthorBlockDone(commit, result) ->
      match result with
      | Ok blocked ->
        let held = heldRelation(model, (Author.handle commit.Author))

        {
          model with
              MenuBusy = None
              Relations =
                model.Relations
                |> Map.add (Author.handle commit.Author) {
                  held with
                      IsBlocking = blocked.IsBlocking
                }
        },
        Cmd.ofAction(fun () ->
          log.LogInformation(
            "The block of @{Author} settled.",
            (Author.handle commit.Author)
          ))
      | Error error ->
        {
          model with
              MenuBusy = None
              Error = Some error
        },
        failed "the block toggle" error
    | DeleteRequested commit ->
      {
        model with
            MenuBusy = Some commit.Id
            Error = None
      },
      Cmd.OfAsync.perform
        Commits.delete
        (props.Env,
         {
           KeyFile = props.Session.KeyFile
           CommitId = commit.Id
         })
        (fun result -> Deleted(commit.Id, result))
    | Deleted(commitId, result) ->
      match result with
      | Ok() ->
        // The row folds to its marker; the shell answers with the undo
        // toast and owns the restore.
        {
          model with
              Items =
                model.Items
                |> List.map(function
                  | LiveCommit commit when commit.Id = commitId ->
                    Tombstone(tombstoneOf commit)
                  | node -> node)
              MenuBusy = None
        },
        Cmd.batch [
          Cmd.ofAction(fun () ->
            (props.Env :> IProvideTimeline).Timeline.AnnounceDeletion commitId)

          Cmd.ofAction(fun () ->
            log.LogInformation("The commit {Commit} was deleted.", commitId))
        ]
      | Error error ->
        {
          model with
              MenuBusy = None
              Error = Some error
        },
        failed "the commit deletion" error

  let update(props: Props<'env>) = updateCore(loggerOf props, props)

  // The page's inlets from outside: the shared clock, the session
  // store, and — while the page sits in the tree — the since-poll and
  // the shared chrome's requests. The keyed engine stops and starts
  // sources as the model moves.
  let subscribe (props: Props<'env>) (model: Model) : Sub<Msg> =
    let chromeRequests =
      props.Env.Timeline.Requests
      |> Observable.choose(function
        | Timeline.Reload
        | Timeline.CommitLanded -> Some ReloadRequested
        | Timeline.ScrollToTop -> None)

    let scrollRequests =
      props.Env.Timeline.Requests
      |> Observable.choose(function
        | Timeline.ScrollToTop -> Some ScrollRequested
        | _ -> None)

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

      if model.Attached then
        Sub.fromObservable "scroll-top" scrollRequests
    ]
    |> Sub.batch

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

              if not(Option.contains day prevDay) then
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
              | LiveCommit commit ->
                (Author.id commit.Author) <> visit.ViewerId
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

    // The deletion marker rides its own row identity, so the fold from
    // a live commit swaps the row instead of keeping the old one under
    // the shared id.
    | LogRow.TombstoneRow tombstone -> "deleted:" + tombstone.Id
    | LogRow.LastVisitRow -> "last-visit"

// The page's semantic regions; the view only composes these.
module Parts =

  // The hover card source every log card shares.
  let flyoutOf(props: Props<'env>) : UserFlyout.Source voption =
    ValueSome {
      UserFlyout.Fetch = props.Env.Profiles.Fetch
      OnOpen =
        fun username -> Navigation.go(props.Nav, "/profile/" + username, ignore)
      OnOpenOrg =
        fun handle -> Navigation.go(props.Nav, "/org/" + handle, ignore)
      OnOpenTag =
        fun tag ->
          Navigation.go(
            props.Nav,
            "/search/" + Uri.EscapeDataString tag,
            ignore
          )
      Cache = props.Env.ImageCache
    }

  // What one log feed render needs: the page's inlets, its model, and
  // the one row context every realized card follows.
  type FeedArgs<'env
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp
    and 'env :> IProvideDispatcher
    and 'env :> IProvideSession
    and 'env :> IProvideUiClock
    and 'env :> IProvideImageCache
    and 'env :> IProvideAttachmentSources
    and 'env :> IProvideLastVisit
    and 'env :> IProvideProfiles
    and 'env :> IProvideTimeline
    and 'env :> IProvideSshKeys
    and 'env :> IProvideRemotes
    and 'env :> IProvideUserSettings
    and 'env :> IProvideTranslations
    and 'env :> IProvideBrowser
    and 'env :> IProvideLogging> = {
    Props: Props<'env>
    Dispatch: Dispatch<State.Msg>
    Model: IObservable<State.Model>
    Rows: CommitCard.RowProps
  }

  // One feed row: a separator, a commit card with its revealed reply
  // composer, or a deletion marker.
  let row(args: FeedArgs<'env>, logRow: LogRow) : Control =
    let props = args.Props
    let dispatch = args.Dispatch
    let model = args.Model
    let ctx = args.Rows

    match logRow with
    | LogRow.Day label -> FeedParts.dayRow label
    | LogRow.LastVisitRow -> FeedParts.lastVisitRow()
    | LogRow.Node commit ->
      // The reply composer exists only while its row is the answering
      // one: constructing one per realized row kept a hidden MVU
      // program alive for every row in the band, when at most one is
      // ever open. It seeds from the timeline service's parked draft,
      // so a recycled row rebuilds with the typed text, and the draft
      // outlives the page the same way. The rebuild also carries the
      // thread read's participants when its answer lands, so the
      // audience picker rides the same construction.
      let reply =
        ContentControl()
          .Content(
            model
            |> Observable.map(fun current ->
              Option.contains commit.Id current.ReplyingTo,
              current.ReplyPages |> Map.tryFind commit.Id)
            |> Observable.distinctUntilChanged
            |> Observable.map(fun (answering, page) ->
              if answering then
                let participants =
                  page
                  |> Option.map(fun read ->
                    let author = commit.Author

                    let authorRow: ReplyPicker.ParticipantRow = {
                      Id = (Author.id author)
                      Handle = (Author.handle author)
                      IsAuthor = true
                    }

                    let others =
                      read.ReplyParticipants
                      |> List.map(fun participant ->
                        {
                          Id = participant.Id
                          Handle = participant.Handle
                          IsAuthor = false
                        }
                        : ReplyPicker.ParticipantRow)

                    authorRow :: others)
                  |> ValueOption.ofOption

                Composer.Page {
                  Env = props.Env
                  Session = props.Session
                  Target = Composer.ReplyTo commit.Id
                  Participants = participants
                  OnCommitted =
                    fun _ ->
                      props.Env.Timeline.ForgetDraft commit.Id
                      dispatch(State.ReplyPosted commit.Id)
                  OnScheduled = ValueNone
                  OnOpenQueue = ValueNone
                  Draft =
                    ValueSome {
                      Seed = props.Env.Timeline.DraftOf commit.Id
                      Changed =
                        fun text ->
                          props.Env.Timeline.RememberDraft(commit.Id, text)
                    }
                  OnBodyReady = ValueNone
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
        .Children(CommitCard.feedRow ctx (LiveCommit commit), preview, reply)

    | LogRow.TombstoneRow tombstone ->
      CommitCard.feedRow ctx (Tombstone tombstone)

  // The new-commits pill: a count over the feed, one click to take it.
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

  // The row inputs the page compares by reference, so a tick that
  // left them the same does not reshape the feed.
  let rowInputsComparer = {
    new IEqualityComparer<CommitNode list * bool * DateTimeOffset option> with
      member _.Equals
        (
          (leftItems, leftRanked, leftMarker),
          (rightItems, rightRanked, rightMarker)
        ) =
        ReferenceEqualityComparer.Instance.Equals(leftItems, rightItems)
        && leftRanked = rightRanked
        && ReferenceEqualityComparer.Instance.Equals(leftMarker, rightMarker)

      member _.GetHashCode((items, ranked, marker)) =
        HashCode.Combine(
          RuntimeHelpers.GetHashCode items,
          ranked,
          RuntimeHelpers.GetHashCode marker
        )
  }

  // The timeline itself: the virtualized feed over the page's rows.
  // The shared chrome's return-to-top signal reaches the page model,
  // and the scroller's place parks in the timeline service, so the
  // next page of this log picks it back up.
  let feed(args: FeedArgs<'env>) : Control =
    let props = args.Props
    let model = args.Model
    let place = props.Env.Timeline.OffsetOf props.Log
    let hasMore = model |> Observable.map _.NextCursor.IsSome

    let rows =
      model
      |> Observable.map(fun current ->
        current.Items, current.Ranked, current.Marker)
      |> Observable.distinctUntilChangedCompare(rowInputsComparer)
      |> Observable.map(fun (items, ranked, marker) ->
        State.toRows(
          items,
          ranked,
          marker
          |> Option.map(fun visitedAt -> {
            VisitedAt = visitedAt
            ViewerId = props.Session.Session.User.Id
          })
        ))

    let scrollTo =
      model
      |> Observable.map _.ScrollRequests
      |> Observable.distinctUntilChanged
      |> Observable.skip 1
      |> Observable.map(fun _ -> 0.0)

    Feed.view {
      Items = rows
      KeyOf = State.keyOf
      Row = fun logRow -> row(args, logRow)
      Footer =
        ValueSome(
          PageChrome.loadMore(
            hasMore,
            fun () -> args.Dispatch State.MoreRequested
          )
        )
      HasMore = hasMore
      ScrollTo = ValueSome scrollTo
      Ride = false
      Restore = (if place > 0.0 then ValueSome place else ValueNone)
      OnScroll =
        ValueSome(fun offset -> props.Env.Timeline.Remember(props.Log, offset))
      OnRide = ValueNone
      OnNeedMore = ValueSome(fun () -> args.Dispatch State.MoreRequested)
    }

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let ctx =
    CommitRows.row(
      {
        OnEvent =
          function
          | CommitCard.Opened c ->
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
          | CommitCard.MenuOpened commit -> dispatch(State.MenuOpened commit)
        OnOpenExternal = fun url -> props.Env.Browser.Open(url) |> ignore
        ViewerId = props.Session.Session.User.Id
        Images = props.Env.ImageCache
        Flyout = Parts.flyoutOf props
        Zone = zoneOf((props.Env :> IProvideUserSettings).UserSettings.Current)
        OnPin = ValueNone
        Menu =
          ValueSome {
            Share =
              if props.Session.Session.Remotes.IsSome then
                ValueSome(State.ShareOpened >> dispatch)
              else
                ValueNone
            Report = ValueSome(State.ReportOpened >> dispatch)
            Delete =
              ValueSome(fun commit -> dispatch(State.DeleteRequested commit))
            ToggleFollow =
              fun commit -> dispatch(State.AuthorFollowToggled commit)
            ToggleMute = fun commit -> dispatch(State.AuthorMuteToggled commit)
            Block = fun commit -> dispatch(State.AuthorBlockRequested commit)
            Relations =
              model
              |> Observable.map(fun current ->
                fun commit ->
                  current.Relations
                  |> Map.tryFind(Author.handle commit.Author)
                  |> ValueOption.ofOption)
            Busy = model |> Observable.map _.MenuBusy
          }
        Actions = true
        OnStar = fun commit -> dispatch(State.StarToggled commit)
        OnStars = ValueNone
        OnVote = fun commit -> dispatch(State.PollVoteRequested commit)
        OnPastel = fun commit -> dispatch(State.PastelGiven commit)
        Translation =
          model
          |> Observable.map(fun current ->
            fun commit ->
              TranslationModel.view {
                Automatic = current.Automatic
                Book = current.Translations
                Now = current.Now
                Commit = commit
              })
      },
      model,
      (fun current -> {
        Pasteis = current.Pasteis
        Now = current.Now
        PollChoices = current.PollChoices
        Voting = current.Voting
        Pinning = None
      })
    )

  let feedArgs: Parts.FeedArgs<'env> = {
    Props = props
    Dispatch = dispatch
    Model = model
    Rows = ctx
  }

  // The pinned area takes no horizontal inset: the shared chrome above
  // the outlet carries the band and the composer rail, so the page
  // starts a hair under it, on the reading column's left edge.
  let pinnedPage =
    PageChrome.page {
      Model = model
      TitleOf = fun _ -> ""
      Padding = Thickness(0.0, 8.0, 0.0, 0.0)
      ErrorOf = _.Error
      Busy =
        fun current ->
          if current.Loading then
            PageChrome.Words "Loading the log…"
          else
            PageChrome.Off
      Empty =
        fun current ->
          if
            not current.Loading && current.Error.IsNone && current.Items.IsEmpty
          then
            PageChrome.Words "Nothing here yet. Commit something."
          else
            PageChrome.Off
      Header = [ Parts.newPill(model, dispatch) ]
      Body = Parts.feed feedArgs
    }

  // The cached log page reports its tree presence, so the since-poll
  // and the chrome subscription pause while the page is stashed and
  // resume on the way back.
  pinnedPage.AttachedToVisualTree.Add(fun _ ->
    dispatch(State.AttachedChanged true))

  pinnedPage.DetachedFromVisualTree.Add(fun _ ->
    dispatch(State.AttachedChanged false))

  let shareLayer =
    DialogChrome.layer {
      Open =
        model
        |> Observable.map(fun state -> state.ShareOpen)
        |> Observable.map ValueOption.isSome
      Content =
        fun () ->
          let isOpen =
            model
            |> Observable.first
            |> Observable.toEnumerable
            |> Seq.tryHead
            |> function
              | Some state -> state.ShareOpen
              | None -> ValueNone

          match isOpen with
          | ValueSome commit ->
            ShareDialog.Page {
              Env = props.Env
              Session = props.Session
              Commit = commit
              Sent =
                fun _ ->
                  dispatch State.ShareClosed
                  dispatch State.Shared
              Close = fun () -> dispatch State.ShareClosed
            }
          | ValueNone -> Panel()
      Scrim = true
      OnScrimClick = ValueSome(fun () -> dispatch State.ShareClosed)
      Place = DialogChrome.Center
    }

  let reportLayer =
    DialogChrome.layer {
      Open =
        model
        |> Observable.map(fun state -> state.ReportOpen)
        |> Observable.map ValueOption.isSome
      Content =
        fun () ->
          let isOpen =
            model
            |> Observable.first
            |> Observable.toEnumerable
            |> Seq.tryHead
            |> function
              | Some state -> state.ReportOpen
              | None -> ValueNone

          match isOpen with
          | ValueSome commit ->
            ReportDialog.Page {
              Env = props.Env
              Session = props.Session
              Commit = commit
              Sent = fun () -> dispatch State.ReportClosed
              Close = fun () -> dispatch State.ReportClosed
            }
          | ValueNone -> Panel()
      Scrim = true
      OnScrimClick = ValueSome(fun () -> dispatch State.ReportClosed)
      Place = DialogChrome.Center
    }

  Grid().Children(pinnedPage, shareLayer, reportLayer)

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLastVisit
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideSshKeys
  and 'env :> IProvideRemotes
  and 'env :> IProvideUserSettings
  and 'env :> IProvideTranslations
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    props.Env.Timeline.RememberLog props.Log

    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Programs.runPage(props.Env.Dispatcher, program)

    this.Content <- root
    props.Context.AddDisposable lifetime
