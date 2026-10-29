module DevNull.Client.Views.ProfileView

open System
open System.Globalization
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Media
open Avalonia.Controls.Templates
open Avalonia.Layout
open NXUI.Extensions
open IcedTasks
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open Navs
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Env
open Microsoft.Extensions.Logging
open DevNull.Client.ImageCache
open DevNull.Client.Navigation
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.UiClock

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging> = {
  Env: 'env
  Username: string
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

/// The page's state logic: model, messages, update, and the
/// subscriptions that feed it from outside.
module State =

  /// The feed tab the page shows: the user's own commits or their
  /// replies.
  type Tab =
    | Commits
    | Replies

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
  }

  type Msg =
    | Fetched of Result<ProfileDetails, AppError>
    | CommitsFetched of Result<Page<CommitNode>, AppError>
    | MoreCommits
    | TabChosen of Tab
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
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState

  let replaceNode(items: CommitNode list, updated: Commit) : CommitNode list =
    items
    |> List.map(function
      | LiveCommit commit when commit.Id = updated.Id -> LiveCommit updated
      | node -> node)

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
  }

  /// One feed read: whose commits, which tab, and the pager cursor.
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

  let update (props: Props<'env>) msg model =
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

    let log =
      props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Profile"

    // Every log line is an effect: update returns it as a command,
    // and the loop writes it after the computation.
    let failed what error =
      Cmd.ofAction(fun () ->
        log.LogWarning(
          "The {What} failed: {Error}",
          what,
          bounded 300 (describe error)
        ))

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
        let vote() =
          Commits.vote(
            props.Env,
            {
              KeyFile = props.Session.KeyFile
              CommitId = commit.Id
              OptionId = option.Id
            }
          )

        {
          model with
              Error = None
              Voting = Some commit.Id
        },
        Cmd.OfAsync.perform vote () PollVotedDone
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
      let give() =
        Pasteis.give(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            CommitId = commit.Id
          }
        )

      { model with Error = None }, Cmd.OfAsync.perform give () PastelDone
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
      let pasteis =
        match state with
        | SignedIn entry -> entry.Session.PasteisMode
        | SignedOut -> model.Pasteis

      { model with Pasteis = pasteis }, Cmd.none

  /// The page's inlets from outside: the shared clock and the session
  /// store.
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

