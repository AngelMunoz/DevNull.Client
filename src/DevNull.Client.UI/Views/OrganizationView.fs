module DevNull.Client.Views.OrganizationView

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
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
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging> = {
  Env: 'env
  Handle: string
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

module State =

  // The feed tab the page shows: the commits that mention the
  // organization, or the organization's own commits.
  type OrgTab =
    | Mentions
    | Commits

  type Model = {
    ProfileLoading: bool
    MentionsLoading: bool
    CommitsLoading: bool
    Error: AppError option
    Details: OrganizationProfile option
    MentionsItems: CommitNode list
    MentionsNextCursor: string option
    CommitsItems: CommitNode list
    CommitsNextCursor: string option
    Tab: OrgTab
    Now: DateTimeOffset
    Pasteis: bool
    Voting: string option
    PollChoices: Map<string, string>
    Automatic: bool
    Translations: TranslationModel.Book
    // The viewer's relation per author handle, as the More menu reads
    // it.
    Relations: Map<string, AuthorRelation>
    // The commit whose menu action is in flight, so its items disable.
    MenuBusy: string option
    HeaderBusy: bool
  }

  type Msg =
    | Fetched of Result<OrganizationProfile, AppError>
    | MentionsFetched of Result<Page<CommitNode>, AppError>
    | MentionsAppended of Result<Page<CommitNode>, AppError>
    | CommitsFetched of Result<Page<CommitNode>, AppError>
    | CommitsAppended of Result<Page<CommitNode>, AppError>
    | TabChosen of OrgTab
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
    | FollowToggled
    | FollowDone of Result<unit, AppError>
    | ClaimRequested
    | ClaimDone of Result<unit, AppError>

  let initial = {
    ProfileLoading = true
    MentionsLoading = true
    CommitsLoading = false
    Error = None
    Details = None
    MentionsItems = []
    MentionsNextCursor = None
    CommitsItems = []
    CommitsNextCursor = None
    Tab = Mentions
    Now = DateTimeOffset.UtcNow
    Pasteis = false
    Voting = None
    PollChoices = Map.empty
    Automatic = false
    Translations = TranslationModel.initial
    Relations = Map.empty
    MenuBusy = None
    HeaderBusy = false
  }

  [<Struct>]
  type MentionsQuery = {
    Session: SignedInSession
    Handle: string
    Cursor: string option
  }

  let fetchMentions(env, query: MentionsQuery) =
    Organizations.mentions(
      env,
      {
        KeyFile = query.Session.KeyFile
        Handle = query.Handle
        Cursor = query.Cursor
      }
    )

  let fetchCommits(env, query: MentionsQuery) =
    Organizations.commits(
      env,
      {
        KeyFile = query.Session.KeyFile
        Handle = query.Handle
        Cursor = query.Cursor
      }
    )

  let inline itemsOf(tab: OrgTab, model: Model) =
    match tab with
    | Mentions -> model.MentionsItems
    | Commits -> model.CommitsItems

  let inline nextCursorOf(tab: OrgTab, model: Model) =
    match tab with
    | Mentions -> model.MentionsNextCursor
    | Commits -> model.CommitsNextCursor

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
        Organizations.get
        (props.Env,
         {
           KeyFile = props.Session.KeyFile
           Handle = props.Handle
         })
        Fetched

      Cmd.OfAsync.perform
        fetchMentions
        (props.Env,
         {
           Session = props.Session
           Handle = props.Handle
           Cursor = None
         })
        MentionsFetched
    ]

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
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Organization"

  let updateCore (log: ILogger, props: Props<'env>) msg model =
    let failure = CommitActions.failure log

    let failed what error =
      Cmd.ofAction(fun () -> failure what error)

    match msg with
    | Fetched result ->
      match result with
      | Ok profile ->
        {
          model with
              ProfileLoading = false
              HeaderBusy = false
              Details = Some profile
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The organization {Handle} arrived.", props.Handle))
      | Error error ->
        {
          model with
              ProfileLoading = false
              HeaderBusy = false
              Error = Some error
        },
        failed $"the organization {props.Handle}" error
    | MentionsFetched result ->
      match result with
      | Ok page ->
        {
          model with
              MentionsLoading = false
              MentionsItems = page.Items
              MentionsNextCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The mentions of {Handle} fetched {Count} commits.",
            props.Handle,
            page.Items.Length
          ))
      | Error error ->
        {
          model with
              MentionsLoading = false
              Error = Some error
        },
        failed $"the mentions of {props.Handle}" error
    | CommitsFetched result ->
      match result with
      | Ok page ->
        {
          model with
              CommitsLoading = false
              CommitsItems = page.Items
              CommitsNextCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The commits of {Handle} fetched {Count} commits.",
            props.Handle,
            page.Items.Length
          ))
      | Error error ->
        {
          model with
              CommitsLoading = false
              Error = Some error
        },
        failed $"the commits of {props.Handle}" error
    | TabChosen tab ->
      if tab = model.Tab then
        model, Cmd.none
      else
        let empty =
          match tab with
          | Mentions -> model.MentionsItems.IsEmpty
          | Commits -> model.CommitsItems.IsEmpty

        let chosen = { model with Tab = tab }

        if empty then
          match tab with
          | Mentions ->
            {
              chosen with
                  MentionsLoading = true
                  Error = None
            },
            Cmd.OfAsync.perform
              fetchMentions
              (props.Env,
               {
                 Session = props.Session
                 Handle = props.Handle
                 Cursor = None
               })
              MentionsFetched
          | Commits ->
            {
              chosen with
                  CommitsLoading = true
                  Error = None
            },
            Cmd.OfAsync.perform
              fetchCommits
              (props.Env,
               {
                 Session = props.Session
                 Handle = props.Handle
                 Cursor = None
               })
              CommitsFetched
        else
          chosen, Cmd.none
    | MoreRequested ->
      match model.Tab with
      | Mentions ->
        model,
        Cmd.OfAsync.perform
          fetchMentions
          (props.Env,
           {
             Session = props.Session
             Handle = props.Handle
             Cursor = model.MentionsNextCursor
           })
          MentionsAppended
      | Commits ->
        model,
        Cmd.OfAsync.perform
          fetchCommits
          (props.Env,
           {
             Session = props.Session
             Handle = props.Handle
             Cursor = model.CommitsNextCursor
           })
          CommitsAppended
    | MentionsAppended result ->
      match result with
      | Ok page ->
        {
          model with
              MentionsItems = model.MentionsItems @ page.Items
              MentionsNextCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The mentions of {Handle} grew by {Count} commits.",
            props.Handle,
            page.Items.Length
          ))
      | Error error ->
        { model with Error = Some error },
        failed $"the next mentions of {props.Handle}" error
    | CommitsAppended result ->
      match result with
      | Ok page ->
        {
          model with
              CommitsItems = model.CommitsItems @ page.Items
              CommitsNextCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The commits of {Handle} grew by {Count} commits.",
            props.Handle,
            page.Items.Length
          ))
      | Error error ->
        { model with Error = Some error },
        failed $"the next commits of {props.Handle}" error
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
              MentionsItems = replaceNode(model.MentionsItems, updated)
              CommitsItems = replaceNode(model.CommitsItems, updated)
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
              MentionsItems = replaceNode(model.MentionsItems, updated)
              CommitsItems = replaceNode(model.CommitsItems, updated)
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
              MentionsItems = replaceNode(model.MentionsItems, pastel.Commit)
              CommitsItems = replaceNode(model.CommitsItems, pastel.Commit)
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
    | FollowToggled ->
      let following =
        model.Details |> Option.map _.IsFollowing |> Option.defaultValue false

      let request: Organizations.Request = {
        KeyFile = props.Session.KeyFile
        Handle = props.Handle
      }

      {
        model with
            HeaderBusy = true
            Error = None
      },
      if following then
        Cmd.OfAsync.perform
          Organizations.unfollow
          (props.Env, request)
          FollowDone
      else
        Cmd.OfAsync.perform Organizations.follow (props.Env, request) FollowDone
    | FollowDone result ->
      match result with
      | Ok() ->
        model,
        Cmd.OfAsync.perform
          Organizations.get
          (props.Env,
           {
             KeyFile = props.Session.KeyFile
             Handle = props.Handle
           })
          Fetched
      | Error error ->
        {
          model with
              HeaderBusy = false
              Error = Some error
        },
        failed "the organization follow" error
    | ClaimRequested ->
      {
        model with
            HeaderBusy = true
            Error = None
      },
      Cmd.OfAsync.perform
        Organizations.claim
        (props.Env,
         {
           KeyFile = props.Session.KeyFile
           Handle = props.Handle
         })
        ClaimDone
    | ClaimDone result ->
      match result with
      | Ok() ->
        model,
        Cmd.OfAsync.perform
          Organizations.get
          (props.Env,
           {
             KeyFile = props.Session.KeyFile
             Handle = props.Handle
           })
          Fetched
      | Error error ->
        {
          model with
              HeaderBusy = false
              Error = Some error
        },
        failed "the organization claim" error
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
              MentionsItems =
                model.MentionsItems
                |> List.map(function
                  | LiveCommit commit when commit.Id = commitId ->
                    Tombstone(tombstoneOf commit)
                  | node -> node)
              CommitsItems =
                model.CommitsItems
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

