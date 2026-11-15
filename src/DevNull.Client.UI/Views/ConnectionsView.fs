module DevNull.Client.Views.ConnectionsView

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
open DevNull.Client.Components.CommitActions
open DevNull.Client.Components.SearchParts
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Env
open Microsoft.Extensions.Logging
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Navigation
open DevNull.Client.Profiles
open DevNull.Client.Stores

/// Which list the page shows; each route names its own.
type Kind =
  | Followers
  | Following

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging> = {
  Env: 'env
  Username: string
  Kind: Kind
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

// The page's state logic: model, messages, update, and the
// subscriptions that feed it from outside.
module State =

  // One row of the list: the organizations section above the people
  // it leads to, the people themselves, and the ghost row that pulls
  // the next organizations page in place.
  type FeedRow =
    | OrgCaption
    | OrgNode of FollowedOrganization
    | OrgMore
    | PeopleCaption
    | UserNode of User

  type Model = {
    Loading: bool
    Error: AppError option
    Owner: User option
    People: User list
    NextCursor: string option
    Organizations: FollowedOrganization list
    OrgCursor: string option
    OrgsBusy: bool
    // The handle whose follow control is in flight, so its row stays
    // armed until the server answers.
    FollowBusy: string option
  }

  type Msg =
    | Fetched of Result<ProfileDetails, AppError>
    | PeopleFetched of Result<Page<User>, AppError>
    | PeopleAppended of Result<Page<User>, AppError>
    | MorePeople
    | OrgsFetched of Result<Page<FollowedOrganization>, AppError>
    | OrgsAppended of Result<Page<FollowedOrganization>, AppError>
    | MoreOrganizations
    | FollowToggled of User
    | FollowDone of User * following: bool * Result<unit, AppError>

  let initial = {
    Loading = true
    Error = None
    Owner = None
    People = []
    NextCursor = None
    Organizations = []
    OrgCursor = None
    OrgsBusy = false
    FollowBusy = None
  }

  let fetchPeople(env, kind: Kind, request: Users.ListRequest) =
    match kind with
    | Followers -> Users.followers(env, request)
    | Following -> Users.following(env, request)

  let listRequest
    (session: SignedInSession, username: string, cursor: string option)
    : Users.ListRequest = {
    KeyFile = session.KeyFile
    Username = username
    Cursor = cursor
  }

  let init (props: Props<'env>) () =
    let request = listRequest(props.Session, props.Username, None)

    let orgs =
      match props.Kind with
      | Followers -> Cmd.none
      | Following ->
        Cmd.OfAsync.perform
          Users.followedOrganizations
          (props.Env, request)
          OrgsFetched

    { initial with Loading = true },
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
        fetchPeople
        (props.Env, props.Kind, request)
        PeopleFetched

      orgs
    ]

  let loggerOf(props: Props<'env>) =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Connections"

  // The key of one feed row: the captions keep their own, so a
  // section head stays put while rows land under it.
  let inline rowKey(row: FeedRow) : string =
    match row with
    | OrgCaption -> "orgs"
    | OrgNode org -> "org:" + org.Handle
    | OrgMore -> "orgs-more"
    | PeopleCaption -> "people"
    | UserNode user -> "user:" + user.Id

  // The rows one list renders: the organizations section and its
  // show-more row first, then the people heading above the people
  // when the section stands.
  let rowsOf(model: Model) : FeedRow list = [
    if model.Organizations.IsEmpty then
      for user in model.People do
        UserNode user
    else
      OrgCaption

      for org in model.Organizations do
        OrgNode org

      if model.OrgCursor.IsSome then
        OrgMore

      if not model.People.IsEmpty then
        PeopleCaption

        for user in model.People do
          UserNode user
  ]

  let updateCore (log: ILogger, props: Props<'env>) msg model =
    // Every log line is an effect: update returns it as a command,
    // and the loop writes it after the computation.
    let failure = CommitActions.failure log

    let failed what error =
      Cmd.ofAction(fun () -> failure what error)

    match msg with
    | Fetched result ->
      // The header's display name rides this read; a failed read
      // leaves the page at the handle and never errors the list.
      match result with
      | Ok details ->
        {
          model with
              Owner = Some details.Profile.User
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The owner @{User} of the {Kind} list arrived.",
            props.Username,
            props.Kind
          ))
      | Error error -> model, failed $"the profile of {props.Username}" error
    | PeopleFetched result ->
      match result with
      | Ok page ->
        {
          model with
              Loading = false
              Error = None
              People = page.Items
              NextCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The {Kind} list of @{User} arrived ({Count}).",
            props.Kind,
            props.Username,
            page.Items.Length
          ))
      | Error error ->
        {
          model with
              Loading = false
              Error = Some error
        },
        failed $"the {props.Kind} list of {props.Username}" error
    | MorePeople ->
      model,
      Cmd.OfAsync.perform
        fetchPeople
        (props.Env,
         props.Kind,
         listRequest(props.Session, props.Username, model.NextCursor))
        PeopleAppended
    | PeopleAppended result ->
      match result with
      | Ok page ->
        {
          model with
              People = model.People @ page.Items
              NextCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The {Kind} list of @{User} grew by {Count}.",
            props.Kind,
            props.Username,
            page.Items.Length
          ))
      | Error error ->
        { model with Error = Some error },
        failed $"the next {props.Kind} page of {props.Username}" error
    | OrgsFetched result ->
      match result with
      | Ok page ->
        {
          model with
              Organizations = page.Items
              OrgCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "{Count} followed organizations arrived for @{User}.",
            page.Items.Length,
            props.Username
          ))
      | Error error ->
        model, failed $"the followed organizations of {props.Username}" error
    | MoreOrganizations when model.OrgCursor.IsSome && not model.OrgsBusy ->
      { model with OrgsBusy = true },
      Cmd.OfAsync.perform
        Users.followedOrganizations
        (props.Env, listRequest(props.Session, props.Username, model.OrgCursor))
        OrgsAppended
    | MoreOrganizations -> model, Cmd.none
    | OrgsAppended result ->
      match result with
      | Ok page ->
        {
          model with
              OrgsBusy = false
              Organizations = model.Organizations @ page.Items
              OrgCursor = page.NextCursor
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The followed organizations of @{User} grew by {Count}.",
            props.Username,
            page.Items.Length
          ))
      | Error error ->
        {
          model with
              OrgsBusy = false
              Error = Some error
        },
        failed $"the next followed organizations of {props.Username}" error
    | FollowToggled user ->
      if model.FollowBusy = Some user.Handle then
        model, Cmd.none
      else
        let following = user.IsFollowed = Some true

        let toggle() =
          let request: Users.Request = {
            KeyFile = props.Session.KeyFile
            Username = user.Handle
          }

          if following then
            Users.unfollow(props.Env, request)
          else
            Users.follow(props.Env, request)

        {
          model with
              FollowBusy = Some user.Handle
              Error = None
        },
        Cmd.OfAsync.perform toggle () (fun result ->
          FollowDone(user, not following, result))
    | FollowDone(user, following, result) ->
      match result with
      | Ok() ->
        // The row's own flag moves from the answer; the rest of the
        // list keeps its place.
        let followed(rows: User list) =
          rows
          |> List.map(fun current ->
            if current.Id = user.Id then
              {
                current with
                    IsFollowed = Some following
              }
            else
              current)

        {
          model with
              FollowBusy = None
              People = followed model.People
        },
        Cmd.ofAction(fun () ->
          log.LogInformation(
            "The follow of @{User} {State}.",
            user.Handle,
            (if following then "landed" else "was taken back")
          ))
      | Error error ->
        {
          model with
              FollowBusy = None
              Error = Some error
        },
        failed $"the follow of @{user.Handle}" error

  let update(props: Props<'env>) = updateCore(loggerOf props, props)

  let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> = Sub.none

// The page's semantic regions; the view only composes these.
module Parts =

  /// What the rows read: the search-row pieces they share, the
  /// viewer's id, whether the site's Follows-you chip shows, the
  /// follow controls' in-flight slice, the dispatch, and the page's
  /// opener.
  [<Struct>]
  type RowEnv = {
    Ctx: RowCtx
    ViewerId: string
    /// Whether the site's Follows-you chip shows on the rows: the
    /// following lists carry it always, a followers list only when it
    /// belongs to another user.
    Chip: bool
    Busy: IObservable<string option>
    Dispatch: Dispatch<State.Msg>
    Open: string -> unit
    Browser: IBrowser
  }

  // The header row: the back affordance, the owner's name once the
  // read lands, and the handle with its lane dot.
  let header(props: Props<'env>, owner: User option) : Control =
    let back =
      Button()
        .Name("ConnectionsBack")
        .Classes(Theme.name Theme.GhostButton)
        .Content(Icons.iconAt(Glyph.ChevronLeft, 16.0))
        .OnClickHandler(fun _ _ ->
          Navigation.go(props.Nav, "/profile/" + props.Username, ignore))

    ToolTip.SetTip(back, $"Back to @{props.Username}")

    let named =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .VerticalAlignment(VerticalAlignment.Center)
            .Text(owner |> Option.map _.Name |> Option.defaultValue "")
            .IsVisible(owner.IsSome),
          ProfileCard.laneHandle(
            owner |> Option.bind _.Lane,
            owner |> Option.map _.Handle |> Option.defaultValue props.Username
          )
        )

    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(8.0)
      .Children(back, named)

  // The header host, rebuilt when the owner arrives: the display name
  // and the lane come with the read.
  let headerHost
    (props: Props<'env>, model: IObservable<State.Model>)
    : Control =
    let content =
      PageChrome.rebuildOn(
        model,
        (fun current -> current.Owner),
        fun current -> header(props, current.Owner)
      )

    ContentControl().Content(content.ToBinding())

  // One small label chip, the site's Follows-you mark.
  let chip(text: string) : Control =
    Border()
      .Classes(Theme.name Theme.Chip)
      .CornerRadius(999.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Child(
        TextBlock().Classes(Theme.name Theme.Muted).FontSize(11.0).Text(text)
      )

  // The follow control's word: Follow back when the row's person
  // follows the viewer and the viewer does not follow them yet, Follow
  // otherwise, and the site's Unfollow once followed.
  let inline followWord(user: User, following: bool) : string =
    if following then "Unfollow"
    elif user.FollowsYou = Some true then "Follow back"
    else "Follow"

  // The identity line: the name beside the site's lane handle — the
  // pinned header's "● @handle" form with its gap — with the star
  // column keeping the pair packed left.
  let identityLine(name: string, handle: string, lane: Lane option) : Control =
    let grid = Grid()

    grid.ColumnDefinitions <- ColumnDefinitions("Auto,Auto,*")

    grid.Children(
      TextBlock()
        .Classes(Theme.name Theme.Subtitle)
        .VerticalAlignment(VerticalAlignment.Center)
        .MaxLines(1)
        .Text(name)
        .Column(0),
      ProfileCard.laneHandle(lane, handle).Column(1).Margin(6.0, 0.0, 0.0, 0.0)
    )

  // The bio line, capped at two lines: the only context a row gives
  // for a person the viewer may not know.
  let bioLine(text: string option) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .FontSize(13.0)
      .MaxLines(2)
      .TextWrapping(TextWrapping.Wrap)
      .TextTrimming(TextTrimming.CharacterEllipsis)
      .Text(text |> Option.defaultValue "")
      .IsVisible(
        text |> Option.map(fun bio -> bio <> "") |> Option.defaultValue false
      )

  // The row's shared frame, the web row's flex form: the avatar in
  // its own left lane, the text beside it, and the trailing controls
  // at the right edge, vertically centered. Person and org rows share
  // it, so every avatar sits at one x.
  let rowFrame
    (avatar: Control, content: Control, trailing: Control voption)
    : Control =
    let trailingColumn: Control =
      match trailing with
      | ValueSome control -> control
      | ValueNone -> Panel()

    let grid = Grid()

    grid.ColumnDefinitions <- ColumnDefinitions("Auto,*,Auto")

    grid.Children(
      avatar.Column(0).VerticalAlignment(VerticalAlignment.Top),
      content.Column(1).Margin(8.0, 0.0, 8.0, 0.0),
      trailingColumn.Column(2).VerticalAlignment(VerticalAlignment.Center)
    )

  // The 40 DIP avatar a person row leads with, the hover card riding
  // it.
  let userAvatar(ctx: RowCtx, user: User) : Control =
    let avatar =
      Avatar.view {
        Name = user.Name
        Url = user.AvatarUrl
        Lane = user.Lane
        Size = 40.0
        Cache = ctx.Images
        Square = false
        Viewable = false
      }

    match ctx.Flyout with
    | ValueSome source -> UserFlyout.attach(source, user, avatar)
    | ValueNone -> avatar

  // The text block beside the avatar: the identity line over the
  // two-line bio. A live user's name wears the badge.
  let userText(browser: IBrowser, user: User) : Control =
    let grid = Grid()

    grid.ColumnDefinitions <- ColumnDefinitions("Auto,Auto,Auto,*")

    let openExternal(url: string) = browser.Open(url) |> ignore

    grid.Children(
      TextBlock()
        .Classes(Theme.name Theme.Subtitle)
        .VerticalAlignment(VerticalAlignment.Center)
        .MaxLines(1)
        .Text(user.Name)
        .Column(0),
      ProfileCard
        .laneHandle(user.Lane, user.Handle)
        .Column(1)
        .Margin(6.0, 0.0, 0.0, 0.0),
      (LiveBadge.view(openExternal, user.Live))
        .Column(2)
        .Margin(6.0, 0.0, 0.0, 0.0)
        .VerticalAlignment(VerticalAlignment.Center)
    )
    |> ignore

    StackPanel().Spacing(2.0).Children(grid, bioLine user.Bio)

  // The organization row: the avatar in the shared left lane, the
  // text beside it, and the whole row opening the org page. No follow
  // control rides it this round.
  let orgRow(env: RowEnv, org: FollowedOrganization) : Control =
    let content =
      Button()
        .Name("OpenOrgConnection:" + org.Handle)
        .Classes(Theme.name Theme.GhostButton)
        .Padding(0.0)
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .HorizontalContentAlignment(HorizontalAlignment.Left)
        .Content(
          StackPanel()
            .Spacing(2.0)
            .Children(
              identityLine(org.Name, org.Handle, org.Lane),
              bioLine org.Bio
            )
        )
        .OnClickHandler(fun _ _ ->
          env.Open("/org/" + Uri.EscapeDataString org.Handle))

    rowFrame(
      Avatar.view {
        Name = org.Name
        Url = org.AvatarUrl
        Lane = org.Lane
        Size = 40.0
        Cache = env.Ctx.Images
        Square = false
        Viewable = false
      },
      content,
      ValueNone
    )

  // The ghost row that pulls the next organizations page in place.
  let orgMore
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    Button()
      .Name("ShowMoreOrganizations")
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Margin(0.0, 8.0, 0.0, 8.0)
      .Content("Show more organizations")
      .IsEnabled(model |> Observable.map(fun current -> not current.OrgsBusy))
      .OnClickHandler(fun _ _ -> dispatch State.MoreOrganizations)

  // One person as a row: the 40 DIP avatar in the left lane, the text
  // beside it, and the follow control with its Follows-you chip at
  // the right. The viewer's own row carries no control.
  let personRow(env: RowEnv, user: User) : Control =
    let content =
      Button()
        .Name("OpenConnection:" + user.Handle)
        .Classes(Theme.name Theme.GhostButton)
        .Padding(0.0)
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .HorizontalContentAlignment(HorizontalAlignment.Left)
        .Content(userText(env.Browser, user))
        .OnClickHandler(fun _ _ ->
          env.Open("/profile/" + Uri.EscapeDataString user.Handle))

    let busy =
      env.Busy |> Observable.map(fun current -> current = Some user.Handle)

    let follow =
      Button()
        .Name("FollowConnection:" + user.Handle)
        .Classes(Theme.name Theme.NavButton)
        .Content(followWord(user, user.IsFollowed = Some true))
        .IsEnabled(busy |> Observable.map not)
        .OnClickHandler(fun _ _ -> env.Dispatch(State.FollowToggled user))

    let trailingItems: Control array = [|
      if env.Chip && user.FollowsYou = Some true then
        chip "Follows you"

      if user.Id <> env.ViewerId then
        follow
    |]

    let trailing: Control voption =
      if Array.isEmpty trailingItems then
        ValueNone
      else
        ValueSome(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(trailingItems)
        )

    rowFrame(userAvatar(env.Ctx, user), content, trailing)
      .Name("ConnectionRow:" + user.Handle)

// The words the empty list says, the site's own sentences.
let inline emptyWords(kind: Kind, username: string) : string =
  match kind with
  | Followers -> $"Nobody is following @{username} yet."
  | Following -> $"@{username} isn't following anyone yet."

// The path of one of the page's two routes.
let inline pathOf(kind: Kind, username: string) : string =
  match kind with
  | Followers -> "/profile/" + username + "/followers"
  | Following -> "/profile/" + username + "/following"

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let source: UserFlyout.Source = {
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
        Navigation.go(props.Nav, "/org/" + Uri.EscapeDataString handle, ignore)
    OnOpenTag =
      fun tag ->
        Navigation.go(props.Nav, "/search/" + Uri.EscapeDataString tag, ignore)
    Cache = props.Env.ImageCache
  }

  // The Follows-you chip follows the site's rule: the following lists
  // carry it always, a followers list only when it belongs to another
  // user — the viewer's own followers all follow the viewer, so the
  // mark would say nothing there.
  let rowEnv: Parts.RowEnv = {
    Ctx = {
      Images = props.Env.ImageCache
      Flyout = ValueSome source
      Now = DateTimeOffset.UtcNow
    }
    ViewerId = props.Session.Session.User.Id
    Chip =
      props.Kind = Following
      || props.Username <> props.Session.Session.User.Handle
    Busy = model |> Observable.map _.FollowBusy
    Dispatch = dispatch
    Open = fun url -> Navigation.go(props.Nav, url, ignore)
    Browser = props.Env.Browser
  }

  let isFollowers = model |> Observable.map(fun _ -> props.Kind = Followers)

  // The empty list's words live in the list area, not the page's
  // pinned empty slot: they sit under the tabs, centered there, the
  // way the web centers them in the feed body.
  let emptyLine =
    model
    |> Observable.map(fun current ->
      if
        not current.Loading
        && current.Error.IsNone
        && current.People.IsEmpty
        && (props.Kind = Followers || current.Organizations.IsEmpty)
      then
        Some(emptyWords(props.Kind, props.Username))
      else
        None)

  let choose(kind: Kind) : unit -> unit =
    fun () ->
      if props.Kind <> kind then
        Navigation.go(props.Nav, pathOf(kind, props.Username), ignore)

  let row =
    function
    | State.OrgCaption -> SearchParts.heading "Organizations"
    | State.OrgNode org -> Parts.orgRow(rowEnv, org)
    | State.OrgMore -> Parts.orgMore(model, dispatch)
    | State.PeopleCaption -> SearchParts.heading "People"
    | State.UserNode user -> Parts.personRow(rowEnv, user)

  ProfileShell.view {
    Model = model
    Title = fun _ -> ""
    Kind = None
    Padding = Theme.pinnedPadding
    ErrorOf = _.Error
    Busy =
      fun current ->
        if current.Loading then
          PageChrome.Words "Loading the list…"
        else
          PageChrome.Off
    Empty = fun _ -> PageChrome.Off
    Note = fun _ -> None
    Header = fun () -> Parts.headerHost(props, model)
    Meta = ValueNone
    FeedLabel = None
    Tabs = [
      {
        Label = "Followers"
        Active = isFollowers
        Badge = ValueNone
        Choose = choose Followers
      }
      {
        Label = "Following"
        Active = isFollowers |> Observable.map not
        Badge = ValueNone
        Choose = choose Following
      }
    ]
    Feed = {
      Items = model |> Observable.map State.rowsOf
      KeyOf = State.rowKey
      Row = row
      HasMore = model |> Observable.map _.NextCursor.IsSome
      OnMore = fun () -> dispatch State.MorePeople
      OnRide = ValueNone
      End = emptyLine
    }
  }

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
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
