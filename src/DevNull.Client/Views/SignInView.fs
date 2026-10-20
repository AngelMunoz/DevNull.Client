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
open DevNull.Client.Stores
open DevNull.Client.Theme

exception SignInError of string

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
}

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
  | StepFailed of message: string

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

let runBrowser(url: string) : unit =
  match startBrowser url with
  | Ok() -> ()
  | Error message -> raise(SignInError message)

let exnMessage(exn: exn) =
  match exn with
  | SignInError message -> message
  | other -> other.Message

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
      model, Cmd.OfFunc.attempt runBrowser url (StepFailed << exnMessage)
    | _ -> model, Cmd.none
  | StepFailed message -> { model with Status = Failed message }, Cmd.none
  | SessionObserved result ->
    match result with
    | Ok session ->
      recordSession(props, session)
      model, Cmd.none
    | Error message -> { model with Status = Failed message }, Cmd.none

let view props (model: IObservable<Model>) (dispatch: Dispatch<Msg>) : Control =
  let status = ref initial.Status

  model |> Observable.add(fun current -> status.Value <- current.Status)

  let statusText =
    TextBlock()
      .TextWrapping(TextWrapping.Wrap)
      .TextAlignment(TextAlignment.Center)
      .Classes(Theme.name Theme.Muted)
      .Text(
        model
        |> Observable.map(fun current ->
          match current.Status with
          | Idle -> "Sign in with the SSH key your devnull account knows."
          | Preparing -> "Preparing sign-in…"
          | AwaitingApproval _ ->
            "Waiting for approval in the browser. Approve this device to continue."
          | Failed message -> message)
      )

  let primaryLabel =
    model
    |> Observable.map(fun current ->
      match current.Status with
      | Idle -> "Sign in"
      | Preparing -> "Preparing…"
      | AwaitingApproval _ -> "Open the browser again"
      | Failed _ -> "Try again")

  let card =
    StackPanel()
      .Spacing(14.0)
      .MaxWidth(360.0)
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.Title)
          .TextAlignment(TextAlignment.Center)
          .Text("Sign in to /dev/null"),
        statusText,
        Button()
          .HorizontalAlignment(HorizontalAlignment.Stretch)
          .Classes(Theme.name Theme.AccentButton)
          .Content(TextBlock().Text(primaryLabel))
          .OnClickHandler(fun _ _ ->
            match status.Value with
            | AwaitingApproval _ -> dispatch ReopenBrowser
            | _ -> dispatch StartRequested)
      )

  ScrollViewer()
    .Content(Grid().Children(card.VerticalAlignment(VerticalAlignment.Center)))

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvidePkceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram (init props) (update props) (view props)

    this.Content <- Program.runWith () program
