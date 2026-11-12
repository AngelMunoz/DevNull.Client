module DevNull.Client.Layouts.Editor.RepoTab

open System
open System.Reactive.Disposables
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Threading
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.UiClock

type FollowedList =
  | Followers
  | Following

type ListState = {
  Kind: FollowedList option
  Items: User list
  Cursor: string option
  Loading: bool
  Error: AppError option
}

type Model = {
  Loading: bool
  Error: AppError option
  Details: ProfileDetails option
  Commits: CommitNode list
  CommitsCursor: string option
  CommitsLoading: bool
  CommitsError: AppError option
  List: ListState
  FollowBusy: bool
  Now: DateTimeOffset
}

type Msg =
  | ProfileFetched of Result<ProfileDetails, AppError>
  | CommitsFetched of Result<Page<CommitNode>, AppError>
  | CommitsAppended of Result<Page<CommitNode>, AppError>
  | MoreCommits
  | FollowToggled
  | FollowDone of ProfileDetails * Result<unit, AppError>
  | ListToggled of FollowedList
  | ListFetched of FollowedList * Result<Page<User>, AppError>
  | ListAppended of FollowedList * Result<Page<User>, AppError>
  | MoreListRequested
  | ClockTicked of moment: DateTimeOffset

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache> = {
  Env: 'env
  Session: SignedInSession
  Handle: string
  Zone: Zone
  Now: unit -> DateTimeOffset
  OnOpenCommit: Commit -> unit
  OnOpenRepo: string -> unit
}

let emptyList = {
  Kind = None
  Items = []
  Cursor = None
  Loading = false
  Error = None
}

let initial = {
  Loading = true
  Error = None
  Details = None
  Commits = []
  CommitsCursor = None
  CommitsLoading = true
  CommitsError = None
  List = emptyList
  FollowBusy = false
  Now = DateTimeOffset.MinValue
}

