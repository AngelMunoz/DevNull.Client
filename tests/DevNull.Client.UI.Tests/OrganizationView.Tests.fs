module DevNull.Client.UI.Tests.OrganizationView

open Avalonia.Controls
open Expecto
open DevNull.Client.Core.Http
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let startShell(store: SessionStore, http: IHttp) : Async<Window> =
  let env = TestEnv(store, http)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

let showsText(window: Window, text: string) : Async<bool> =
  waitForAsync(5000, (fun () -> window), fun root -> hasText(root, text))

let clickWhenShown(window: Window, text: string) : Async<unit> = async {
  let! found =
    waitForAsync(5000, (fun () -> buttonWithText(window, text)), Option.isSome)

  if not found then
    failtestf "the button %s never showed" text

  do! onUiAsync(fun () -> buttonWithText(window, text) |> Option.iter click)
}

/// Opens the organization page through the org-kind mention the boot
/// log carries.
let openOrganization(window: Window) : Async<unit> = async {
  let! mentioned = showsText(window, ", it was DNS.")

  Expect.isTrue mentioned "the organization mention never rendered"

  do! clickWhenShown(window, "@devnull")

  let! opened = showsText(window, "Organization feed")

  Expect.isTrue opened "the organization page never opened"
}

let tests =
  testList "organization view" [
    testCaseAsync
      "the organization page renders its header, badge, tab, and mentions"
      (async {
        let api = organizationMentionLogApi(organizationMentionsPageJson)

        let! window = startShell(signedInStore(), api :> IHttp)

        do! openOrganization(window)

        let! feed = showsText(window, "Organization feed")

        Expect.isTrue feed "the organization feed label never rendered"

        let! badge = showsText(window, "Organization")

        Expect.isTrue badge "the organization badge never rendered"

        let! kind = showsText(window, "organization")

        Expect.isTrue kind "the organization kind label never rendered"

        let! tab = showsText(window, "Mentions")

        Expect.isTrue tab "the mentions tab never rendered"

        let! name = showsText(window, "DevNull")

        Expect.isTrue name "the organization name never rendered"

        let! handle = showsText(window, "@devnull")

        Expect.isTrue handle "the organization handle never rendered"

        let! bio = showsText(window, "A quiet place for loud commits.")

        Expect.isTrue bio "the organization bio never rendered"

        let! location = showsText(window, "The void")

        Expect.isTrue location "the organization location never rendered"

        let! website = showsText(window, "devnull.example")

        Expect.isTrue website "the organization website never rendered"

        let! github = showsText(window, "GitHub")

        Expect.isTrue github "the organization GitHub line never rendered"

        let! site = showsText(window, "dev-null.sh/org/devnull")

        Expect.isTrue site "the organization site address never rendered"

        do! clickWhenShown(window, "Load more")

        let! next = showsText(window, "Second commit.")

        Expect.isTrue next "the organization mentions cursor page never loaded"

        Expect.exists
          api.Requests
          (fun request ->
            request.Method = "GET"
            && request.Target = "/api/v1/organizations/devnull")
          "the organization read"

        Expect.exists
          api.Requests
          (fun request ->
            request.Target.StartsWith "/api/v1/organizations/devnull/mentions")
          "the organization mentions read"

        Expect.exists
          api.Requests
          (fun request ->
            request.Target.Contains "cursor=next"
            && request.Target.StartsWith
              "/api/v1/organizations/devnull/mentions")
          "the organization mentions cursor read"
      })

    testCaseAsync
      "the organization page shows the empty sentence"
      (async {
        let api = organizationMentionLogApi(organizationMentionsEmptyPageJson)

        let! window = startShell(signedInStore(), api :> IHttp)

        do! openOrganization(window)

        let! empty =
          showsText(
            window,
            "Nobody has mentioned @devnull yet. Mention it in a commit and it shows up here."
          )

        Expect.isTrue empty "the empty mentions sentence never rendered"
      })

    testCaseAsync
      "the organization page shows the end-of-feed sentence"
      (async {
        let api = organizationMentionLogApi(organizationMentionsEndPageJson)

        let! window = startShell(signedInStore(), api :> IHttp)

        do! openOrganization(window)

        let! card = showsText(window, ", it was DNS.")

        Expect.isTrue card "the organization mention card never rendered"

        let! ended =
          showsText(window, "That's the first commit mentioning @devnull.")

        Expect.isTrue ended "the end-of-feed sentence never rendered"
      })

    testCaseAsync
      "an organization mention in a commit body opens the organization page"
      (async {
        let api = organizationMentionLogApi(organizationMentionsPageJson)

        let! window = startShell(signedInStore(), api :> IHttp)

        let! mentioned = showsText(window, ", it was DNS.")

        Expect.isTrue mentioned "the organization mention never rendered"

        do! clickWhenShown(window, "@devnull")

        let! opened = showsText(window, "Organization feed")

        Expect.isTrue opened "the organization page never opened"

        Expect.exists
          api.Requests
          (fun request -> request.Target = "/api/v1/organizations/devnull")
          "the organization route never read the profile"
      })
  ]
