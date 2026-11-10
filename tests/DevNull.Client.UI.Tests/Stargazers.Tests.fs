module DevNull.Client.UI.Tests.Stargazers

// The stargazers list over the real shell: the author's own star
// count opens "Starred by", another person's commit offers no
// entry, and the list walks its cursor page by page.

open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open DevNull.Client.Core.Http
open DevNull.Client.UI.Tests.App
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

/// Opens the viewer's own commit thread from the log.
let openOwnThreadAsync(window: Window) : Async<unit> = async {
  let! shown = showsText(window, "My own commit.")

  Expect.isTrue shown "the own commit never rendered"

  do! clickWhenShown(window, "My own commit.")

  let! thread = seesVisible(window, "Back to the log")

  Expect.isTrue thread "the own thread never rendered"
}

/// Clicks the star count once it shows.
let clickStarCountAsync(window: Window) : Async<unit> = async {
  let! present =
    waitForAsync(
      5000,
      (fun () -> controlByName(window, "StarCount")),
      Option.isSome
    )

  Expect.isTrue present "the star count never showed"

  do! clickNamed(window, "StarCount")
}

let tests =
  testList "stargazers" [
    testCaseAsync
      "the own commit's star count opens the stargazers"
      (async {
        let api = starsThreadApi(ok starsPageOneJson, None)
        let! window = startShell(signedInStore(), api :> IHttp)

        do! openOwnThreadAsync window
        do! clickStarCountAsync window

        let! card =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "StargazersCard")),
            Option.isSome
          )

        Expect.isTrue card "the stargazers card never opened"

        let! title = seesVisible(window, "Starred by")

        Expect.isTrue title "the title never showed"

        let! count = seesVisible(window, "7 people starred this commit.")

        Expect.isTrue count "the count line never showed"

        let! person = seesVisible(window, "Kai")

        Expect.isTrue person "the stargazer never listed"

        let! handle = seesVisible(window, "@kai")

        Expect.isTrue handle "the stargazer handle never listed"

        Expect.exists
          api.Requests
          (fun request ->
            request.Method = "GET"
            && request.Target =
              "/api/v1/commits/018f4c1e-0aa0-7c3a-9b2d-5e6f7a8b9c0d/stars")
          "the star list was never fetched"
      })

    testCaseAsync
      "another person's commit offers no stargazers entry"
      (async {
        let api = threadApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")

        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "It was DNS.")

        let! thread = seesVisible(window, "Back to the log")

        Expect.isTrue thread "the thread never rendered"

        let! count = probeUiAsync(fun () -> controlByName(window, "StarCount"))

        Expect.isNone count "another person's commit offered a star count"

        let! card =
          probeUiAsync(fun () -> controlByName(window, "StargazersCard"))

        Expect.isNone card "another person's commit opened its star list"
      })

    testCaseAsync
      "the star list walks its cursor page by page"
      (async {
        let api = starsThreadApi(ok starsPageNextJson, Some starsPageTwoJson)
        let! window = startShell(signedInStore(), api :> IHttp)

        do! openOwnThreadAsync window
        do! clickStarCountAsync window

        let! first = seesVisible(window, "Kai")

        Expect.isTrue first "the first page never listed"

        let! seen = seesVisible(window, "Mona")

        Expect.isFalse seen "the second page listed unasked"

        do! clickNamed(window, "StarsMore")

        let! second = seesVisible(window, "Mona")

        Expect.isTrue second "the second page never listed"
      })

    testCaseAsync
      "a failed star list speaks the app's error words"
      (async {
        let api = starsThreadApi(refused(404, "not your commit to ask"), None)

        let! window = startShell(signedInStore(), api :> IHttp)

        do! openOwnThreadAsync window
        do! clickStarCountAsync window

        let! error =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "StarsError")),
            Option.isSome
          )

        Expect.isTrue error "the error line never showed"

        let! words =
          probeUiAsync(fun () ->
            controlByName(window, "StarsError")
            |> Option.bind(fun control ->
              match control with
              | :? TextBlock as block -> block.Text |> Option.ofObj
              | _ -> None))

        Expect.isSome words "the error line never showed"

        Expect.stringContains
          words.Value
          "not your commit to ask"
          "the error words never showed"
      })

    testCaseAsync
      "a stargazer row opens the profile and closes the list"
      (async {
        let api = starsThreadApi(ok starsPageOneJson, None)
        let! window = startShell(signedInStore(), api :> IHttp)

        do! openOwnThreadAsync window
        do! clickStarCountAsync window

        let! row =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "StargazerRow")),
            Option.isSome
          )

        Expect.isTrue row "the stargazer row never listed"

        do! clickNamed(window, "StargazerRow")

        let! gone =
          waitForAsync(
            5000,
            (fun () -> controlByName(window, "StargazersCard")),
            Option.isNone
          )

        Expect.isTrue gone "the stargazers card never closed"

        let! leftThread =
          probeUiAsync(fun () -> controlByName(window, "BackToLog"))

        Expect.isNone leftThread "the row left the thread for no page"
      })
  ]
