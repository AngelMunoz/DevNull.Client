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
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.Errors
open DevNull.Client.Env
open Microsoft.Extensions.Logging
open DevNull.Client.ImageCache
open DevNull.Client.Formatting
open DevNull.Client.Profiles
open DevNull.Client.Stores
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
  and 'env :> IProvideUserSettings
  and 'env :> IProvideTranslations
  and 'env :> IProvideLogging> = {
  Env: 'env
  Handle: string
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

module State =

  type Model = {
    ProfileLoading: bool
    MentionsLoading: bool
    Error: AppError option
    Details: OrganizationProfile option
    Items: CommitNode list
    NextCursor: string option
    Now: DateTimeOffset
    Pasteis: bool
    Voting: string option
    PollChoices: Map<string, string>
    Automatic: bool
    Translations: TranslationModel.Book
  }

  type Msg =
    | Fetched of Result<OrganizationProfile, AppError>
    | MentionsFetched of Result<Page<CommitNode>, AppError>
    | MentionsAppended of Result<Page<CommitNode>, AppError>
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

  let initial = {
    ProfileLoading = true
    MentionsLoading = true
    Error = None
    Details = None
    Items = []
    NextCursor = None
    Now = DateTimeOffset.UtcNow
    Pasteis = false
    Voting = None
    PollChoices = Map.empty
    Automatic = false
    Translations = TranslationModel.initial
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
              Details = Some profile
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The organization {Handle} arrived.", props.Handle))
      | Error error ->
        {
          model with
              ProfileLoading = false
              Error = Some error
        },
        failed $"the organization {props.Handle}" error
    | MentionsFetched result ->
      match result with
      | Ok page ->
        {
          model with
              MentionsLoading = false
              Items = page.Items
              NextCursor = page.NextCursor
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
    | MoreRequested ->
      model,
      Cmd.OfAsync.perform
        fetchMentions
        (props.Env,
         {
           Session = props.Session
           Handle = props.Handle
           Cursor = model.NextCursor
         })
        MentionsAppended
    | MentionsAppended result ->
      match result with
      | Ok page ->
        {
          model with
              Items = model.Items @ page.Items
              NextCursor = page.NextCursor
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

  let badge() : Control =
    Border()
      .Classes(Theme.name Theme.Chip)
      .CornerRadius(999.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Child(
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .FontSize(11.0)
          .Text("Organization")
      )

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
          Lane = None
          Size = 64.0
          Cache = images
        },
        StackPanel()
          .VerticalAlignment(VerticalAlignment.Center)
          .Spacing(4.0)
          .Children(
            TextBlock().Classes(Theme.name Theme.Headline).Text(profile.Name),
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(8.0)
              .Children(
                TextBlock()
                  .Classes(Theme.name Theme.Mono)
                  .Text("@" + profile.Handle),
                badge()
              )
          )
      )

  let linkButton(log: ILogger, label: string, url: string) : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(8.0, 4.0, 8.0, 4.0)
      .Content(label)
      .OnClickHandler(fun _ args ->
        args.Handled <- true

        match startBrowser url with
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

  let metaLine(log: ILogger, profile: OrganizationProfile) : Control =
    let pieces: Control array = [|
      yield! profile.Location |> Option.map locationLine |> Option.toArray

      yield!
        profile.Website
        |> Option.map(fun url -> linkButton(log, withoutScheme url, url))
        |> Option.toArray

      yield!
        profile.GithubUrl
        |> Option.map(fun url -> linkButton(log, "GitHub", url))
        |> Option.toArray

      linkButton(log, withoutScheme profile.HtmlUrl, profile.HtmlUrl)
    |]

    WrapPanel().ItemSpacing(8.0).Children(pieces)

  let meta(log: ILogger, profile: OrganizationProfile) : Control =
    StackPanel().Spacing(8.0).Children(bioLine profile, metaLine(log, profile))

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

  let metaHost(log: ILogger, model: IObservable<State.Model>) : Control =
    ContentControl()
      .Margin(0.0, 12.0, 0.0, 0.0)
      .Content(
        model
        |> Observable.map _.Details
        |> Observable.distinctUntilChanged
        |> Observable.map(function
          | Some profile -> meta(log, profile)
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
          | CommitCard.ShareRequested _ -> ()
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
        OnPin = ValueNone
        OnShare = ValueNone
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
        Pinning = None
      })
    )

  let endSentence =
    model
    |> Observable.map(fun current ->
      let ended =
        not current.ProfileLoading
        && not current.MentionsLoading
        && current.Error.IsNone
        && not current.Items.IsEmpty
        && current.NextCursor.IsNone

      if ended then
        Some $"That's the first commit mentioning @{props.Handle}."
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
        let loading = current.ProfileLoading || current.MentionsLoading

        if loading then
          PageChrome.Words "Loading the organization…"
        else
          PageChrome.Off
    Empty =
      fun current ->
        if
          not current.ProfileLoading
          && not current.MentionsLoading
          && current.Error.IsNone
          && current.Items.IsEmpty
        then
          PageChrome.Words
            $"Nobody has mentioned @{props.Handle} yet. Mention it in a commit and it shows up here."
        else
          PageChrome.Off
    Note = fun _ -> None
    Header = fun () -> Parts.headerHost(props.Env.ImageCache, model)
    Meta = ValueSome(fun () -> Parts.metaHost(log, model))
    FeedLabel = Some "Organization feed"
    Tabs = [
      {
        Label = "Mentions"
        Active = System.Reactive.Linq.Observable.Return true
        Badge = ValueNone
        Choose = ignore
      }
    ]
    Feed = {
      Items = model |> Observable.map _.Items
      KeyOf = FeedParts.nodeKey
      Row = fun node -> ProfileShell.commitRow(ctx, node)
      HasMore = model |> Observable.map _.NextCursor.IsSome
      OnMore = fun () -> dispatch State.MoreRequested
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
