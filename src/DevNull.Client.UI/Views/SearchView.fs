module DevNull.Client.Views.SearchView

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open Microsoft.Extensions.Logging
open Navs
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Formatting
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Search
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
  SignedIn: SignedInSession
  Term: string
  Recents: RecentSearchStore
  Searches: Search.Session
  Nav: INavigable
  Context: RouteContext
}

// The page's state logic.
module State =

  type Model = {
    // The box's text as typed; the results answer the term last
    // submitted, not the draft.
    Term: string
    Answer: CommitsFound voption
    Profiles: ProfilesFound voption
    Now: DateTimeOffset
    Recents: string list
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
    | SearchedFor of string
    | Cleared
    | Picked of string
    | ProfilesArrived of ProfilesFound
    | CommitsArrived of CommitsFound
    | CardEvent of CommitCard.Event
    | StarToggled of Commit
    | StarDone of Result<Commit, AppError>
    | ClockTicked of moment: DateTimeOffset
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

  let initial (props: Props<'env>) () : Model * Cmd<Msg> =
    let term = props.Term.Trim()

    let model = {
      Term = props.Term
      Answer = ValueNone
      Profiles = ValueNone
      Now = DateTimeOffset.UtcNow
      Recents = props.Recents.Recent(props.SignedIn.Session.User.Id)
      Automatic = props.SignedIn.Session.AutomaticTranslation
      Translations =
        TranslationModel.seed(
          (props.Env :> IProvideTranslations).Translations.Snapshot()
        )
      Relations = Map.empty
      MenuBusy = None
    }

    if term = "" then
      model, Cmd.none
    else
      model,
      Cmd.batch [
        Cmd.ofAction(fun () -> props.Searches.Set term)

        Cmd.ofAction(fun () ->
          props.Recents.Remember(props.SignedIn.Session.User.Id, term))
      ]

  // The answer that belongs to the term last submitted.
  let answerOf(model: Model) : CommitsFound =
    match SearchTerms.commitTerm model.Term, model.Answer with
    | ValueSome term, ValueSome found when found.Term = term -> found
    | _ -> {
        Term = ""
        Commits = []
        Throttled = false
      }

  // The profile answer that belongs to the term last submitted.
  let profilesOf(model: Model) : ProfilesFound =
    match SearchTerms.peopleTerm model.Term, model.Profiles with
    | ValueSome term, ValueSome found when found.Term = term -> found
    | _ -> {
        Term = ""
        Users = []
        Organizations = []
        Throttled = false
      }

  // True while the submitted term still owes its answer.
  let searching(model: Model) : bool =
    SearchTerms.commitTerm model.Term |> ValueOption.isSome
    && (answerOf model).Term = ""

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

  // The answer after one commit id folds to its deletion marker.
  let foldedAnswer(model: Model, commitId: string) : CommitsFound voption =
    model.Answer
    |> ValueOption.map(fun found -> {
      found with
          Commits =
            found.Commits
            |> List.map(function
              | LiveCommit commit when commit.Id = commitId ->
                Tombstone(tombstoneOf commit)
              | node -> node)
    })

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
            KeyFile = props.SignedIn.KeyFile
            CommitId = commit.Id
            Language = language
          }
        )
    )

  let loggerOf(props: Props<'env>) =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Search"

  let updateCore (log: ILogger, props: Props<'env>) msg (model: Model) =
    let failure = CommitActions.failure log

    let failed what error =
      Cmd.ofAction(fun () -> failure what error)

    match msg with
    | SearchedFor term ->
      let trimmed = term.Trim()

      { model with Term = trimmed },
      Cmd.batch [
        Cmd.ofAction(fun () -> props.Searches.Set trimmed)

        Cmd.ofAction(fun () ->
          props.Recents.Remember(props.SignedIn.Session.User.Id, trimmed)

          log.LogDebug("The commit search for {Term} was sent.", trimmed))
      ]
    | Cleared ->
      {
        model with
            Term = ""
            Answer = ValueNone
            Profiles = ValueNone
            Recents = props.Recents.Recent(props.SignedIn.Session.User.Id)
      },
      Cmd.none
    | Picked term -> model, Cmd.ofMsg(SearchedFor term)
    | ProfilesArrived found ->
      {
        model with
            Profiles = ValueSome found
      },
      Cmd.none
    | CommitsArrived found -> { model with Answer = ValueSome found }, Cmd.none
    | CardEvent(CommitCard.Opened commit) ->
      model,
      Cmd.ofEffect(fun _ ->
        Navigation.go(props.Nav, "/commit/" + commit.Id, ignore))
    | StarToggled commit ->
      model,
      Cmd.OfAsync.perform
        CommitActions.toggleStar
        (props.Env, props.SignedIn, commit)
        StarDone
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
    | CardEvent _ -> model, Cmd.none
    | StarDone result ->
      match result with
      | Ok updated ->
        {
          model with
              Answer =
                model.Answer
                |> ValueOption.map(fun found -> {
                  found with
                      Commits = replaceNode(found.Commits, updated)
                })
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The star on {Commit} settled.", updated.Id))
      | Error error -> model, failed "the star" error
    | ClockTicked moment -> { model with Now = moment }, Cmd.none
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
        commit.Author.Id = props.SignedIn.Session.User.Id
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
           Session = props.SignedIn
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
           Session = props.SignedIn
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
           Session = props.SignedIn
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
        { model with MenuBusy = None }, failed "the block toggle" error
    | DeleteRequested commit ->
      { model with MenuBusy = Some commit.Id },
      Cmd.OfAsync.perform
        Commits.delete
        (props.Env,
         {
           KeyFile = props.SignedIn.KeyFile
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
              Answer = foldedAnswer(model, commitId)
              MenuBusy = None
        },
        Cmd.batch [
          Cmd.ofAction(fun () ->
            (props.Env :> IProvideTimeline).Timeline.AnnounceDeletion commitId)

          Cmd.ofAction(fun () ->
            log.LogInformation("The commit {Commit} was deleted.", commitId))
        ]
      | Error error ->
        { model with MenuBusy = None }, failed "the commit deletion" error

  let update(props: Props<'env>) = updateCore(loggerOf props, props)

  // The page's inlets: its session's two channels and the clock.
  let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
    [
      Sub.fromObservable
        "profiles"
        (props.Searches.Profiles |> Observable.map ProfilesArrived)

      Sub.fromObservable
        "commits"
        (props.Searches.Commits |> Observable.map CommitsArrived)

      Sub.fromObservable
        "clock"
        (props.Env.UiClock.Minute |> Observable.map ClockTicked)
    ]
    |> Sub.batch

// The page's semantic regions.
module Parts =

  // The header's box: the loupe, the box, and the clear button the
  // draft fills.
  let inputRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let box =
      TextBox()
        .Name("SearchBox")
        .MaxLength(100)
        .PlaceholderText("Search commits by meaning")
        .Background(Brushes.Transparent)
        .BorderThickness(Thickness(0.0))
        .VerticalAlignment(VerticalAlignment.Center)
        .FontSize(15.0)
        .Text(
          model |> Observable.map(fun current -> current.Term) |> _.ToBinding()
        )

    box.KeyDown.Add(fun args ->
      if args.Key = Key.Enter then
        args.Handled <- true

        if not(String.IsNullOrWhiteSpace box.Text) then
          dispatch(State.SearchedFor(nonNull box.Text)))

    let clear =
      Button()
        .Name("SearchClear")
        .Classes(Theme.name Theme.GhostButton)
        .Padding(4.0)
        .Content(Icons.iconAt(Glyph.Close, 16.0))
        .OnClickHandler(fun _ _ -> dispatch State.Cleared)
        .IsVisible(model |> Observable.map(fun current -> current.Term <> ""))

    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .Height(40.0)
      .Margin(16.0, 0.0, 16.0, 8.0)
      .Child(
        DockPanel()
          .LastChildFill(true)
          .Children(
            Icons.iconAt(Glyph.Search, 16.0).Margin(10.0, 0.0, 8.0, 0.0),
            clear.DockRight().Margin(4.0),
            box
          )
      )

  // One quiet row the empty state offers: a remembered search or a
  // suggestion, bordered, picked by a click.
  let termRow(dispatch: Dispatch<State.Msg>, term: string) : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton, Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .Padding(12.0, 8.0, 12.0, 8.0)
      .Margin(16.0, 0.0, 16.0, 8.0)
      .Content(SearchParts.termIdentity term)
      .OnClickHandler(fun _ _ -> dispatch(State.Picked term))

  // One section of quiet rows under its heading.
  let quietSection
    (dispatch: Dispatch<State.Msg>, headingText: string, terms: string list)
    : Control =
    StackPanel()
      .Margin(0.0, 8.0, 0.0, 8.0)
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.Subtitle)
          .Margin(20.0, 8.0, 20.0, 8.0)
          .Text(headingText),
        StackPanel()
          .Children([| for term in terms -> termRow(dispatch, term) |])
      )

  // The results header: the section's name and the match count.
  let countHeader(count: int) : Control =
    let word = if count = 1 then "match" else "matches"

    let grid = Grid()
    grid.ColumnDefinitions <- ColumnDefinitions("*,Auto")

    grid.Children(
      TextBlock()
        .Classes(Theme.name Theme.Subtitle)
        .Margin(20.0, 12.0, 20.0, 8.0)
        .Text("Most relevant commits")
        .Column(0),
      TextBlock()
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
        .FontSize(12.0)
        .Margin(0.0, 12.0, 20.0, 8.0)
        .Text($"{count} {word}")
        .Column(1)
    )
    |> ignore

    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Child(grid)

  // The placeholder cards that hold the results' place while the
  // answer is out.
  let placeholders() : Control =
    let cards: Control array = [|
      for _ in 1..3 ->
        Border()
          .Classes(Theme.name Theme.SurfaceBack)
          .CornerRadius(8.0)
          .Height(96.0)
          .Margin(16.0, 8.0, 16.0, 8.0)
          .Opacity(0.6)
    |]

    StackPanel().Children(cards) :> _

  // One quiet row a profile section lists, opening its page on a
  // click.
  let profileRow(nav: INavigable, url: string, content: Control) : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton, Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .Padding(12.0, 8.0, 12.0, 8.0)
      .Margin(16.0, 0.0, 16.0, 8.0)
      .Content(content)
      .OnClickHandler(fun _ _ -> Navigation.go(nav, url, ignore))

  // One profile section: its rows under the section heading the page
  // already wears above its commit results.
  let profileSection(headingText: string, rows: Control array) : Control =
    StackPanel()
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Margin(0.0, 8.0, 0.0, 8.0)
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.Subtitle)
          .Margin(20.0, 8.0, 20.0, 8.0)
          .Text(headingText),
        StackPanel()
          .HorizontalAlignment(HorizontalAlignment.Stretch)
          .Children(rows)
      )

  // The fatal line and its hint, the site's own words.
  let fatalOf(term: string) : Control =
    StackPanel()
      .Margin(0.0, 48.0, 0.0, 48.0)
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .TextAlignment(TextAlignment.Center)
          .Text($"fatal: nothing close to '{term}'"),
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .TextAlignment(TextAlignment.Center)
          .TextWrapping(TextWrapping.Wrap)
          .Margin(24.0, 8.0, 24.0, 0.0)
          .Text(
            "Try describing the idea in other words, or pick one of the searches below."
          )
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let ctx =
    CommitRows.row(
      {
        OnEvent = State.CardEvent >> dispatch
        ViewerId = props.SignedIn.Session.User.Id
        Images = props.Env.ImageCache
        Flyout =
          ValueSome {
            Fetch = props.Env.Profiles.Fetch
            OnOpen =
              fun username ->
                Navigation.go(
                  props.Nav,
                  "/profile/" + Uri.EscapeDataString username,
                  ignore
                )
            OnOpenOrg =
              fun handle ->
                Navigation.go(
                  props.Nav,
                  "/org/" + Uri.EscapeDataString handle,
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
                  |> Map.tryFind commit.Author.Handle
                  |> ValueOption.ofOption)
            Busy = model |> Observable.map _.MenuBusy
          }
        Actions = false
        OnStar = fun commit -> dispatch(State.StarToggled commit)
        OnVote = ignore
        OnPastel = ignore
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
        Pasteis = false
        Now = current.Now
        PollChoices = Map.empty
        Voting = None
        Pinning = None
      })
    )

  let rowCtx: SearchParts.RowCtx = {
    Images = props.Env.ImageCache
    Flyout = ValueNone
    Now = DateTimeOffset.UtcNow
  }

  let bodyKey(current: State.Model) =
    let answer = State.answerOf current
    let found = State.profilesOf current

    current.Term,
    current.Recents,
    answer.Term,
    answer.Commits,
    found.Term,
    found.Users,
    found.Organizations

  let body =
    PageChrome.rebuildOn(
      model,
      bodyKey,
      fun current ->
        let term = current.Term.Trim()

        let suggestions =
          SearchParts.suggestions |> List.filter(fun words -> words <> term)

        let quietChildren: Control array = [|
          if not current.Recents.IsEmpty then
            Parts.quietSection(dispatch, "Recent searches", current.Recents)

          Parts.quietSection(dispatch, "Try searching for…", suggestions)
        |]

        let quietRows = StackPanel().Children(quietChildren)

        if term = "" then
          quietRows
        else
          let answer = State.answerOf current

          let found = State.profilesOf current

          let developers = [|
            for user in found.Users ->
              Parts.profileRow(
                props.Nav,
                "/profile/" + Uri.EscapeDataString user.Handle,
                SearchParts.peopleIdentity(rowCtx, user)
              )
          |]

          let organizations = [|
            for organization in found.Organizations ->
              Parts.profileRow(
                props.Nav,
                "/org/" + Uri.EscapeDataString organization.Handle,
                SearchParts.orgIdentity(rowCtx, organization)
              )
          |]

          let profileRows =
            StackPanel()
              .HorizontalAlignment(HorizontalAlignment.Stretch)
              .Children(
                [|
                  if developers.Length > 0 then
                    Parts.profileSection("Developers", developers)

                  if organizations.Length > 0 then
                    Parts.profileSection("Organizations", organizations)
                |]
              )

          let results =
            if State.searching current then
              Parts.placeholders()
            elif answer.Commits.IsEmpty then
              Parts.fatalOf term
            else
              let rows =
                ItemsControl()
                  .ItemTemplate(
                    FuncDataTemplate<CommitNode>(
                      (fun node _ -> CommitCard.feedRow ctx node),
                      false
                    )
                  )
                  .BindItemsSource(
                    model
                    |> Observable.map(fun now ->
                      answer.Commits
                      :> System.Collections.Generic.IReadOnlyList<CommitNode>),
                    FeedParts.nodeKey
                  )

              StackPanel()
                .Children(Parts.countHeader(answer.Commits.Length), rows)

          StackPanel().Children(profileRows, results, quietRows)
    )

  PageChrome.page {
    Model = model
    TitleOf = fun _ -> ""
    Padding = Thickness(0.0, Theme.bandHeight + 8.0, 0.0, 0.0)
    ErrorOf = fun _ -> None
    Busy = fun _ -> PageChrome.Off
    Empty = fun _ -> PageChrome.Off
    Header = [ Parts.inputRow(model, dispatch) ]
    Body = ScrollViewer().Content(body |> _.ToBinding())
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
      Program.mkProgram (State.initial props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Programs.runPage(props.Env.Dispatcher, program)

    this.Content <- root
    props.Context.AddDisposable lifetime
