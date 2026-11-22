module DevNull.Client.UI.Tests.PostAsTests

// Headless tests for posting as an organization: the composer's
// identity picker and the ownership gates on organization commits.

open System
open System.Text.Json
open System.Text.Json.Nodes
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
open JDeck

open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let devnullOrg: MeOrganization = {
  Id = "01a151db-aaaa-727b-a7ce-c911a7556242"
  Handle = "devnull"
  Name = "DevNull"
  AvatarUrl = Some "https://avatars.example/devnull.png"
  Bio = Some "The org."
  Location = Some "Berlin"
  Website = Some "https://dev-null.sh"
  GithubUrl = Some "https://github.com/devnull"
  HtmlUrl = "https://dev-null.sh/org/devnull"
  Lane = Some(LaneIndex 2)
  Role = Admin
}

let opsOrg: MeOrganization = {
  Id = "01a151db-bbbb-727b-a7ce-c911a7556242"
  Handle = "ops"
  Name = "Ops Room"
  AvatarUrl = None
  Bio = None
  Location = None
  Website = None
  GithubUrl = None
  HtmlUrl = "https://dev-null.sh/org/ops"
  Lane = Some(LaneColor "#b34a2e")
  Role = Member
}

/// One signed-in session carrying the membership rows, with the
/// store the test env needs beside it.
let signedInWith(orgs: MeOrganization list) : SignedInSession * SessionStore =
  let session = {
    sessionOf "octocat" with
        Organizations = orgs
  }

  let store =
    SessionStore.create {
      Initial = Some session
      KeyFile = DeviceAuth.devicePrivateKeyPath
      Forget = ignore
      Dispatcher = Harness.ui
    }

  {
    Session = session
    KeyFile = DeviceAuth.devicePrivateKeyPath
  },
  store

let named(root: Control, name: string) : Control option =
  root.GetVisualDescendants()
  |> Seq.tryFind(fun visual -> visual.Name = name)
  |> Option.map(fun visual -> visual :?> Control)

let clickNamed(root: Control, name: string) : Async<unit> =
  onUiAsync(fun () ->
    named(root, name)
    |> Option.iter(function
      | :? Button as button -> click button
      | _ -> ()))

/// The post-as menu's card, once the trigger stood it open.
let authorCardOf(window: Window) : Control option =
  named(window, "ComposerAuthorDialog")
  |> Option.bind(function
    | :? Popup as popup -> Option.ofObj popup.Child
    | _ -> None)

/// The More menu's card, once its trigger stood it open.
let menuCardOf(window: Window) : Control option =
  named(window, "CommitMenuFlyout")
  |> Option.bind(function
    | :? Popup as popup -> Option.ofObj popup.Child
    | _ -> None)

/// Clicks one row of the post-as menu by its name, resolving the
/// open card on the session thread.
let clickAuthorRow(window: Window, name: string) : Async<unit> =
  onUiAsync(fun () ->
    authorCardOf window
    |> Option.bind(fun card -> named(card, name))
    |> Option.iter(function
      | :? Button as button -> click button
      | _ -> ()))

/// One row of the post-as menu, waited out: the rows land a beat
/// after the dialog stands.
let waitAuthorRow(window: Window, name: string) : Async<bool> =
  waitForAsync(
    5000,
    (fun () ->
      authorCardOf window |> Option.bind(fun card -> named(card, name))),
    Option.isSome
  )

/// True while the composer's avatar wears the square organization
/// backing instead of the round user disc.
let avatarSquare(window: Window) : bool option =
  named(window, "ComposerAvatar")
  |> Option.map(fun avatar ->
    avatar.GetVisualDescendants()
    |> Seq.exists(function
      | :? Border -> true
      | _ -> false))

let mountComposer(entry: SignedInSession, env: TestEnv) : Async<Window> =
  probeUiAsync(fun () ->
    let page =
      Composer.Page {
        Env = env
        Session = entry
        Target = Composer.Fresh
        Participants = ValueNone
        Guilds = ValueNone
        Guild = ValueNone
        OnCommitted = ignore
        OnScheduled = ValueNone
        OnOpenQueue = ValueNone
        Draft = ValueNone
        OnBodyReady = ValueNone
      }

    let window = Window(Width = 480.0, Height = 640.0, Content = page)
    window.Show()
    window)

