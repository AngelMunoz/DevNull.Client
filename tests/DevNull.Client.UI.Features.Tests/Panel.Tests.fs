module DevNull.Client.UI.Tests.EditorPanel

open System
open Expecto
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Editor
open DevNull.Client.UI.Tests.Fixtures

let inbox =
  match NotificationPage.decode notificationsJson with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned inbox never decoded: {failure.Message}"

let sample = inbox.Page.Items |> List.head

let tests =
  testList "editor panel" [
    testCase "the panel opens and closes on each tab" (fun () ->
      for tab in
        [
          EditorPanel.Kind NotificationKind.Replied
          EditorPanel.Activity
          EditorPanel.Terminal
        ] do
        let opened = EditorPanel.toggle(tab, EditorPanel.closed)

        Expect.isTrue opened.Open $"the {tab} tab did not open"
        Expect.equal opened.Tab tab "the open tab"

        let closed = EditorPanel.toggle(tab, opened)

        Expect.isFalse closed.Open $"the {tab} tab did not close"
        Expect.equal closed.Tab tab "the kept tab"

        let selected = EditorPanel.select(tab, EditorPanel.closed)

        Expect.isTrue selected.Open "select did not open the panel"
        Expect.equal selected.Tab tab "select did not pick the tab")

    testCase "the overlay starts below 1000 px" (fun () ->
      Expect.isFalse (EditorPanel.overlay Full) "the full band overlays"

      Expect.isTrue
        (EditorPanel.overlay Folded)
        "the folded band does not overlay"

      Expect.isTrue
        (EditorPanel.overlay Trimmed)
        "the trimmed band does not overlay")

    testCase "notification kinds become tabs in their display order" (fun () ->
      let replied = {
        sample with
            Type = NotificationKind.Replied
      }

      let approved = {
        sample with
            Type = NotificationKind.Approved
      }

      let rejected = {
        sample with
            Type = NotificationKind.Rejected
      }

      let items = [ replied; approved; replied; rejected ]

      Expect.equal
        (EditorPanel.kindsOf items)
        [
          NotificationKind.Replied
          NotificationKind.Approved
          NotificationKind.Rejected
        ]
        "the kind order"

      Expect.equal
        (EditorPanel.notificationsOf(items, NotificationKind.Replied)).Length
        2
        "the reply rows"

      Expect.equal
        (EditorPanel.notificationsOf(items, NotificationKind.Approved)).Length
        1
        "the approval rows"

      Expect.equal
        (EditorPanel.effectiveTab(
          items,
          EditorPanel.Kind NotificationKind.Quoted
        ))
        (EditorPanel.Kind NotificationKind.Replied)
        "a missing kind did not fall to the first kind")

    testCase "only approved and rejected carry a mark" (fun () ->
      Expect.equal
        (EditorPanel.severityOf NotificationKind.Approved)
        (Some "✓")
        "the approval mark"

      Expect.equal
        (EditorPanel.severityOf NotificationKind.Rejected)
        (Some "✗")
        "the rejection mark"

      Expect.equal
        (EditorPanel.severityOf NotificationKind.Replied)
        None
        "a reply carried a mark")

    testCase "unread follows the server's count" (fun () ->
      let unread = { sample with UnreadCount = 2 }

      let read = { sample with UnreadCount = 0 }

      Expect.isTrue (EditorPanel.isUnread unread) "the unread row was read"
      Expect.isFalse (EditorPanel.isUnread read) "the read row was unread")

    testCase "only a live commit points a problem at a thread" (fun () ->
      let unlinked = { sample with Commit = None }

      Expect.isSome
        (EditorPanel.commitOf sample)
        "the linked row lost its commit"

      Expect.isNone
        (EditorPanel.commitOf unlinked)
        "the unlinked row gained a commit")

    testCase "the output keeps the newest lines at the cap" (fun () ->
      let lines =
        List.fold
          (fun held index ->
            EditorPanel.appendOutput(
              {
                Ref = string index
                Result = "ok"
                At = DateTimeOffset.UnixEpoch.AddSeconds(float index)
              },
              held
            )) [] [ 0 .. EditorPanel.outputCap + 9 ]

      Expect.equal lines.Length EditorPanel.outputCap "the output cap"

      Expect.equal (lines |> List.head |> _.Ref) "10" "the oldest line stayed"

      Expect.equal
        (lines |> List.last |> _.Ref)
        "59"
        "the newest line is not last")
  ]
