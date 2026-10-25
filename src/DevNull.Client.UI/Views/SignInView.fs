module DevNull.Client.Views.SignInView

open System
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open IcedTasks
open MVUish.Avalonia
open Navs
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.Errors
open DevNull.Client.Navigation
open DevNull.Client.SessionFiles
open DevNull.Client.Stores
open DevNull.Client.Theme

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvidePkceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> = {
  Env: 'env
  Nav: INavigable
  OnDone: unit -> unit
  Context: RouteContext
}

/// The page's state logic: the sign-in flow, its messages, and update.
module State =

  type Status =
    | Idle
    | Preparing
    | AwaitingApproval of url: string
    | Failed of message: string

  type Model = { Status: Status }

  type Msg =
    | StartRequested
    | ReopenBrowser
    | Prepared of Result<SignInStart, string>
    | SessionObserved of Result<DeviceSession, string>

  let initial =
    match loadPending() with
    | Some _ -> { Status = AwaitingApproval "" }
    | None -> { Status = Idle }

  /// One blocking sign-in start: the app key generation, the pending file,
  /// and the browser launch, all off the UI thread.
  let prepare(env, key: DeviceKey) : Result<SignInStart, string> =
    let started = startAuthorization(env, key)

    match savePending started.Pending with
    | Error message -> Error message
    | Ok() ->
      match startBrowser started.Url with
      | Ok() -> Ok started
      | Error message -> Error message

  /// Waits until the session file or the error file shows up, or the
  /// approval times out, the way the CLI's `devnull auth` does.
  let awaitSession() : Async<Result<DeviceSession, string>> =
    let started = DateTimeOffset.UtcNow

    let rec loop() = asyncEx {
      match loadSession() with
      | Some session -> return Ok session
      | None ->
        match takeLastError() with
        | Some message -> return Error message
        | None when DateTimeOffset.UtcNow - started > TimeSpan.FromMinutes 5.0 ->
          deletePending()
          return Error "The sign-in expired. Start again."
        | None ->
          do! Async.Sleep 1000
          return! loop()
    }

    loop()

  let init (props: Props<'env>) () =
    match initial.Status with
    | AwaitingApproval _ ->
      initial, Cmd.OfAsync.perform awaitSession () SessionObserved
    | _ -> initial, Cmd.none

  let recordSession(props: Props<'env>, session: DeviceSession) : unit =
    props.Env.Session.Enter {
      Session = session
      KeyFile = devicePrivateKeyPath
    }

    props.OnDone()

  let update (props: Props<'env>) msg model =
    match msg with
    | StartRequested ->
      let work() =
        Async.AwaitTask(
          Task.Run(fun () ->
            match ensureKey() with
            | Error message -> Error message
            | Ok key -> prepare(props.Env, key))
        )

      ({ model with Status = Preparing }: Model),
      Cmd.OfAsync.perform work () Prepared
    | Prepared result ->
      match result with
      | Ok started ->
        {
          model with
              Status = AwaitingApproval started.Url
        },
        Cmd.OfAsync.perform awaitSession () SessionObserved
      | Error message -> { model with Status = Failed message }, Cmd.none
    | ReopenBrowser ->
      match model.Status with
      | AwaitingApproval url ->
        match startBrowser url with
        | Ok() -> model, Cmd.none
        | Error message -> { model with Status = Failed message }, Cmd.none
      | _ -> model, Cmd.none
    | SessionObserved result ->
      match result with
      | Ok session ->
        recordSession(props, session)
        model, Cmd.none
      | Error message -> { model with Status = Failed message }, Cmd.none

/// The page's semantic regions; the view only composes these.
module Parts =

  /// The status line: what the flow is doing right now.
  let statusLine(model: IObservable<State.Model>) : Control =
    TextBlock()
      .TextWrapping(TextWrapping.Wrap)
      .TextAlignment(TextAlignment.Center)
      .Classes(Theme.name Theme.Muted)
      .Text(
        model
        |> Observable.map(fun current ->
          match current.Status with
          | State.Idle ->
            "Sign in with the SSH key your /dev/null account knows."
          | State.Preparing -> "Preparing sign-in…"
          | State.AwaitingApproval _ ->
            "Waiting for approval in the browser. Approve this device to continue."
          | State.Failed message -> message)
      )

  /// The primary actions: start, or open the browser again while an
  /// approval is pending.
  let primaryActions
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control array =
    let primaryLabel =
      model
      |> Observable.map(fun current ->
        match current.Status with
        | State.Idle -> "Sign in"
        | State.Preparing -> "Preparing…"
        | State.AwaitingApproval _ -> "Open the browser again"
        | State.Failed _ -> "Try again")

    let awaiting =
      model
      |> Observable.map(fun current ->
        match current.Status with
        | State.AwaitingApproval _ -> true
        | State.Idle
        | State.Preparing
        | State.Failed _ -> false)

    let startButton =
      Button()
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .Classes(Theme.name Theme.AccentButton)
        .Content(TextBlock().Text(primaryLabel))
        .IsVisible(awaiting |> Observable.map not)
        .OnClickHandler(fun _ _ -> dispatch State.StartRequested)

    let reopenButton =
      Button()
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .Classes(Theme.name Theme.AccentButton)
        .Content("Open the browser again")
        .IsVisible(awaiting)
        .OnClickHandler(fun _ _ -> dispatch State.ReopenBrowser)

    [| startButton; reopenButton |]

  /// The sign-in card.
  let card
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let title =
      TextBlock()
        .Classes(Theme.name Theme.Title)
        .TextAlignment(TextAlignment.Center)
        .Text("Sign in to /dev/null")

    StackPanel()
      .Spacing(14.0)
      .MaxWidth(360.0)
      .Children(
        Array.append
          [| title :> Control; statusLine model |]
          (primaryActions(model, dispatch))
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  ScrollViewer()
    .Content(
      Grid()
        .Children(
          Parts
            .card(model, dispatch)
            .VerticalAlignment(VerticalAlignment.Center)
        )
    )

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvidePkceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root
    props.Context.AddDisposable lifetime
