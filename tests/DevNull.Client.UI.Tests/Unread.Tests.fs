module DevNull.Client.UI.Tests.Unread

// The unread service: one /me poll pushes the count into the session
// store, a failed poll keeps the last count, and the inbox-visible gate
// holds every request while the inbox is on screen.

open System
open System.Reactive.Subjects

open Expecto

open DevNull.Client.Core.Http
open DevNull.Client.Stores
open DevNull.Client.Unread
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
  testList "unread service" [
    testCaseAsync
      "a poll pushes the unread count into the session store"
      (async {
        let store = signedInStore()
        let api = meApi 7
        let env = TestEnv(store, api :> IHttp)
        let inboxVisible = new BehaviorSubject<bool>(false)

        use poller =
          (env :> IProvideUnread)
            .Unread.Start(inboxVisible, TimeSpan.FromMilliseconds 20.0)

        let! refreshed =
          waitForAsync(
            5000,
            (fun () -> store.Current),
            (function
            | SignedIn entry -> entry.Session.UnreadNotificationsCount = 7
            | SignedOut -> false)
          )

        Expect.isTrue refreshed "the store never took the polled count"
      })

    testCaseAsync
      "a failed poll keeps the last count"
      (async {
        let store = signedInStore()

        let api =
          FakeApi(fun _ -> {
            Status = 500
            Headers = []
            Body = "{}"
          })

        let env = TestEnv(store, api :> IHttp)
        let inboxVisible = new BehaviorSubject<bool>(false)

        use poller =
          (env :> IProvideUnread)
            .Unread.Start(inboxVisible, TimeSpan.FromMilliseconds 20.0)

        let! attempted =
          waitForAsync(
            2000,
            (fun () -> api.Requests),
            (fun requests -> not requests.IsEmpty)
          )

        Expect.isTrue attempted "the service never sent a request"

        let! count =
          probeUiAsync(fun () ->
            match store.Current with
            | SignedIn entry -> entry.Session.UnreadNotificationsCount
            | SignedOut -> -1)

        Expect.equal count 3 "a failed poll changed the count"
      })

    testCaseAsync
      "a paused service sends nothing until it resumes"
      (async {
        let store = signedInStore()
        let api = meApi 7
        let env = TestEnv(store, api :> IHttp)
        let inboxVisible = new BehaviorSubject<bool>(true)

        use poller =
          (env :> IProvideUnread)
            .Unread.Start(inboxVisible, TimeSpan.FromMilliseconds 20.0)

        do! Async.Sleep 150
        do! pumpAsync()

        Expect.isEmpty api.Requests "the paused service sent a request"

        do! onUiAsync(fun () -> inboxVisible.OnNext false)

        let! resumed =
          waitForAsync(
            2000,
            (fun () -> api.Requests),
            (fun requests -> not requests.IsEmpty)
          )

        Expect.isTrue resumed "the service never resumed"
      })
  ]