let fetchProfile(props: Props<'env>) : Async<Result<ProfileDetails, AppError>> =
  Users.get(
    props.Env,
    {
      KeyFile = props.Session.KeyFile
      Username = props.Handle
    }
  )

let fetchCommits
  (props: Props<'env>, cursor: string option)
  : Async<Result<Page<CommitNode>, AppError>> =
  Users.commits(
    props.Env,
    {
      KeyFile = props.Session.KeyFile
      Username = props.Handle
      Replies = false
      Cursor = cursor
    }
  )

let fetchList
  (props: Props<'env>, kind: FollowedList, cursor: string option)
  : Async<Result<Page<User>, AppError>> =
  let request: Users.ListRequest = {
    KeyFile = props.Session.KeyFile
    Username = props.Handle
    Cursor = cursor
  }

  match kind with
  | Followers -> Users.followers(props.Env, request)
  | Following -> Users.following(props.Env, request)

let follow
  (props: Props<'env>, following: bool)
  : Async<Result<unit, AppError>> =
  let request: Users.Request = {
    KeyFile = props.Session.KeyFile
    Username = props.Handle
  }

  if following then
    Users.unfollow(props.Env, request)
  else
    Users.follow(props.Env, request)

let init(props: Props<'env>) =
  { initial with Now = props.Now() },
  Cmd.batch [
    Cmd.OfAsync.perform fetchProfile props ProfileFetched
    Cmd.OfAsync.perform fetchCommits (props, None) CommitsFetched
  ]

let update(props: Props<'env>, msg: Msg, model: Model) =
  match msg with
  | ProfileFetched result ->
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
  | MoreCommits ->
    match model.CommitsCursor with
    | Some cursor ->
      {
        model with
            CommitsLoading = true
            CommitsError = None
      },
      Cmd.OfAsync.perform fetchCommits (props, Some cursor) CommitsAppended
    | None -> model, Cmd.none
  | FollowToggled ->
    match model.Details with
    | Some details when not model.FollowBusy ->
      let wasFollowing = details.IsFollowing
      let shift = if wasFollowing then -1 else 1

      let profile = {
        details.Profile with
            FollowersCount =
              details.Profile.FollowersCount
              |> Option.map(fun count -> max 0 (count + shift))
      }

      let nextDetails = {
        details with
            IsFollowing = not wasFollowing
            Profile = profile
      }

      {
        model with
            Details = Some nextDetails
            FollowBusy = true
            Error = None
      },
      Cmd.OfAsync.perform follow (props, wasFollowing) (fun result ->
        FollowDone(details, result))
    | _ -> model, Cmd.none
  | FollowDone(prior, result) ->
    match result with
    | Ok() ->
      {
        model with
            FollowBusy = false
            Error = None
      },
      Cmd.none
    | Error error ->
      {
        model with
            Details = Some prior
            FollowBusy = false
            Error = Some error
      },
      Cmd.none
  | ListToggled kind ->
    if model.List.Kind = Some kind then
      { model with List = emptyList }, Cmd.none
    else
      let next = {
        model with
            List = {
              emptyList with
                  Kind = Some kind
                  Loading = true
            }
      }

      next,
      Cmd.OfAsync.perform fetchList (props, kind, None) (fun result ->
        ListFetched(kind, result))
  | ListFetched(kind, result) ->
    if model.List.Kind <> Some kind then
      model, Cmd.none
    else
      match result with
      | Ok page ->
        {
          model with
              List = {
                model.List with
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
              List = {
                model.List with
                    Loading = false
                    Error = Some error
              }
        },
        Cmd.none
  | ListAppended(kind, result) ->
    if model.List.Kind <> Some kind then
      model, Cmd.none
    else
      match result with
      | Ok page ->
        {
          model with
              List = {
                model.List with
                    Loading = false
                    Error = None
                    Items = model.List.Items @ page.Items
                    Cursor = page.NextCursor
              }
        },
        Cmd.none
      | Error error ->
        {
          model with
              List = {
                model.List with
                    Loading = false
                    Error = Some error
              }
        },
        Cmd.none
  | MoreListRequested ->
    match model.List.Kind, model.List.Cursor with
    | Some kind, Some cursor ->
      {
        model with
            List = {
              model.List with
                  Loading = true
                  Error = None
            }
      },
      Cmd.OfAsync.perform fetchList (props, kind, Some cursor) (fun result ->
        ListAppended(kind, result))
    | _ -> model, Cmd.none
  | ClockTicked moment ->
    let next = { model with Now = moment }

    next, Cmd.none

let subscribe(props: Props<'env>, _model: Model) : Sub<Msg> =
  [
    Sub.fromObservable
      "clock"
      (props.Env.UiClock.Minute |> Observable.map ClockTicked)
  ]
  |> Sub.batch

let statPair(count: int, label: string) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(4.0)
    .VerticalAlignment(VerticalAlignment.Center)
    .Children(
      TextBlock().Classes(Theme.name Theme.StatNum).Text(string count),
      TextBlock().Classes(Theme.name Theme.Muted).Text(label)
    )

let chip(text: string, lane: Lane option) : Control =
  let label =
    TextBlock().Classes(Theme.name Theme.Mono).FontSize(11.0).Text(text)

  Theme.laneText(label, lane)

  let chip =
    Border()
      .Classes(Theme.name Theme.Chip)
      .Padding(8.0, 2.0, 8.0, 2.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Child(label)

  Theme.laneBorder(chip, lane)
  chip

let levelChip(achievements: Achievements, lane: Lane option) : Control =
  chip($"Lv. {achievements.Level} · {achievements.Rank}", lane)

let streakChip(streaks: Streaks, lane: Lane option) : Control =
  chip($"streak {streaks.Current} · longest {streaks.Longest}", lane)

let badgeChip(badge: Badge, lane: Lane option) : Control =
  let progress = max 0 badge.Progress
  let target = max 1 badge.Target

  let status =
    if badge.Unlocked then
      $"unlocked · {progress}/{target}"
    else
      $"{progress}/{target}"

  let bar =
    ProgressBar()
      .Minimum(0.0)
      .Maximum(float target)
      .Value(float progress)
      .Height(4.0)

  let body =
    StackPanel()
      .Spacing(2.0)
      .Children(
        TextBlock().Text(badge.Icon).Classes(Theme.name Theme.Mono),
        TextBlock().Text(badge.Name).Classes(Theme.name Theme.Subtitle),
        TextBlock()
          .Text(status)
          .Classes(Theme.name Theme.Mono, Theme.name Theme.Muted),
        bar
      )

  let shield =
    Border()
      .Name($"RepoBadge-{badge.Id}")
      .Classes(Theme.name Theme.Chip)
      .Padding(8.0, 4.0, 8.0, 4.0)
      .Child(body)

  if badge.Unlocked then
    Theme.laneBorder(shield, lane)

  shield

let shieldsView(details: ProfileDetails) : Control =
  let lane = details.Profile.Lane

  let chips: Control array = [|
    match details.Achievements with
    | Some achievements ->
      yield levelChip(achievements, lane)

      for badge in achievements.Badges do
        yield badgeChip(badge, lane)
    | None -> ()

    match details.Streaks with
    | Some streaks -> yield streakChip(streaks, lane)
    | None -> ()
  |]

  if Array.isEmpty chips then
    Panel()
  else
    let wrapped =
      WrapPanel().Orientation(Orientation.Horizontal).Children(chips)

    wrapped.Classes(Theme.name Theme.SurfaceBack).Margin(0.0, 2.0, 0.0, 2.0)

let activityView(activity: Activity, lane: Lane option) : Control =
  let caption =
    TextBlock()
      .Name("RepoActivityScale")
      .Classes(Theme.name Theme.Mono)
      .Text($"activity · {activity.Scale}")

  match activity.Buckets with
  | [] -> StackPanel().Children(caption)
  | buckets ->
    let maxCount = buckets |> List.maxBy _.Count |> _.Count |> max 1

    let bars: Control array = [|
      for bucket in buckets do
        let height = 8.0 + float bucket.Count / float maxCount * 44.0

        let bar =
          Border()
            .Width(6.0)
            .Height(height)
            .CornerRadius(1.0)
            .VerticalAlignment(VerticalAlignment.Bottom)
            .Margin(0.0, 0.0, 1.0, 0.0)

        Theme.laneBack(bar, lane)
        ToolTip.SetTip(bar, $"{bucket.Date} · {bucket.Count}")
        bar
    |]

    let graph =
      ScrollViewer()
        .HorizontalScrollBarVisibility(ScrollBarVisibility.Auto)
        .VerticalScrollBarVisibility(ScrollBarVisibility.Disabled)
        .Content(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Height(52.0)
            .Children(bars)
        )

    StackPanel().Spacing(4.0).Children(caption, graph)

let readmeView
  (details: ProfileDetails, rowProps: CommitCard.RowProps)
  : Control =
  match details.PinnedCommit with
  | Some(LiveCommit commit) ->
    Border()
      .Name("RepoReadme")
      .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .Padding(10.0)
      .Child(
        StackPanel()
          .Spacing(6.0)
          .Children(
            TextBlock()
              .Text("README — pinned")
              .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted),
            CommitCard.feedRow rowProps (LiveCommit commit)
          )
      )
  | Some(Tombstone _) ->
    Border()
      .Name("RepoReadme")
      .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .Padding(10.0)
      .Child(
        TextBlock().Text("README — deleted").Classes(Theme.name Theme.FgMuted)
      )
  | None -> Panel()

let headerView
  (props: Props<'env>, model: Model, dispatch: Dispatch<Msg>)
  : Control =
  match model.Details with
  | None ->
    if model.Loading then
      TextBlock()
        .Text("Loading the repository…")
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
    else
      PageChrome.errorPanel(Observable.single model.Error)
  | Some details ->
    let profile = details.Profile
    let user = profile.User
    let lane = profile.Lane

    let identity =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(12.0)
        .Children(
          Avatar.view {
            Name = user.Name
            Url = user.AvatarUrl
            Lane = lane
            Size = 64.0
            Cache = props.Env.ImageCache
            Square = false
            Viewable = false
          },
          StackPanel()
            .VerticalAlignment(VerticalAlignment.Center)
            .Spacing(3.0)
            .Children(
              TextBlock().Text(user.Name).Classes(Theme.name Theme.Headline),
              ProfileCard.laneHandle(lane, user.Handle),
              TextBlock()
                .Text(
                  [ profile.Location; Some $"joined {profile.CreatedAt.Year}" ]
                  |> List.choose id
                  |> String.concat " · "
                )
                .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted),
              TextBlock()
                .Text(profile.Bio |> Option.defaultValue "")
                .Classes(Theme.name Theme.Muted)
                .TextWrapping(TextWrapping.Wrap)
                .IsVisible(profile.Bio.IsSome)
            )
        )

    let follow: Control =
      if user.Handle = props.Session.Session.User.Handle then
        Panel() :> Control
      else
        Button()
          .Name("RepoFollow")
          .Classes(Theme.name Theme.NavButton)
          .Content(if details.IsFollowing then "Unfollow" else "Follow")
          .IsEnabled(not model.FollowBusy)
          .OnClickHandler(fun _ _ -> dispatch FollowToggled)
        :> Control

    let top = Grid()
    top.ColumnDefinitions <- ColumnDefinitions("*,Auto")
    top.Children(identity.Column(0), follow.Column(1)) |> ignore

    let counts =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(16.0)
        .Children(
          statPair(profile.CommitsCount |> Option.defaultValue 0, "commits"),
          Button()
            .Name("RepoFollowers")
            .Classes(Theme.name Theme.GhostButton)
            .Padding(Thickness(0.0))
            .Content(
              statPair(
                profile.FollowersCount |> Option.defaultValue 0,
                "followers"
              )
            )
            .OnClickHandler(fun _ _ -> dispatch(ListToggled Followers)),
          Button()
            .Name("RepoFollowing")
            .Classes(Theme.name Theme.GhostButton)
            .Padding(Thickness(0.0))
            .Content(
              statPair(
                profile.FollowingCount |> Option.defaultValue 0,
                "following"
              )
            )
            .OnClickHandler(fun _ _ -> dispatch(ListToggled Following))
        )

    let error = PageChrome.errorPanel(Observable.single model.Error)

    StackPanel()
      .Spacing(10.0)
      .Children(top, counts, shieldsView(details), error)

let bodyView(details: ProfileDetails, rowProps: CommitCard.RowProps) : Control =
  StackPanel()
    .Spacing(12.0)
    .Children(
      readmeView(details, rowProps),
      match details.Activity with
      | Some activity -> activityView(activity, details.Profile.Lane)
      | None -> Panel()
    )

let listView
  (props: Props<'env>, list: ListState, dispatch: Dispatch<Msg>)
  : Control =
  match list.Kind with
  | None -> Panel()
  | Some kind ->
    let label =
      match kind with
      | Followers -> "FOLLOWERS"
      | Following -> "FOLLOWING"

    let rows: Control array = [|
      for user in list.Items do
        let row =
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(
              Avatar.view {
                Name = user.Name
                Url = user.AvatarUrl
                Lane = user.Lane
                Size = 28.0
                Cache = props.Env.ImageCache
                Square = false
                Viewable = false
              },
              StackPanel()
                .VerticalAlignment(VerticalAlignment.Center)
                .Spacing(1.0)
                .Children(
                  TextBlock().Text(user.Name).Classes(Theme.name Theme.Subtitle),
                  ProfileCard.laneHandle(user.Lane, user.Handle)
                )
            )

        yield
          Button()
            .Name($"RepoList-{user.Handle}")
            .Classes(Theme.name Theme.NavItem)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Content(row)
            .OnClickHandler(fun _ _ -> props.OnOpenRepo user.Handle)
    |]

    let more =
      PageChrome.loadMore(
        Observable.single list.Cursor.IsSome,
        fun () -> dispatch MoreListRequested
      )

    more.Name <- "RepoListMore"

    let loading =
      TextBlock()
        .Text("Reading the list…")
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
        .IsVisible(list.Loading && List.isEmpty list.Items)

    let error = PageChrome.errorPanel(Observable.single list.Error)

    StackPanel()
      .Spacing(6.0)
      .Margin(0.0, 4.0, 0.0, 4.0)
      .Children(
        DockPanel()
          .LastChildFill(false)
          .Children(
            TextBlock()
              .Text(label)
              .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted),
            Button()
              .Name("RepoListClose")
              .Classes(Theme.name Theme.GhostButton)
              .Padding(Thickness(4.0, 0.0))
              .Content(TextBlock().Text("×"))
              .OnClickHandler(fun _ _ -> dispatch(ListToggled kind))
              .DockRight()
          ),
        loading,
        error,
        StackPanel().Spacing(2.0).Children(rows),
        more
      )

let commitsView(model: Model, rowProps: CommitCard.RowProps) : Control =
  let rows: Control array = [|
    for node in model.Commits do
      CommitCard.feedRow rowProps node
  |]

  let empty =
    TextBlock()
      .Text(
        if model.CommitsLoading then
          "Reading commits…"
        else
          "No commits yet."
      )
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .IsVisible(List.isEmpty model.Commits)

  let error = PageChrome.errorPanel(Observable.single model.CommitsError)

  StackPanel()
    .Spacing(8.0)
    .Children(empty, error, StackPanel().Spacing(8.0).Children(rows))

let view
  (props: Props<'env>, model: IObservable<Model>, dispatch: Dispatch<Msg>)
  : Control =
  let rowProps: CommitCard.RowProps = {
    OnEvent =
      function
      | Opened commit -> props.OnOpenCommit commit
      | _ -> ()
    OnOpenExternal = ignore
    ViewerId = props.Session.Session.User.Id
    Pasteis = Observable.single props.Session.Session.PasteisMode
    Now = model |> Observable.map(fun current -> current.Now)
    PollChoices = Observable.single(Map.empty<string, string>)
    Dense = false
    Images = props.Env.ImageCache
    Flyout = ValueNone
    Pin = ValueNone
    Star = ValueNone
    Stars = ValueNone
    Vote = ValueNone
    Pastel = ValueNone
    Menu = ValueNone
    Translation = Observable.single(fun _ -> ValueNone)
    Zone = props.Zone
  }

  let headerHost =
    let header =
      PageChrome.rebuildOn(
        model,
        (fun current ->
          current.Details, current.FollowBusy, current.Loading, current.Error),
        fun current -> headerView(props, current, dispatch)
      )

    ContentControl().Content(header |> _.ToBinding())

  let listHost =
    let list =
      PageChrome.rebuildOn(
        model,
        (fun current -> current.List),
        fun current -> listView(props, current.List, dispatch)
      )

    ContentControl().Content(list |> _.ToBinding())

  let bodyHost =
    let body =
      PageChrome.rebuildOn(
        model,
        (fun current -> current.Details),
        fun current ->
          match current.Details with
          | Some details -> bodyView(details, rowProps)
          | None -> Panel()
      )

    ContentControl().Content(body |> _.ToBinding())

  let commitsHost =
    let commits =
      PageChrome.rebuildOn(
        model,
        (fun current ->
          current.Commits,
          current.CommitsCursor,
          current.CommitsLoading,
          current.CommitsError),
        fun current -> commitsView(current, rowProps)
      )

    ContentControl().Content(commits |> _.ToBinding())

  let more =
    let button =
      PageChrome.loadMore(
        model |> Observable.map(fun current -> current.CommitsCursor.IsSome),
        fun () -> dispatch MoreCommits
      )

    button.Name <- "RepoMoreCommits"
    button

  ScrollViewer()
    .Name("RepoTab")
    .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
    .Content(
      StackPanel()
        .Spacing(12.0)
        .Margin(Thickness(16.0, 12.0))
        .Children(
          headerHost,
          listHost,
          bodyHost,
          TextBlock()
            .Text($"HISTORY @{props.Handle}")
            .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted),
          commitsHost,
          more
        )
    )

let program(props: Props<'env>) : Program<unit, Model, Msg, Control> =
  Program.mkProgram
    (fun () -> init props)
    (fun msg model -> update(props, msg, model))
    (fun model dispatch -> view(props, model, dispatch))
  |> Program.withSubscription(fun model -> subscribe(props, model))

module Live =

  let count = new BehaviorSubject<int>(0)

  let started() = count.OnNext(count.Value + 1)

  let stopped() = count.OnNext(max 0 (count.Value - 1))

  let current() = count.Value

  let observe = count :> IObservable<int>

let mount(dispatcher: IDispatcher, props: Props<'env>) =
  let view, handle = DevNull.Client.Programs.runPage(dispatcher, program props)

  Live.started()

  let once = new BooleanDisposable()

  let lifetime =
    Disposable.Create(fun () ->
      if not once.IsDisposed then
        once.Dispose()
        handle.Dispose()
        Live.stopped())

  view, lifetime
