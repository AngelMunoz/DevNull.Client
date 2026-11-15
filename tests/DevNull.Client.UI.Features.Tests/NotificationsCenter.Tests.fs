module DevNull.Client.UI.Tests.NotificationsCenterTests

open System
open Expecto
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.ImageCache
open DevNull.Client.Workbench
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let images = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let page =
  match NotificationPage.decode notificationsJson with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned notifications never decoded: {failure.Message}"

let baseNote = page.Page.Items.Head

let props
  (env: TestEnv, onCheckout: string -> unit)
  : NotificationsCenter.Props<TestEnv> = {
  Env = env
  KeyFile = "/tmp/devnull-test-id_ed25519"
  Images = images
  Now = Observable.single(DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero))
  OnCheckout = onCheckout
}

let initialModel(onCheckout: string -> unit) =
  let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
  let p = props(env, onCheckout)
  let model, _ = NotificationsCenter.init p
  p, model

let shownModel(onCheckout: string -> unit) =
  let p, initial = initialModel onCheckout

  let model, _ =
    NotificationsCenter.update(p, NotificationsCenter.Fetched(Ok page), initial)

  p, model

let run(cmd: Cmd<NotificationsCenter.Msg>) =
  cmd (fun _ -> ()) (fun (_, ex) -> failwith ex.Message)

let tests =
  testList "workbench notifications center" [
    testCase "the kind lines use the site's words"
    <| fun () ->
      let line(kind: NotificationKind) =
        NotificationsCenter.kindLine {
          baseNote with
              Type = kind
              Organization = None
        }

      Expect.equal (line NotificationKind.Followed) "followed you" "followed"

      Expect.equal
        (line NotificationKind.Replied)
        "replied to your commit"
        "replied"

      Expect.equal (line NotificationKind.Quoted) "quoted your commit" "quoted"

      Expect.equal
        (line NotificationKind.Starred)
        "starred your commit"
        "starred"

      Expect.equal (line NotificationKind.Mentioned) "mentioned you" "mentioned"
      Expect.equal (line NotificationKind.LeveledUp) "sent an update" "level"

      Expect.equal
        (line NotificationKind.Approved)
        "Your commit was approved"
        "approved"

      Expect.equal
        (line NotificationKind.Rejected)
        "Your commit was rejected"
        "rejected"

      Expect.equal
        (line NotificationKind.FirstCommit)
        "made their first commit"
        "first"

      Expect.equal
        (line(NotificationKind.UnknownKind "mystery"))
        "sent an update"
        "unknown"

    testCase "the group line counts the API's events"
    <| fun () ->
      Expect.equal
        (NotificationsCenter.groupLine {
          baseNote with
              Type = NotificationKind.Starred
              EventCount = 3
        })
        (Some "3 stars")
        "starred group"

      Expect.equal
        (NotificationsCenter.groupLine {
          baseNote with
              Type = NotificationKind.Followed
              EventCount = 2
        })
        (Some "2 followers")
        "followed group"

      Expect.equal
        (NotificationsCenter.groupLine { baseNote with EventCount = 1 })
        None
        "single event"

    testCase "the unread flag stays with the server's count"
    <| fun () ->
      Expect.isFalse
        (NotificationsCenter.isUnread { baseNote with UnreadCount = 0 })
        "read"

      Expect.isTrue
        (NotificationsCenter.isUnread { baseNote with UnreadCount = 2 })
        "unread"

    testCase "a commit row checks out its commit"
    <| fun () ->
      let checkedOut = ResizeArray<string>()
      let p, model = shownModel checkedOut.Add

      let model, cmd =
        NotificationsCenter.update(
          p,
          NotificationsCenter.Opened baseNote,
          model
        )

      run cmd

      Expect.equal
        (checkedOut |> Seq.toList)
        [ "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d" ]
        "the checkout sink"

      let noCommit = { baseNote with Commit = None }

      let model, cmd =
        NotificationsCenter.update(
          p,
          NotificationsCenter.Opened noCommit,
          model
        )

      run cmd

      Expect.equal
        (checkedOut |> Seq.toList)
        [ "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d" ]
        "the no-commit row moved the checkout"

    testCase "the inbox keeps the API order and appends its cursor"
    <| fun () ->
      let p, model = initialModel ignore
      Expect.isTrue model.Loading "the inbox started reading"

      let first = {
        page with
            Page = {
              page.Page with
                  NextCursor = Some "next"
            }
      }

      let model, _ =
        NotificationsCenter.update(
          p,
          NotificationsCenter.Fetched(Ok first),
          model
        )

      Expect.equal model.Items first.Page.Items "the first page"
      Expect.equal model.Cursor (Some "next") "the cursor"
      Expect.isFalse model.Loading "the first page settled"

      let model, _ =
        NotificationsCenter.update(p, NotificationsCenter.MoreRequested, model)

      Expect.isTrue model.More "the append started"

      let second = {
        page with
            Page = {
              page.Page with
                  NextCursor = None
                  Items = [ { baseNote with Id = "second" } ]
            }
      }

      let model, _ =
        NotificationsCenter.update(
          p,
          NotificationsCenter.Appended(Ok second),
          model
        )

      Expect.equal
        model.Items
        (first.Page.Items @ second.Page.Items)
        "the appended order stayed"

      Expect.isNone model.Cursor "the cursor is spent"

    testCase "a manual refresh keeps the server's read state"
    <| fun () ->
      let p, model = shownModel ignore
      let unread = model.Items |> List.map _.UnreadCount

      let model, _ =
        NotificationsCenter.update(
          p,
          NotificationsCenter.RefreshRequested,
          model
        )

      Expect.isTrue model.Loading "the refresh did not start"

      let model, _ =
        NotificationsCenter.update(
          p,
          NotificationsCenter.Fetched(Ok page),
          model
        )

      Expect.equal
        (model.Items |> List.map _.UnreadCount)
        unread
        "the read state moved"
  ]
