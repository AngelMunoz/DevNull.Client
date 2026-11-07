module DevNull.Client.UI.Tests.CommitMenu

// The commit card's More menu over the real shell: the site's rows and
// their gates, the relation read that arms the author items, the
// follow, mute, and block calls with their words, the delete ask with
// its undo, the clicks that never open the card behind them, and the
// names every control carries.

open System
open System.Threading.Tasks
open Avalonia.Automation
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.RouteStore
open DevNull.Client.Shell
open DevNull.Client.Theme
open DevNull.Client.UI.Tests.App
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness
open DevNull.Client.UI.Tests.Report
open DevNull.Client.UI.Tests.Share

/// The canned commit with hubot as its author, so the viewer and the
/// menu's author read apart.
let hubotCommit =
  commitJson
    .Replace(
      "\"id\":\"01a151db-7fee-727b-a7ce-c911a7556242\"",
      "\"id\":\"01a151db-7fee-727b-a7ce-c911a7556243\""
    )
    .Replace("\"handle\":\"octocat\"", "\"handle\":\"hubot\"")
    .Replace("\"name\":\"The Octocat\"", "\"name\":\"The Hubot\"")

let hubotPage =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
    hubotCommit

let hubotPageWithStatus(status: string) =
  hubotPage.Replace("\"status\":\"approved\"", $"\"status\":\"{status}\"")

/// One commit whose poll still takes votes, so the choice toggles
/// stand in the card.
let pollPage =
  sprintf
    "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
    (strangerPollCommit openPoll)

/// The profile of hubot with the relation flags a test picks.
let hubotProfileWith(following: bool, muting: bool) =
  profileJson
    .Replace(
      "\"id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\"",
      "\"id\":\"01a151db-7fee-727b-a7ce-c911a7556243\""
    )
    .Replace("\"handle\":\"octocat\"", "\"handle\":\"hubot\"")
    .Replace("\"name\":\"The Octocat\"", "\"name\":\"The Hubot\"")
    .Replace("\"is_following\":false", $"\"is_following\":{following}")
    .Replace("\"is_muting\":false", $"\"is_muting\":{muting}")

/// hubot as the bare user the mute and block answers carry.
let hubotUser =
  apiUserJson
    .Replace(
      "\"id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\"",
      "\"id\":\"01a151db-7fee-727b-a7ce-c911a7556243\""
    )
    .Replace("\"handle\":\"octocat\"", "\"handle\":\"hubot\"")
    .Replace("\"name\":\"The Octocat\"", "\"name\":\"The Hubot\"")

let muteAnswer(mut: bool) =
  ok(sprintf "{\"data\":%s,\"is_muting\":%b}" hubotUser mut)

let blockAnswer(block: bool) =
  ok(sprintf "{\"data\":%s,\"is_blocking\":%b}" hubotUser block)

/// The api of the menu tests: the log answers its canned page, the
/// profile read answers the test's own flags, and the follow, mute,
/// and block endpoints answer the test's own responses.
let menuApi
  (
    profile: string,
    follow: HttpResponse,
    mute: HttpResponse,
    block: HttpResponse
  ) : FakeApi =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok hubotPage
    elif request.Target = "/api/v1/users/hubot" then
      ok profile
    elif request.Target = "/api/v1/users/hubot/follow" then
      follow
    elif request.Target = "/api/v1/users/hubot/mute" then
      mute
    elif request.Target = "/api/v1/users/hubot/block" then
      block
    elif
      request.Method = "DELETE" && request.Target.StartsWith "/api/v1/commits/"
    then
      ok ""
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The canned profile as the page's own profile worker answers it.
let profileDetailsOf(json: string) : ProfileDetails =
  match ProfileDetails.decode json with
  | Ok details -> details
  | Error failure ->
    failwith $"the canned profile never decoded: {failure.Message}"

/// The requests one endpoint took, in order.
let requestsTo(api: FakeApi, target: string) =
  api.Requests |> List.filter(fun request -> request.Target = target)

