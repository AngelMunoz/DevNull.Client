module DevNull.Client.UI.Tests.WhoToFollow

open System
open Avalonia
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open DevNull.Client.Components
open DevNull.Client.Core.Http
open DevNull.Client.RouteStore
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

/// One suggestion as the API sends it: the reason counts vary, so the
/// row's two reason lines can be told apart.
let suggestionJson(index: int, known: int, followers: int) =
  $"""{{"id":"suggested-{index}","handle":"person{index}","name":"Suggested Person {index}","bio":"Bio {index}.","followers_count":{followers},"followed_by_following_count":{known},"is_followed":false,"follows_you":false}}"""

/// One page of six suggestions: three shown and three spares.
let suggestionsPage(start: int) =
  let people = [ for offset in 0..5 -> suggestionJson(start + offset, 0, 0) ]

  // The first row says why it is known; the second says how many
  // followers it has.
  people
  |> List.mapi(fun index person ->
    match index with
    | 0 ->
      person.Replace(
        "\"followers_count\":0,\"followed_by_following_count\":0",
        "\"followers_count\":11,\"followed_by_following_count\":2"
      )
    | 1 ->
      person.Replace(
        "\"followers_count\":0,\"followed_by_following_count\":0",
        "\"followers_count\":5,\"followed_by_following_count\":0"
      )
    | _ -> person)
  |> String.concat ","

let suggestionsBody(start: int) =
  sprintf """{"data":[%s]}""" (suggestionsPage start)

/// The api of the rail: six people a round, a different six for the
/// two page contexts, and empty answers for the two actions.
let suggestionsApi() =
  FakeApi(fun request ->
    match request.Method, request.Target with
    | "GET", target when target.StartsWith "/api/v1/me/suggested-users" ->
      let start =
        [ "handle=octocat", 21; "commit_id=", 31; "round=1", 7 ]
        |> List.tryPick(fun (marker, start) ->
          if target.Contains marker then Some start else None)
        |> Option.defaultValue 1

      ok(suggestionsBody start)
    | "POST", target when target.EndsWith "/dismiss" -> ok ""
    | ("POST" | "DELETE"), target when target.EndsWith "/follow" -> ok ""
    | _, target when target.StartsWith "/api/v1/log/" -> ok emptyLogPageJson
    | _ ->
      match accountAnswer request with
      | ValueSome answer -> answer
      | ValueNone -> ok "{}")

/// One suggestion with a name and bio long enough to press the row
/// actions against the aside's right edge.
let longSuggestionJson(index: int) =
  $"""{{"id":"suggested-{index}","handle":"person{index}","name":"Suggested Person {index} With A Long Name That Pushes The Actions","bio":"A long bio that keeps the identity line wide enough to test the row layout.","followers_count":11,"followed_by_following_count":2,"is_followed":false,"follows_you":false}}"""

let longSuggestionsApi() =
  FakeApi(fun request ->
    match request.Method, request.Target with
    | "GET", target when target.StartsWith "/api/v1/me/suggested-users" ->
      let people =
        [ for index in 1..6 -> longSuggestionJson index ] |> String.concat ","

      ok(sprintf """{"data":[%s]}""" people)
    | _, target when target.StartsWith "/api/v1/log/" -> ok emptyLogPageJson
    | _ ->
      match accountAnswer request with
      | ValueSome answer -> answer
      | ValueNone -> ok "{}")

let controlByName(root: Control, name: string) : Control option =
  root.GetVisualDescendants()
  |> Seq.tryFind(fun visual -> visual.Name = name)
  |> Option.map(fun visual -> visual :?> Control)

let buttonByName(root: Control, name: string) : Button option =
  controlByName(root, name)
  |> Option.bind(fun control ->
    match control with
    | :? Button as button -> Some button
    | _ -> None)

let waitForName(window: Window, name: string) : Async<bool> =
  waitForAsync(5000, (fun () -> controlByName(window, name)), Option.isSome)

/// The shell's right column, the only scroller set to the aside width.
let asideScroller(root: Control) : ScrollViewer option =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? ScrollViewer as scroller when abs(scroller.Width - 320.0) < 0.5 ->
      Some scroller
    | _ -> None)
  |> Seq.tryHead

let showsText(window: Window, text: string) : Async<bool> =
  waitForAsync(5000, (fun () -> window), fun root -> hasText(root, text))

