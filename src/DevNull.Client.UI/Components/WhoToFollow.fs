module DevNull.Client.Components.WhoToFollow

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open Microsoft.Extensions.Logging
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.RouteStore
open DevNull.Client.Stores
open DevNull.Client.Theme

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideRoutes
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  OnOpen: string -> unit
}

module State =

  /// One read of the rail: the day's round and the page it sits beside.
  [<Struct>]
  type FetchRequest = {
    KeyFile: string
    Round: int
    Context: Users.SuggestedContext
  }

  type Model = {
    Context: Users.SuggestedContext option
    Round: int
    Shown: User list
    Spares: User list
    Loading: bool
    Loaded: bool
    Busy: Set<string>
    Followed: Set<string>
    Dismissed: Set<string>
    Error: AppError option
  }

  type Msg =
    | PathChanged of path: string
    | RefreshRequested
    | Arrived of request: FetchRequest * result: Result<User list, AppError>
    | ToppedUp of request: FetchRequest * result: Result<User list, AppError>
    | DismissRequested of user: User
    | DismissDone of user: User * result: Result<unit, AppError>
    | FollowRequested of user: User
    | FollowDone of
      user: User *
      following: bool *
      result: Result<unit, AppError>

  let takeAtMost count items = List.truncate count items

  [<TailCall>]
  let rec dropAtMost count items =
    if count <= 0 then
      items
    else
      match items with
      | [] -> []
      | _ :: rest -> dropAtMost (count - 1) rest

  /// The context a route asks the suggestions for: the page's own
  /// handle or commit, else the general rail.
  let contextOf
    (session: SignedInSession, path: string)
    : Users.SuggestedContext =
    let unescape(text: string) =
      try
        Uri.UnescapeDataString text
      with :? UriFormatException ->
        text

    let path = if String.IsNullOrWhiteSpace path then "" else path

    let route = path.Split('?') |> Array.tryHead |> Option.defaultValue ""

    let segments =
      route.Split('/', StringSplitOptions.RemoveEmptyEntries)
      |> Array.map unescape

    match segments with
    | [| "profile" |] -> Users.Profile session.Session.User.Handle
    | [| "profile"; handle |] when handle <> "" -> Users.Profile handle
    | [| "commit"; commitId |] when commitId <> "" ->
      Users.CommitThread commitId
    | [| "quote"; commitId |] when commitId <> "" -> Users.CommitThread commitId
    | _ -> Users.General

  let fetch(env, request: FetchRequest) =
    Users.suggested(
      env,
      {
        KeyFile = request.KeyFile
        Round = request.Round
        Context = request.Context
      }
    )

  let init (props: Props<'env>) () =
    let path = props.Env.Routes.Current

    let context =
      if String.IsNullOrWhiteSpace path then
        None
      else
        Some(contextOf(props.Session, path))

    let model = {
      Context = context
      Round = 0
      Shown = []
      Spares = []
      Loading = context.IsSome
      Loaded = false
      Busy = Set.empty
      Followed = Set.empty
      Dismissed = Set.empty
      Error = None
    }

    match context with
    | None -> model, Cmd.none
    | Some context ->
      let answer: FetchRequest = {
        KeyFile = props.Session.KeyFile
        Round = 0
        Context = context
      }

      model,
      Cmd.OfAsync.perform fetch (props.Env, answer) (fun result ->
        Arrived(answer, result))

  let update (props: Props<'env>) (msg: Msg) (model: Model) =
    let log =
      props.Env.LoggerFactory.CreateLogger
        "DevNull.Client.Components.WhoToFollow"

    let warning what error =
      Cmd.ofAction(fun () ->
        log.LogWarning(
          "The {What} failed: {Error}",
          what,
          bounded 300 (describe error)
        ))

    let request(context: Users.SuggestedContext, round: int) : FetchRequest = {
      KeyFile = props.Session.KeyFile
      Round = round
      Context = context
    }

    let withoutDismissed
      (result: Result<User list, AppError>, dismissed: Set<string>)
      =
      result
      |> Result.map(fun users ->
        users |> List.filter(fun user -> not(dismissed.Contains user.Id)))

    match msg with
    | PathChanged path ->
      let context = contextOf(props.Session, path)

      if model.Context = Some context then
        model, Cmd.none
      else
        let answer = request(context, model.Round)

        {
          model with
              Context = Some context
              Loading = true
              Loaded = false
              Error = None
              Shown = []
              Spares = []
              Followed = Set.empty
        },
        Cmd.OfAsync.perform fetch (props.Env, answer) (fun result ->
          Arrived(answer, result))
    | RefreshRequested ->
      if model.Loading || model.Shown.IsEmpty then
        model, Cmd.none
      else
        let round = model.Round + 1

        let answer =
          request(model.Context |> Option.defaultValue Users.General, round)

        {
          model with
              Round = round
              Loading = true
              Error = None
        },
        Cmd.OfAsync.perform fetch (props.Env, answer) (fun result ->
          Arrived(answer, result))
    | Arrived(answer, result) ->
      if
        model.Context <> Some answer.Context || model.Round <> answer.Round
      then
        model, Cmd.none
      else
        match withoutDismissed(result, model.Dismissed) with
        | Ok users ->
          let shown = takeAtMost 3 users
          let spares = users |> dropAtMost 3 |> takeAtMost 3

          {
            model with
                Shown = shown
                Spares = spares
                Loading = false
                Loaded = true
                Followed = Set.empty
                Error = None
          },
          Cmd.ofAction(fun () ->
            log.LogDebug(
              "Who to follow round {Round} arrived with {Count} people.",
              answer.Round,
              users.Length
            ))
        | Error error ->
          {
            model with
                Loading = false
                Loaded = true
                Error = Some error
          },
          warning "who to follow" error
    | ToppedUp(answer, result) ->
      if
        model.Context <> Some answer.Context || model.Round <> answer.Round
      then
        model, Cmd.none
      else
        match withoutDismissed(result, model.Dismissed) with
        | Ok users ->
          let seen = model.Shown @ model.Spares |> List.map _.Id |> Set.ofList

          let fresh =
            users |> List.filter(fun user -> not(seen.Contains user.Id))

          let missingShown = max 0 (3 - model.Shown.Length)
          let promoted = takeAtMost missingShown fresh
          let rest = dropAtMost missingShown fresh
          let missingSpares = max 0 (3 - model.Spares.Length)

          {
            model with
                Shown = model.Shown @ promoted
                Spares = model.Spares @ takeAtMost missingSpares rest
                Loading = false
                Loaded = true
                Error = None
          },
          Cmd.ofAction(fun () ->
            log.LogDebug(
              "Who to follow topped up round {Round} with {Count} people.",
              answer.Round,
              users.Length
            ))
        | Error error ->
          {
            model with
                Loading = false
                Loaded = true
                Error = Some error
          },
          warning "the who to follow top-up" error
    | DismissRequested user ->
      if model.Busy.Contains user.Id then
        model, Cmd.none
      else
        let answer: Users.Request = {
          KeyFile = props.Session.KeyFile
          Username = user.Handle
        }

        {
          model with
              Busy = Set.add user.Id model.Busy
              Error = None
        },
        Cmd.OfAsync.perform Users.dismiss (props.Env, answer) (fun result ->
          DismissDone(user, result))
    | DismissDone(user, result) ->
      match result with
      | Ok _ ->
        let shown =
          model.Shown |> List.filter(fun current -> current.Id <> user.Id)

        let spares =
          model.Spares |> List.filter(fun current -> current.Id <> user.Id)

        let missing = max 0 (3 - shown.Length)
        let promoted = takeAtMost missing spares
        let rest = dropAtMost missing spares

        let next = {
          model with
              Shown = shown @ promoted
              Spares = rest
              Busy = Set.remove user.Id model.Busy
              Dismissed = Set.add user.Id model.Dismissed
              Error = None
        }

        let noted =
          Cmd.ofAction(fun () ->
            log.LogInformation(
              "Dismissed {User} from who to follow.",
              user.Handle
            ))

        if next.Shown.Length < 3 then
          let answer =
            request(
              next.Context |> Option.defaultValue Users.General,
              next.Round
            )

          { next with Loading = true },
          Cmd.batch [
            noted
            Cmd.OfAsync.perform fetch (props.Env, answer) (fun result ->
              ToppedUp(answer, result))
          ]
        else
          next, noted
      | Error error ->
        {
          model with
              Busy = Set.remove user.Id model.Busy
              Error = Some error
        },
        warning $"the dismissal of @{user.Handle}" error
    | FollowRequested user ->
      if model.Busy.Contains user.Id then
        model, Cmd.none
      else
        let following = model.Followed.Contains user.Id

        let answer: Users.Request = {
          KeyFile = props.Session.KeyFile
          Username = user.Handle
        }

        let call = if following then Users.unfollow else Users.follow

        {
          model with
              Busy = Set.add user.Id model.Busy
              Error = None
        },
        Cmd.OfAsync.perform call (props.Env, answer) (fun result ->
          FollowDone(user, not following, result))
    | FollowDone(user, following, result) ->
      match result with
      | Ok _ ->
        {
          model with
              Busy = Set.remove user.Id model.Busy
              Followed =
                if following then
                  Set.add user.Id model.Followed
                else
                  Set.remove user.Id model.Followed
              Error = None
        },
        Cmd.ofAction(fun () ->
          if following then
            log.LogInformation(
              "Followed {User} from who to follow.",
              user.Handle
            )
          else
            log.LogInformation(
              "Unfollowed {User} from who to follow.",
              user.Handle
            ))
      | Error error ->
        {
          model with
              Busy = Set.remove user.Id model.Busy
              Error = Some error
        },
        warning $"the follow of @{user.Handle}" error

  let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
    props.Env.Routes.Path
    |> Observable.filter(fun path -> not(String.IsNullOrWhiteSpace path))
    |> Observable.distinctUntilChanged
    |> Observable.map PathChanged
    |> Sub.fromObservable "path"

/// The reason line the web puts under a suggestion: the people the
/// viewer's follows know, then the follower count.
let reasonOf(user: User, exact: bool) : string option =
  let countText(count: int) =
    if exact then string count else Formatting.shortCount count

  match user.FollowedByFollowingCount with
  | Some count when count > 0 ->
    Some $"Followed by {countText count} you follow"
  | _ ->
    match user.FollowersCount with
    | Some count when count > 0 ->
      Some(
        if count = 1 then
          "1 follower"
        else
          $"{countText count} followers"
      )
    | _ -> None

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let flyout: UserFlyout.Source = {
    Fetch = props.Env.Profiles.Fetch
    OnOpen =
      fun handle -> props.OnOpen("/profile/" + Uri.EscapeDataString handle)
    Cache = props.Env.ImageCache
  }

  let row(user: User) : Control =
    let name =
      TextBlock()
        .Classes(Theme.name Theme.Subtitle)
        .FontSize(13.0)
        .MaxLines(1)
        .TextTrimming(TextTrimming.CharacterEllipsis)
        .Text(user.Name)

    let handle = ProfileCard.laneHandle(user.Lane, user.Handle)

    let bioText = user.Bio |> Option.defaultValue ""

    let bio =
      TextBlock()
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
        .FontSize(11.0)
        .MaxLines(1)
        .TextTrimming(TextTrimming.CharacterEllipsis)
        .Text("# " + bioText)
        .IsVisible(not(String.IsNullOrWhiteSpace bioText))

    let reason = reasonOf(user, false)

    let reasonBlock =
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .FontSize(11.0)
        .MaxLines(1)
        .TextTrimming(TextTrimming.CharacterEllipsis)
        .Text(reason |> Option.defaultValue "")
        .IsVisible(reason.IsSome)

    reasonOf(user, true)
    |> Option.iter(fun exact -> ToolTip.SetTip(reasonBlock, exact))

    let details = Grid()
    details.ColumnDefinitions <- ColumnDefinitions("*")
    details.RowDefinitions <- RowDefinitions("Auto,Auto,Auto,Auto")

    details.Children(
      name.Row(0),
      handle.Row(1).Margin(0.0, 1.0, 0.0, 0.0),
      bio.Row(2).Margin(0.0, 1.0, 0.0, 0.0),
      reasonBlock.Row(3).Margin(0.0, 1.0, 0.0, 0.0)
    )
    |> ignore

    let identity =
      Button()
        .Name("OpenSuggestion:" + user.Handle)
        .Classes(Theme.name Theme.GhostButton)
        .Padding(0.0)
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .HorizontalContentAlignment(HorizontalAlignment.Left)
        .Content(details)
        .OnClickHandler(fun _ _ ->
          props.OnOpen("/profile/" + Uri.EscapeDataString user.Handle))

    let avatar =
      Avatar.view {
        Name = user.Name
        Url = user.AvatarUrl
        Lane = user.Lane
        Size = 36.0
        Cache = props.Env.ImageCache
      }

    let avatarWithFlyout = UserFlyout.attach(flyout, user, avatar)

    let busy =
      model |> Observable.map(fun current -> current.Busy.Contains user.Id)

    let followed =
      model |> Observable.map(fun current -> current.Followed.Contains user.Id)

    let follow =
      Button()
        .Name("FollowSuggestion:" + user.Handle)
        .Classes(Theme.name Theme.GhostButton)
        .Padding(4.0)
        .Content(Icons.iconAt(Glyph.UserPlus, 15.0))
        .IsVisible(followed |> Observable.map not)
        .IsEnabled(busy |> Observable.map not)
        .OnClickHandler(fun _ _ -> dispatch(State.FollowRequested user))

    ToolTip.SetTip(follow, $"Follow @{user.Handle}")

    let unfollow =
      Button()
        .Name("UnfollowSuggestion:" + user.Handle)
        .Classes(Theme.name Theme.GhostButton)
        .Padding(4.0)
        .Content(Icons.iconAt(Glyph.Check, 15.0))
        .IsVisible(followed)
        .IsEnabled(busy |> Observable.map not)
        .OnClickHandler(fun _ _ -> dispatch(State.FollowRequested user))

    ToolTip.SetTip(unfollow, $"Following @{user.Handle}. Click to unfollow.")

    let dismiss =
      Button()
        .Name("DismissSuggestion:" + user.Handle)
        .Classes(Theme.name Theme.GhostButton)
        .Padding(4.0)
        .Content(Icons.iconAt(Glyph.Close, 15.0))
        .IsEnabled(busy |> Observable.map not)
        .OnClickHandler(fun _ _ -> dispatch(State.DismissRequested user))

    ToolTip.SetTip(
      dismiss,
      $"Not interested. @{user.Handle} won't be suggested again."
    )

    let actions =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(2.0)
        .VerticalAlignment(VerticalAlignment.Top)
        .Children(follow, unfollow, dismiss)

    let grid = Grid()
    grid.ColumnDefinitions <- ColumnDefinitions("Auto,*,Auto")

    grid
      .Name("WhoToFollowRow:" + user.Handle)
      .Margin(0.0, 0.0, 0.0, 16.0)
      .Children(
        avatarWithFlyout.Column(0).VerticalAlignment(VerticalAlignment.Top),
        identity.Column(1).Margin(8.0, 0.0, 4.0, 0.0),
        actions.Column(2)
      )

  let refresh =
    Button()
      .Name("WhoToFollowRefresh")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(4.0)
      .Content(Icons.iconAt(Glyph.Refresh, 15.0))
      .OnClickHandler(fun _ _ -> dispatch State.RefreshRequested)
      .IsVisible(
        model |> Observable.map(fun current -> not current.Shown.IsEmpty)
      )
      .IsEnabled(
        model
        |> Observable.map(fun current ->
          not current.Loading && not current.Shown.IsEmpty)
      )

  ToolTip.SetTip(refresh, "Suggest other people")

  let header = Grid()
  header.ColumnDefinitions <- ColumnDefinitions("*,Auto")

  header.Children(
    TextBlock()
      .Classes(Theme.name Theme.Subtitle)
      .FontSize(14.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Text("Who to follow")
      .Column(0),
    refresh.Column(1)
  )
  |> ignore

  let rows =
    ItemsControl()
      .ItemTemplate(
        FuncDataTemplate<User>(
          (fun user _ ->
            if isNull(box user) then Panel() :> Control else row user),
          false
        )
      )
      .BindItemsSource(
        model
        |> Observable.map(fun current -> current.Shown :> IReadOnlyList<User>),
        fun user -> user.Id
      )
      .IsVisible(
        model |> Observable.map(fun current -> not current.Shown.IsEmpty)
      )

  let busy =
    TextBlock()
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .FontSize(12.0)
      .Text("Reading who to follow…")
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Loading && current.Shown.IsEmpty)
      )

  let errorLine =
    TextBlock()
      .Classes(Theme.name Theme.FgDanger)
      .FontSize(11.0)
      .TextWrapping(TextWrapping.Wrap)
      .Text("Could not read who to follow.")
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Error.IsSome && current.Shown.IsEmpty && current.Loaded)
      )

  let empty =
    StackPanel()
      .Spacing(4.0)
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .FontSize(12.0)
          .Text("fatal: no one left to suggest"),
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .FontSize(11.0)
          .TextWrapping(TextWrapping.Wrap)
          .Text(
            "You've followed or dismissed everyone we'd suggest. New people show up here as they start committing."
          )
      )
      .IsVisible(
        model
        |> Observable.map(fun current ->
          not current.Loading
          && current.Shown.IsEmpty
          && current.Loaded
          && not current.Dismissed.IsEmpty
          && current.Error.IsNone)
      )

  let body = StackPanel().Spacing(12.0).Children(busy, errorLine, empty, rows)

  Border()
    .Name("WhoToFollow")
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
    .Padding(0.0, 16.0, 0.0, 0.0)
    .Child(StackPanel().Spacing(12.0).Children(header, body))
    .IsVisible(
      model
      |> Observable.map(fun current ->
        current.Loading
        || not current.Shown.IsEmpty
        || (current.Loaded && not current.Dismissed.IsEmpty))
    )

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideRoutes
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    // The aside replaces the rail when the signed-in user changes; it is
    // never cached or re-shown, so the first detach ends its program.
    let root, _ = Program.runWithLifetime this () program

    this.Content <- root