/// The enabled state of one menu row, through the wrapping panels: a
/// destructive ask disables from its panel, so the row reads the
/// effective value the input pipeline obeys.
let rowEnabled(window: Window, label: string) : bool option =
  menuRow(window, label) |> Option.map _.IsEffectivelyEnabled

let tests =
  testList "commit card menu" [
    testCaseAsync
      "a stranger's approved commit carries the site's rows"
      (async {
        let api =
          menuApi(
            hubotProfileWith(false, false),
            noContent,
            muteAnswer false,
            blockAnswer false
          )

        let! window = startShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"

        do! openMenuAsync window

        let! rows =
          waitForAsync(
            5000,
            (fun () ->
              menuRow(window, "Copy link"),
              menuRow(window, "Send in Remotes"),
              menuRow(window, "Follow @hubot"),
              menuRow(window, "Mute @hubot"),
              menuRow(window, "Block @hubot"),
              menuRow(window, "Report commit")),
            fun (copy, share, follow, mute, block, report) ->
              Option.isSome copy
              && Option.isSome share
              && Option.isSome follow
              && Option.isSome mute
              && Option.isSome block
              && Option.isSome report
          )

        Expect.isTrue rows "the stranger's menu did not carry every site row"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "the viewer's own commit carries delete and no author rows"
      (async {
        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/log/" then
              ok ownLogJson
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let! window = startShell(remotesStoreOn(), api :> IHttp)

        let! shown = showsText(window, "My own commit.")

        Expect.isTrue shown "the log never rendered"

        do! openMenuAsync window
        do! settleAsync()

        let! rows =
          probeUiAsync(fun () ->
            menuContent window
            |> Option.map(fun content ->
              content.GetVisualDescendants()
              |> Seq.choose(fun visual ->
                match visual with
                | :? Button as button -> Some(string button.Content)
                | _ -> None)
              |> List.ofSeq))

        Expect.isSome rows "the open menu never carried its rows"

        Expect.equal
          rows
          (Some [ "Copy link"; "Send in Remotes"; "Delete commit" ])
          "the own commit's menu carried the wrong rows"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a pending commit carries neither send nor report"
      (async {
        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/log/" then
              ok(hubotPageWithStatus "pending")
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let! window = startShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"

        do! openMenuAsync window
        do! settleAsync()

        let! absent =
          probeUiAsync(fun () ->
            menuRow(window, "Send in Remotes") |> Option.isNone
            && menuRow(window, "Report commit") |> Option.isNone)

        Expect.isTrue absent "the pending card offered send or report"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "the remotes off hide send"
      (async {
        let api =
          menuApi(
            hubotProfileWith(false, false),
            noContent,
            muteAnswer false,
            blockAnswer false
          )

        let! window = startShell(signedInStore(), api :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"

        do! openMenuAsync window
        do! settleAsync()

        let! absent =
          probeUiAsync(fun () ->
            menuRow(window, "Send in Remotes") |> Option.isNone)

        Expect.isTrue absent "the card offered send with the remotes off"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "opening the menu asks for the author and arms the items"
      (async {
        let gate = TaskCompletionSource<unit>()

        let profiles =
          FakeProfiles(
            Details = Some(profileDetailsOf profileJson),
            Gate = Some gate
          )

        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/log/" then
              ok hubotPage
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let env = TestEnv(remotesStoreOn(), api :> IHttp, profiles = profiles)

        let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
        let! _ = showAsync window
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"

        do! openMenuAsync window

        let! asked =
          waitForAsync(
            5000,
            (fun () -> profiles.Asked),
            fun names -> names |> List.contains "hubot"
          )

        Expect.isTrue asked "the menu never asked for the author"

        // The disabled binding lands a beat after the ask, so the
        // probe pumps once before it reads the rows.
        do! settleAsync()

        let! waiting =
          probeUiAsync(fun () ->
            rowEnabled(window, "Follow @hubot"),
            rowEnabled(window, "Mute @hubot"),
            rowEnabled(window, "Block @hubot"))

        Expect.equal
          waiting
          (Some false, Some false, Some false)
          "the author items armed before the relation landed"

        gate.SetResult(())

        let! armed =
          waitForAsync(
            5000,
            (fun () ->
              rowEnabled(window, "Follow @hubot"),
              rowEnabled(window, "Mute @hubot")),
            fun (follow, mute) -> follow = Some true && mute = Some true
          )

        Expect.isTrue armed "the author items never armed"

        let! words =
          probeUiAsync(fun () ->
            menuRow(window, "Follow @hubot") |> Option.isSome,
            menuRow(window, "Unfollow @hubot") |> Option.isNone,
            menuRow(window, "Mute @hubot") |> Option.isSome,
            menuRow(window, "Unmute @hubot") |> Option.isNone)

        let followShown, unfollowAbsent, muteShown, unmuteAbsent = words

        Expect.isTrue followShown "the not-following word never showed"

        Expect.isTrue
          unfollowAbsent
          "the held word showed while the relation holds false"

        Expect.isTrue muteShown "the not-muting word never showed"

        Expect.isTrue
          unmuteAbsent
          "the held word showed while the relation holds false"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "follow posts, unfollow deletes, and the words flip"
      (async {
        let api =
          menuApi(
            hubotProfileWith(false, false),
            noContent,
            muteAnswer false,
            blockAnswer false
          )

        let! window = startShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"

        do! openMenuAsync window
        do! clickMenuRowAsync(window, "Follow @hubot")

        let! posted =
          waitForAsync(
            5000,
            (fun () -> requestsTo(api, "/api/v1/users/hubot/follow")),
            fun calls ->
              calls |> List.exists(fun request -> request.Method = "POST")
          )

        Expect.isTrue posted "the follow never posted"

        let! flipped =
          waitForAsync(
            5000,
            (fun () -> menuRow(window, "Unfollow @hubot")),
            Option.isSome
          )

        Expect.isTrue flipped "the row never read Unfollow"

        do! clickMenuRowAsync(window, "Unfollow @hubot")

        let! deleted =
          waitForAsync(
            8000,
            (fun () -> requestsTo(api, "/api/v1/users/hubot/follow")),
            fun calls ->
              calls |> List.exists(fun request -> request.Method = "DELETE")
          )

        Expect.isTrue deleted "the unfollow never deleted"

        let! rolled =
          waitForAsync(
            5000,
            (fun () -> menuRow(window, "Follow @hubot")),
            Option.isSome
          )

        Expect.isTrue rolled "the word never rolled back"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "a refused follow rolls the word back"
      (async {
        let api =
          menuApi(
            hubotProfileWith(false, false),
            refused(500, "the server said no"),
            muteAnswer false,
            blockAnswer false
          )

        let! window = startShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"

        do! openMenuAsync window
        do! clickMenuRowAsync(window, "Follow @hubot")

        let! posted =
          waitForAsync(
            8000,
            (fun () -> requestsTo(api, "/api/v1/users/hubot/follow")),
            not << List.isEmpty
          )

        Expect.isTrue posted "the follow never posted"

        let! rolled =
          waitForAsync(
            8000,
            (fun () ->
              menuRow(window, "Unfollow @hubot") |> Option.isNone,
              menuRow(window, "Follow @hubot") |> Option.isSome),
            fun (unfollowGone, followBack) -> unfollowGone && followBack
          )

        Expect.isTrue rolled "the word never rolled back after the refusal"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "mute posts and the answer's flag sets the word"
      (async {
        let api =
          menuApi(
            hubotProfileWith(false, false),
            noContent,
            muteAnswer true,
            blockAnswer false
          )

        let! window = startShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"

        do! openMenuAsync window
        do! clickMenuRowAsync(window, "Mute @hubot")

        let! posted =
          waitForAsync(
            5000,
            (fun () -> requestsTo(api, "/api/v1/users/hubot/mute")),
            fun calls ->
              calls |> List.exists(fun request -> request.Method = "POST")
          )

        Expect.isTrue posted "the mute never posted"

        let! flipped =
          waitForAsync(
            5000,
            (fun () -> menuRow(window, "Unmute @hubot")),
            Option.isSome
          )

        Expect.isTrue flipped "the answer's flag never set the word"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "block asks with the site's words and confirms with a post"
      (async {
        let api =
          menuApi(
            hubotProfileWith(false, false),
            noContent,
            muteAnswer false,
            blockAnswer true
          )

        let! window = startShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "The Hubot")

        Expect.isTrue shown "the log never rendered"

        do! openMenuAsync window
        do! clickMenuRowAsync(window, "Block @hubot")

        let! ask =
          waitForAsync(5000, (fun () -> confirmContent window), Option.isSome)

        Expect.isTrue ask "the block ask never stood"

        let! words =
          probeUiAsync(fun () ->
            hasText(window, "Block @hubot?"),
            hasText(
              window,
              "You'll stop following each other. Neither of you will be able to message, follow, reply to, or star the other, or see each other's commits. They won't be told, but they'll see your profile as unavailable. You can unblock them any time from your settings."
            ))

        Expect.equal words (true, true) "the ask never said the site's words"

        do!
          onUiAsync(fun () ->
            confirmContent window
            |> Option.bind(fun card -> buttonWithText(card, "Block"))
            |> Option.iter click)

        let! posted =
          waitForAsync(
            5000,
            (fun () -> requestsTo(api, "/api/v1/users/hubot/block")),
            fun calls ->
              calls |> List.exists(fun request -> request.Method = "POST")
          )

        Expect.isTrue posted "the confirmed block never posted"

        let! flipped =
          waitForAsync(
            5000,
            (fun () -> menuRow(window, "Unblock @hubot")),
            Option.isSome
          )

        Expect.isTrue flipped "the result never set the word"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "delete asks, confirms with a delete, and the undo lands it back"
      (async {
        let api = undoApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "My own commit.")

        Expect.isTrue shown "the log never rendered"

        do! openOwnMenu window
        do! clickMenuRowAsync(window, "Delete commit")

        let! ask =
          waitForAsync(5000, (fun () -> confirmContent window), Option.isSome)

        Expect.isTrue ask "the delete ask never stood"

        let! words =
          probeUiAsync(fun () ->
            hasText(window, "Delete this commit?"),
            hasText(window, "You can undo for 30 seconds."))

        Expect.equal words (true, true) "the ask never said the site's words"

        do!
          onUiAsync(fun () ->
            confirmContent window
            |> Option.bind(fun card -> buttonWithText(card, "Delete"))
            |> Option.iter click)

        let! deleted =
          waitForAsync(
            8000,
            (fun () ->
              api.Requests
              |> List.exists(fun request ->
                request.Method = "DELETE"
                && request.Target.StartsWith "/api/v1/commits/018f4c1e-0aa0")),
            id
          )

        Expect.isTrue deleted "the confirmed delete never went out"

        let! folded =
          waitForAsync(
            5000,
            (fun () -> hasText(window, "This commit was deleted.")),
            id
          )

        let! own, stranger, toast =
          probeUiAsync(fun () ->
            hasText(window, "My own commit."),
            hasText(window, "It was DNS."),
            hasText(window, "Commit deleted."))

        Expect.isTrue
          folded
          $"the row never folded to its marker; own={own} stranger={stranger} toast={toast}"

        Expect.isTrue toast "the undo toast never stood"

        do! clickWhenShown(window, "Undo")

        let! restored =
          waitForAsync(
            8000,
            (fun () ->
              api.Requests
              |> List.exists(fun request ->
                request.Method = "POST"
                && request.Target.EndsWith "/restoration")),
            id
          )

        Expect.isTrue restored "the undo never restored"

        let! gone =
          waitForAsync(
            10000,
            (fun () ->
              not(hasText(window, "This commit was deleted.")),
              api.Requests
              |> List.exists(fun request ->
                request.Method = "GET"
                && request.Target.StartsWith "/api/v1/log/")),
            fun (markerLeft, reloaded) -> markerLeft && reloaded
          )

        Expect.isTrue gone "the reload never took the marker away"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "card actions never open the card behind them"
      (async {
        let api =
          menuApi(
            hubotProfileWith(false, false),
            noContent,
            muteAnswer false,
            blockAnswer false
          )

        let env = TestEnv(remotesStoreOn(), api :> IHttp)

        let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
        let! _ = showAsync window

        let paths = ResizeArray<string>()

        do!
          onUiAsync(fun () ->
            (env :> IProvideRoutes).Routes.Path.Subscribe(paths.Add) |> ignore)

        do! settleAsync()

        let! ready = showsText(window, "The Hubot")

        Expect.isTrue ready "the log never rendered"

        let clickByName(name: string) =
          onUiAsync(fun () ->
            controlByName(window, name)
            |> Option.iter(fun control -> click(control :?> Button)))

        do! clickByName "ReplyButton"
        do! clickByName "StarButton"
        do! clickByName "PastelButton"
        do! openMenuAsync window
        do! clickMenuRowAsync(window, "Copy link")
        do! clickMenuRowAsync(window, "Follow @hubot")
        do! clickMenuRowAsync(window, "Mute @hubot")
        do! clickMenuRowAsync(window, "Block @hubot")

        let! cancelStood =
          waitForAsync(5000, (fun () -> confirmContent window), Option.isSome)

        Expect.isTrue cancelStood "the block ask never stood"

        do!
          onUiAsync(fun () ->
            confirmContent window
            |> Option.bind(fun card -> buttonWithText(card, "Cancel"))
            |> Option.iter click)

        do! clickMenuRowAsync(window, "Send in Remotes")
        do! settleAsync()

        let opened =
          paths
          |> Seq.filter(fun path -> path.StartsWith "/commit/")
          |> Seq.length

        Expect.equal opened 0 "a card action opened the card behind it"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "It was DNS.")
            |> Option.iter(fun card -> click(card :?> Button)))

        let! walked =
          waitForAsync(
            5000,
            (fun () ->
              paths
              |> Seq.filter(fun path -> path.StartsWith "/commit/")
              |> Seq.length),
            fun count -> count > 0
          )

        Expect.isTrue walked "the counter never armed"

        do! onUiAsync(fun () -> window.Close())
      })

    testCaseAsync
      "every icon-only card control carries its name"
      (async {
        let api =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/log/" then
              ok pollPage
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let! window = startShell(remotesStoreOn(), api :> IHttp)
        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        let! named =
          waitForAsync(
            5000,
            (fun () ->
              controlByName(window, "PollChoiceo1"),
              controlByName(window, "PollChoiceo2")),
            fun (first, second) -> Option.isSome first && Option.isSome second
          )

        Expect.isTrue named "the poll toggles never stood"

        let! names =
          probeUiAsync(fun () ->
            let nameOf(name: string) =
              controlByName(window, name)
              |> Option.map(fun control ->
                AutomationProperties.GetName control |> Option.ofObj)

            nameOf "ReplyButton",
            nameOf "StarButton",
            nameOf "PastelButton",
            nameOf "QuoteButton",
            nameOf "PollChoiceo1",
            nameOf "PollChoiceo2",
            nameOf "CardImageButton",
            nameOf "CommitMenuButton")

        let reply, star, pastel, quote, yes, no, image, trigger = names

        Expect.equal
          reply
          (Some(Some "Reply"))
          "the reply control carried no name"

        Expect.equal star (Some(Some "Star")) "the star control carried no name"

        Expect.equal
          pastel
          (Some(Some "Pastel"))
          "the pastel control carried no name"

        Expect.equal
          quote
          (Some(Some "Quote"))
          "the quote control carried no name"

        Expect.equal yes (Some(Some "Yes")) "the first choice carried no name"
        Expect.equal no (Some(Some "No")) "the second choice carried no name"

        Expect.equal
          image
          (Some(Some "Commit image"))
          "the image control carried no name"

        Expect.equal
          trigger
          (Some(Some "More actions"))
          "the menu trigger carried no name"

        let! rowName =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? Button as button when
                button.Classes.Contains(Theme.name Theme.CommitRow)
                ->
                Some(AutomationProperties.GetName button |> Option.ofObj)
              | _ -> None))

        Expect.equal
          rowName
          (Some(Some "Commit b8b9c0d by @hubot"))
          "the card row carried no name"

        do! onUiAsync(fun () -> window.Close())
      })
  ]