/// One organization-authored commit, from the canned person commit
/// with its author swapped for the org.
let orgCommitJson =
  commitJson.Replace(
    "\"author\":{\"id\":\"01a151db-7fee-727b-a7ce-c911a7556242\",\"handle\":\"octocat\",\"name\":\"The Octocat\",\"avatar_url\":null}",
    "\"author\":{\"type\":\"organization\",\"handle\":\"devnull\",\"name\":\"DevNull\",\"avatar_url\":\"https://avatars.example/devnull.png\",\"bio\":\"The org.\",\"website\":\"https://dev-null.sh\",\"github_url\":null,\"html_url\":\"https://dev-null.sh/org/devnull\",\"is_followed\":false,\"can_claim\":false,\"settings_url\":null,\"lane\":null,\"followers_count\":11,\"admins\":[{\"id\":\"018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d\",\"handle\":\"octocat\",\"name\":\"The Octocat\",\"avatar_url\":null}]}"
  )

let orgCommit() : Commit =
  match
    CommitNode.decodePage(
      sprintf
        "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
        orgCommitJson
    )
  with
  | Ok page ->
    match page.Items |> List.exactlyOne with
    | LiveCommit commit -> commit
    | Tombstone _ -> failtest "expected a live commit"
  | Error failure -> failtestf "%A" failure

let mountCard(env: TestEnv, commit: Commit, orgs: string list) : Async<Window> =
  probeUiAsync(fun () ->
    let rows: CommitCard.RowProps = {
      OnEvent = ignore
      OnOpenExternal = ignore
      ViewerId = (sessionOf "octocat").User.Id
      ViewerOrgs = orgs
      Pasteis = Observable.single false
      Now = Observable.single DateTimeOffset.UtcNow
      PollChoices = Observable.single Map.empty
      Dense = false
      Images = (env :> IProvideImageCache).ImageCache
      Flyout = ValueNone
      Pin =
        ValueSome {
          Command = ignore
          Busy = Observable.single None
        }
      Star = ValueNone
      Stars = ValueNone
      Vote = ValueNone
      Pastel = ValueNone
      Menu =
        ValueSome {
          Share = ValueNone
          Report = ValueNone
          Delete = ValueSome ignore
          ToggleFollow = ignore
          ToggleMute = ignore
          Block = ignore
          Relations = Observable.single(fun _ -> ValueNone)
          Busy = Observable.single None
        }
      Translation = Observable.single(fun _ -> ValueNone)
      Zone = {
        Info = TimeZoneInfo.Utc
        Label = "UTC"
      }
    }

    let window =
      Window(
        Width = 480.0,
        Height = 640.0,
        Content = CommitCard.feedRow rows (LiveCommit commit)
      )

    window.Show()
    window)

