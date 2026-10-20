module DevNull.Client.Views.ProfileView

open System
open System.Collections.Generic
open Avalonia.Controls
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
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Navigation
open DevNull.Client.Stores
open DevNull.Client.Theme

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> = {
  Env: 'env
  Username: string
  Session: SignedInSession
  Nav: INavigable
}

type Model = {
  Loading: bool
  Error: AppError option
  Details: ProfileDetails option
  Commits: CommitNode list
  NextCursor: string option
}

type Msg =
  | Fetched of Result<ProfileDetails, AppError>
  | CommitsFetched of Result<Page<CommitNode>, AppError>
  | MoreCommits
  | FollowToggled
  | MuteToggled
  | BlockToggled
  | FollowDone of Result<unit, AppError>
  | MuteDone of Result<MuteResult, AppError>
  | BlockDone of Result<BlockResult, AppError>

let initial = {
  Loading = true
  Error = None
  Details = None
  Commits = []
  NextCursor = None
}

let fetchCommits
  (env, session: SignedInSession, username: string, cursor: string option)
  =
  Users.commits(
    env,
    {
      KeyFile = session.KeyFile
      Username = username
      Replies = false
      Cursor = cursor
    }
  )

let init (props: Props<'env>) () =
  initial,
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
      (props.Env, props.Session, props.Username, None)
      CommitsFetched
  ]

let update (props: Props<'env>) msg model =
  let username =
    model.Details
    |> Option.map(fun details -> details.Profile.User.Username)
    |> Option.defaultValue props.Username

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
            Commits = model.Commits @ page.Items
            NextCursor = page.NextCursor
      },
      Cmd.none
    | Error error -> { model with Error = Some error }, Cmd.none
  | MoreCommits ->
    model,
    Cmd.OfAsync.perform
      fetchCommits
      (props.Env, props.Session, username, model.NextCursor)
      CommitsFetched
  | FollowToggled ->
    let following =
      model.Details |> Option.map(_.IsFollowing) |> Option.defaultValue false

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
      model.Details |> Option.map(_.IsMuting) |> Option.defaultValue false

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
      Cmd.OfAsync.perform
        Users.get
        (props.Env,
         {
           KeyFile = props.Session.KeyFile
           Username = username
         })
        Fetched
    | Error error -> { model with Error = Some error }, Cmd.none
  | MuteDone result ->
    match result with
    | Ok _ ->
      { model with Error = None },
      Cmd.OfAsync.perform
        Users.get
        (props.Env,
         {
           KeyFile = props.Session.KeyFile
           Username = username
         })
        Fetched
    | Error error -> { model with Error = Some error }, Cmd.none
  | BlockDone result ->
    match result with
    | Ok _ ->
      Navigation.go(props.Nav, "/log/latest", ignore)
      model, Cmd.none
    | Error error -> { model with Error = Some error }, Cmd.none

let nodeKey(node: CommitNode) =
  match node with
  | LiveCommit commit -> commit.Id
  | Tombstone tombstone -> tombstone.Id

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let errorText =
    TextBlock()
      .TextWrapping(TextWrapping.Wrap)
      .Foreground(Brushes.danger)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Error |> Option.map describe |> Option.defaultValue "")
      )

  let errorPanel =
    Border()
      .Classes(Theme.name Theme.Card)
      .Margin(0.0, 8.0, 0.0, 8.0)
      .Child(errorText)
      .IsVisible(model |> Observable.map(_.Error.IsSome))

  let busy =
    Status.busy "Loading the profile…"
    |> fun control -> control.IsVisible(model |> Observable.map _.Loading)

  // The header is rebuilt when the profile changes: the follow, mute,
  // and block states come back from the server after every action.
  let headerHost = ContentControl()

  headerHost.Content(
    model
    |> Observable.map(fun current ->
      match current.Details with
      | Some details ->
        let user = details.Profile.User

        let isSelf = user.Id = props.Session.Session.User.Id

        let lane =
          user.Lane
          |> Option.bind(function
            | LaneIndex index -> Some index
            | LaneColor _ -> None)

        let bio = details.Profile.Bio |> Option.defaultValue ""

        let stats =
          $"commits {details.Profile.CommitsCount |> Option.defaultValue 0} · "
          + $"followers {details.Profile.FollowersCount |> Option.defaultValue 0} · "
          + $"following {details.Profile.FollowingCount |> Option.defaultValue 0}"

        let followButton =
          Button()
            .Classes(Theme.name Theme.NavButton)
            .Content(if details.IsFollowing then "Unfollow" else "Follow")
            .OnClickHandler(fun _ _ -> dispatch FollowToggled)

        let muteButton =
          Button()
            .Classes(Theme.name Theme.NavButton)
            .Content(if details.IsMuting then "Unmute" else "Mute")
            .OnClickHandler(fun _ _ -> dispatch MuteToggled)

        let blockButton =
          Button()
            .Classes(Theme.name Theme.DangerButton)
            .Content("Block")
            .OnClickHandler(fun _ _ -> dispatch BlockToggled)

        let actions: Control array =
          if isSelf then
            [||]
          else
            [| followButton; muteButton; blockButton |]

        Border()
          .Classes(Theme.name Theme.Card)
          .Margin(0.0, 0.0, 0.0, 8.0)
          .Child(
            StackPanel()
              .Spacing(4.0)
              .Children(
                StackPanel()
                  .Orientation(Orientation.Horizontal)
                  .Spacing(10.0)
                  .Children(
                    Avatar.view {
                      Name = user.Name
                      Url = user.AvatarUrl
                      Lane = lane
                      Size = 56.0
                    },
                    StackPanel()
                      .Spacing(2.0)
                      .Children(
                        TextBlock()
                          .Classes(Theme.name Theme.Title)
                          .Text(user.Name),
                        TextBlock()
                          .Classes(Theme.name Theme.Muted)
                          .Text($"@{user.Username}"),
                        TextBlock().Text(bio).TextWrapping(TextWrapping.Wrap),
                        TextBlock().Classes(Theme.name Theme.Muted).Text(stats)
                      )
                  ),
                StackPanel()
                  .Orientation(Orientation.Horizontal)
                  .Spacing(8.0)
                  .Children(actions)
              )
          )
        :> obj
      | None -> Status.empty "" :> obj)
  )
  |> ignore

  let commitRow(node: CommitNode) : Control =
    match node with
    | LiveCommit commit ->
      CommitCard.view {
        Commit = commit
        OnEvent =
          fun event ->
            match event with
            | CommitCard.Opened c ->
              Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
            | _ -> ()
      }
    | Tombstone tombstone -> CommitCard.tombstone tombstone

  let more =
    Button()
      .Content("Load more")
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Margin(0.0, 8.0, 0.0, 16.0)
      .IsVisible(model |> Observable.map _.NextCursor.IsSome)
      .OnClickHandler(fun _ _ -> dispatch MoreCommits)

  let commits =
    Feed.view {
      Items = model |> Observable.map _.Commits
      KeyOf = nodeKey
      Row = commitRow
      Footer = ValueSome more
    }

  let pinned =
    StackPanel()
      .MaxWidth(720.0)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Margin(Theme.pagePadding)
      .Children(headerHost, errorPanel, busy)

  let root = Grid()
  root.RowDefinitions <- RowDefinitions("Auto,*")

  root.Children(pinned.Row(0), commits.Margin(24.0, 0.0, 24.0, 0.0).Row(1))
  |> ignore

  root

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram (init props) (update props) (view props)

    this.Content <- Program.runWith () program
