module DevNull.Client.UI.Tests.PostAsTests

// Headless tests for posting as an organization: the composer's
// identity picker, inline and rail placement, live membership
// refreshes, and ownership gates on organization commits.

open System
open System.Runtime.ExceptionServices
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
open DevNull.Client.Shell
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

let allNamed(root: Control, name: string) : Control list =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    if visual.Name = name then
      Some(visual :?> Control)
    else
      None)
  |> List.ofSeq

let clickNamed(root: Control, name: string) : Async<unit> =
  onUiAsync(fun () ->
    named(root, name)
    |> Option.iter(function
      | :? Button as button -> click button
      | _ -> ()))

/// Window-space rectangle for a control by summing Bounds.Position
/// across visual ancestors.
let windowRect(control: Control) : Rect =
  let rec walk(visual: Visual, x: float, y: float) =
    match visual.GetVisualParent() with
    | null -> x, y
    | parent ->
      let dx, dy =
        match parent with
        | :? Control as parentControl ->
          parentControl.Bounds.X, parentControl.Bounds.Y
        | _ -> 0.0, 0.0

      walk(parent, x + dx, y + dy)

  let left, top = walk(control, control.Bounds.X, control.Bounds.Y)
  Rect(left, top, control.Bounds.Width, control.Bounds.Height)

/// Runs a test body against an opened window and always closes the
/// window on exit so every mounted MVUish program stops cleanly.
let withWindow
  (openWindow: Async<Window>)
  (run: Window -> Async<unit>)
  : Async<unit> = async {
  let! window = openWindow

  let! outcome = async {
    try
      do! run window
      return Ok()
    with ex ->
      return Error ex
  }

  do! onUiAsync(fun () -> window.Close())

  match outcome with
  | Ok() -> ()
  | Error ex -> ExceptionDispatchInfo.Capture(ex).Throw()
}

/// The post-as menu's card, once the trigger stood it open.
let authorCardOf(window: Window) : Control option =
  named(window, "ComposerAuthorDialog")
  |> Option.bind(function
    | :? Popup as popup when popup.IsOpen -> Option.ofObj popup.Child
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
        AvatarHost = ValueNone
      }

    let window = Window(Width = 480.0, Height = 640.0, Content = page)
    window.Show()
    window)