/// The page's semantic regions; the view only composes these.
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

  /// The follow, mute, and block actions; the viewer's own header
  /// carries none.
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

  /// One small label chip, the site's "Follows you" and "Muted" marks.
  let mark(text: string) : Control =
    Border()
      .Classes(Theme.name Theme.Chip)
      .CornerRadius(999.0)
      .Child(
        TextBlock().Classes(Theme.name Theme.Muted).FontSize(11.0).Text(text)
      )

  /// The lane-tinted level chip the profile and the XP shelf carry.
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
        .Child(label)

    Theme.laneBorder(chip, lane)

    chip

  /// The joined-and-links line under the bio: company, location, the
  /// website host, and the join month.
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

  /// The activity bars: one lane-colored column per bucket, bottom
  /// aligned, the height following the count.
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

  /// The streak line under the activity.
  let streaksView(streaks: Streaks) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .Text(
        $"longest streak {streaks.Longest} days · current streak {streaks.Current} days"
      )

  /// The XP shelf: the level chip, the progress bar toward the next
  /// level, and the badge count.
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

  /// The level chip when achievements exist, nothing when they do not.
  let achievementsChip
    (achievements: Achievements option, lane: Lane option)
    : Control =
    match achievements with
    | Some shelf -> levelChip(shelf, lane)
    | None -> Status.empty ""

  /// The sections between the identity header and the tabs: the
  /// activity bars, the streak line, and the XP shelf.
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

  /// What the identity header reads: the profile, the viewer's
  /// relation to it, the dispatch, and the image cache.
  [<Struct>]
  type IdentityCtx = {
    Images: ImageCacheService
    Details: ProfileDetails
    IsSelf: bool
    Dispatch: Dispatch<State.Msg>
  }

  /// The identity header: the avatar in its lane ring, the actions, the
  /// name, the handle row, the bio, the meta line, and the stats.
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
      ProfileCard.stats profile
    |]

    StackPanel().Spacing(8.0).Children(headerChildren)

  /// The header host, rebuilt when the profile changes: the follow,
  /// mute, and block states come back from the server after every
  /// action.
  let headerHost
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    ContentControl()
      .Content(
        model
        |> Observable.map(fun current ->
          match current.Details with
          | Some details ->
            let isSelf =
              details.Profile.User.Id = props.Session.Session.User.Id

            StackPanel()
              .Spacing(16.0)
              .Children(
                Array.append
                  [|
                    identity {
                      Images = props.Env.ImageCache
                      Details = details
                      IsSelf = isSelf
                      Dispatch = dispatch
                    }
                  |]
                  (profileSections details)
              )
            :> Control
          | None -> Status.empty "")
        |> _.ToBinding()
      )

  /// The row context every profile card shares.
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
      | CommitCard.ReplyToggled c ->
        Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
      | CommitCard.MoreRepliesRequested c ->
        Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
      | CommitCard.StarToggled c -> dispatch(State.StarToggled c)
      | CommitCard.PastelGiven c -> dispatch(State.PastelGiven c)
      | CommitCard.PollChoiceChanged(commit, option, chosen) ->
        dispatch(State.PollChoiceChanged(commit, option, chosen))
      | CommitCard.PollVoteRequested commit ->
        dispatch(State.PollVoteRequested commit)
      | CommitCard.QuoteRequested c ->
        Navigation.go(props.Nav, "/quote/" + c.Id, ignore)
    Pasteis = model |> Observable.map _.Pasteis
    ViewerId = props.Session.Session.User.Id
    Now = model |> Observable.map _.Now
    Voting = model |> Observable.map _.Voting
    PollChoices = model |> Observable.map _.PollChoices
    Dense = false
    Images = props.Env.ImageCache
    Flyout =
      ValueSome {
        UserFlyout.Fetch = props.Env.Profiles.Fetch
        OnOpen =
          fun username ->
            Navigation.go(props.Nav, "/profile/" + username, ignore)
        Cache = props.Env.ImageCache
      }
  }

  /// The commits feed under the tabs.
  let commits
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    let ctx = commitCtx(props, dispatch, model)

    Feed.view {
      Items = model |> Observable.map _.Commits
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
          PageChrome.loadMore(
            model |> Observable.map _.NextCursor.IsSome,
            fun () -> dispatch State.MoreCommits
          )
        )
      ScrollTop = ValueNone
      Restore = ValueNone
      OnScroll = ValueNone
      OnNeedMore = ValueNone
    }

  /// The Commits/Replies tab bar; the active tab carries the site's
  /// underline.
  let tabStrip
    (dispatch: Dispatch<State.Msg>, model: IObservable<State.Model>)
    : Control =
    let kinds: (State.Tab * string * int) list = [
      State.Commits, "Commits", 0
      State.Replies, "Replies", 1
    ]

    let strip = Grid()

    strip.ColumnDefinitions <- ColumnDefinitions("*,*")

    let buttons: Control array = [|
      for kind, label, index in kinds ->
        let button =
          Button()
            .Classes(Theme.name Theme.TabButton)
            .Content(label)
            .Column(index)
            .OnClickHandler(fun _ _ -> dispatch(State.TabChosen kind))

        Theme.bindClass(
          button,
          Theme.TabButtonActive,
          model |> Observable.map(fun current -> current.Tab = kind)
        )

        button
    |]

    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Child(strip.Children(buttons))

  /// The tabbed body: the strip over the feed.
  let body
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    let root = Grid()

    root.RowDefinitions <- RowDefinitions("Auto,*")

    root.Children(
      tabStrip(dispatch, model).Row(0),
      commits(props, dispatch, model).Row(1)
    )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  PageChrome.page {
    Model = model
    TitleOf = fun _ -> ""
    Padding = Theme.pinnedPadding
    ErrorOf = _.Error
    Busy = fun current -> current.Loading, "Loading the profile…"
    Empty =
      fun current ->
        not current.Loading && current.Error.IsNone && current.Commits.IsEmpty,
        (match current.Tab with
         | State.Commits -> "No commits yet."
         | State.Replies -> "No replies yet.")
    Header = [ Parts.headerHost(props, dispatch, model) ]
    Body = Parts.body(props, dispatch, model)
  }

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root
    props.Context.AddDisposable lifetime
