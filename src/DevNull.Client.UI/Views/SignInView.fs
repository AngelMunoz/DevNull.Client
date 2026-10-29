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
open DevNull.Client.Env
open DevNull.Client.Errors
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Logging
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
  and 'env :> IProvideSession
  and 'env :> IProvideLogging> = {
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
    | PendingChecked of PendingAuthorization option
    | Prepared of Result<SignInStart, string>
    | SessionObserved of Result<DeviceSession, string>

  /// One sign-in start: the app key generation, the pending file, and
  /// the browser launch.
  let prepare
    (log: ILogger, env, key: DeviceKey)
    : Async<Result<SignInStart, string>> = asyncResult {
    let started = startAuthorization(env, key)

    log.LogDebug("The device authorization started; the browser opens.")

    do! savePending(log, started.Pending)

    match startBrowser started.Url with
    | Ok() -> return started
    | Error message ->
      log.LogWarning("The browser did not open: {Error}", message)

      return! Error message
  }

  /// Wakes when anything under the app's data folder changes — the
  /// second instance's session or error file, or its own log lines —
  /// and on a backstop beat either way: the change makes the common
  /// case instant, the beat keeps the wait correct when a change
  /// lands before the watch does.
  let waitForChange() : Async<unit> = asyncEx {
    let touched =
      TaskCompletionSource<unit>(
        TaskCreationOptions.RunContinuationsAsynchronously
      )

    use watcher = new IO.FileSystemWatcher(dataDirectory)

    watcher.Changed.Add(fun _ -> touched.TrySetResult() |> ignore)
    watcher.Created.Add(fun _ -> touched.TrySetResult() |> ignore)
    watcher.Renamed.Add(fun _ -> touched.TrySetResult() |> ignore)
    watcher.EnableRaisingEvents <- true

    do!
      Task.WhenAny(touched.Task, Task.Delay 1000)
      |> Async.AwaitTask
      |> Async.Ignore
  }

  /// Waits until the session file or the error file shows up, or the
  /// approval times out, the way the CLI's `devnull auth` does.
  let awaitSession(log: ILogger) : Async<Result<DeviceSession, string>> =
    let started = DateTimeOffset.UtcNow

    let rec loop() = asyncEx {
      match! loadSession log with
      | Some session -> return Ok session
      | None ->
        match! takeLastError log with
        | Some message -> return Error message
        | None when DateTimeOffset.UtcNow - started > TimeSpan.FromMinutes 5.0 ->
          log.LogWarning("The sign-in approval timed out after 5 minutes.")
          deletePending log
          return Error "The sign-in expired. Start again."
        | None ->
          do! waitForChange()
          return! loop()
    }

    loop()

  let init (props: Props<'env>) () =
    let log = props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.SignIn"

    // The pending check rides a command, so the page builds without
    // touching the disk.
    { Status = Idle }, Cmd.OfAsync.perform loadPending log PendingChecked

  /// The session lands, and the caller navigates away — both effects
  /// the loop runs after update, the way navigation inside update
  /// would dispose this program before the model is published.
  let recordSessionCmd
    (props: Props<'env>, log: ILogger, session: DeviceSession)
    : Cmd<Msg> =
    Cmd.ofEffect(fun _ ->
      log.LogInformation(
        "The session of {Handle} was recorded.",
        session.User.Handle
      )

      props.Env.Session.Enter {
        Session = session
        KeyFile = devicePrivateKeyPath
      }

      props.OnDone())

  let update (props: Props<'env>) msg model =
    let log = props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.SignIn"

    match msg with
    | StartRequested ->
      let work() = asyncResult {
        let! key = ensureKey log

        return! prepare(log, props.Env, key)
      }

      ({ model with Status = Preparing }: Model),
      Cmd.batch [
        Cmd.OfAsync.perform work () Prepared

        Cmd.ofAction(fun () -> log.LogInformation("The sign-in starts."))
      ]
    | PendingChecked pending ->
      match pending with
      | Some _ ->
        log.LogDebug("A sign-in is already pending; the page waits for it.")

        ({ Status = AwaitingApproval "" }: Model),
        Cmd.OfAsync.perform awaitSession log SessionObserved
      | None -> model, Cmd.none
    | Prepared result ->
      match result with
      | Ok started ->
        {
          model with
              Status = AwaitingApproval started.Url
        },
        Cmd.batch [
          Cmd.OfAsync.perform awaitSession log SessionObserved

          Cmd.ofAction(fun () ->
            log.LogDebug("The approval waits in the browser."))
        ]
      | Error message ->
        { model with Status = Failed message },
        Cmd.ofAction(fun () ->
          log.LogWarning("The sign-in could not start: {Error}", message))
    | ReopenBrowser ->
      match model.Status with
      | AwaitingApproval url ->
        match startBrowser url with
        | Ok() -> model, Cmd.none
        | Error message ->
          { model with Status = Failed message },
          Cmd.ofAction(fun () ->
            log.LogWarning("The browser did not open again: {Error}", message))
      | _ -> model, Cmd.none
    | SessionObserved result ->
      match result with
      | Ok session -> model, recordSessionCmd(props, log, session)
      | Error message ->
        { model with Status = Failed message },
        Cmd.ofAction(fun () ->
          log.LogWarning("The sign-in did not complete: {Error}", message))

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
  and 'env :> IProvideSession
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root
    props.Context.AddDisposable lifetime
