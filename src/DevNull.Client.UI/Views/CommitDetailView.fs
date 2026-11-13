module DevNull.Client.Views.CommitDetailView

open System
open System.Collections.Generic
open System.Reactive.Disposables
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Shapes
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open MVUish.Avalonia.Collections
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
open DevNull.Client.ImageCache
open DevNull.Client.Formatting
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Timeline
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore
open DevNull.Client.TranslationCache

// The hover card source record; the alias keeps its long name out of
// every use site.
type HoverSource = DevNull.Client.Components.UserFlyout.Source

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
  and 'env :> IProvideProfiles
  and 'env :> IProvideSshKeys
  and 'env :> IProvideTimeline
  and 'env :> IProvideRemotes
  and 'env :> IProvideUserSettings
  and 'env :> IProvideTranslations
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging> = {
  Env: 'env
  CommitId: string
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
  Quoting: bool
}

// The page's state logic: model, messages, update, and the
// subscriptions that feed it from outside.
module State =

  // One nested reply list the page grew past the payload's preview
  // slice: the replies shown and the cursor the next page asks for.
  [<Struct>]
  type PreviewState = {
    Shown: CommitNode list
    Cursor: string option
  }

  [<Struct>]
  type StarsAsk = {
    Session: SignedInSession
    CommitId: string
    Cursor: string option
  }

  type Model = {
    Loading: bool
    Error: AppError option
    Page: CommitPage option
    Replies: CommitNode list
    RepliesCursor: string option
    // The commits closest in meaning to the focused one, closest
    // first; a failed read stays an empty list and shows nothing.
    Related: CommitNode list
    Note: string option
    Now: DateTimeOffset
    Pasteis: bool
    Replying: bool
    // The commit whose poll vote is in flight, so its buttons disable.
    Voting: string option
    // The commit whose pin call is in flight, so its button disables.
    Pinning: string option
    // The option a viewer picked in each open poll, by commit id.
    PollChoices: Map<string, string>
    // Whether the quote composer is armed: from the route, or from
    // the focused card's quote action.
    Quoting: bool
    // The nested reply lists the page grew past their preview
    // slices, by parent id.
    Expanded: Map<string, PreviewState>
    // Whether translations show by themselves.
    Automatic: bool
    // Every commit's translation on this page.
    Translations: TranslationModel.Book
    // The commit whose share dialog stands open; the page model owns
    // it, and the dialog layer reads it.
    ShareOpen: Commit voption
    // The commit whose report dialog stands open.
    ReportOpen: Commit voption
    // The commit whose stargazers list stands open; the page model
    // owns it, and the dialog layer reads it.
    StarsOpen: Commit voption
    // The stargazers read so far, newest first.
    Stars: User list
    StarsCursor: string option
    StarsBusy: bool
    StarsError: AppError option
    // The viewer's relation per author handle, as the More menu reads
    // it.
    Relations: Map<string, AuthorRelation>
    // The commit whose menu action is in flight, so its items disable.
    MenuBusy: string option
    // The commit id whose deletion landed; the card folds to its
    // marker in place, the way the site keeps the deleted card inert.
    Deleted: string option
  }

  type Msg =
    | ThreadFetched of Result<CommitPage, AppError>
    | RepliesFetched of Result<Page<CommitNode>, AppError>
    | RelatedFetched of Result<CommitNode list, AppError>
    | ReloadRequested
    | MoreReplies
    | CardEvent of CommitCard.Event
    | StarToggled of Commit
    | PollVoteRequested of Commit
    | PastelGiven of Commit
    | PastelRequested of commitId: string
    | DeleteRequested of Commit
    | StarDone of Result<Commit, AppError>
    | PollVotedDone of Result<Commit, AppError>
    | PastelDone of Result<PastelResult, AppError>
    | Deleted of Result<unit, AppError>
    | PinToggled of Commit
    | PinDone of pinned: bool * Result<Commit, AppError>
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState
    | NodeMoreRequested of Commit
    | NodeRepliesLoaded of commitId: string * Result<Page<CommitNode>, AppError>
    | TranslationDone of Commit * Result<TranslationPayload, AppError>
    | TranslationRecalled of Commit * TranslationModel.Book option
    | ShareOpened of Commit
    | ShareClosed
    | Shared
    | ReportOpened of Commit
    | ReportClosed
    | StarsOpened of Commit
    | StarsClosed
    | StarsFetched of StarsAsk * Result<Page<User>, AppError>
    | StarsMore
    | MenuOpened of Commit
    | RelationLoaded of handle: string * AuthorRelation voption
    | AuthorFollowToggled of Commit
    | AuthorFollowDone of Commit * Result<unit, AppError>
    | AuthorMuteToggled of Commit
    | AuthorMuteDone of Commit * Result<MuteResult, AppError>
    | AuthorBlockRequested of Commit
    | AuthorBlockDone of Commit * Result<BlockResult, AppError>

  let initial = {
    Loading = true
    Error = None
    Page = None
    Replies = []
    RepliesCursor = None
    Related = []
    Note = None
    Now = DateTimeOffset.UtcNow
    Pasteis = false
    Replying = false
    Voting = None
    Pinning = None
    PollChoices = Map.empty
    Quoting = false
    Expanded = Map.empty
    Automatic = false
    Translations = TranslationModel.initial
    ShareOpen = ValueNone
    ReportOpen = ValueNone
    StarsOpen = ValueNone
    Stars = []
    StarsCursor = None
    StarsBusy = false
    StarsError = None
    Relations = Map.empty
    MenuBusy = None
    Deleted = None
  }

  let thread(env, session: SignedInSession, commitId: string) =
    Commits.show(
      env,
      {
        KeyFile = session.KeyFile
        CommitId = commitId
      }
    )

  let related(env, session: SignedInSession, commitId: string) =
    Commits.related(
      env,
      {
        KeyFile = session.KeyFile
        CommitId = commitId
      }
    )

  [<Struct>]
  type RepliesAsk = {
    Session: SignedInSession
    CommitId: string
    Cursor: string option
  }

  let replies(env, ask: RepliesAsk) =
    Commits.replies(
      env,
      {
        KeyFile = ask.Session.KeyFile
        CommitId = ask.CommitId
        Newest = false
        Cursor = ask.Cursor
      }
    )

  let stargazers(env, ask: StarsAsk) =
    Commits.stargazers(
      env,
      {
        KeyFile = ask.Session.KeyFile
        CommitId = ask.CommitId
        Cursor = ask.Cursor
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
          Quoting = props.Quoting
    },
    Cmd.batch [
      Cmd.OfAsync.perform
        thread
        (props.Env, props.Session, props.CommitId)
        ThreadFetched

      Cmd.OfAsync.perform
        replies
        (props.Env,
         {
           Session = props.Session
           CommitId = props.CommitId
           Cursor = None
         })
        RepliesFetched

      // The related commits read beside the thread itself, the moment
      // the page mounts: nothing of it blocks the thread, and a failed
      // read renders nothing.
      Cmd.OfAsync.perform
        related
        (props.Env, props.Session, props.CommitId)
        RelatedFetched
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

  let withCommit(updated: Commit, model: Model) : Model = {
    model with
        Page =
          model.Page |> Option.map(fun page -> { page with Commit = updated })
        Replies =
          model.Replies
          |> List.map(function
            | LiveCommit commit when commit.Id = updated.Id ->
              LiveCommit updated
            | node -> node)
  }

  let loggerOf(props: Props<'env>) =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.CommitDetail"

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

  let updateCore (log: ILogger, props: Props<'env>) msg model =
    // Every log line is an effect: update returns it as a command,
    // and the loop writes it after the computation.
    let failure = CommitActions.failure log

    let failed what error =
      Cmd.ofAction(fun () -> failure what error)

    match msg with
    | ThreadFetched result ->
      match result with
      | Ok page ->
        {
          model with
              Loading = false
              Error = None
              Page = Some page
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The thread of {Commit} arrived.", props.CommitId))
      | Error error ->
        {
          model with
              Loading = false
              Error = Some error
        },
        failed "the thread fetch" error
    | RepliesFetched result ->
      match result with
      | Ok page ->
        {
          model with
              Replies = model.Replies @ page.Items
              RepliesCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The replies of {Commit} arrived ({Count}).",
            props.CommitId,
            page.Items.Length
          ))
      | Error error ->
        { model with Error = Some error }, failed "the replies fetch" error
    | ReloadRequested ->
      // The whole thread re-reads, replies from their first page: a
      // landed reply arrives through the same read as everything else,
      // so it shows at once instead of on the next visit.
      {
        model with
            Loading = true
            Error = None
            Replies = []
            RepliesCursor = None
      },
      Cmd.batch [
        Cmd.OfAsync.perform
          thread
          (props.Env, props.Session, props.CommitId)
          ThreadFetched

        Cmd.OfAsync.perform
          replies
          (props.Env,
           {
             Session = props.Session
             CommitId = props.CommitId
             Cursor = None
           })
          RepliesFetched
      ]
    | MoreReplies ->
      model,
      Cmd.OfAsync.perform
        replies
        (props.Env,
         {
           Session = props.Session
           CommitId = props.CommitId
           Cursor = model.RepliesCursor
         })
        RepliesFetched
    | StarToggled commit ->
      { model with Error = None },
      Cmd.OfAsync.perform
        CommitActions.toggleStar
        (props.Env, props.Session, commit)
        StarDone
    | CardEvent(CommitCard.Opened commit) ->
      // Navigation is a command, never an inline effect: navigating
      // inside update disposes this very program before the loop can
      // publish the model, and the loop logs an ObjectDisposedException.
      model,
      Cmd.ofEffect(fun _ ->
        Navigation.go(props.Nav, "/commit/" + commit.Id, ignore))
    | PastelGiven commit ->
      { model with Error = None; Note = None },
      Cmd.OfAsync.perform
        CommitActions.pastel
        (props.Env,
         {
           Session = props.Session
           Commit = commit
         })
        PastelDone
    | CardEvent(CommitCard.QuoteRequested commit) ->
      // The focused card quotes in place; any other card opens its own
      // thread with the quote composer armed over it.
      if commit.Id = props.CommitId then
        {
          model with
              Quoting = true
              Replying = false
        },
        Cmd.none
      else
        model,
        Cmd.ofEffect(fun _ ->
          Navigation.go(props.Nav, "/quote/" + commit.Id, ignore))
    | CardEvent(CommitCard.ReplyToggled _) ->
      {
        model with
            Replying = not model.Replying
            Quoting = false
      },
      Cmd.none
    | CardEvent(CommitCard.PollChoiceChanged(commit, option, chosen)) ->
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
    | CardEvent(CommitCard.MoreRepliesRequested commit) ->
      model,
      Cmd.ofEffect(fun _ ->
        Navigation.go(props.Nav, "/commit/" + commit.Id, ignore))
    | CardEvent(CommitCard.TranslateRequested commit) when
      TranslationModel.canReuse(model.Translations, commit)
      ->
      {
        model with
            Translations =
              TranslationModel.showTranslated(model.Translations, commit.Id)
      },
      Cmd.none
    | CardEvent(CommitCard.TranslateRequested commit) ->
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
    | CardEvent(CommitCard.OriginalRequested commit) ->
      {
        model with
            Translations =
              TranslationModel.showOriginal(model.Translations, commit.Id)
      },
      Cmd.none
    | CardEvent(CommitCard.MenuOpened commit) ->
      model, Cmd.ofMsg(MenuOpened commit)
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
        {
          withCommit(updated, model) with
              Pinning = None
              Note =
                Some(
                  if pinned then
                    "Commit pinned to your profile."
                  else
                    "Commit unpinned."
                )
        },
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
      | Error error ->
        {
          model with
              Pinning = None
              Error = Some error
        },
        failed "the profile pin" error
    | RelatedFetched result ->
      match result with
      | Ok nodes ->
        { model with Related = nodes },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The related commits of {Commit} arrived ({Count}).",
            props.CommitId,
            nodes.Length
          ))
      | Error error ->
        // The section stays absent: a failed read must not paint the
        // page's error surface, which speaks for the thread itself.
        model,
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The related commits of {Commit} did not arrive: {Error}",
            props.CommitId,
            bounded 300 (describe error)
          ))
    | NodeMoreRequested commit ->
      // The first ask reads the newest page whole, the way the site's
      // preview does; the next ones walk the cursor it left.
      let cursor =
        model.Expanded |> Map.tryFind commit.Id |> Option.bind _.Cursor

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
        (fun page -> NodeRepliesLoaded(commit.Id, page))
    | NodeRepliesLoaded(commitId, result) ->
      match result with
      | Ok page ->
        let known =
          model.Expanded
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
              Expanded = model.Expanded |> Map.add commitId grown
        },
        Cmd.none
      | Error error ->
        { model with Error = Some error },
        failed "the nested replies fetch" error
    | PastelRequested commitId ->
      model.Page
      |> Option.map _.Commit
      |> Option.filter(fun commit -> commit.Id = commitId)
      |> Option.map(fun commit -> model, Cmd.ofMsg(PastelGiven commit))
      |> Option.defaultValue(model, Cmd.none)
    | DeleteRequested commit ->
      let remove() =
        Commits.delete(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            CommitId = commit.Id
          }
        )

      {
        model with
            Error = None
            MenuBusy = Some commit.Id
      },
      Cmd.batch [
        Cmd.OfAsync.perform remove () Deleted

        Cmd.ofAction(fun () ->
          log.LogInformation(
            "The commit {Commit} is being deleted.",
            commit.Id
          ))
      ]
    | StarDone result ->
      match result with
      | Ok updated ->
        withCommit(updated, model),
        Cmd.ofAction(fun () ->
          log.LogDebug("The star on {Commit} settled.", updated.Id))
      | Error error ->
        { model with Error = Some error }, failed "the star" error
    | PollVotedDone result ->
      match result with
      | Ok updated ->
        {
          withCommit(updated, model) with
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
    | PastelDone result ->
      match result with
      | Ok pastel ->
        {
          withCommit(pastel.Commit, model) with
              Note =
                Some $"Pastel given. {pastel.PasteisRemaining} left this cycle."
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The pastel on {Commit} landed.", pastel.Commit.Id))
      | Error error ->
        { model with Error = Some error }, failed "the pastel" error
    | Deleted result ->
      match result with
      | Ok _ ->
        {
          model with
              Deleted = Some props.CommitId
              MenuBusy = None
        },
        Cmd.batch [
          Cmd.ofAction(fun () ->
            (props.Env :> IProvideTimeline).Timeline.AnnounceDeletion
              props.CommitId)

          Cmd.ofAction(fun () ->
            log.LogInformation(
              "The commit {Commit} was deleted.",
              props.CommitId
            ))
        ]
      | Error error ->
        {
          model with
              Error = Some error
              MenuBusy = None
        },
        failed "the commit deletion" error
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
    | StarsOpened commit ->
      // The gate decides inside the case: only the author's own
      // commit opens the list, and the list reads fresh on every
      // open, so stars given in the meantime show up without a reload.
      let own =
        (Author.id commit.Author) = props.Session.Session.User.Id
        && not(Author.isOrganization commit.Author)

      match own with
      | false -> model, Cmd.none
      | true ->
        {
          model with
              StarsOpen = ValueSome commit
              Stars = []
              StarsCursor = None
              StarsBusy = true
              StarsError = None
        },
        Cmd.OfAsync.perform
          stargazers
          (props.Env,
           {
             Session = props.Session
             CommitId = commit.Id
             Cursor = None
           })
          (fun result ->
            StarsFetched(
              {
                Session = props.Session
                CommitId = commit.Id
                Cursor = None
              },
              result
            ))
    | StarsClosed -> { model with StarsOpen = ValueNone }, Cmd.none
    | StarsFetched(ask, result) ->
      match result with
      | Ok page ->
        {
          model with
              Stars =
                match ask.Cursor with
                | None -> page.Items
                | Some _ -> model.Stars @ page.Items
              StarsCursor = page.NextCursor
              StarsBusy = false
              StarsError = None
        },
        Cmd.none
      | Error error ->
        {
          model with
              StarsBusy = false
              StarsError = Some error
        },
        failed "the stars fetch" error
    | StarsMore ->
      // One walk both the load-more word and the retry run: the
      // cursor the list stands on, none on the first page.
      model.StarsOpen
      |> ValueOption.filter(fun _ -> not model.StarsBusy)
      |> ValueOption.map(fun commit ->
        let ask: StarsAsk = {
          Session = props.Session
          CommitId = commit.Id
          Cursor = model.StarsCursor
        }

        {
          model with
              StarsBusy = true
              StarsError = None
        },
        Cmd.OfAsync.perform stargazers (props.Env, ask) (fun result ->
          StarsFetched(ask, result)))
      |> ValueOption.defaultValue(model, Cmd.none)

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

  // The hover card source every thread card shares.
  let flyoutOf<'env
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp
    and 'env :> IProvideDispatcher
    and 'env :> IProvideSession
    and 'env :> IProvideUiClock
    and 'env :> IProvideImageCache
    and 'env :> IProvideAttachmentSources
    and 'env :> IProvideProfiles
    and 'env :> IProvideSshKeys
    and 'env :> IProvideTimeline
    and 'env :> IProvideRemotes
    and 'env :> IProvideUserSettings
    and 'env :> IProvideTranslations
    and 'env :> IProvideBrowser
    and 'env :> IProvideLogging>
    (props: Props<'env>)
    : HoverSource voption =
    let source: HoverSource = {
      Fetch = props.Env.Profiles.Fetch
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

    ValueSome source

  // The thread page's shared card context: the row props every card
  // follows, the page's live model, and its dispatch.
  type Ctx = {
    Rows: CommitCard.RowProps
    Model: IObservable<State.Model>
    Dispatch: Dispatch<State.Msg>
  }

  // The "Show N more" walk of one node's replies, the muted dot of
  // the site's continuation.
  let moreNodeReplies
    (dispatch: Dispatch<State.Msg>, commit: Commit, remaining: int)
    : Control =
    let label =
      match remaining with
      | 1 -> "Show the last reply"
      | count -> $"Show {count} more replies"

    Button()
      .Name("NodeMoreReplies")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(4.0, 2.0, 4.0, 2.0)
      .Margin(0.0, 2.0, 0.0, 2.0)
      .HorizontalAlignment(HorizontalAlignment.Left)
      .Content(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            Ellipse()
              .Width(10.0)
              .Height(10.0)
              .StrokeThickness(1.5)
              .StrokeDashArray(
                Avalonia.Collections.AvaloniaList<double>(2.0, 2.0)
              )
              .Classes(Theme.name Theme.MutedEdge),
            TextBlock().Text(label)
          )
      )
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        dispatch(State.NodeMoreRequested commit))

  // Grows one parent's children on the thread page: the payload's
  // preview slice, or the list the page walked through the cursor,
  // plus the walk button while replies remain. The host rebuilds
  // only when the shown list changed.
  let growChildren
    (ctx: Ctx)
    (child: CommitCard.ChainCtx)
    (parent: Commit)
    : Control =
    ContentControl()
      .Content(
        ctx.Model
        |> Observable.map(fun current ->
          current.Expanded |> Map.tryFind parent.Id)
        |> Observable.distinctUntilChanged
        |> Observable.map(fun grown ->
          match grown with
          | Some state -> ValueSome state.Shown
          | None ->
            parent.ReplyPreview |> Option.map _.Data |> ValueOption.ofOption)
        |> Observable.distinctUntilChanged
        |> Observable.map(fun shown ->
          let carried = shown |> ValueOption.defaultValue []

          let remaining =
            parent.RepliesCount
            |> Option.map(fun count -> count - carried.Length)
            |> Option.defaultValue 0

          let children: Control array = [|
            if not carried.IsEmpty then
              CommitCard.chainRows(
                {
                  child with
                      ParentLane = (Author.lane parent.Author)
                      ParentHandle = (Author.handle parent.Author)
                },
                carried
              )

            if remaining > 0 then
              moreNodeReplies(ctx.Dispatch, parent, remaining)
          |]

          StackPanel().Children(children): Control)
        |> _.ToBinding()
      )

  // The chain context of the thread page: every parent grows its
  // children through the cursor.
  let threadChain
    (ctx: Ctx, lane: Lane option, handle: string)
    : CommitCard.ChainCtx = {
    Props = ctx.Rows
    Depth = 0
    ParentLane = lane
    ParentHandle = handle
    Grow = ValueSome(growChildren ctx)
  }

  // One ancestor hop above the card: the author and a one-line body.
  let ancestorRow(props: Props<'env>, node: CommitNode) : Control =
    match node with
    | LiveCommit commit ->
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(4.0, 2.0, 4.0, 2.0)
        .Content(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(6.0)
            .Children(
              TextBlock().Text($"@{(Author.handle commit.Author)}"),
              (let body =
                CommitCard.body(
                  {
                    Flyout = flyoutOf props
                    Tone = CommitCard.Accent
                    Mentions = commit.Mentions
                    OnOpenExternal =
                      ValueSome(fun url ->
                        props.Env.Browser.Open(url) |> ignore)
                  },
                  commit.Body,
                  commit.BodySegments
                )

               match body with
               | :? TextBlock as block ->
                 block.Classes.Add(Theme.name Theme.Muted)
                 block.MaxLines <- 1
                 block.TextTrimming <- TextTrimming.CharacterEllipsis
                 block :> Control
               | other -> other)
            )
        )
        .OnClickHandler(fun _ _ ->
          Navigation.go(props.Nav, "/commit/" + commit.Id, ignore))
    | Tombstone _ ->
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Text("deleted")
        .Margin(4.0, 2.0)

  // The wire URL of the log the back line returns to: the log the
  // reader last had open, or Latest when no log page stands behind
  // the thread in this session.
  let backTarget(log: LogKind option) : string =
    match log with
    | Some kind -> "/log/" + LogKinds.wire kind
    | None -> "/log/latest"

  // The back affordance above the thread.
  let backLine(props: Props<'env>) : Control =
    Button()
      .Name("BackToLog")
      .Classes(Theme.name Theme.GhostButton)
      .Content("Back to the log")
      .OnClickHandler(fun _ _ ->
        Navigation.go(props.Nav, backTarget props.Env.Timeline.LastLog, ignore))

  // The transient note line (the pastel cycle count after a give).
  let noteLine(model: IObservable<State.Model>) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Text(
        model
        |> Observable.map(fun current -> current.Note |> Option.defaultValue "")
      )
      .IsVisible(model |> Observable.map _.Note.IsSome)

  // The loading line while the thread reads.
  let busy(model: IObservable<State.Model>) : Control =
    Status
      .busy("Loading the commit…")
      .IsVisible(model |> Observable.map _.Loading)

  // The card is rebuilt whenever the focused page changes: the snapshot
  // keeps the star visual honest, and an unrelated tick leaves it alone.
  // A landed deletion folds it to the marker in place.
  let cardHost(ctx: Ctx) : Control =
    let content =
      PageChrome.rebuildOn(
        ctx.Model,
        (fun current -> current.Page, current.Deleted),
        (fun current ->
          match current.Page, current.Deleted with
          | Some page, Some deleted when deleted = page.Commit.Id ->
            CommitCard.tombstone()
          | Some page, _ ->
            CommitCard.feedRow ctx.Rows (LiveCommit page.Commit)
          | None, _ -> Status.empty "")
      )

    ContentControl().Content(content.ToBinding())

  // The ancestor chain above the card.
  let ancestors(props: Props<'env>, model: IObservable<State.Model>) : Control =
    ItemsControl()
      .ItemTemplate(
        FuncDataTemplate<CommitNode>(
          (fun node _ -> ancestorRow(props, node)),
          false
        )
      )
      .BindItemsSource(
        model
        |> Observable.map(fun current ->
          match current.Page with
          | Some page -> page.Ancestors :> IReadOnlyList<CommitNode>
          | None -> [] :> IReadOnlyList<CommitNode>),
        FeedParts.nodeKey
      )

  // The reply column under the card: the root replies as the site's
  // slim chain. The focused card runs its lane tail to its bottom
  // edge while replies show, so the first row's elbow starts exactly
  // at that boundary.
  let replies(ctx: Ctx) : Control =
    ContentControl()
      .Content(
        ctx.Model
        |> Observable.map(fun current ->
          match current.Page with
          | Some page ->
            Some(
              (Author.lane page.Commit.Author),
              (Author.handle page.Commit.Author),
              current.Replies
            )
          | None -> None)
        |> Observable.distinctUntilChanged
        |> Observable.map(function
          | Some(lane, handle, nodes) when not nodes.IsEmpty ->
            StackPanel()
              .Margin(40.0, 0.0, 0.0, 0.0)
              .Children(
                CommitCard.chainRows(threadChain(ctx, lane, handle), nodes)
              )
            : Control
          | _ -> Status.empty "")
        |> _.ToBinding()
      )

  // The pager under the replies, while another page exists.
  let moreReplies
    (dispatch: Dispatch<State.Msg>, model: IObservable<State.Model>)
    : Control =
    Button()
      .Content("More replies")
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Margin(0.0, 8.0, 0.0, 8.0)
      .IsVisible(model |> Observable.map _.RepliesCursor.IsSome)
      .OnClickHandler(fun _ _ -> dispatch State.MoreReplies)

  // The audience rows of one thread read: the parent's author first,
  // then the read's other participants, every row render-ready.
  let participantsOf
    (page: CommitPage option)
    : ReplyPicker.ParticipantRow list voption =
    page
    |> Option.map(fun read ->
      let author = read.Commit.Author

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

  // The answer composers under the thread. The reply composer hides
  // until the card's reply button reveals it; it mounts at that first
  // reveal, when the thread's read has landed and its page carries the
  // participants the picker reads — no fetch of its own. A later
  // toggle only hides the mounted composer. The quote composer arms
  // from the route or the focused card's quote action; a landed quote
  // opens the new commit's thread. Both mount once: a rebuild on every
  // model change would wipe the typed text.
  let answerComposer
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    let replyHost = ContentControl()

    let mounted = new SerialDisposable()

    replyHost.AttachedToVisualTree.Add(fun _ ->
      mounted.Disposable <-
        model
        |> Observable.map(fun current -> current.Replying)
        |> Observable.filter(fun replying -> replying)
        |> Observable.take 1
        |> Observable.subscribe(fun _ ->
          let page =
            model
            |> Observable.first
            |> Observable.toEnumerable
            |> Seq.tryHead
            |> Option.bind _.Page

          replyHost.Content <-
            Composer.Page {
              Env = props.Env
              Session = props.Session
              Target = Composer.ReplyTo props.CommitId
              Participants = participantsOf page
              OnCommitted = fun _ -> dispatch State.ReloadRequested
              OnScheduled = ValueNone
              OnOpenQueue = ValueNone
              Draft = ValueNone
              OnBodyReady = ValueNone
            }
            :> Control))

    replyHost.DetachedFromVisualTree.Add(fun _ ->
      mounted.Disposable <- Disposable.Empty)

    let quote =
      Composer.Page {
        Env = props.Env
        Session = props.Session
        Target = Composer.Quoting props.CommitId
        Participants = ValueNone
        OnCommitted =
          fun commit -> Navigation.go(props.Nav, "/commit/" + commit.Id, ignore)
        OnScheduled = ValueNone
        OnOpenQueue = ValueNone
        Draft = ValueNone
        OnBodyReady = ValueNone
      }

    StackPanel()
      .Children(
        replyHost.IsVisible(
          model |> Observable.map(fun current -> current.Replying)
        ),
        quote.IsVisible(model |> Observable.map(fun current -> current.Quoting))
      )

  // One stargazer row: the avatar, the name, and the handle, opening
  // the person's profile and closing the list on a click.
  let stargazerRow
    (props: Props<'env>, dispatch: Dispatch<State.Msg>, person: User)
    : Control =
    let columns = Grid()

    columns.ColumnDefinitions <- ColumnDefinitions("Auto,*")

    columns.Children(
      Avatar.view {
        Name = person.Name
        Url = person.AvatarUrl
        Lane = person.Lane
        Size = 36.0
        Cache = props.Env.ImageCache
        Square = false
        Viewable = false
      }
      |> _.Column(0)
      |> _.Margin(0.0, 0.0, 12.0, 0.0),
      StackPanel()
        .VerticalAlignment(VerticalAlignment.Center)
        .Spacing(2.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Text(person.Name)
            .TextTrimming(TextTrimming.CharacterEllipsis),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text("@" + person.Handle)
            .TextTrimming(TextTrimming.CharacterEllipsis)
        )
        .Column(1)
    )
    |> ignore

    Button()
      .Name("StargazerRow")
      .Classes(Theme.name Theme.CommitRow)
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .Content(columns)
      .OnClickHandler(fun _ _ ->
        dispatch State.StarsClosed
        Navigation.go(props.Nav, "/profile/" + person.Handle, ignore))

  // The "Starred by" list: the commit's star total over the people
  // who starred it, newest first, with the load-more word while a
  // cursor remains. A failed read speaks in the app's error words.
  let stargazersDialog
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let countLine =
      model
      |> Observable.map(fun current ->
        current.StarsOpen
        |> ValueOption.toOption
        |> Option.bind _.StarsCount
        |> Option.defaultValue 0)

    let rows =
      let people =
        model
        |> Observable.map(fun current -> current.Stars)
        |> Observable.distinctUntilChanged
        |> Observable.map(fun people ->
          StackPanel().Spacing(2.0).Children [|
            for person in people -> stargazerRow(props, dispatch, person)
          |]
          : Control)

      ContentControl().Name("StargazersRows").Content(people.ToBinding())

    let more =
      Button()
        .Name("StarsMore")
        .Classes(Theme.name Theme.GhostButton)
        .Content("Show more")
        .HorizontalAlignment(HorizontalAlignment.Left)
        .IsVisible(
          model
          |> Observable.map(fun current ->
            current.StarsCursor.IsSome
            && not current.StarsBusy
            && current.StarsError.IsNone)
        )
        .OnClickHandler(fun _ _ -> dispatch State.StarsMore)

    let loading =
      DevNull.Client.Components.PageChrome
        .marquee(model |> Observable.map _.StarsBusy, Thickness())
        .Name("StarsLoading")

    let failure =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8.0)
        .Children(
          TextBlock()
            .Name("StarsError")
            .Classes(Theme.name Theme.FgDanger)
            .TextWrapping(TextWrapping.Wrap)
            .Text(
              model
              |> Observable.map(fun current ->
                current.StarsError
                |> Option.map describe
                |> Option.defaultValue "")
            ),
          Button()
            .Name("StarsRetry")
            .Classes(Theme.name Theme.GhostButton)
            .Content("Try again")
            .OnClickHandler(fun _ _ -> dispatch State.StarsMore)
        )
        .IsVisible(model |> Observable.map _.StarsError.IsSome)

    Border()
      .Name("StargazersCard")
      .Classes(Theme.name Theme.Card)
      .MaxWidth(420.0)
      .Child(
        StackPanel()
          .Spacing(12.0)
          .Children(
            TextBlock()
              .Name("StarsTitle")
              .Classes(Theme.name Theme.Title)
              .Text("Starred by"),
            TextBlock()
              .Name("StarsCountLine")
              .Classes(Theme.name Theme.Muted)
              .Text(
                countLine
                |> Observable.map(fun count ->
                  let word = if count = 1 then "person" else "people"

                  $"{shortCount count} {word} starred this commit.")
              ),
            ScrollViewer()
              .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
              .MaxHeight(320.0)
              .Content(rows),
            loading,
            failure,
            more,
            Button()
              .Name("StarsClose")
              .Classes(Theme.name Theme.GhostButton)
              .Content("Close")
              .HorizontalAlignment(HorizontalAlignment.Right)
              .OnClickHandler(fun _ _ -> dispatch State.StarsClosed)
          )
      )

  // The related-commits section under the thread: the heading over
  // the commits closest in meaning to the focused one, drawn as feed
  // cards. Nothing close — or a read that failed — renders nothing at
  // all, the way the site's section stays absent.
  let related(ctx: Ctx, model: IObservable<State.Model>) : Control =
    let header =
      Border()
        .Classes(Theme.name Theme.Hairline)
        .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
        .Child(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Margin(0.0, 12.0, 0.0, 8.0)
            .Text("Related commits")
        )

    let rows =
      ItemsControl()
        .ItemTemplate(
          FuncDataTemplate<CommitNode>(
            (fun node _ -> CommitCard.feedRow ctx.Rows node),
            false
          )
        )
        .BindItemsSource(
          model |> Observable.map(fun current -> current.Related),
          FeedParts.nodeKey
        )

    Border()
      .Name("RelatedCommits")
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
      .Margin(0.0, 16.0, 0.0, 0.0)
      .Child(StackPanel().Children(header, rows))
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Page.IsSome && not current.Related.IsEmpty)
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let rows =
    CommitRows.row(
      {
        OnEvent = State.CardEvent >> dispatch
        OnOpenExternal = fun url -> props.Env.Browser.Open(url) |> ignore
        ViewerId = props.Session.Session.User.Id
        Images = props.Env.ImageCache
        Flyout = Parts.flyoutOf props
        Zone = zoneOf((props.Env :> IProvideUserSettings).UserSettings.Current)
        OnPin = ValueSome(State.PinToggled >> dispatch)
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
        OnStars = ValueSome(fun commit -> dispatch(State.StarsOpened commit))
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

  let ctx: Parts.Ctx = {
    Rows = rows
    Model = model
    Dispatch = dispatch
  }

  let content =
    StackPanel()
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Children(
        Parts.backLine props,
        PageChrome.errorPanel(model |> Observable.map _.Error),
        Parts.noteLine model,
        Parts.busy model,
        Parts.ancestors(props, model),
        Parts.cardHost ctx,
        // The replies tie into the card's rail directly; the page-level
        // affordances follow the chain, the way the site's thread does.
        Parts.replies ctx,
        Parts.moreReplies(dispatch, model),
        Parts.answerComposer(props, dispatch, model),
        Parts.related(ctx, model)
      )

  /// The share dialog's overlay layer: it opens from the model's open
  /// share and samples the class-owned subject on each open edge, the
  /// one synchronous read the content factory needs.
  let shareLayer
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    DialogChrome.layer {
      Open =
        model
        |> Observable.map(_.ShareOpen)
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

  let reportLayer
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
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

  let stargazersLayer
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    DialogChrome.layer {
      Open =
        model
        |> Observable.map(_.StarsOpen)
        |> Observable.map ValueOption.isSome
      Content =
        fun () ->
          let isOpen =
            model
            |> Observable.first
            |> Observable.toEnumerable
            |> Seq.tryHead
            |> function
              | Some state -> state.StarsOpen
              | None -> ValueNone

          match isOpen with
          | ValueSome _ -> Parts.stargazersDialog(props, model, dispatch)
          | ValueNone -> Panel()
      Scrim = true
      OnScrimClick = ValueSome(fun () -> dispatch State.StarsClosed)
      Place = DialogChrome.Center
    }

  // The page scrolls one axis: a wrapped body and composer measure
  // against the column, never an unbounded extent. The rows run edge
  // to edge of the reading column, the width the feed hands its own
  // cards — a horizontal inset here narrows every card below it.
  let page =
    ScrollViewer()
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
      .Margin(0.0, Theme.bandHeight, 0.0, 0.0)
      .Content(content)


  Grid()
    .Children(
      page,
      shareLayer(model, dispatch),
      reportLayer(model, dispatch),
      stargazersLayer(model, dispatch)
    )

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
  and 'env :> IProvideProfiles
  and 'env :> IProvideSshKeys
  and 'env :> IProvideTimeline
  and 'env :> IProvideRemotes
  and 'env :> IProvideUserSettings
  and 'env :> IProvideTranslations
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Programs.runPage(props.Env.Dispatcher, program)

    this.Content <- root
    props.Context.AddDisposable lifetime
