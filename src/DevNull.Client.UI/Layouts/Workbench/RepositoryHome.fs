module DevNull.Client.Workbench.RepositoryHome

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Env
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore

type PeopleKind =
  | Followers
  | Following

type PeopleList = {
  Kind: PeopleKind
  Loading: bool
  Error: AppError option
  Items: User list
  Cursor: string option
}

type Model = {
  Handle: string
  Loading: bool
  Error: AppError option
  Details: ProfileDetails option
  Commits: CommitNode list
  CommitsCursor: string option
  CommitsLoading: bool
  CommitsError: AppError option
  FollowBusy: bool
  FollowError: AppError option
  People: PeopleList option
  PollChoices: Map<string, string>
  Now: DateTimeOffset
}

type Msg =
  | Shown of Result<ProfileDetails, AppError>
  | CommitsFetched of Result<Page<CommitNode>, AppError>
  | MoreCommitsRequested
  | CommitsAppended of Result<Page<CommitNode>, AppError>
  | FollowToggled
  | FollowDone of wanted: bool * Result<unit, AppError>
  | FollowersRequested
  | FollowingRequested
  | PeopleFetched of kind: PeopleKind * Result<Page<User>, AppError>
  | MorePeopleRequested
  | PeopleAppended of kind: PeopleKind * Result<Page<User>, AppError>
  | PeopleClosed
  | OpenRepository of handle: string
  | Opened of Commit
  | QuoteRequested of Commit
  | ReplyRequested of Commit
  | ExternalOpened of url: string
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PollVotedDone of Result<Commit, AppError>
  | CommitUpdated of Commit
  | CommitDeleted of string
  | ClockTicked of moment: DateTimeOffset

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession option
  Handle: string
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Flyout: DevNull.Client.Components.UserFlyout.Source voption
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnOpened: Commit -> unit
  OnReply: Commit -> unit
  OnQuote: Commit -> unit
  OnOpenExternal: string -> unit
  OnOpenRepository: string -> unit
  OnListChanged: bool -> unit
  OnDialog: RowActions.Dialog -> unit
}

[<Struct>]
type View = { Center: Control; Lists: Control }

let inline canFollow(viewerId: string, details: ProfileDetails) =
  details.Profile.User.Id <> viewerId

