module DevNull.Client.Components.ProfileCard

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open IcedTasks
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Stores
open DevNull.Client.Theme

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> = { Env: 'env; Session: SignedInSession }

type Model = {
  Details: ProfileDetails option
  Busy: bool
  Error: AppError option
}

type Msg = Fetched of Result<ProfileDetails, AppError>

let initial = {
  Details = None
  Busy = true
  Error = None
}

let fetch(props: Props<'env>) =
  Users.get(
    props.Env,
    {
      KeyFile = props.Session.KeyFile
      Username = props.Session.Session.User.Username
    }
  )

let init (props: Props<'env>) () =
  initial, Cmd.OfAsync.perform fetch props Fetched

let update msg model =
  match msg with
  | Fetched result ->
    match result with
    | Ok details ->
      {
        model with
            Busy = false
            Error = None
            Details = Some details
      },
      Cmd.none
    | Error error ->
      {
        model with
            Busy = false
            Error = Some error
      },
      Cmd.none

let errorText(model: IObservable<Model>) : Control =
  TextBlock()
    .Foreground(Brushes.danger)
    .TextWrapping(TextWrapping.Wrap)
    .Text(
      model
      |> Observable.map(fun current ->
        current.Error |> Option.map describe |> Option.defaultValue "")
    )
    .IsVisible(model |> Observable.map _.Error.IsSome)

/// The handle line: a lane dot beside the @handle, the way the site's
/// aside marks the user's lane.
let laneHandle(lane: Lane option, username: string) : Control =
  let dot = Ellipse().Width(8.0).Height(8.0).Fill(Brushes.laneOf lane)

  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(6.0)
    .VerticalAlignment(VerticalAlignment.Center)
    .Children(
      dot,
      TextBlock().Classes(Theme.name Theme.Mono).Text("@" + username)
    )

/// One count-and-label pair of the stats row.
let statRow(count: int, label: string) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(4.0)
    .VerticalAlignment(VerticalAlignment.Center)
    .Children(
      TextBlock().Classes(Theme.name Theme.StatNum).Text(string count),
      TextBlock().Classes(Theme.name Theme.Muted).Text(label)
    )

let view (model: IObservable<Model>) (_: Dispatch<Msg>) : Control =
  let busy =
    Status.busy("Loading your card…").IsVisible(model |> Observable.map _.Busy)

  let card: IObservable<Control> =
    model
    |> Observable.map(fun current ->
      match current.Details with
      | Some details ->
        let user = details.Profile.User

        let bio = details.Profile.Bio |> Option.defaultValue ""

        StackPanel()
          .Spacing(16.0)
          .Children(
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(12.0)
              .Children(
                Avatar.view {
                  Name = user.Name
                  Url = user.AvatarUrl
                  Lane = details.Profile.Lane
                  Size = 48.0
                },
                StackPanel()
                  .VerticalAlignment(VerticalAlignment.Center)
                  .Spacing(2.0)
                  .Children(
                    TextBlock()
                      .Classes(Theme.name Theme.Subtitle)
                      .Text(user.Name),
                    laneHandle(details.Profile.Lane, user.Username)
                  )
              ),
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(16.0)
              .Children(
                statRow(
                  details.Profile.CommitsCount |> Option.defaultValue 0,
                  "commits"
                ),
                statRow(
                  details.Profile.FollowingCount |> Option.defaultValue 0,
                  "following"
                ),
                statRow(
                  details.Profile.FollowersCount |> Option.defaultValue 0,
                  "followers"
                )
              ),
            TextBlock()
              .Classes(Theme.name Theme.Muted)
              .IsVisible(details.Profile.Bio.IsSome)
              .TextWrapping(TextWrapping.Wrap)
              .Text(bio)
          )
      | None -> Status.empty "")

  let host = ContentControl()

  host.Content(card.ToBinding()) |> ignore

  StackPanel().Spacing(8.0).Children(host, errorText model, busy)

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram (init props) update view

    this.Content <- Program.runWith () program
