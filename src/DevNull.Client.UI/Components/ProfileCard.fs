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
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.Theme

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideImageCache> = { Env: 'env; Session: SignedInSession }

/// The card's state logic: model, messages, and update.
module State =

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
        Username = props.Session.Session.User.Handle
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

/// The handle line: a lane dot beside the @handle, the way the site's
/// aside marks the user's lane.
let laneHandle(lane: Lane option, username: string) : Control =
  let dot = Ellipse().Width(8.0).Height(8.0)

  Theme.laneFill(dot, lane)

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

/// The identity head: the avatar beside the name and the lane handle.
let identityHead(cache: ImageCacheService, profile: Profile) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(12.0)
    .Children(
      Avatar.view {
        Name = profile.User.Name
        Url = profile.User.AvatarUrl
        Lane = profile.Lane
        Size = 48.0
        Cache = cache
      },
      StackPanel()
        .VerticalAlignment(VerticalAlignment.Center)
        .Spacing(2.0)
        .Children(
          TextBlock().Classes(Theme.name Theme.Subtitle).Text(profile.User.Name),
          laneHandle(profile.Lane, profile.User.Handle)
        )
    )

/// The counts row: commits, following, followers.
let stats(profile: Profile) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(16.0)
    .Children(
      statRow(profile.CommitsCount |> Option.defaultValue 0, "commits"),
      statRow(profile.FollowingCount |> Option.defaultValue 0, "following"),
      statRow(profile.FollowersCount |> Option.defaultValue 0, "followers")
    )

/// The bio line, when the profile carries one.
let bioLine(profile: Profile) : Control =
  TextBlock()
    .Classes(Theme.name Theme.Muted)
    .IsVisible(profile.Bio.IsSome)
    .TextWrapping(TextWrapping.Wrap)
    .Text(profile.Bio |> Option.defaultValue "")

/// The card's semantic regions; the view only composes these.
module Parts =

  /// The error line under the card.
  let errorText(model: IObservable<State.Model>) : Control =
    TextBlock()
      .Classes(Theme.name Theme.FgDanger)
      .TextWrapping(TextWrapping.Wrap)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Error |> Option.map describe |> Option.defaultValue "")
      )
      .IsVisible(model |> Observable.map _.Error.IsSome)

  /// The loading line while the profile reads.
  let busy(model: IObservable<State.Model>) : Control =
    Status.busy("Loading your card…").IsVisible(model |> Observable.map _.Busy)

  /// The card itself, rebuilt whenever the profile changes.
  let cardHost
    (cache: ImageCacheService, model: IObservable<State.Model>)
    : Control =
    let card: IObservable<Control> =
      model
      |> Observable.map(fun current ->
        match current.Details with
        | Some details ->
          StackPanel()
            .Spacing(16.0)
            .Children(
              identityHead(cache, details.Profile),
              stats details.Profile,
              bioLine details.Profile
            )
        | None -> Status.empty "")

    let host = ContentControl()

    host.Content(card.ToBinding()) |> ignore

    host

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (_: Dispatch<State.Msg>)
  : Control =
  StackPanel()
    .Spacing(8.0)
    .Children(
      Parts.cardHost(props.Env.ImageCache, model),
      Parts.errorText model,
      Parts.busy model
    )

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideImageCache>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram (State.init props) State.update (view props)

    let root, _ = Program.runWithLifetime this () program

    this.Content <- root
