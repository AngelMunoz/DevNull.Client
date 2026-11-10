module DevNull.Client.Components.LiveNow

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.Stores

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache> = { Env: 'env; Session: SignedInSession }

// The section's state logic: model, messages, update, and the
// minute poll that feeds it from outside.
module State =

  type Model = { Users: User list; Failed: bool }

  type Msg =
    | Loaded of Result<User list, AppError>
    | Polled

  let initial = { Users = []; Failed = false }

  let fetch(props: Props<'env>) =
    Me.liveFollowing(props.Env, { KeyFile = props.Session.KeyFile })

  let init (props: Props<'env>) () =
    initial, Cmd.OfAsync.perform fetch props Loaded

  let update (props: Props<'env>) message (model: Model) : Model * Cmd<Msg> =
    match message with
    | Loaded result ->
      match result with
      | Ok users -> { Users = users; Failed = false }, Cmd.none
      | Error _ ->
        // A failed poll keeps the last list.
        { model with Failed = true }, Cmd.none
    | Polled -> model, Cmd.OfAsync.perform fetch props Loaded

  let subscribe(props: Props<'env>) : Sub<Msg> =
    Sub.interval "live-again" (TimeSpan.FromSeconds 60.0) (fun () -> Polled)

// The section's semantic regions; the view only composes these.
module Parts =

  // One live-now row: the name over the handle, beside the badge,
  // opening the stream outside the app.
  let row(cache: ImageCacheService, user: User) : Control =
    let streamUrl = user.Live |> Option.map _.Url |> Option.defaultValue ""

    Button()
      .Name("OpenLiveNow:" + user.Handle)
      .Classes(Theme.name Theme.GhostButton)
      .Padding(8.0, 6.0, 8.0, 6.0)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Content(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .Children(
            Avatar.view {
              Name = user.Name
              Url = user.AvatarUrl
              Lane = user.Lane
              Size = 28.0
              Cache = cache
              Square = false
              Viewable = false
            },
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(6.0)
              .VerticalAlignment(VerticalAlignment.Center)
              .Children(
                TextBlock()
                  .Classes(Theme.name Theme.Subtitle)
                  .FontSize(13.0)
                  .Text(user.Name),
                LiveBadge.view user.Live
              )
          )
      )
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        DeviceAuth.startBrowser streamUrl |> ignore)

  // The card, shown while anyone the viewer follows is live.
  let section(props: Props<'env>, model: IObservable<State.Model>) : Control =
    let rows: IObservable<Control> =
      PageChrome.rebuildOn(
        model,
        (fun current -> current.Users),
        fun current ->
          match current.Users with
          | [] -> Panel()
          | users ->
            StackPanel().Spacing(2.0).Children [|
              for user in users -> row(props.Env.ImageCache, user)
            |]
      )

    let host = ContentControl()

    host.Content(rows.ToBinding()) |> ignore

    Border()
      .Name("LiveNow")
      .Classes(Theme.name Theme.Card)
      .Padding(12.0, 10.0, 12.0, 10.0)
      .IsVisible(model |> Observable.map(fun current -> current.Users <> []))
      .Child(
        StackPanel()
          .Spacing(8.0)
          .Children(
            TextBlock().Classes(Theme.name Theme.Subtitle).Text("Live now"),
            host
          )
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (_: Dispatch<State.Msg>)
  : Control =
  Parts.section(props, model)

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(fun _ -> State.subscribe props)

    let root, _ = Programs.runComponent(this, props.Env.Dispatcher, program)

    this.Content <- root