let tests =
  testList "post as organization" [
    testCaseAsync
      "the avatar stands alone without memberships"
      (async {
        let entry, store = signedInWith []
        let env = TestEnv(store, logApi())

        let! window = mountComposer(entry, env)

        let! avatar =
          waitForAsync(
            5000,
            (fun () -> named(window, "ComposerAvatar")),
            Option.isSome
          )

        Expect.isTrue avatar "the avatar never stood"

        let! trigger =
          probeUiAsync(fun () -> named(window, "ComposerAuthorTrigger"))

        Expect.isNone trigger "no chevron without memberships"

        let! dialog =
          probeUiAsync(fun () -> named(window, "ComposerAuthorDialog"))

        Expect.isNone dialog "no menu without memberships"
      })

    testCaseAsync
      "a pick swaps the avatar to the org"
      (async {
        let entry, store = signedInWith [ devnullOrg; opsOrg ]
        let env = TestEnv(store, logApi())

        let! window = mountComposer(entry, env)

        let! avatar =
          waitForAsync(
            5000,
            (fun () -> named(window, "ComposerAvatar")),
            Option.isSome
          )

        Expect.isTrue avatar "the avatar never stood"

        let! round = probeUiAsync(fun () -> avatarSquare window)
        Expect.equal (Some false) round "the user avatar is round"

        do! clickNamed(window, "ComposerAuthorTrigger")

        let! menu =
          waitForAsync(5000, (fun () -> authorCardOf window), Option.isSome)

        Expect.isTrue menu "the menu never opened"

        let! userRow = waitAuthorRow(window, "ComposerAuthorUser")
        Expect.isTrue userRow "the user row never stood"

        let! orgRow = waitAuthorRow(window, "ComposerAuthordevnull")
        Expect.isTrue orgRow "the org row never stood"

        do! clickAuthorRow(window, "ComposerAuthordevnull")

        let! square =
          waitForAsync(5000, (fun () -> avatarSquare window), ((=)(Some true)))

        Expect.isTrue square "the avatar never swapped to the org"
      })

    testCaseAsync
      "a pick rides the post and the choice resets after"
      (async {
        let entry, store = signedInWith [ devnullOrg; opsOrg ]
        let http = logApi()
        let env = TestEnv(store, http)

        let! window = mountComposer(entry, env)
        do! clickNamed(window, "ComposerAuthorTrigger")

        let! menu =
          waitForAsync(5000, (fun () -> authorCardOf window), Option.isSome)

        Expect.isTrue menu "the menu never opened"

        do! clickAuthorRow(window, "ComposerAuthordevnull")

        let! square =
          waitForAsync(5000, (fun () -> avatarSquare window), ((=)(Some true)))

        Expect.isTrue square "the avatar never swapped to the org"

        do!
          onUiAsync(fun () ->
            named(window, "ComposerBody")
            |> Option.iter(function
              | :? TextBox as box -> box.Text <- "Hello org"
              | _ -> ()))

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Commit") |> Option.iter click)

        let! posted =
          waitForAsync(
            5000,
            (fun () ->
              http.Requests
              |> List.tryFind(fun request ->
                request.Target = "/api/v1/commits")),
            Option.isSome
          )

        Expect.isTrue posted "the post never left"

        let body =
          http.Requests
          |> List.tryFind(fun request -> request.Target = "/api/v1/commits")
          |> Option.bind(fun request ->
            match request.Body with
            | Text text -> Some text
            | _ -> None)

        Expect.equal
          (Some "{\"body\":\"Hello org\",\"author\":\"devnull\"}")
          body
          "the author rode the post"

        let! round =
          waitForAsync(5000, (fun () -> avatarSquare window), ((=)(Some false)))

        Expect.isTrue round "the choice never reset after the post"
      })

    testCaseAsync
      "the delete and pin rows stand for a member-owned org commit"
      (async {
        let _, store = signedInWith [ devnullOrg ]
        let env = TestEnv(store, logApi())

        let! window = mountCard(env, orgCommit(), [ "devnull" ])

        let! rendered =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "It was DNS."))
          )

        Expect.isTrue rendered "the card never rendered"

        let! pin = probeUiAsync(fun () -> named(window, "PinButton"))
        Expect.isTrue pin.IsSome "the pin never stood for a member"

        do! clickNamed(window, "CommitMenuButton")

        let! menu =
          waitForAsync(5000, (fun () -> menuCardOf window), Option.isSome)

        Expect.isTrue menu "the menu never opened"

        let! delete =
          waitForAsync(
            5000,
            (fun () ->
              menuCardOf window
              |> Option.bind(fun card -> buttonWithText(card, "Delete commit"))),
            Option.isSome
          )

        Expect.isTrue delete "the delete never stood for a member"
      })

    testCaseAsync
      "the delete and pin rows stay down without a membership"
      (async {
        let _, store = signedInWith []
        let env = TestEnv(store, logApi())

        let! window = mountCard(env, orgCommit(), [])

        let! rendered =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> hasText(root, "It was DNS."))
          )

        Expect.isTrue rendered "the card never rendered"

        let! pin = probeUiAsync(fun () -> named(window, "PinButton"))
        Expect.isNone pin "the pin stood with no membership"

        do! clickNamed(window, "CommitMenuButton")

        let! menu =
          waitForAsync(5000, (fun () -> menuCardOf window), Option.isSome)

        Expect.isTrue menu "the menu never opened"

        let! copy =
          waitForAsync(
            5000,
            (fun () ->
              menuCardOf window
              |> Option.bind(fun card -> buttonWithText(card, "Copy link"))),
            Option.isSome
          )

        Expect.isTrue copy "the menu rows never stood"

        let! delete =
          probeUiAsync(fun () ->
            menuCardOf window
            |> Option.bind(fun card -> buttonWithText(card, "Delete commit")))

        Expect.isNone delete "the delete stood with no membership"
      })

    test "the session file keeps the membership rows" {
      let stored = JsonObject()

      stored.["organizations"] <-
        Json.sequence([ devnullOrg; opsOrg ], Me.encodeOrganization)

      use document = JsonDocument.Parse(stored.ToJsonString())

      Expect.equal
        [ devnullOrg; opsOrg ]
        (match Me.decodeOrganizations document.RootElement with
         | Ok rows -> rows
         | Error _ -> [])
        "round-trip"
    }

    test "absent membership rows read as none" {
      use document = JsonDocument.Parse "{}"

      Expect.isTrue
        (match Me.decodeOrganizations document.RootElement with
         | Ok rows -> rows.IsEmpty
         | Error _ -> false)
        "absent"
    }
  ]
