module DevNull.Client.UI.Tests.Unread

// The unread poller: one /me poll pushes the count into the session
// store, a failed poll keeps the last count, and the pause gate holds
// every request while the inbox is on screen.

open System
open System.Reactive.Subjects
open System.Threading

open Expecto

open DevNull.Client.Core.Http
open DevNull.Client.Stores
open DevNull.Client.UnreadPoller
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let meWithUnread(unread: int) : string =
  meJson.Replace(
    "\"unread_notifications_count\":3",
    $"\"unread_notifications_count\":{unread}"
  )

let meApi(unread: int) =
  FakeApi(fun request ->
    if request.Target = "/api/v1/me" then
      ok(meWithUnread unread)
    else
      ok "{}")

let tests =
  testList "unread poller" [
    test "a poll pushes the unread count into the session store" {
      let store = signedInStore()
      let api = meApi 7
      let env = TestEnv(store, api :> IHttp)
      let paused = new BehaviorSubject<bool>(false)

      use poller = DevNull.Client.UnreadPoller.run(env, paused, 20)

      let refreshed =
        waitFor(
          5000,
          (fun () -> store.Current),
          (function
          | SignedIn entry -> entry.Session.UnreadNotificationsCount = 7
          | SignedOut -> false)
        )

      Expect.isTrue refreshed "the store never took the polled count"
    }

    test "a failed poll keeps the last count" {
      let store = signedInStore()

      let api =
        FakeApi(fun _ -> {
          Status = 500
          Headers = []
          Body = "{}"
        })

      let env = TestEnv(store, api :> IHttp)
      let paused = new BehaviorSubject<bool>(false)

      use poller = DevNull.Client.UnreadPoller.run(env, paused, 20)

      let attempted =
        waitFor(
          2000,
          (fun () -> api.Requests),
          (fun requests -> not requests.IsEmpty)
        )

      Expect.isTrue attempted "the poller never sent a request"

      let count =
        probeUi(fun () ->
          match store.Current with
          | SignedIn entry -> entry.Session.UnreadNotificationsCount
          | SignedOut -> -1)

      Expect.equal count 3 "a failed poll changed the count"
    }

    test "a paused poller sends nothing until it resumes" {
      let store = signedInStore()
      let api = meApi 7
      let env = TestEnv(store, api :> IHttp)
      let paused = new BehaviorSubject<bool>(true)

      use poller = DevNull.Client.UnreadPoller.run(env, paused, 20)

      Thread.Sleep 150
      pump()

      Expect.isEmpty api.Requests "the paused poller sent a request"

      onUi(fun () -> paused.OnNext false)

      let resumed =
        waitFor(
          2000,
          (fun () -> api.Requests),
          (fun requests -> not requests.IsEmpty)
        )

      Expect.isTrue resumed "the poller never resumed"
    }
  ]
