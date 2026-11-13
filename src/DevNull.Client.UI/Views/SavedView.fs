module DevNull.Client.Views.SavedView

open System
open Avalonia.Controls
open NXUI.Extensions
open MVUish.Avalonia
open Navs
open DevNull.Client
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
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

// The page's state logic: model, messages, update, and the
// subscriptions that feed it from outside.
module State =

  type Model = {
    Loading: bool
    Error: AppError option
    Items: CommitNode list
    NextCursor: string option
    Now: DateTimeOffset
    Pasteis: bool
    Voting: string option
    PollChoices: Map<string, string>
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
    | Fetched of Result<Page<CommitNode>, AppError>
    | Appended of Result<Page<CommitNode>, AppError>
    | MoreRequested
    | StarToggled of Commit
    | StarDone of Result<Commit, AppError>
    | PollChoiceChanged of Commit * PollOption * chosen: bool
    | PollVoteRequested of Commit
    | PollVotedDone of Result<Commit, AppError>
    | PastelGiven of Commit
    | PastelDone of Result<PastelResult, AppError>
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

  let initial = {
    Loading = true
    Error = None
    Items = []
    NextCursor = None
    Now = DateTimeOffset.UtcNow
    Pasteis = false
    Voting = None
    PollChoices = Map.empty
    Automatic = false
    Translations = TranslationModel.initial
    Relations = Map.empty
    MenuBusy = None
  }

  let fetch(env, session: SignedInSession, cursor: string option) =
    Stars.index(
      env,
      {
        KeyFile = session.KeyFile
        Cursor = cursor
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
    Cmd.OfAsync.perform fetch (props.Env, props.Session, None) Fetched

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

  let loggerOf(props: Props<'env>) =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Saved"

  let updateCore (log: ILogger, props: Props<'env>) msg model =
    // Every log line is an effect: update returns it as a command,
    // and the loop writes it after the computation.
    let failure = CommitActions.failure log

    let failed what error =
      Cmd.ofAction(fun () -> failure what error)

    match msg with
    | Fetched result ->
      match result with
      | Ok page ->
        {
          model with
              Loading = false
              Error = None
              Items = page.Items
              NextCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The saved list fetched {Count} commits.",
            page.Items.Length
          ))
      | Error error ->
        {
          model with
              Loading = false
              Error = Some error
        },
        failed "the saved list fetch" error
    | MoreRequested ->
      model,
      Cmd.OfAsync.perform
        fetch
        (props.Env, props.Session, model.NextCursor)
        Appended
    | Appended result ->
      match result with
      | Ok page ->
        {
          model with
              Items = model.Items @ page.Items
              NextCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The saved list grew by {Count} commits.",
            page.Items.Length
          ))
      | Error error ->
        { model with Error = Some error },
        failed "the saved list next page" error
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
        Cmd.ofAction(fun () ->
          log.LogDebug("The star on {Commit} settled.", updated.Id))
      | Error error ->
        { model with Error = Some error }, failed "the star" error
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

  // The saved list itself: the virtualized feed over the rows.
  let feed
    (
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>,
      ctx: CommitCard.RowProps
    ) : Control =
    let hasMore = model |> Observable.map _.NextCursor.IsSome

    Feed.view {
      Items = model |> Observable.map _.Items
      KeyOf = FeedParts.nodeKey
      Row =
        fun node ->
          let card = CommitCard.feedRow ctx node

          match node with
          | LiveCommit commit ->
            StackPanel()
              .Children(card, CommitCard.replyBlock(ctx, commit, ValueNone))
            : Control
          | Tombstone _ -> card
      Footer =
        ValueSome(
          PageChrome.loadMore(hasMore, fun () -> dispatch State.MoreRequested)
        )
      HasMore = hasMore
      ScrollTo = ValueNone
      Ride = false
      Restore = ValueNone
      OnScroll = ValueNone
      OnRide = ValueNone
      OnNeedMore = ValueNone
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
        OnOpenExternal = fun url -> props.Env.Browser.Open(url) |> ignore
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
            OnOpenTag =
              fun tag ->
                Navigation.go(
                  props.Nav,
                  "/search/" + Uri.EscapeDataString tag,
                  ignore
                )
            Cache = props.Env.ImageCache
          }
        Zone = zoneOf((props.Env :> IProvideUserSettings).UserSettings.Current)
        OnPin = ValueNone
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

  PageChrome.page {
    Model = model
    TitleOf = fun _ -> "Saved"
    Padding = Theme.pinnedPadding
    ErrorOf = _.Error
    Busy =
      fun current ->
        if current.Loading then
          PageChrome.Words "Loading the saved list…"
        else
          PageChrome.Off
    Empty =
      fun current ->
        if
          not current.Loading && current.Error.IsNone && current.Items.IsEmpty
        then
          PageChrome.Words "Save commits to find them here."
        else
          PageChrome.Off
    Header = []
    Body = Parts.feed(dispatch, model, ctx)
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