module Parts =

  let identity
    (images: ImageCacheService, profile: OrganizationProfile)
    : Control =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(12.0)
      .Children(
        Avatar.view {
          Name = profile.Name
          Url = profile.AvatarUrl
          Lane = profile.Lane
          Size = 64.0
          Cache = images
          Square = false
          Viewable = false
        },
        StackPanel()
          .VerticalAlignment(VerticalAlignment.Center)
          .Spacing(4.0)
          .Children(
            TextBlock()
              .Classes(Theme.name Theme.Headline)
              .TextTrimming(TextTrimming.CharacterEllipsis)
              .Text(profile.Name),
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(8.0)
              .Children(ProfileCard.laneHandle(profile.Lane, profile.Handle))
          )
      )

  let linkButton
    (browser: IBrowser, log: ILogger, label: string, url: string)
    : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(8.0, 4.0, 8.0, 4.0)
      .Content(label)
      .OnClickHandler(fun _ args ->
        args.Handled <- true

        match browser.Open url with
        | Ok() -> ()
        | Error message ->
          log.LogWarning("The page did not open: {Error}", message))

  let withoutScheme(url: string) : string =
    url.Replace("https://", "").Replace("http://", "")

  let locationLine(location: string) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .TextWrapping(TextWrapping.Wrap)
      .Text(location)

  let bioLine(profile: OrganizationProfile) : Control =
    TextBlock()
      .TextWrapping(TextWrapping.Wrap)
      .IsVisible(profile.Bio.IsSome)
      .Text(profile.Bio |> Option.defaultValue "")

  let metaLine
    (browser: IBrowser, log: ILogger, profile: OrganizationProfile)
    : Control =
    let pieces: Control array = [|
      yield! profile.Location |> Option.map locationLine |> Option.toArray

      yield!
        profile.Website
        |> Option.map(fun url ->
          linkButton(browser, log, withoutScheme url, url))
        |> Option.toArray

      yield!
        profile.GithubUrl
        |> Option.map(fun url -> linkButton(browser, log, "GitHub", url))
        |> Option.toArray

      linkButton(browser, log, withoutScheme profile.HtmlUrl, profile.HtmlUrl)
    |]

    WrapPanel().ItemSpacing(8.0).Children(pieces)

  let meta
    (browser: IBrowser, log: ILogger, profile: OrganizationProfile)
    : Control =
    StackPanel()
      .Spacing(8.0)
      .Children(bioLine profile, metaLine(browser, log, profile))

  let headerHost
    (images: ImageCacheService, model: IObservable<State.Model>)
    : Control =
    ContentControl()
      .Content(
        model
        |> Observable.map _.Details
        |> Observable.distinctUntilChanged
        |> Observable.map(function
          | Some profile -> identity(images, profile)
          | None -> Panel(): Control)
        |> _.ToBinding()
      )

  let metaHost
    (browser: IBrowser, log: ILogger, model: IObservable<State.Model>)
    : Control =
    ContentControl()
      .Margin(0.0, 12.0, 0.0, 0.0)
      .Content(
        model
        |> Observable.map _.Details
        |> Observable.distinctUntilChanged
        |> Observable.map(function
          | Some profile -> meta(browser, log, profile)
          | None -> Panel(): Control)
        |> _.ToBinding()
      )

  let followButton
    (
      profile: OrganizationProfile,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    Button()
      .Name("FollowOrganization:" + profile.Handle)
      .Classes(Theme.name Theme.NavButton)
      .Content(if profile.IsFollowing then "Unfollow" else "Follow")
      .IsEnabled(model |> Observable.map(fun current -> not current.HeaderBusy))
      .OnClickHandler(fun _ _ -> dispatch State.FollowToggled)

  let claimButton
    (
      profile: OrganizationProfile,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    Button()
      .Name("ClaimOrganization:" + profile.Handle)
      .Classes(Theme.name Theme.NavButton)
      .Content("Claim this organization")
      .IsEnabled(model |> Observable.map(fun current -> not current.HeaderBusy))
      .OnClickHandler(fun _ _ -> dispatch State.ClaimRequested)

  let actions
    (
      profile: OrganizationProfile,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let children: Control array = [|
      followButton(profile, model, dispatch)

      if profile.CanClaim then
        claimButton(profile, model, dispatch)

      yield!
        profile.FollowersCount
        |> Option.map(fun count ->
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(4.0)
            .VerticalAlignment(VerticalAlignment.Center)
            .Children(
              TextBlock().Classes(Theme.name Theme.StatNum).Text(string count),
              TextBlock().Classes(Theme.name Theme.Muted).Text("followers")
            )
          : Control)
        |> Option.toArray
    |]

    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(12.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Children(children)

  let actionsHost
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    ContentControl()
      .Margin(12.0, 0.0, 0.0, 0.0)
      .VerticalAlignment(VerticalAlignment.Top)
      .Content(
        model
        |> Observable.map _.Details
        |> Observable.distinctUntilChanged
        |> Observable.map(function
          | Some profile -> actions(profile, model, dispatch)
          | None -> Panel(): Control)
        |> _.ToBinding()
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let log =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Organization"

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

  let endSentence =
    model
    |> Observable.map(fun current ->
      let items = State.itemsOf(current.Tab, current)

      let ended =
        not current.ProfileLoading
        && not current.MentionsLoading
        && not current.CommitsLoading
        && current.Error.IsNone
        && not items.IsEmpty
        && (State.nextCursorOf(current.Tab, current)).IsNone

      if ended then
        match current.Tab with
        | State.Mentions ->
          Some $"That's the first commit mentioning @{props.Handle}."
        | State.Commits -> Some "That's the oldest commit."
      else
        None)

  ProfileShell.view {
    Model = model
    Title =
      fun current ->
        current.Details |> Option.map _.Name |> Option.defaultValue ""
    Kind = Some "organization"
    Padding = Theme.pinnedPadding
    ErrorOf = _.Error
    Busy =
      fun current ->
        let loading =
          current.ProfileLoading
          || current.MentionsLoading
          || current.CommitsLoading

        if loading then
          PageChrome.Words "Loading the organization…"
        else
          PageChrome.Off
    Empty =
      fun current ->
        let items = State.itemsOf(current.Tab, current)

        let loading =
          match current.Tab with
          | State.Mentions -> current.MentionsLoading
          | State.Commits -> current.CommitsLoading

        if
          not current.ProfileLoading
          && not loading
          && current.Error.IsNone
          && items.IsEmpty
        then
          PageChrome.Words(
            match current.Tab with
            | State.Mentions ->
              $"Nobody has mentioned @{props.Handle} yet. Mention it in a commit and it shows up here."
            | State.Commits -> "No commits yet."
          )
        else
          PageChrome.Off
    Note = fun _ -> None
    Header =
      fun () ->
        DockPanel()
          .Children(
            Parts.actionsHost(model, dispatch).DockRight(),
            Parts.headerHost(props.Env.ImageCache, model)
          )
    Meta = ValueSome(fun () -> Parts.metaHost(props.Env.Browser, log, model))
    FeedLabel = Some "Organization feed"
    Tabs = [
      {
        Label = "Mentions"
        Active =
          model |> Observable.map(fun current -> current.Tab = State.Mentions)
        Badge = ValueNone
        Choose = fun () -> dispatch(State.TabChosen State.Mentions)
      }
      {
        Label = "Commits"
        Active =
          model |> Observable.map(fun current -> current.Tab = State.Commits)
        Badge = ValueNone
        Choose = fun () -> dispatch(State.TabChosen State.Commits)
      }
    ]
    Feed = {
      Items =
        model
        |> Observable.map(fun current -> State.itemsOf(current.Tab, current))
      KeyOf = FeedParts.nodeKey
      Row = fun node -> ProfileShell.commitRow(ctx, node)
      HasMore =
        model
        |> Observable.map(fun current ->
          (State.nextCursorOf(current.Tab, current)).IsSome)
      OnMore = fun () -> dispatch State.MoreRequested
      OnRide = ValueNone
      End = endSentence
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
