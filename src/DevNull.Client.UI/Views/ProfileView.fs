module DevNull.Client.Views.ProfileView

open System
open System.Collections.Generic
open System.Globalization
open System.Runtime.CompilerServices
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Media
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open Navs
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Env
open Microsoft.Extensions.Logging
open DevNull.Client.ImageCache
open DevNull.Client.Formatting
open DevNull.Client.Profiles
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
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideUserSettings
  and 'env :> IProvideTranslations
  and 'env :> IProvideLogging> = {
  Env: 'env
  Username: string
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

// The page's state logic: model, messages, update, and the
// subscriptions that feed it from outside.
module State =

  // The feed tab the page shows: the user's own commits or their
  // replies.
  type Tab =
    | Commits
    | Replies

  // One row of the profile feed: the pinned caption, the pinned
  // commit it marks, or one commit of the list.
  [<Struct>]
  type FeedRow =
    | PinnedCaption
    | PinnedNode of node: CommitNode
    | FeedNode of node: CommitNode

  // The header's shape: the automatic fold, or the form the reader
  // pinned with the chevron.
  type HeaderForm =
    | Auto
    | Full
    | Compact

  type Model = {
    Loading: bool
    Error: AppError option
    Details: ProfileDetails option
    Commits: CommitNode list
    NextCursor: string option
    Tab: Tab
    Now: DateTimeOffset
    Pasteis: bool
    Voting: string option
    PollChoices: Map<string, string>
    Pinning: string option
    Note: string option
    // The form the reader pinned, or Auto.
    HeaderForm: HeaderForm
    // The fold the last read place asked for, while the form is Auto.
    AutoCompact: bool
    // Whether the window is short for the full header, decided once
    // from the full header's first layout.
    ShortWindow: bool
    // Whether the first read place already landed.
    PlaceReported: bool
    // Whether translations show by themselves.
    Automatic: bool
    // Every commit's translation on this page.
    Translations: TranslationModel.Book
    // The viewer's relation per author handle, as the More menu reads
    // it.
    Relations: Map<string, AuthorRelation>
    // The commit whose menu action is in flight, so its items disable.
    MenuBusy: string option
  }

  type Msg =
    | Fetched of Result<ProfileDetails, AppError>
    | CommitsFetched of Result<Page<CommitNode>, AppError>
    | CommitsRefreshed of Result<Page<CommitNode>, AppError>
    | MoreCommits
    | TabChosen of Tab
    | HeaderChosen of HeaderForm
    | FeedRead of Components.Feed.Ride
    | StarToggled of Commit
    | StarDone of Result<Commit, AppError>
    | PollChoiceChanged of Commit * PollOption * chosen: bool
    | PollVoteRequested of Commit
    | PollVotedDone of Result<Commit, AppError>
    | PastelGiven of Commit
    | PastelDone of Result<PastelResult, AppError>
    | FollowToggled
    | MuteToggled
    | BlockToggled
    | FollowDone of Result<unit, AppError>
    | MuteDone of Result<MuteResult, AppError>
    | BlockDone of Result<BlockResult, AppError>
    | PinToggled of Commit
    | PinDone of pinned: bool * Result<Commit, AppError>
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState
    | TranslateRequested of Commit
    | OriginalRequested of Commit
    | TranslationDone of Commit * Result<TranslationPayload, AppError>
    | TranslationRecalled of Commit * TranslationModel.Book option
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

  // The key of one feed row: the pinned rows carry their own so the
  // pinned commit can appear above while its feed copy stays its own
  // row.
  let inline rowKey(row: FeedRow) : string =
    match row with
    | PinnedCaption -> "pinned-caption"
    | PinnedNode node -> "pinned:" + FeedParts.nodeKey node
    | FeedNode node -> FeedParts.nodeKey node

  // The rows one profile render lists: the pinned commit under its
  // caption first — the Commits tab only, as the site shows it — then
  // the feed.
  let rowsOf(model: Model) : FeedRow list =
    let pinned =
      match model.Tab, model.Details |> Option.bind _.PinnedCommit with
      | Commits, Some node -> [ PinnedCaption; PinnedNode node ]
      | _ -> []

    pinned @ (model.Commits |> List.map FeedNode)

  let initial = {
    Loading = true
    Error = None
    Details = None
    Commits = []
    NextCursor = None
    Tab = Commits
    Now = DateTimeOffset.UtcNow
    Pasteis = false
    Voting = None
    PollChoices = Map.empty
    Pinning = None
    Note = None
    HeaderForm = Auto
    AutoCompact = false
    ShortWindow = false
    PlaceReported = false
    Automatic = false
    Translations = TranslationModel.initial
    Relations = Map.empty
    MenuBusy = None
  }

  // The feed viewport under which the full header gives way at once:
  // about three feed rows.
  let HeaderFloor = 300.0

  // The form the header takes: the reader's pin wins, Auto follows
  // the last automatic fold.
  let inline effectiveForm(model: Model) : HeaderForm =
    match model.HeaderForm with
    | Auto -> if model.AutoCompact then Compact else Full
    | chosen -> chosen

  // One feed read: whose commits, which tab, and the pager cursor.
  [<Struct>]
  type FeedQuery = {
    Session: SignedInSession
    Username: string
    Replies: bool
    Cursor: string option
  }

  let fetchCommits(env, query: FeedQuery) =
    Users.commits(
      env,
      {
        KeyFile = query.Session.KeyFile
        Username = query.Username
        Replies = query.Replies
        Cursor = query.Cursor
      }
    )

  let init (props: Props<'env>) () =
    {
      initial with
          Pasteis = props.Session.Session.PasteisMode
          Automatic = props.Session.Session.AutomaticTranslation
          Translations =
            TranslationModel.seed(
              (props.Env :> IProvideTranslations).Translations.Snapshot()
            )
    },
    Cmd.batch [
      Cmd.OfAsync.perform
        Users.get
        (props.Env,
         {
           KeyFile = props.Session.KeyFile
           Username = props.Username
         })
        Fetched

      Cmd.OfAsync.perform
        fetchCommits
        (props.Env,
         {
           Session = props.Session
           Username = props.Username
           Replies = false
           Cursor = None
         })
        CommitsFetched
    ]

  // Runs one commit's translate call: the language its own translation or
  // the settings pick, and the 503 retries.
  let translate(props: Props<'env>, commit: Commit) =
    let language =
      TranslationModel.languageFor(
        (props.Env :> IProvideUserSettings).UserSettings.Current,
        commit
      )

    TranslationModel.execute(
      TranslationModel.retryDelays,
      fun () ->
        Commits.translate(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            CommitId = commit.Id
            Language = language
          }
        )
    )

  let loggerOf(props: Props<'env>) =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Profile"

  let updateCore (log: ILogger, props: Props<'env>) msg model =
    let username =
      model.Details
      |> Option.map(fun details -> details.Profile.User.Handle)
      |> Option.defaultValue props.Username

    let refetch(replies: bool) =
      Cmd.OfAsync.perform
        fetchCommits
        (props.Env,
         {
           Session = props.Session
           Username = username
           Replies = replies
           Cursor = None
         })
        CommitsFetched

    // Every log line is an effect: update returns it as a command,
    // and the loop writes it after the computation.
    let failure = CommitActions.failure log

    let failed what error =
      Cmd.ofAction(fun () -> failure what error)

    match msg with
    | Fetched result ->
      match result with
      | Ok details ->
        {
          model with
              Loading = false
              Error = None
              Details = Some details
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The profile of {User} arrived.", username))
      | Error error ->
        {
          model with
              Loading = false
              Error = Some error
        },
        failed $"the profile of {username}" error
    | CommitsFetched result ->
      match result with
      | Ok page ->
        {
          model with
              Loading = false
              Commits = model.Commits @ page.Items
              NextCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The commits of {User} arrived ({Count}).",
            username,
            page.Items.Length
          ))
      | Error error ->
        { model with Error = Some error },
        failed $"the commits of {username}" error
    | MoreCommits ->
      model,
      Cmd.OfAsync.perform
        fetchCommits
        (props.Env,
         {
           Session = props.Session
           Username = username
           Replies = model.Tab = Replies
           Cursor = model.NextCursor
         })
        CommitsFetched
    | TabChosen kind ->
      if kind = model.Tab then
        model, Cmd.none
      else
        {
          model with
              Loading = true
              Error = None
              Commits = []
              NextCursor = None
              Tab = kind
        },
        refetch(kind = Replies)
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
              Commits = replaceNode(model.Commits, updated)
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The star on {Commit} settled.", updated.Id))
      | Error error ->
        { model with Error = Some error }, failed "the star" error
    | PinToggled commit ->
      // The card's own gate already says this is the author's
      // top-level commit; the flag on it says which way the toggle
      // goes.
      let pin = not commit.IsPinned

      let call() =
        let request: CommitPins.ActionRequest = {
          KeyFile = props.Session.KeyFile
          CommitId = commit.Id
        }

        if pin then
          CommitPins.pin(props.Env, request)
        else
          CommitPins.unpin(props.Env, request)

      {
        model with
            Pinning = Some commit.Id
            Error = None
            Note = None
      },
      Cmd.batch [
        Cmd.OfAsync.perform call () (fun result -> PinDone(pin, result))

        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The pin of {Commit} is being {State}.",
            commit.Id,
            if pin then "set" else "cleared"
          ))
      ]
    | PinDone(pinned, result) ->
      match result with
      | Ok updated ->
        // The slot moves at once from the answer; the feed read below
        // reconciles where the commit itself sits, which only the
        // server knows after a pin.
        let details =
          model.Details
          |> Option.map(fun details -> {
            details with
                PinnedCommit =
                  (if pinned then Some(LiveCommit updated) else None)
          })

        let commits =
          if pinned then
            model.Commits
            |> List.filter(fun node -> FeedParts.nodeKey node <> updated.Id)
          else
            model.Commits

        {
          model with
              Pinning = None
              Note =
                Some(
                  if pinned then
                    "Commit pinned to your profile."
                  else
                    "Commit unpinned."
                )
              Details = details
              Commits = commits
        },
        Cmd.batch [
          Cmd.OfAsync.perform
            fetchCommits
            (props.Env,
             {
               Session = props.Session
               Username = username
               Replies = model.Tab = Replies
               Cursor = None
             })
            CommitsRefreshed

          Cmd.ofAction(fun () ->
            match pinned with
            | true ->
              log.LogInformation(
                "The commit {Commit} was pinned to the profile.",
                updated.Id
              )
            | false ->
              log.LogInformation(
                "The pin of the commit {Commit} was taken off.",
                updated.Id
              ))
        ]
      | Error error ->
        {
          model with
              Pinning = None
              Error = Some error
        },
        failed "the profile pin" error
    | CommitsRefreshed result ->
      // The read after a settled pin: it replaces the list, because
      // the server moved the commit in it. A failure keeps the
      // already-patched rows and says so in the log only — the pin
      // itself succeeded.
      match result with
      | Ok page ->
        {
          model with
              Commits = page.Items
              NextCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The commits of {User} were read again.", username))
      | Error error -> model, failed "the commits refresh" error
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
              Commits = replaceNode(model.Commits, updated)
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
              Commits = replaceNode(model.Commits, pastel.Commit)
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The pastel on {Commit} landed.", pastel.Commit.Id))
      | Error error ->
        { model with Error = Some error }, failed "the pastel" error
    | FollowToggled ->
      let following =
        model.Details |> Option.map _.IsFollowing |> Option.defaultValue false

      let toggle() =
        let request: Users.Request = {
          KeyFile = props.Session.KeyFile
          Username = username
        }

        if following then
          Users.unfollow(props.Env, request)
        else
          Users.follow(props.Env, request)

      { model with Error = None }, Cmd.OfAsync.perform toggle () FollowDone
    | MuteToggled ->
      let muting =
        model.Details |> Option.map _.IsMuting |> Option.defaultValue false

      let toggle() =
        let request: Users.Request = {
          KeyFile = props.Session.KeyFile
          Username = username
        }

        if muting then
          Users.unmute(props.Env, request)
        else
          Users.mute(props.Env, request)

      { model with Error = None }, Cmd.OfAsync.perform toggle () MuteDone
    | BlockToggled ->
      let toggle() =
        let request: Users.Request = {
          KeyFile = props.Session.KeyFile
          Username = username
        }

        Users.block(props.Env, request)

      { model with Error = None }, Cmd.OfAsync.perform toggle () BlockDone
    | FollowDone result ->
      match result with
      | Ok _ ->
        // the follow state lives on the profile; a refetch keeps it honest
        { model with Error = None },
        Cmd.batch [
          Cmd.OfAsync.perform
            Users.get
            (props.Env,
             {
               KeyFile = props.Session.KeyFile
               Username = username
             })
            Fetched

          Cmd.ofAction(fun () ->
            log.LogInformation("The follow of {User} changed.", username))
        ]
      | Error error ->
        { model with Error = Some error },
        failed $"the follow of {username}" error
    | MuteDone result ->
      match result with
      | Ok _ ->
        { model with Error = None },
        Cmd.batch [
          Cmd.OfAsync.perform
            Users.get
            (props.Env,
             {
               KeyFile = props.Session.KeyFile
               Username = username
             })
            Fetched

          Cmd.ofAction(fun () ->
            log.LogInformation("The mute of {User} changed.", username))
        ]
      | Error error ->
        { model with Error = Some error },
        failed $"the mute of {username}" error
    | BlockDone result ->
      match result with
      | Ok _ ->
        model,
        Cmd.batch [
          Cmd.ofEffect(fun _ -> Navigation.go(props.Nav, "/log/latest", ignore))

          Cmd.ofAction(fun () ->
            log.LogInformation("The block of {User} changed.", username))
        ]
      | Error error ->
        { model with Error = Some error },
        failed $"the block of {username}" error
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
        Async.Start(
          TranslationModel.store(
            (props.Env :> IProvideTranslations).Translations,
            args
          )
        ))
    | MenuOpened commit ->
      // The relation the menu speaks loads once per author handle; the
      // read carries no block flag, so that flag moves only when a
      // block answers. The viewer's own commit needs no read at all.
      if
        commit.Author.Id = props.Session.Session.User.Id
        || model.Relations |> Map.containsKey commit.Author.Handle
      then
        model, Cmd.none
      else
        model,
        Cmd.OfAsync.perform
          props.Env.Profiles.Fetch
          commit.Author.Handle
          (fun details ->
            let toRelation(loaded: ProfileDetails) : AuthorRelation = {
              IsFollowing = loaded.IsFollowing
              IsMuting = loaded.IsMuting
              IsBlocking = false
            }

            let relation =
              details |> Option.map toRelation |> ValueOption.ofOption

            RelationLoaded(commit.Author.Handle, relation))
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
      let held = heldRelation(model, commit.Author.Handle)

      {
        model with
            MenuBusy = Some commit.Id
            Relations =
              model.Relations
              |> Map.add commit.Author.Handle {
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
            commit.Author.Handle
          ))
      | Error error ->
        let held = heldRelation(model, commit.Author.Handle)

        {
          model with
              MenuBusy = None
              Relations =
                model.Relations
                |> Map.add commit.Author.Handle {
                  held with
                      IsFollowing = not held.IsFollowing
                }
        },
        failed "the follow toggle" error
    | AuthorMuteToggled commit ->
      let held = heldRelation(model, commit.Author.Handle)

      {
        model with
            MenuBusy = Some commit.Id
            Relations =
              model.Relations
              |> Map.add commit.Author.Handle {
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
        let held = heldRelation(model, commit.Author.Handle)

        {
          model with
              MenuBusy = None
              Relations =
                model.Relations
                |> Map.add commit.Author.Handle {
                  held with
                      IsMuting = mute.IsMuting
                }
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The mute of @{Author} settled.", commit.Author.Handle))
      | Error error ->
        let held = heldRelation(model, commit.Author.Handle)

        {
          model with
              MenuBusy = None
              Relations =
                model.Relations
                |> Map.add commit.Author.Handle {
                  held with
                      IsMuting = not held.IsMuting
                }
        },
        failed "the mute toggle" error
    | AuthorBlockRequested commit ->
      let held = heldRelation(model, commit.Author.Handle)

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
        let held = heldRelation(model, commit.Author.Handle)

        {
          model with
              MenuBusy = None
              Relations =
                model.Relations
                |> Map.add commit.Author.Handle {
                  held with
                      IsBlocking = blocked.IsBlocking
                }
        },
        Cmd.ofAction(fun () ->
          log.LogInformation(
            "The block of @{Author} settled.",
            commit.Author.Handle
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
        // toast and owns the restore. The pinned slot carries the same
        // commit when it is pinned, so it folds there too.
        {
          model with
              Commits =
                model.Commits
                |> List.map(function
                  | LiveCommit commit when commit.Id = commitId ->
                    Tombstone(tombstoneOf commit)
                  | node -> node)
              Details =
                model.Details
                |> Option.map(fun details ->
                  match details.PinnedCommit with
                  | Some(LiveCommit pinned) when pinned.Id = commitId -> {
                      details with
                          PinnedCommit = Some(Tombstone(tombstoneOf pinned))
                    }
                  | _ -> details)
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
    | HeaderChosen Full -> { model with HeaderForm = Full }, Cmd.none
    | HeaderChosen Compact -> { model with HeaderForm = Compact }, Cmd.none
    | HeaderChosen Auto -> model, Cmd.none
    | FeedRead place when model.Details.IsSome ->
      if model.PlaceReported then
        // The window's shortness decided once at the first layout;
        // the reading place keeps following the scroll.
        let compact = model.ShortWindow || place.Offset > 0.5

        if compact = model.AutoCompact then
          model, Cmd.none
        else
          { model with AutoCompact = compact }, Cmd.none
      else
        let shortWindow = place.Viewport < HeaderFloor

        {
          model with
              PlaceReported = true
              ShortWindow = shortWindow
              AutoCompact = shortWindow || place.Offset > 0.5
        },
        Cmd.none
    | FeedRead _ -> model, Cmd.none

  let update(props: Props<'env>) = updateCore(loggerOf props, props)

  // The page's inlets from outside: the shared clock and the session
  // store.
  let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
    [
      Sub.fromObservable
        "clock"
        (props.Env.UiClock.Minute |> Observable.map ClockTicked)

      Sub.fromObservable
        "session"
        (props.Env.Session.Observe |> Observable.map SessionChanged)
    ]
    |> Sub.batch

// The page's semantic regions; the view only composes these.
module Parts =

  let followButton
    (details: ProfileDetails, dispatch: Dispatch<State.Msg>)
    : Control =
    Button()
      .Classes(Theme.name Theme.NavButton)
      .Content(if details.IsFollowing then "Unfollow" else "Follow")
      .OnClickHandler(fun _ _ -> dispatch State.FollowToggled)

  let muteButton
    (details: ProfileDetails, dispatch: Dispatch<State.Msg>)
    : Control =
    Button()
      .Classes(Theme.name Theme.NavButton)
      .Content(if details.IsMuting then "Unmute" else "Mute")
      .OnClickHandler(fun _ _ -> dispatch State.MuteToggled)

  let blockButton(dispatch: Dispatch<State.Msg>) : Control =
    Button()
      .Classes(Theme.name Theme.DangerButton)
      .Content("Block")
      .OnClickHandler(fun _ _ -> dispatch State.BlockToggled)

  // The follow, mute, and block actions; the viewer's own header
  // carries none.
  let actions
    (details: ProfileDetails, isSelf: bool, dispatch: Dispatch<State.Msg>)
    : Control array =
    if isSelf then
      [||]
    else
      [|
        followButton(details, dispatch)
        muteButton(details, dispatch)
        blockButton dispatch
      |]

  // One small label chip, the site's "Follows you" and "Muted" marks.
  let mark(text: string) : Control =
    Border()
      .Classes(Theme.name Theme.Chip)
      .CornerRadius(999.0)
      .Child(
        TextBlock().Classes(Theme.name Theme.Muted).FontSize(11.0).Text(text)
      )

  // The lane-tinted level chip the profile and the XP shelf carry.
  let levelChip(achievements: Achievements, lane: Lane option) : Control =
    let label =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .FontSize(11.0)
        .Text($"Lv. {achievements.Level} · {achievements.Rank}")

    Theme.laneText(label, lane)

    let chip =
      Border()
        .BorderThickness(Thickness(1.0))
        .CornerRadius(999.0)
        .Padding(8.0, 2.0, 8.0, 2.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .HorizontalAlignment(HorizontalAlignment.Left)
        .Child(label)

    Theme.laneBorder(chip, lane)

    chip

  // The joined-and-links line under the bio: company, location, the
  // website host, and the join month.
  let metaLine(profile: Profile) : Control =
    let stripScheme(url: string) =
      url.Replace("https://", "").Replace("http://", "")

    let joined =
      profile.CreatedAt.ToString("MMMM yyyy", CultureInfo.InvariantCulture)

    let parts: string list = [
      match profile.Company with
      | Some company -> company
      | None -> ()

      match profile.Location with
      | Some location -> location
      | None -> ()

      match profile.Website |> Option.map stripScheme with
      | Some website -> website
      | None -> ()

      $"Joined {joined}"
    ]

    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .TextWrapping(TextWrapping.Wrap)
      .Text(String.Join(" · ", parts))

  // The activity bars: one lane-colored column per bucket, bottom
  // aligned, the height following the count.
  let activityView(activity: Activity, lane: Lane option) : Control =
    let buckets = activity.Buckets |> List.truncate 52

    let caption =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text($"activity · {activity.Scale}")

    match buckets with
    | [] -> StackPanel().Children(caption)
    | _ ->
      let maxCount = buckets |> List.maxBy _.Count |> _.Count |> max 1

      let bars = Grid()

      bars.Height <- 56.0

      bars.ColumnDefinitions <-
        ColumnDefinitions(String.Join(",", [ for _ in buckets -> "6" ]))

      buckets
      |> List.iteri(fun index bucket ->
        let height = 8.0 + float bucket.Count / float maxCount * 44.0

        let bar =
          Border()
            .Width(6.0)
            .Height(height)
            .CornerRadius(1.0)
            .VerticalAlignment(VerticalAlignment.Bottom)
            .Column(index)

        Theme.laneBack(bar, lane)

        bars.Children(bar) |> ignore)

      StackPanel().Spacing(6.0).Children(caption, bars)

  // The streak line under the activity.
  let streaksView(streaks: Streaks) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .Text(
        $"longest streak {streaks.Longest} days · current streak {streaks.Current} days"
      )

  // The XP shelf: the level chip, the progress bar toward the next
  // level, and the badge count.
  let xpView(achievements: Achievements, lane: Lane option) : Control =
    let progress = achievements.LevelProgress |> max 0 |> min 100

    let fill =
      Border()
        .Height(6.0)
        .CornerRadius(3.0)
        .VerticalAlignment(VerticalAlignment.Center)

    Theme.laneBack(fill, lane)

    let track = Grid()

    track.Height <- 6.0

    track.ColumnDefinitions <-
      ColumnDefinitions($"{progress}*,{100 - progress}*")

    track.Children(fill.Column(0)) |> ignore

    let next =
      achievements.NextLevelXp
      |> Option.map(fun target -> $" · next {target}")
      |> Option.defaultValue ""

    let badges =
      let unlocked =
        achievements.Badges |> List.filter _.Unlocked |> List.length

      $"{unlocked}/{List.length achievements.Badges} badges"

    StackPanel()
      .Spacing(6.0)
      .Margin(0.0, 16.0, 0.0, 0.0)
      .Children(
        DockPanel()
          .LastChildFill(true)
          .Children(
            TextBlock()
              .Classes(Theme.name Theme.Mono)
              .Text($"{achievements.Xp} xp{next}")
              .DockRight(),
            levelChip(achievements, lane)
          ),
        Border()
          .Classes(Theme.name Theme.SurfaceBack)
          .CornerRadius(3.0)
          .Child(track),
        TextBlock().Classes(Theme.name Theme.Muted).Text(badges)
      )

  // The level chip when achievements exist, nothing when they do not.
  let achievementsChip
    (achievements: Achievements option, lane: Lane option)
    : Control =
    match achievements with
    | Some shelf -> levelChip(shelf, lane)
    | None -> Status.empty ""

  // The sections between the identity header and the tabs: the
  // activity bars, the streak line, and the XP shelf.
  let profileSections(details: ProfileDetails) : Control array =
    let lane = details.Profile.Lane

    [|
      match details.Activity with
      | Some activity -> activityView(activity, lane)
      | None -> ()

      match details.Streaks with
      | Some streaks -> streaksView streaks
      | None -> ()

      match details.Achievements with
      | Some shelf -> xpView(shelf, lane)
      | None -> ()
    |]

  // What the identity header reads: the profile, the viewer's
  // relation to it, the dispatch, the image cache, and the nav the
  // count buttons follow.
  [<Struct>]
  type IdentityCtx = {
    Images: ImageCacheService
    Details: ProfileDetails
    IsSelf: bool
    Dispatch: Dispatch<State.Msg>
    Nav: INavigable
  }

  // One count-and-label pair, the stats row's piece.
  let inline statPair(count: int, label: string) : Control =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(4.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Children(
        TextBlock().Classes(Theme.name Theme.StatNum).Text(string count),
        TextBlock().Classes(Theme.name Theme.Muted).Text(label)
      )

  // The stats row whose following and followers counts are quiet
  // buttons over their numbers: each opens its list for the profile's
  // own handle. The flyout's card keeps the plain counts row.
  let counts(profile: Profile, nav: INavigable) : Control =
    let statButton(name: string, pair: Control, url: string) =
      Button()
        .Name(name + ":" + profile.User.Handle)
        .Classes(Theme.name Theme.GhostButton)
        .Padding(0.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Content(pair)
        .OnClickHandler(fun _ _ -> Navigation.go(nav, url, ignore))

    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(16.0)
      .Children(
        statPair(profile.CommitsCount |> Option.defaultValue 0, "commits"),
        statButton(
          "ProfileFollowingCount",
          statPair(profile.FollowingCount |> Option.defaultValue 0, "following"),
          "/profile/" + profile.User.Handle + "/following"
        ),
        statButton(
          "ProfileFollowersCount",
          statPair(profile.FollowersCount |> Option.defaultValue 0, "followers"),
          "/profile/" + profile.User.Handle + "/followers"
        )
      )

  // The chevron at the header row's right edge: the glyph points
  // where the header goes next, and the click pins the form it names.
  // The full form shows ChevronsUp and names Compact; the compact bar
  // shows ChevronsDown and names Full.
  let headerToggle
    (dispatch: Dispatch<State.Msg>, glyph: Icons.Glyph, choose: State.HeaderForm) : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Name("ProfileHeaderToggle")
      .Content(Icons.icon glyph)
      .VerticalAlignment(VerticalAlignment.Center)
      .OnClickHandler(fun _ _ -> dispatch(State.HeaderChosen choose))

  // The identity header: the avatar in its lane ring, the actions, the
  // name, the handle row, the bio, the meta line, and the stats.
  let identity(ctx: IdentityCtx) : Control =
    let details = ctx.Details
    let isSelf = ctx.IsSelf
    let dispatch = ctx.Dispatch
    let profile = details.Profile
    let user = profile.User

    let ring =
      Border()
        .Classes(Theme.name Theme.CardBack)
        .BorderThickness(Thickness(2.0))
        .CornerRadius(44.0)
        .Padding(3.0)
        .Child(
          Avatar.view {
            Name = user.Name
            Url = user.AvatarUrl
            Lane = profile.Lane
            Size = 80.0
            Cache = ctx.Images
          }
        )

    Theme.laneBorder(ring, profile.Lane)

    let headerChildren: Control array = [|
      DockPanel()
        .LastChildFill(false)
        .Children(
          headerToggle(ctx.Dispatch, Icons.ChevronsUp, State.HeaderForm.Compact)
            .DockRight(),
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .VerticalAlignment(VerticalAlignment.Top)
            .DockRight()
            .Children(actions(details, isSelf, dispatch)),
          ring
        )
      TextBlock().Classes(Theme.name Theme.Headline).Text(user.Name)
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(10.0)
        .Children(
          ProfileCard.laneHandle(profile.Lane, user.Handle),
          mark("Follows you").IsVisible(details.FollowsYou),
          mark("Muted").IsVisible(details.IsMuting),
          achievementsChip(details.Achievements, profile.Lane)
        )
      TextBlock()
        .TextWrapping(TextWrapping.Wrap)
        .IsVisible(profile.Bio.IsSome)
        .Text(profile.Bio |> Option.defaultValue "")
      metaLine profile
      counts(profile, ctx.Nav)
    |]

    StackPanel().Spacing(8.0).Children(headerChildren)

  // The compact header: the small avatar, the name, the handle, and
  // the counts on one row, with the unfold chevron at its right edge.
  let compactHeader(ctx: IdentityCtx) : Control =
    let profile = ctx.Details.Profile
    let user = profile.User

    let row =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(10.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Children(
          Avatar.view {
            Name = user.Name
            Url = user.AvatarUrl
            Lane = profile.Lane
            Size = 32.0
            Cache = ctx.Images
          },
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .VerticalAlignment(VerticalAlignment.Center)
            .Text(user.Name),
          ProfileCard.laneHandle(profile.Lane, user.Handle),
          counts(profile, ctx.Nav)
        )

    DockPanel()
      .LastChildFill(false)
      .MinHeight(44.0)
      .Children(
        headerToggle(ctx.Dispatch, Icons.ChevronsDown, State.HeaderForm.Full)
          .DockRight(),
        row
      )

  // The space the full header never takes: the page's 88 DIP of fixed
  // chrome (the 40 DIP band padding, the 48 DIP tab strip) plus a
  // 120 DIP feed band.
  let HeaderCapRoom = 208.0

  // The header host, rebuilt when the profile changes: the follow,
  // mute, and block states come back from the server after every
  // action. Both forms build with it and only their visibility flips,
  // so a fold never rebuilds the feed below.
  let headerHost
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    let form =
      model
      |> Observable.map State.effectiveForm
      |> Slices.gate id
      |> Slices.share

    let formIs(wanted: State.HeaderForm) =
      form |> Observable.map(fun current -> current = wanted)

    let content =
      PageChrome.rebuildOn(
        model,
        (fun current -> current.Details),
        (fun current ->
          match current.Details with
          | Some details ->
            let ctx = {
              Images = props.Env.ImageCache
              Details = details
              IsSelf = details.Profile.User.Id = props.Session.Session.User.Id
              Dispatch = dispatch
              Nav = props.Nav
            }

            // The full form is the identity and the sections together,
            // capped at the space above a minimum feed band: a pinned
            // header scrolls inside the cap instead of starving the
            // feed. The cap follows the window on the scroller's own
            // layout passes, so the subscription dies with the control.
            let fullStack =
              StackPanel()
                .Spacing(16.0)
                .Children(
                  Array.append [| identity ctx |] (profileSections details)
                )

            let fullForm =
              ScrollViewer()
                .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
                .Content(fullStack)
                .IsVisible(formIs State.HeaderForm.Full)

            fullForm.LayoutUpdated.Add(fun _ ->
              match TopLevel.GetTopLevel fullForm |> Option.ofObj with
              | Some top ->
                let cap = top.ClientSize.Height - HeaderCapRoom

                if cap > 0.0 && fullForm.MaxHeight <> cap then
                  fullForm.MaxHeight <- cap
              | None -> ())

            let compactForm =
              (compactHeader ctx).IsVisible(formIs State.HeaderForm.Compact)

            StackPanel().Spacing(16.0).Children(fullForm, compactForm)
          | None -> Status.empty "")
      )

    ContentControl().Content(content.ToBinding())

// The row inputs the page compares by reference, so a tick that left
// them the same does not reshape the feed.
let rowInputsComparer = {
  new IEqualityComparer<ProfileDetails option * CommitNode list * State.Tab> with
    member _.Equals
      (
        (leftDetails, leftCommits, leftTab),
        (rightDetails, rightCommits, rightTab)
      ) =
      leftTab = rightTab
      && ReferenceEqualityComparer.Instance.Equals(leftDetails, rightDetails)
      && ReferenceEqualityComparer.Instance.Equals(leftCommits, rightCommits)

    member _.GetHashCode((details, commits, tab)) =
      HashCode.Combine(
        RuntimeHelpers.GetHashCode details,
        RuntimeHelpers.GetHashCode commits,
        tab.GetHashCode()
      )
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
          | CommitCard.ReplyToggled c ->
            Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
          | CommitCard.MoreRepliesRequested c ->
            Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
          | CommitCard.QuoteRequested c ->
            Navigation.go(props.Nav, "/quote/" + c.Id, ignore)
          | CommitCard.PollChoiceChanged(commit, option, chosen) ->
            dispatch(State.PollChoiceChanged(commit, option, chosen))
          | CommitCard.TranslateRequested commit ->
            dispatch(State.TranslateRequested commit)
          | CommitCard.OriginalRequested commit ->
            dispatch(State.OriginalRequested commit)
          | CommitCard.MenuOpened commit -> dispatch(State.MenuOpened commit)
        ViewerId = props.Session.Session.User.Id
        Images = props.Env.ImageCache
        Flyout =
          ValueSome {
            UserFlyout.Fetch = props.Env.Profiles.Fetch
            OnOpen =
              fun username ->
                Navigation.go(props.Nav, "/profile/" + username, ignore)
            OnOpenOrg =
              fun handle -> Navigation.go(props.Nav, "/org/" + handle, ignore)
            Cache = props.Env.ImageCache
          }
        Zone = zoneOf((props.Env :> IProvideUserSettings).UserSettings.Current)
        OnPin = ValueSome(State.PinToggled >> dispatch)
        Menu =
          ValueSome {
            Share = ValueNone
            Report = ValueNone
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
                  |> Map.tryFind commit.Author.Handle
                  |> ValueOption.ofOption)
            Busy = model |> Observable.map _.MenuBusy
          }
        Actions = true
        OnStar = fun commit -> dispatch(State.StarToggled commit)
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
        Pinning = current.Pinning
      })
    )

  ProfileShell.view {
    Model = model
    Title = fun _ -> ""
    Kind = None
    Padding = Theme.pinnedPadding
    ErrorOf = _.Error
    Busy =
      fun current ->
        if current.Loading then
          PageChrome.Words "Loading the profile…"
        else
          PageChrome.Off
    Empty =
      fun current ->
        // A profile with nothing but a pinned commit shows the pinned
        // slot, not the empty state — the server keeps the pinned
        // commit out of the list itself.
        let pinned =
          match current.Tab with
          | State.Commits ->
            current.Details |> Option.bind _.PinnedCommit |> Option.isSome
          | State.Replies -> false

        if
          not current.Loading
          && current.Error.IsNone
          && current.Commits.IsEmpty
          && not pinned
        then
          PageChrome.Words(
            match current.Tab with
            | State.Commits -> "No commits yet."
            | State.Replies -> "No replies yet."
          )
        else
          PageChrome.Off
    Note = _.Note
    Header = fun () -> Parts.headerHost(props, dispatch, model)
    Meta = ValueNone
    FeedLabel = None
    Tabs = [
      {
        Label = "Commits"
        Active =
          model |> Observable.map(fun current -> current.Tab = State.Commits)
        Badge = ValueNone
        Choose = fun () -> dispatch(State.TabChosen State.Commits)
      }
      {
        Label = "Replies"
        Active =
          model |> Observable.map(fun current -> current.Tab = State.Replies)
        Badge = ValueNone
        Choose = fun () -> dispatch(State.TabChosen State.Replies)
      }
    ]
    Feed = {
      Items =
        model
        |> Observable.distinctUntilChangedKeyCompare
          (fun current -> current.Details, current.Commits, current.Tab)
          rowInputsComparer
        |> Observable.map State.rowsOf
      KeyOf = State.rowKey
      Row =
        function
        | State.PinnedCaption -> FeedParts.pinnedRow()
        | State.PinnedNode node -> ProfileShell.commitRow(ctx, node)
        | State.FeedNode node -> ProfileShell.commitRow(ctx, node)
      HasMore = model |> Observable.map _.NextCursor.IsSome
      OnMore = fun () -> dispatch State.MoreCommits
      OnRide = ValueSome(State.FeedRead >> dispatch)
      End = System.Reactive.Linq.Observable.Return(None: string option)
    }
  }

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideUserSettings
  and 'env :> IProvideTranslations
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Programs.runPage(props.Env.Dispatcher, program)

    this.Content <- root
    props.Context.AddDisposable lifetime
