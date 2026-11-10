module DevNull.Client.UI.Tests.ProfilePictures

// The profile picture viewer over the real shell: the profile
// header avatar opens the picture full size where a URL stands,
// and nothing where none does.

open Avalonia.Controls
open Expecto
open DevNull.Client.Core.Http
open DevNull.Client.UI.Tests.App
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

/// The profile answer with an avatar URL: the image over the
/// initial, and — once armed — the picture's viewer.
let profileWithAvatarJson =
  profileJson.Replace(
    "\"name\":\"The Octocat\",\"avatar_url\":null",
    "\"name\":\"The Octocat\",\"avatar_url\":\"https://avatars.example/octocat.png\""
  )

/// The api of the picture tests: the log feeds the boot, and the
/// profile answers with the test's own avatar state.
let avatarApi(profileBody: string) =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif request.Target = "/api/v1/users/octocat" then
      ok profileBody
    elif request.Target.StartsWith "/api/v1/users/octocat/commits" then
      ok(commitsPageOf commitJson)
    elif request.Target.Contains "/replies" then
      ok
        "{\"data\":[],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// Opens the profile page from the shell's Profile. The window
/// runs tall, so the full header — the avatar the viewer arms —
/// stands instead of the compact one.
let openProfileAsync(window: Window) : Async<unit> = async {
  do!
    onUiAsync(fun () ->
      window.Width <- 1600.0
      window.Height <- 2000.0)

  let! shown = showsText(window, "It was DNS.")

  Expect.isTrue shown "the log never rendered"

  do! clickWhenShown(window, "Profile")

  let! profile = seesVisible(window, "Builds things.")

  Expect.isTrue profile "the profile page never rendered"
}

/// The viewer window standing open, by its name.
let viewer(window: Window) : Window option =
  window.OwnedWindows |> Seq.tryFind(fun open_ -> open_.Name = "ImageViewer")

/// Clicks the header avatar once it offers the viewer.
let openViewerAsync(window: Window) : Async<unit> = async {
  let! present =
    waitForAsync(
      5000,
      (fun () -> controlByName(window, "AvatarView")),
      Option.isSome
    )

  Expect.isTrue present "the header avatar never offered the viewer"

  do! clickNamed(window, "AvatarView")
}

let tests =
  testList "profile pictures" [
    testCaseAsync
      "the profile header avatar opens the picture viewer"
      (async {
        let api = avatarApi profileWithAvatarJson
        let! window = startShell(signedInStore(), api :> IHttp)

        do! openProfileAsync window
        do! openViewerAsync window

        let! opened =
          waitForAsync(5000, (fun () -> viewer window), Option.isSome)

        Expect.isTrue opened "the picture viewer never opened"

        let! counted =
          probeUiAsync(fun () ->
            viewer window
            |> Option.map(fun viewer -> hasText(viewer, "1 / 1"))
            |> Option.defaultValue false)

        Expect.isTrue counted "the viewer lost count of its photos"

        let! fetched =
          waitForAsync(
            5000,
            (fun () -> api.ByteRequests),
            (fun targets ->
              targets
              |> List.exists(fun target ->
                target.Contains "avatars.example/octocat.png"))
          )

        Expect.isTrue fetched "the viewer never fetched the picture"

        do!
          onUiAsync(fun () ->
            viewer window
            |> Option.iter(fun viewer ->
              controlByName(viewer, "ViewerClose")
              |> Option.iter(fun control ->
                match control with
                | :? Button as button -> click button
                | _ -> ())))

        let! closed =
          waitForAsync(5000, (fun () -> viewer window), Option.isNone)

        Expect.isTrue closed "the picture viewer never closed"
      })

    testCaseAsync
      "an avatar without a URL opens no viewer"
      (async {
        let api = avatarApi profileJson
        let! window = startShell(signedInStore(), api :> IHttp)

        do! openProfileAsync window

        let! absent =
          probeUiAsync(fun () -> controlByName(window, "AvatarView"))

        Expect.isNone absent "an avatar without a URL offered the viewer"

        let! owned = probeUiAsync(fun () -> viewer window |> Option.isNone)

        Expect.isTrue owned "a viewer stood open unasked"
      })
  ]