let startShell(store: SessionStore, http: IHttp) : Async<Window> =
  let env = TestEnv(store, http)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

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

        do!
          withWindow (mountComposer(entry, env)) (fun window -> async {
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
      })

    testCaseAsync
      "inline composer places the avatar beside the body box on the same row"
      (async {
        let entry, store = signedInWith [ devnullOrg ]
        let env = TestEnv(store, logApi())

        do!
          withWindow (mountComposer(entry, env)) (fun window -> async {
            let! laidOut =
              waitForAsync(
                5000,
                (fun () ->
                  named(window, "ComposerAvatar"),
                  named(window, "ComposerBody")),
                (function
                | Some avatar, Some body ->
                  avatar.Bounds.Width > 0.0 && body.Bounds.Width > 0.0
                | _ -> false)
              )

            Expect.isTrue laidOut "composer avatar and body never laid out"

            let! rects =
              probeUiAsync(fun () ->
                match
                  named(window, "ComposerAvatar"),
                  named(window, "ComposerBody")
                with
                | Some avatar, Some body -> windowRect avatar, windowRect body
                | _ -> failwith "avatar or body missing")

            let avatarRect, bodyRect = rects

            Expect.isLessThanOrEqual
              avatarRect.Right
              (bodyRect.Left + 1.0)
              $"avatar should stand before the body box (avatar={avatarRect}, body={bodyRect})"

            Expect.isLessThan
              avatarRect.Top
              bodyRect.Bottom
              $"avatar should overlap the body box vertically (avatar={avatarRect}, body={bodyRect})"

            Expect.isLessThan
              bodyRect.Top
              avatarRect.Bottom
              $"avatar should not sit above the body box (avatar={avatarRect}, body={bodyRect})"
          })
      })

    testCaseAsync
      "website layout mounts one live identity avatar in the rail slot"
      (async {
        let _, store = signedInWith [ devnullOrg; opsOrg ]

        do!
          withWindow (startShell(store, logApi() :> IHttp)) (fun window -> async {
            let! mounted =
              waitForAsync(
                5000,
                (fun () ->
                  named(window, "ComposerRailNode"),
                  named(window, "ComposerAuthorTrigger")),
                (function
                | Some _, Some _ -> true
                | _ -> false)
              )

            Expect.isTrue mounted "rail node and identity trigger never stood"

            let! avatarCount =
              probeUiAsync(fun () ->
                (allNamed(window, "ComposerAvatar")).Length)

            Expect.equal
              1
              avatarCount
              "website layout must show one composer avatar"

            let! inRail =
              probeUiAsync(fun () ->
                named(window, "ComposerRailNode")
                |> Option.map(fun node ->
                  named(node, "ComposerAuthorTrigger").IsSome
                  && named(node, "ComposerAvatar").IsSome)
                |> Option.defaultValue false)

            Expect.isTrue
              inRail
              "composer identity trigger must live inside ComposerRailNode"

            do! clickNamed(window, "ComposerAuthorTrigger")

            let! orgRow = waitAuthorRow(window, "ComposerAuthordevnull")

            Expect.isTrue
              orgRow
              "the org row never stood from the rail trigger"

            do! clickAuthorRow(window, "ComposerAuthordevnull")

            let! square =
              waitForAsync(
                5000,
                (fun () -> avatarSquare window),
                ((=)(Some true))
              )

            Expect.isTrue square "the rail avatar never swapped to the org"
          })
      })

    testCaseAsync
      "a pick swaps the avatar to the org"
      (async {
        let entry, store = signedInWith [ devnullOrg; opsOrg ]
        let env = TestEnv(store, logApi())

        do!
          withWindow (mountComposer(entry, env)) (fun window -> async {
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
              waitForAsync(
                5000,
                (fun () -> authorCardOf window),
                Option.isSome
              )

            Expect.isTrue menu "the menu never opened"

            let! userRow = waitAuthorRow(window, "ComposerAuthorUser")
            Expect.isTrue userRow "the user row never stood"

            let! orgRow = waitAuthorRow(window, "ComposerAuthordevnull")
            Expect.isTrue orgRow "the org row never stood"

            do! clickAuthorRow(window, "ComposerAuthordevnull")

            let! square =
              waitForAsync(
                5000,
                (fun () -> avatarSquare window),
                ((=)(Some true))
              )

            Expect.isTrue square "the avatar never swapped to the org"
          })
      })

    testCaseAsync
      "a pick rides the post and the choice resets after"
      (async {
        let entry, store = signedInWith [ devnullOrg; opsOrg ]
        let http = logApi()
        let env = TestEnv(store, http)

        do!
          withWindow (mountComposer(entry, env)) (fun window -> async {
            do! clickNamed(window, "ComposerAuthorTrigger")

            let! menu =
              waitForAsync(
                5000,
                (fun () -> authorCardOf window),
                Option.isSome
              )

            Expect.isTrue menu "the menu never opened"

            let! orgRow = waitAuthorRow(window, "ComposerAuthordevnull")
            Expect.isTrue orgRow "the org row never stood"

            do! clickAuthorRow(window, "ComposerAuthordevnull")

            let! square =
              waitForAsync(
                5000,
                (fun () -> avatarSquare window),
                ((=)(Some true))
              )

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
              |> List.tryFind(fun request ->
                request.Target = "/api/v1/commits")
              |> Option.bind(fun request ->
                match request.Body with
                | Text text -> Some text
                | _ -> None)

            Expect.equal
              (Some "{\"body\":\"Hello org\",\"author\":\"devnull\"}")
              body
              "the author rode the post"

            let! round =
              waitForAsync(
                5000,
                (fun () -> avatarSquare window),
                ((=)(Some false))
              )

            Expect.isTrue round "the choice never reset after the post"
          })
      })

    testCaseAsync
      "a session refresh dropping the picked org falls back to posting as the user"
      (async {
        let entry, store = signedInWith [ devnullOrg; opsOrg ]
        let http = logApi()
        let env = TestEnv(store, http)

        do!
          withWindow (mountComposer(entry, env)) (fun window -> async {
            do! clickNamed(window, "ComposerAuthorTrigger")

            let! orgRow = waitAuthorRow(window, "ComposerAuthordevnull")
            Expect.isTrue orgRow "the org row never stood"

            do! clickAuthorRow(window, "ComposerAuthordevnull")

            let! square =
              waitForAsync(
                5000,
                (fun () -> avatarSquare window),
                ((=)(Some true))
              )

            Expect.isTrue square "the avatar never swapped to the org"

            store.Refresh {
              entry.Session with
                  Organizations = [ opsOrg ]
            }

            let! fellBack =
              waitForAsync(
                5000,
                (fun () -> avatarSquare window),
                ((=)(Some false))
              )

            Expect.isTrue
              fellBack
              "dropping the picked org did not restore the user avatar"

            do!
              onUiAsync(fun () ->
                named(window, "ComposerBody")
                |> Option.iter(function
                  | :? TextBox as box -> box.Text <- "Hello fallback"
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

            Expect.isTrue posted "the fallback post never left"

            let body =
              http.Requests
              |> List.tryFind(fun request ->
                request.Target = "/api/v1/commits")
              |> Option.bind(fun request ->
                match request.Body with
                | Text text -> Some text
                | _ -> None)

            Expect.equal
              (Some "{\"body\":\"Hello fallback\"}")
              body
              "the lost org must not ride the post"
          })
      })

    testCaseAsync
      "the delete and pin rows stand for a member-owned org commit"
      (async {
        let _, store = signedInWith [ devnullOrg ]
        let env = TestEnv(store, logApi())

        do!
          withWindow (mountCard(env, orgCommit(), [ "devnull" ])) (fun window -> async {
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
                  |> Option.bind(fun card ->
                    buttonWithText(card, "Delete commit"))),
                Option.isSome
              )

            Expect.isTrue delete "the delete never stood for a member"
          })
      })

    testCaseAsync
      "the delete and pin rows stay down without a membership"
      (async {
        let _, store = signedInWith []
        let env = TestEnv(store, logApi())

        do!
          withWindow (mountCard(env, orgCommit(), [])) (fun window -> async {
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
                  |> Option.bind(fun card ->
                    buttonWithText(card, "Copy link"))),
                Option.isSome
              )

            Expect.isTrue copy "the menu rows never stood"

            let! delete =
              probeUiAsync(fun () ->
                menuCardOf window
                |> Option.bind(fun card ->
                  buttonWithText(card, "Delete commit")))

            Expect.isNone delete "the delete stood with no membership"
          })
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
