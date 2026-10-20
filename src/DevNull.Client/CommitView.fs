module DevNull.Client.CommitView

open System
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open IcedTasks
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Commit
open DevNull.Client.Core.Types
open DevNull.Client.DeviceAuth
open DevNull.Client.Env
open DevNull.Client.Errors

type CommitControls = {
  Body: TextBox
  Reply: TextBox
  Status: TextBlock
  Button: Button
}

let outcomeOf(posted: CommitPosted) =
  let url = CommitPosted.url posted

  match CommitPosted.pendingReview posted with
  | true ->
    $"Committed: {url} (pending review, only you can see it until it is approved)"
  | false -> $"Committed: {url}"

let runCommit(env: AppEnv, controls: CommitControls) =
  asyncEx {
    controls.Button.IsEnabled <- false
    controls.Status.Text <- "Committing…"

    let candidates = KeyFiles.candidates()

    match candidates with
    | [] ->
      controls.Status.Text <-
        "No Ed25519 or RSA SSH public key found in ~/.ssh."
    | keys ->
      match! Commit.findKey(env, keys |> String.concat "\n") with
      | Error error -> controls.Status.Text <- describe error
      | Ok keyId ->
        match KeyIds.value keyId |> KeyFiles.privatePathOf with
        | None ->
          controls.Status.Text <-
            "Could not find the private key file of the matched key."
        | Some keyFile ->
          let input = {
            Body = controls.Body.Text |> Option.ofObj |> Option.defaultValue ""
            ReplyTo = controls.Reply.Text |> Option.ofObj
            KeyFile = keyFile
            Images = []
          }

          let! (result: Result<CommitPosted, AppError>) =
            Commit.commit(env, input)

          controls.Status.Text <-
            match result with
            | Ok posted -> outcomeOf posted
            | Error error -> describe error

    controls.Button.IsEnabled <- true
  }
  |> Async.StartImmediate

let showSession(status: TextBlock, button: Button, session: DeviceSession) =
  status.Text <- $"Signed in as {session.User.Name} (@{session.User.Username})."
  button.Content <- "Sign in again"
  button.IsEnabled <- true

let pollForSession(status: TextBlock, button: Button) =
  let started = DateTimeOffset.UtcNow
  let timer = DispatcherTimer(Interval = TimeSpan.FromSeconds 1.0)

  timer.Tick.Add(fun _ ->
    match loadSession() with
    | Some session ->
      timer.Stop()
      showSession(status, button, session)
    | None ->
      match takeLastError() with
      | Some message ->
        timer.Stop()
        status.Text <- message
        button.IsEnabled <- true
      | None when (DateTimeOffset.UtcNow - started) > TimeSpan.FromMinutes 5.0 ->
        timer.Stop()
        deletePending()
        status.Text <- "The sign-in expired. Start again."
        button.IsEnabled <- true
      | None -> ())

  timer.Start()

let runSignIn(env: AppEnv, status: TextBlock, button: Button) =
  asyncEx {
    button.IsEnabled <- false
    status.Text <- "Preparing sign-in…"

    let! keyOutcome = Task.Run(fun () -> ensureKey()) |> Async.AwaitTask

    match keyOutcome with
    | Error message ->
      status.Text <- message
      button.IsEnabled <- true
    | Ok key ->
      let started = startAuthorization(env, key)

      match savePending started.Pending with
      | Error message ->
        status.Text <- message
        button.IsEnabled <- true
      | Ok() ->
        match startBrowser started.Url with
        | Error message ->
          status.Text <- message
          button.IsEnabled <- true
        | Ok() ->
          status.Text <- "Waiting for approval in the browser…"
          pollForSession(status, button)
  }
  |> Async.StartImmediate

let buildWindow(env: AppEnv, callback: string option) : Window =
  let body =
    TextBox(PlaceholderText = "message", FontSize = 15.0)
      .AcceptsReturn(true)
      .MinHeight(96.0)
      .TextWrapping(TextWrapping.Wrap)

  let reply =
    TextBox(PlaceholderText = "reply hash or link (optional)", Width = 260.0)

  let status = TextBlock().TextWrapping(TextWrapping.Wrap)

  let button = Button(Content = TextBlock().Text("Commit"), FontSize = 15.0)

  let signInStatus = TextBlock().TextWrapping(TextWrapping.Wrap)

  let signInButton =
    Button(Content = TextBlock().Text("Sign in"), FontSize = 15.0)

  let controls = {
    Body = body
    Reply = reply
    Status = status
    Button = button
  }

  let signInPanel =
    StackPanel().Spacing(6.0).Children(signInButton, signInStatus)

  let window =
    Window(Title = "/dev/null", Width = 460.0, Height = 480.0)
      .Content(
        StackPanel()
          .Margin(16.0)
          .Spacing(12.0)
          .Children(signInPanel, body, reply, button, status)
      )

  button.OnClickHandler(fun _ _ -> runCommit(env, controls)) |> ignore

  signInButton.OnClickHandler(fun _ _ ->
    runSignIn(env, signInStatus, signInButton))
  |> ignore

  window.Opened.Add(fun _ ->
    let session = loadSession()

    match session with
    | Some session -> showSession(signInStatus, signInButton, session)
    | None ->
      match takeLastError() with
      | Some message -> signInStatus.Text <- message
      | None -> signInStatus.Text <- "Not signed in."

    match callback with
    | Some uri ->
      asyncEx {
        signInButton.IsEnabled <- false
        signInStatus.Text <- "Finishing sign-in…"

        let! keyOutcome = Task.Run(fun () -> ensureKey()) |> Async.AwaitTask

        match keyOutcome with
        | Error message ->
          signInStatus.Text <- message
          signInButton.IsEnabled <- true
        | Ok key ->
          match! complete(env, key, uri) with
          | Ok session -> showSession(signInStatus, signInButton, session)
          | Error error ->
            signInStatus.Text <- describe error
            signInButton.IsEnabled <- true
      }
      |> Async.StartImmediate
    | None ->
      match session with
      | Some _ -> ()
      | None ->
        match loadPending() with
        | Some _ ->
          signInStatus.Text <- "Waiting for approval in the browser…"
          pollForSession(signInStatus, signInButton)
        | None -> ())

  window