let fetchProfile(props: Props<'env>) : Async<Result<ProfileDetails, AppError>> =
  Users.get(
    props.Env,
    {
      KeyFile = props.KeyFile
      Username = props.Handle
    }
  )

let fetchCommits
  (props: Props<'env>, cursor: string option)
  : Async<Result<Page<CommitNode>, AppError>> =
  Users.commits(
    props.Env,
    {
      KeyFile = props.KeyFile
      Username = props.Handle
      Replies = false
      Cursor = cursor
    }
  )

let fetchPeople
  (props: Props<'env>, kind: PeopleKind, cursor: string option)
  : Async<Result<Page<User>, AppError>> =
  let request: Users.ListRequest = {
    KeyFile = props.KeyFile
    Username = props.Handle
    Cursor = cursor
  }

  match kind with
  | Followers -> Users.followers(props.Env, request)
  | Following -> Users.following(props.Env, request)

let follow(props: Props<'env>, wanted: bool) : Async<Result<unit, AppError>> =
  let request: Users.Request = {
    KeyFile = props.KeyFile
    Username = props.Handle
  }

  if wanted then
    Users.follow(props.Env, request)
  else
    Users.unfollow(props.Env, request)

let adjustFollowers(delta: int, profile: Profile) : Profile = {
  profile with
      FollowersCount =
        profile.FollowersCount |> Option.map(fun count -> max 0 (count + delta))
}

let replaceNode(items: CommitNode list, updated: Commit) =
  items
  |> List.map(function
    | LiveCommit commit when commit.Id = updated.Id -> LiveCommit updated
    | node -> node)

let removeNode(items: CommitNode list, commitId: string) =
  items
  |> List.filter(function
    | LiveCommit commit -> commit.Id <> commitId
    | Tombstone _ -> true)

let vote
  (props: Props<'env>, commit: Commit, option: PollOption)
  : Async<Result<Commit, AppError>> =
  Commits.vote(
    props.Env,
    {
      KeyFile = props.KeyFile
      CommitId = commit.Id
      OptionId = option.Id
    }
  )

let init(props: Props<'env>) : Model * Cmd<Msg> =
  {
    Handle = props.Handle
    Loading = true
    Error = None
    Details = None
    Commits = []
    CommitsCursor = None
    CommitsLoading = true
    CommitsError = None
    FollowBusy = false
    FollowError = None
    People = None
    PollChoices = Map.empty
    Now = DateTimeOffset.UtcNow
  },
  Cmd.batch [
    Cmd.OfAsync.perform fetchProfile props Shown
    Cmd.OfAsync.perform fetchCommits (props, None) CommitsFetched
  ]

let openPeople
  (props: Props<'env>, kind: PeopleKind, model: Model)
  : Model * Cmd<Msg> =
  let list: PeopleList = {
    Kind = kind
    Loading = true
    Error = None
    Items = []
    Cursor = None
  }

  { model with People = Some list },
  Cmd.batch [
    Cmd.ofAction(fun () -> props.OnListChanged true)

    Cmd.OfAsync.perform fetchPeople (props, kind, None) (fun result ->
      PeopleFetched(kind, result))
  ]

let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
  match msg with
  | Shown result ->
    match result with
    | Ok details ->
      {
        model with
            Loading = false
            Error = None
            Details = Some details
      },
      Cmd.none
    | Error error ->
      {
        model with
            Loading = false
            Error = Some error
      },
      Cmd.none
  | CommitsFetched result ->
    match result with
    | Ok page ->
      {
        model with
            Commits = page.Items
            CommitsCursor = page.NextCursor
            CommitsLoading = false
            CommitsError = None
      },
      Cmd.none
    | Error error ->
      {
        model with
            CommitsLoading = false
            CommitsError = Some error
      },
      Cmd.none
  | MoreCommitsRequested ->
    match model.CommitsCursor with
    | Some cursor when not model.CommitsLoading ->
      {
        model with
            CommitsLoading = true
            CommitsError = None
      },
      Cmd.OfAsync.perform fetchCommits (props, Some cursor) CommitsAppended
    | _ -> model, Cmd.none
  | CommitsAppended result ->
    match result with
    | Ok page ->
      {
        model with
            Commits = model.Commits @ page.Items
            CommitsCursor = page.NextCursor
            CommitsLoading = false
            CommitsError = None
      },
      Cmd.none
    | Error error ->
      {
        model with
            CommitsLoading = false
            CommitsError = Some error
      },
      Cmd.none
  | FollowToggled ->
    match model.Details with
    | Some details when
      not model.FollowBusy && canFollow(props.ViewerId, details)
      ->
      let wanted = not details.IsFollowing
      let delta = if wanted then 1 else -1

      {
        model with
            Details =
              Some {
                details with
                    IsFollowing = wanted
                    Profile = adjustFollowers(delta, details.Profile)
              }
            FollowBusy = true
            FollowError = None
      },
      Cmd.OfAsync.perform follow (props, wanted) (fun result ->
        FollowDone(wanted, result))
    | _ -> model, Cmd.none
  | FollowDone(wanted, result) ->
    match result with
    | Ok() ->
      {
        model with
            FollowBusy = false
            FollowError = None
      },
      Cmd.none
    | Error error ->
      let delta = if wanted then -1 else 1

      let details =
        model.Details
        |> Option.map(fun current -> {
          current with
              IsFollowing = not wanted
              Profile = adjustFollowers(delta, current.Profile)
        })

      {
        model with
            Details = details
            FollowBusy = false
            FollowError = Some error
      },
      Cmd.none
  | FollowersRequested -> openPeople(props, Followers, model)
  | FollowingRequested -> openPeople(props, Following, model)
  | PeopleFetched(kind, result) ->
    match model.People with
    | Some list when list.Kind = kind ->
      match result with
      | Ok page ->
        {
          model with
              People =
                Some {
                  list with
                      Loading = false
                      Error = None
                      Items = page.Items
                      Cursor = page.NextCursor
                }
        },
        Cmd.none
      | Error error ->
        {
          model with
              People =
                Some {
                  list with
                      Loading = false
                      Error = Some error
                }
        },
        Cmd.none
    | _ -> model, Cmd.none
  | MorePeopleRequested ->
    match model.People with
    | Some list when not list.Loading && list.Cursor.IsSome ->
      {
        model with
            People =
              Some {
                list with
                    Loading = true
                    Error = None
              }
      },
      Cmd.OfAsync.perform
        fetchPeople
        (props, list.Kind, list.Cursor)
        (fun result -> PeopleAppended(list.Kind, result))
    | _ -> model, Cmd.none
  | PeopleAppended(kind, result) ->
    match model.People with
    | Some list when list.Kind = kind ->
      match result with
      | Ok page ->
        {
          model with
              People =
                Some {
                  list with
                      Loading = false
                      Error = None
                      Items = list.Items @ page.Items
                      Cursor = page.NextCursor
                }
        },
        Cmd.none
      | Error error ->
        {
          model with
              People =
                Some {
                  list with
                      Loading = false
                      Error = Some error
                }
        },
        Cmd.none
    | _ -> model, Cmd.none
  | PeopleClosed ->
    match model.People with
    | Some _ ->
      { model with People = None },
      Cmd.ofAction(fun () -> props.OnListChanged false)
    | None -> model, Cmd.none
  | OpenRepository handle ->
    model, Cmd.ofAction(fun () -> props.OnOpenRepository handle)
  | Opened commit -> model, Cmd.ofAction(fun () -> props.OnOpened commit)
  | QuoteRequested commit -> model, Cmd.ofAction(fun () -> props.OnQuote commit)
  | ReplyRequested commit -> model, Cmd.ofAction(fun () -> props.OnReply commit)
  | ExternalOpened url ->
    model, Cmd.ofAction(fun () -> props.OnOpenExternal url)
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
    | Some option ->
      { model with CommitsError = None },
      Cmd.OfAsync.perform vote (props, commit, option) PollVotedDone
    | None -> model, Cmd.none
  | PollVotedDone result ->
    match result with
    | Ok updated ->
      {
        model with
            Commits = replaceNode(model.Commits, updated)
            PollChoices = model.PollChoices |> Map.remove updated.Id
      },
      Cmd.none
    | Error error -> { model with CommitsError = Some error }, Cmd.none
  | CommitUpdated updated ->
    {
      model with
          Commits = replaceNode(model.Commits, updated)
    },
    Cmd.none
  | CommitDeleted commitId ->
    {
      model with
          Commits = removeNode(model.Commits, commitId)
    },
    Cmd.none
  | ClockTicked moment -> { model with Now = moment }, Cmd.none

let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
  Sub.fromObservable "clock" (props.Now |> Observable.map ClockTicked)

let inline nodeKey(node: CommitNode) = FeedParts.nodeKey node

let laneKey(lane: Lane option) =
  match lane with
  | Some(LaneIndex index) -> string index
  | Some(LaneColor color) -> color
  | None -> "fallback"

let laneSlot(lane: Lane option) =
  match lane with
  | Some(LaneIndex index) -> $"lane {index}"
  | Some(LaneColor color) -> $"lane {color}"
  | None -> "lane —"

let metaText(details: ProfileDetails) : string =
  [
    Some(laneSlot details.Profile.Lane)
    details.Profile.Location
    Some($"joined {details.Profile.CreatedAt.Year}")
  ]
  |> List.choose id
  |> String.concat " · "

let bioText(details: ProfileDetails) : string =
  details.Profile.Bio
  |> Option.orElse details.Profile.User.Bio
  |> Option.defaultValue ""

let statsText(details: ProfileDetails) : string =
  [
    details.Streaks
    |> Option.map(fun streaks ->
      $"streak {streaks.Current} · longest {streaks.Longest}")

    details.Achievements
    |> Option.map(fun achievements ->
      $"level {achievements.Level} · {achievements.Rank} · {achievements.Xp} xp")
  ]
  |> List.choose id
  |> String.concat " · "

let peopleTitle(handle: string, people: PeopleList) =
  let kind =
    match people.Kind with
    | Followers -> "FOLLOWERS"
    | Following -> "FOLLOWING"

  $"{kind} · @{handle}"

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : View =
  let now = model |> Observable.map _.Now |> Slices.share
  let pasteis = Observable.single props.Pasteis
  let polls = model |> Observable.map _.PollChoices |> Slices.share

  let actions =
    props.Session
    |> Option.map(fun session ->
      RowActions.create {
        Env = props.Env
        Session = session
        Now = now
        OfferBasicActions = true
        OnUpdated = CommitUpdated >> dispatch
        OnDeleted = CommitDeleted >> dispatch
        OnDialog = props.OnDialog
        Flyout = props.Flyout
      })

  let busy =
    actions
    |> Option.map _.Busy
    |> Option.defaultValue(Observable.single(None: string option))

  let inline slot(command: Commit -> unit) : ActionSlot voption =
    ValueSome { Command = command; Busy = busy }

  let rowProps(onEvent: Event -> unit) : RowProps = {
    OnEvent =
      fun event ->
        actions |> Option.iter(fun current -> current.Handle event)
        onEvent event
    OnOpenExternal = fun url -> dispatch(ExternalOpened url)
    ViewerId = props.ViewerId
    Pasteis = pasteis
    Now = now
    PollChoices = polls
    Dense = true
    Images = props.Images
    Flyout = actions |> Option.map _.Flyout |> Option.defaultValue ValueNone
    Pin = ValueNone
    Star =
      match actions with
      | Some current -> current.Star
      | None -> ValueNone
    Stars = ValueNone
    Vote = slot(fun commit -> dispatch(PollVoteRequested commit))
    Pastel =
      match actions with
      | Some current -> current.Pastel
      | None -> ValueNone
    Menu =
      match actions with
      | Some current -> current.Menu
      | None -> ValueNone
    Translation =
      actions
      |> Option.map _.Translation
      |> Option.defaultValue(Observable.single(fun (_: Commit) -> ValueNone))
    Zone = props.Zone
  }

  let cardEvent =
    function
    | CommitCard.Opened commit -> dispatch(Opened commit)
    | CommitCard.ReplyToggled commit -> dispatch(ReplyRequested commit)
    | CommitCard.QuoteRequested commit -> dispatch(QuoteRequested commit)
    | CommitCard.PollChoiceChanged(commit, option, chosen) ->
      dispatch(PollChoiceChanged(commit, option, chosen))
    | _ -> ()

  let rowPropsOf = rowProps cardEvent

  let fullProps = { rowPropsOf with Dense = false }

  let userKey(current: Model) =
    current.Details
    |> Option.map(fun details ->
      let user = details.Profile.User

      String.concat "|" [
        user.Id
        user.Handle
        user.Name
        user.AvatarUrl |> Option.defaultValue ""
        laneKey details.Profile.Lane
      ])
    |> Option.defaultValue ""

  let buildIdentity(current: Model) : Control =
    match current.Details with
    | Some details ->
      let user = details.Profile.User

      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(12.0)
        .Children(
          Avatar.view {
            Name = user.Name
            Url = user.AvatarUrl
            Lane = details.Profile.Lane
            Size = 56.0
            Cache = props.Images
            Square = false
            Viewable = false
          },
          StackPanel()
            .Spacing(4.0)
            .VerticalAlignment(VerticalAlignment.Center)
            .Children(
              TextBlock().Classes(Theme.name Theme.Subtitle).Text(user.Name),
              ProfileCard.laneHandle(details.Profile.Lane, user.Handle)
            )
        )
      :> Control
    | None -> Panel() :> Control

  let identity =
    ContentControl()
      .Name("RepositoryIdentity")
      .Content(
        PageChrome.rebuildOn(model, userKey, buildIdentity) |> _.ToBinding()
      )

  let followButton =
    Button()
      .Name("RepositoryFollow")
      .Classes(Theme.name Theme.AccentButton)
      .VerticalAlignment(VerticalAlignment.Top)
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Details
          |> Option.exists(fun details -> canFollow(props.ViewerId, details)))
      )
      .IsEnabled(model |> Observable.map(fun current -> not current.FollowBusy))
      .Content(
        TextBlock()
          .Text(
            model
            |> Observable.map(fun current ->
              current.Details
              |> Option.map(fun details ->
                if details.IsFollowing then "Following" else "Follow")
              |> Option.defaultValue "Follow")
          )
      )
      .OnClickHandler(fun _ _ -> dispatch FollowToggled)

  let top = Grid()
  top.ColumnDefinitions <- ColumnDefinitions("*,Auto")

  top.Children(identity.Column(0), followButton.Column(1)) |> ignore

  let bio =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .TextWrapping(TextWrapping.Wrap)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Details |> Option.map bioText |> Option.defaultValue "")
      )
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Details
          |> Option.exists(fun details -> bioText details <> ""))
      )

  let meta =
    TextBlock()
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .FontSize(12.0)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Details |> Option.map metaText |> Option.defaultValue "")
      )

  let githubKey(current: Model) =
    current.Details
    |> Option.map(fun details -> details.Profile.GithubUrl)
    |> Option.defaultValue ""

  let buildGithub(current: Model) : Control =
    match current.Details with
    | Some details when not(String.IsNullOrWhiteSpace details.Profile.GithubUrl) ->
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Content("github ↗")
        .OnClickHandler(fun _ _ ->
          dispatch(ExternalOpened details.Profile.GithubUrl))
      :> Control
    | _ -> Panel() :> Control

  let github =
    ContentControl()
      .Content(
        PageChrome.rebuildOn(model, githubKey, buildGithub) |> _.ToBinding()
      )

  let metaLine =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(10.0)
      .Children(meta, github)

  let countOf(select: Profile -> int option) : IObservable<int> =
    model
    |> Observable.map(fun current ->
      current.Details
      |> Option.bind(fun details -> select details.Profile)
      |> Option.defaultValue 0)

  let stat(count: IObservable<int>, label: string) : Control =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(4.0)
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.StatNum)
          .Text(count |> Observable.map string),
        TextBlock().Classes(Theme.name Theme.Muted).Text(label)
      )

  let countButton
    (label: string, select: Profile -> int option, onOpen: unit -> unit)
    : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Content(stat(countOf select, label))
      .OnClickHandler(fun _ _ -> onOpen())

  let counts =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(16.0)
      .Children(
        stat(countOf _.CommitsCount, "commits"),
        countButton(
          "followers",
          _.FollowersCount,
          fun () -> dispatch FollowersRequested
        ),
        countButton(
          "following",
          _.FollowingCount,
          fun () -> dispatch FollowingRequested
        )
      )

  let followError = PageChrome.errorPanel(model |> Observable.map _.FollowError)

  let profileLoading =
    PageChrome.collapsingMessagePanel(
      model
      |> Observable.map(fun current ->
        if current.Loading && current.Details.IsNone then
          "Reading the profile…"
        else
          ""),
      Theme.Muted
    )

  let profileError = PageChrome.errorPanel(model |> Observable.map _.Error)

  let header =
    StackPanel().Spacing(8.0).Children(top, bio, metaLine, counts, followError)

  let readmeKey(current: Model) =
    current.Details
    |> Option.bind(fun details -> details.PinnedCommit)
    |> Option.map nodeKey
    |> Option.defaultValue ""

  let buildReadme(current: Model) : Control =
    match
      current.Details |> Option.bind(fun details -> details.PinnedCommit)
    with
    | Some node ->
      Border()
        .Classes(Theme.name Theme.Card, Theme.name Theme.Hairline)
        .Child(
          StackPanel()
            .Spacing(6.0)
            .Margin(Thickness(12.0))
            .Children(
              TextBlock()
                .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
                .Text("README (pinned)"),
              CommitCard.feedRow fullProps node
            )
        )
      :> Control
    | None -> Panel() :> Control

  let readme =
    ContentControl()
      .Name("RepositoryReadme")
      .Content(
        PageChrome.rebuildOn(model, readmeKey, buildReadme) |> _.ToBinding()
      )
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Details
          |> Option.exists(fun details -> details.PinnedCommit.IsSome))
      )

  let activityKey(current: Model) =
    current.Details
    |> Option.bind(fun details -> details.Activity)
    |> Option.map(fun activity ->
      String.concat "|" [
        activity.Scale
        laneKey(
          current.Details |> Option.bind(fun details -> details.Profile.Lane)
        )
        activity.Buckets
        |> List.map(fun bucket -> bucket.Date + ":" + string bucket.Count)
        |> String.concat ","
      ])
    |> Option.defaultValue ""

  let buildActivity(current: Model) : Control =
    match
      current.Details
      |> Option.bind(fun details ->
        details.Activity
        |> Option.map(fun activity -> details.Profile.Lane, activity))
    with
    | Some(lane, activity) ->
      let maxCount =
        activity.Buckets
        |> List.fold (fun best bucket -> max best bucket.Count) 1

      let bars = Grid()

      bars.ColumnDefinitions <-
        ColumnDefinitions(
          String.Join(",", List.replicate (max 1 activity.Buckets.Length) "*")
        )

      activity.Buckets
      |> List.iteri(fun index bucket ->
        let height = 2.0 + 28.0 * float bucket.Count / float maxCount

        let bar =
          Border()
            .Name("ContributionBar")
            .Height(height)
            .VerticalAlignment(VerticalAlignment.Bottom)
            .Margin(Thickness(0.5, 0.0, 0.5, 0.0))

        if bucket.Count = 0 then
          bar.Opacity(0.35) |> ignore

        Theme.laneBack(bar, lane)
        ToolTip.SetTip(bar, $"{bucket.Count} on {bucket.Date}")
        bars.Children(bar.Column(index)) |> ignore)

      let scale =
        if String.IsNullOrWhiteSpace activity.Scale then
          "CONTRIBUTIONS"
        else
          $"CONTRIBUTIONS · {activity.Scale}"

      StackPanel()
        .Spacing(6.0)
        .Children(
          TextBlock().Classes(Theme.name Theme.Mono).Text(scale),
          bars.Height(32.0)
        )
      :> Control
    | None -> Panel() :> Control

  let activity =
    ContentControl()
      .Name("RepositoryActivity")
      .Content(
        PageChrome.rebuildOn(model, activityKey, buildActivity) |> _.ToBinding()
      )
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Details
          |> Option.exists(fun details -> details.Activity.IsSome))
      )

  let stats =
    TextBlock()
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .FontSize(12.0)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Details |> Option.map statsText |> Option.defaultValue "")
      )
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Details
          |> Option.exists(fun details -> statsText details <> ""))
      )

  let commits =
    ItemsControl()
      .Name("RepositoryCommits")
      .ItemTemplate(
        FuncDataTemplate<CommitNode>(
          (fun node _ ->
            if isNull(box node) then
              Panel() :> Control
            else
              CommitCard.feedRow rowPropsOf node),
          false
        )
      )
      .BindItemsSource(
        model
        |> Observable.map(fun current ->
          current.Commits :> IReadOnlyList<CommitNode>),
        FeedParts.nodeKey
      )

  let historyLabel =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .Text(
        model |> Observable.map(fun current -> "HISTORY @" + current.Handle)
      )

  let loadingLine =
    PageChrome.collapsingMessagePanel(
      model
      |> Observable.map(fun current ->
        if current.CommitsLoading && current.Commits.IsEmpty then
          "Reading commits…"
        else
          ""),
      Theme.Muted
    )

  let emptyLine =
    PageChrome.collapsingMessagePanel(
      model
      |> Observable.map(fun current ->
        if not current.CommitsLoading && current.Commits.IsEmpty then
          "No commits yet."
        else
          ""),
      Theme.Muted
    )

  let commitsError =
    PageChrome.errorPanel(model |> Observable.map _.CommitsError)

  let commitsMore =
    PageChrome.loadMore(
      model
      |> Observable.map(fun current ->
        current.CommitsCursor.IsSome && not current.CommitsLoading),
      fun () -> dispatch MoreCommitsRequested
    )

  let centerStack =
    StackPanel()
      .Spacing(12.0)
      .Margin(Thickness(16.0))
      .Children(
        header,
        profileLoading,
        profileError,
        readme,
        activity,
        stats,
        historyLabel,
        commits,
        loadingLine,
        emptyLine,
        commitsError,
        commitsMore
      )

  let center =
    Border()
      .Name("RepositoryCenter")
      .Child(
        ScrollViewer()
          .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
          .Content(centerStack)
      )

  let peopleVisible =
    model
    |> Observable.map(fun current -> current.People.IsSome)
    |> Observable.distinctUntilChanged

  let peopleRows =
    ItemsControl()
      .ItemTemplate(
        FuncDataTemplate<User>(
          (fun user _ ->
            if isNull(box user) then
              Panel() :> Control
            else
              let bio =
                TextBlock()
                  .Classes(Theme.name Theme.Muted)
                  .FontSize(12.0)
                  .Text(user.Bio |> Option.defaultValue "")
                  .TextTrimming(TextTrimming.CharacterEllipsis)
                  .IsVisible(
                    user.Bio
                    |> Option.map(fun text -> text <> "")
                    |> Option.defaultValue false
                  )

              let content =
                StackPanel()
                  .Orientation(Orientation.Horizontal)
                  .Spacing(10.0)
                  .Children(
                    Avatar.view {
                      Name = user.Name
                      Url = user.AvatarUrl
                      Lane = user.Lane
                      Size = 32.0
                      Cache = props.Images
                      Square = false
                      Viewable = false
                    },
                    StackPanel()
                      .Spacing(2.0)
                      .VerticalAlignment(VerticalAlignment.Center)
                      .Children(
                        TextBlock()
                          .Classes(Theme.name Theme.Subtitle)
                          .Text(user.Name),
                        ProfileCard.laneHandle(user.Lane, user.Handle),
                        bio
                      )
                  )

              Button()
                .Classes(Theme.name Theme.CommitRow)
                .HorizontalAlignment(HorizontalAlignment.Stretch)
                .HorizontalContentAlignment(HorizontalAlignment.Left)
                .Padding(Thickness(8.0))
                .Content(content)
                .OnClickHandler(fun _ _ ->
                  dispatch(OpenRepository user.Handle))
              :> Control),
          false
        )
      )
      .BindItemsSource(
        model
        |> Observable.map(fun current ->
          current.People |> Option.map _.Items |> Option.defaultValue []
          :> IReadOnlyList<User>),
        fun user -> user.Id
      )

  let peopleTitleLine =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .Text(
        model
        |> Observable.map(fun current ->
          current.People
          |> Option.map(fun people -> peopleTitle(current.Handle, people))
          |> Option.defaultValue "PEOPLE")
      )

  let peopleClose =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Content("Close")
      .OnClickHandler(fun _ _ -> dispatch PeopleClosed)

  let peopleHead = DockPanel().LastChildFill(true)

  peopleHead.Children(peopleClose.DockRight(), peopleTitleLine) |> ignore

  let peopleLoadingLine =
    PageChrome.collapsingMessagePanel(
      model
      |> Observable.map(fun current ->
        match current.People with
        | Some people when people.Loading && people.Items.IsEmpty ->
          match people.Kind with
          | Followers -> "Reading followers…"
          | Following -> "Reading following…"
        | _ -> ""),
      Theme.Muted
    )

  let peopleEmptyLine =
    PageChrome.collapsingMessagePanel(
      model
      |> Observable.map(fun current ->
        match current.People with
        | Some people when not people.Loading && people.Items.IsEmpty ->
          match people.Kind with
          | Followers -> "No followers yet."
          | Following -> "Not following anyone yet."
        | _ -> ""),
      Theme.Muted
    )

  let peopleError =
    PageChrome.errorPanel(
      model
      |> Observable.map(fun current ->
        current.People |> Option.bind(fun people -> people.Error))
    )

  let peopleMore =
    PageChrome.loadMore(
      model
      |> Observable.map(fun current ->
        match current.People with
        | Some people -> people.Cursor.IsSome && not people.Loading
        | None -> false),
      fun () -> dispatch MorePeopleRequested
    )

  let peopleStack =
    StackPanel()
      .Spacing(8.0)
      .Margin(Thickness(12.0))
      .Children(
        peopleHead,
        peopleRows,
        peopleLoadingLine,
        peopleEmptyLine,
        peopleError,
        peopleMore
      )

  let lists =
    Border()
      .Name("RepositoryPeople")
      .Classes(Theme.name Theme.CardBack)
      .IsVisible(peopleVisible)
      .Child(
        ScrollViewer()
          .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
          .Content(peopleStack)
      )

  { Center = center; Lists = lists }

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> init props)
    (fun msg model -> update(props, msg, model))
    (view props)
  |> Program.withSubscription(subscribe props)
