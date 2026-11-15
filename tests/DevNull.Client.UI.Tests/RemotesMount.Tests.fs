module DevNull.Client.UI.Tests.RemotesMountTests

// The router-free remotes mount: the inbox and thread controls stand
// apart, the target sink follows the store, and disposing the one
// lifetime releases the thread watch.

open Avalonia.Controls
open Expecto
open NXUI.Extensions
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.Stores
open DevNull.Client.Views
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let private partner: User = {
  Id = "01a151db-7fee-727b-a7ce-c911a7556243"
  Handle = "hubot"
  Name = "The Hubot"
  AvatarUrl = None
  Bio = None
  Lane = None
  CommitsCount = None
  FollowersCount = None
  FollowingCount = None
  FollowedByFollowingCount = None
  IsFollowed = None
  FollowsYou = None
  Live = None
}

let private deviceWithRemotes() = {
  sessionOf "octocat" with
      Remotes =
        Some {
          From = FromEveryone
          PullReceipts = true
          Requests = false
          Everyone = true
          UnreadCount = 0
        }
}

let tests =
  testList "remotes mount" [
    testCaseAsync
      "the mount exposes both panes and releases its watches on dispose"
      (async {
        let! window, mount, env, seen =
          probeUiAsync(fun () ->
            let device = deviceWithRemotes()

            let store =
              SessionStore.create {
                Initial = Some device
                KeyFile = devicePrivateKeyPath
                Forget = ignore
                Dispatcher = Harness.ui
              }

            let env = TestEnv(store, FakeApi("{}"))

            (env :> IProvideRemotes)
              .Remotes.SetOpenTarget(Some(Partner partner))

            let session: SignedInSession = {
              Session = device
              KeyFile = devicePrivateKeyPath
            }

            let seen = ResizeArray<ChatTarget option>()

            let mount =
              RemotesMount.mount(
                Harness.ui,
                {
                  Env = env
                  Session = session
                  OnOpenProfile = ignore
                  OnTargetChanged = seen.Add
                }
              )

            let window =
              Window(
                Content = StackPanel().Children(mount.Inbox, mount.Thread)
              )

            window.Show()
            window, mount, env, seen)

        Expect.isNotNull mount.Inbox "the mount never built its inbox"
        Expect.isNotNull mount.Thread "the mount never built its thread"

        let! watched =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRemotes).Remotes.WatchedCount),
            (fun count -> count = 1)
          )

        Expect.isTrue watched "the thread watch never started"

        let! sawOpen =
          waitForAsync(
            5000,
            (fun () -> seen |> Seq.toList),
            (fun targets ->
              targets
              |> List.exists(function
                | Some(Partner user) when user.Handle = "hubot" -> true
                | _ -> false))
          )

        Expect.isTrue sawOpen "the target sink never fired"

        mount.Send State.Closed

        let! closed =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRemotes).Remotes.Current.OpenTarget),
            (fun target -> target.IsNone)
          )

        Expect.isTrue closed "Closed never cleared the open target"

        do!
          onUiAsync(fun () ->
            mount.Lifetime.Dispose()
            window.Close())

        let! released =
          waitForAsync(
            5000,
            (fun () -> (env :> IProvideRemotes).Remotes.WatchedCount),
            (fun count -> count = 0)
          )

        Expect.isTrue released "a thread watch survived the dispose"
      })

    testCaseAsync
      "the account-off panel names both actions"
      (async {
        let enabled = ref false
        let website = ref false

        let! window =
          probeUiAsync(fun () ->
            let panel =
              RemotesMount.disabledPanel(
                (fun () -> enabled.Value <- true),
                (fun () -> website.Value <- true)
              )

            let window = Window(Content = panel)
            window.Show()
            window)

        let! rendered =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "direct messages are off"))
          )

        Expect.isTrue rendered "the panel never rendered"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Turn on remotes") |> Option.iter click

            buttonWithText(window, "Open the Website layout")
            |> Option.iter click)

        let! fired =
          waitForAsync(
            5000,
            (fun () -> enabled.Value, website.Value),
            (fun (on, web) -> on && web)
          )

        Expect.isTrue fired "the panel buttons never fired"

        do! onUiAsync(fun () -> window.Close())
      })
  ]