/// The rail's window, its fake API, and the route store it follows.
type Rail = {
  Window: Window
  Api: FakeApi
  Routes: RouteStore
}

let mountRail() : Async<Rail> =
  let api = suggestionsApi()
  let env = TestEnv(signedInStore(), api :> IHttp)
  let routes = (env :> IProvideRoutes).Routes
  routes.Land "/log/latest"

  let props: WhoToFollow.Props<TestEnv> = {
    Env = env
    Session = signedInSession()
    OnOpen = ignore
  }

  async {
    let! window =
      probeUiAsync(fun () ->
        let window = Window()
        window.Content <- WhoToFollow.Page props :> Control
        window.Show()
        window)

    return {
      Window = window
      Api = api
      Routes = routes
    }
  }

let startShell(store: SessionStore, http: IHttp) : Async<Window> =
  let env = TestEnv(store, http)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

let tests =
  testList "who to follow" [
    testCaseAsync
      "three people show with their reason lines"
      (async {
        let! rail = mountRail()
        let window = rail.Window
        let api = rail.Api

        let! shown = showsText(window, "Suggested Person 1")
        Expect.isTrue shown "the first suggestion never rendered"

        let! known = showsText(window, "Followed by 2 you follow")

        Expect.isTrue known "the known-followers line never rendered"

        let! followers = showsText(window, "5 followers")

        Expect.isTrue followers "the follower line never rendered"

        let! read =
          probeUiAsync(fun () ->
            api.Requests
            |> List.exists(fun request ->
              request.Target.StartsWith "/api/v1/me/suggested-users"))

        Expect.isTrue read "the rail never asked for suggestions"
      })

    testCaseAsync
      "not interested fills the slot with the first spare"
      (async {
        let! rail = mountRail()
        let window = rail.Window
        let api = rail.Api

        let! shown = showsText(window, "Suggested Person 1")
        Expect.isTrue shown "the first suggestion never rendered"

        let! ready = waitForName(window, "DismissSuggestion:person1")
        Expect.isTrue ready "the dismiss action never appeared"

        do!
          onUiAsync(fun () ->
            buttonByName(window, "DismissSuggestion:person1")
            |> Option.iter click)

        let! spare = showsText(window, "Suggested Person 4")
        Expect.isTrue spare "the spare never filled the slot"

        let! gone =
          probeUiAsync(fun () -> hasText(window, "Suggested Person 1"))

        Expect.isFalse gone "the dismissed row stayed"

        let! sent =
          probeUiAsync(fun () ->
            api.Requests
            |> List.exists(fun request ->
              request.Method = "POST"
              && request.Target = "/api/v1/users/person1/dismiss"))

        Expect.isTrue sent "the dismissal never went out"
      })

    testCaseAsync
      "refresh advances to the next round"
      (async {
        let! rail = mountRail()
        let window = rail.Window
        let api = rail.Api

        let! shown = showsText(window, "Suggested Person 1")
        Expect.isTrue shown "the first suggestion never rendered"

        do!
          onUiAsync(fun () ->
            buttonByName(window, "WhoToFollowRefresh") |> Option.iter click)

        let! next = showsText(window, "Suggested Person 7")
        Expect.isTrue next "the next round never rendered"

        let! advanced =
          probeUiAsync(fun () ->
            api.Requests
            |> List.exists(fun request -> request.Target.Contains "round=1"))

        Expect.isTrue advanced "the refresh never asked for the next round"

        let! ready = waitForName(window, "FollowSuggestion:person7")
        Expect.isTrue ready "the refreshed row lost its follow action"

        do!
          onUiAsync(fun () ->
            buttonByName(window, "FollowSuggestion:person7")
            |> Option.iter click)

        let! following = waitForName(window, "UnfollowSuggestion:person7")

        Expect.isTrue following "the refreshed row's follow did not answer"

        let! sent =
          probeUiAsync(fun () ->
            api.Requests
            |> List.exists(fun request ->
              request.Method = "POST"
              && request.Target = "/api/v1/users/person7/follow"))

        Expect.isTrue sent "the refreshed row's follow never went out"

        let! gone =
          probeUiAsync(fun () -> hasText(window, "Suggested Person 1"))

        Expect.isFalse gone "the old round stayed"
      })

    testCaseAsync
      "the route sets the suggestion context"
      (async {
        let! rail = mountRail()
        let window = rail.Window
        let api = rail.Api
        let routes = rail.Routes

        let! shown = showsText(window, "Suggested Person 1")
        Expect.isTrue shown "the general suggestions never rendered"

        routes.Land "/profile/octocat"

        let! profile = showsText(window, "Suggested Person 21")
        Expect.isTrue profile "the profile suggestions never rendered"

        let! profiled =
          probeUiAsync(fun () ->
            api.Requests
            |> List.exists(fun request ->
              request.Target.Contains "handle=octocat"))

        Expect.isTrue profiled "the profile context never went out"

        routes.Land "/commit/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"

        let! thread = showsText(window, "Suggested Person 31")
        Expect.isTrue thread "the thread suggestions never rendered"

        let! threaded =
          probeUiAsync(fun () ->
            api.Requests
            |> List.exists(fun request ->
              request.Target.Contains
                "commit_id=018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"))

        Expect.isTrue threaded "the commit context never went out"
      })

    testCaseAsync
      "a follow marks the row as following"
      (async {
        let! rail = mountRail()
        let window = rail.Window
        let api = rail.Api

        let! shown = showsText(window, "Suggested Person 1")
        Expect.isTrue shown "the first suggestion never rendered"

        let! ready = waitForName(window, "FollowSuggestion:person1")
        Expect.isTrue ready "the follow action never appeared"

        do!
          onUiAsync(fun () ->
            buttonByName(window, "FollowSuggestion:person1")
            |> Option.iter click)

        let! following = waitForName(window, "UnfollowSuggestion:person1")

        Expect.isTrue following "the row never marked the follow"

        let! sent =
          probeUiAsync(fun () ->
            api.Requests
            |> List.exists(fun request ->
              request.Method = "POST"
              && request.Target = "/api/v1/users/person1/follow"))

        Expect.isTrue sent "the follow never went out"
      })

    testCaseAsync
      "the wide aside carries the rail"
      (async {
        let! window = startShell(signedInStore(), suggestionsApi() :> IHttp)

        let! shown = showsText(window, "Nothing here yet. Commit something.")
        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            window.Width <- 1440.0
            window.Height <- 900.0)

        let! rail = waitForName(window, "WhoToFollow")
        Expect.isTrue rail "the rail never mounted"

        let! person = showsText(window, "Suggested Person 1")
        Expect.isTrue person "the rail never loaded its people"
      })

    testCaseAsync
      "the row actions stay inside the aside"
      (async {
        let! window = startShell(signedInStore(), longSuggestionsApi() :> IHttp)

        let! shown = showsText(window, "Nothing here yet. Commit something.")
        Expect.isTrue shown "the log never rendered"

        do!
          onUiAsync(fun () ->
            window.Width <- 1440.0
            window.Height <- 900.0)

        let! loaded =
          showsText(
            window,
            "Suggested Person 1 With A Long Name That Pushes The Actions"
          )

        Expect.isTrue loaded "the long suggestion never rendered"

        let! placed =
          probeUiAsync(fun () ->
            match asideScroller window with
            | Some scroller ->
              let probes =
                [
                  "WhoToFollowRefresh"
                  "FollowSuggestion:person1"
                  "DismissSuggestion:person1"
                ]
                |> List.map(fun name ->
                  match buttonByName(window, name) with
                  | Some button ->
                    let point =
                      button.TranslatePoint(
                        Avalonia.Point(0.0, 0.0),
                        scroller
                      )

                    if point.HasValue then
                      let corner = point.Value

                      $"{name}: x={corner.X:F1} w={button.Bounds.Width:F1} right={corner.X + button.Bounds.Width:F1}",
                      corner.X >= 0.0
                      && corner.X + button.Bounds.Width
                         <= scroller.Bounds.Width + 0.5
                    else
                      $"{name}: no point", false
                  | None -> $"{name}: missing", false)

              let inside = probes |> List.forall snd
              let detail = String.concat "; " (probes |> List.map fst)
              inside, $"{detail}; scroller={scroller.Bounds.Width:F1}"
            | None -> false, "no aside scroller")

        let inside, detail = placed

        Expect.isTrue
          inside
          $"the row actions were outside the aside viewport: {detail}"
      })
  ]
