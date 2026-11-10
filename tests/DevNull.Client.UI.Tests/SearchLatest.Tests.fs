module DevNull.Client.UI.Tests.SearchLatest

// The search page's Latest tab over the real shell: the same
// answer, newest first, inside the past week — and Most relevant
// back on one click, without another fetch.

open System
open Avalonia
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open DevNull.Client.Core.Http
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.App
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

/// One dated answer row: the canned commit re-stamped by days
/// and re-bodied by the words its test watches for.
let datedCommitJson(id: string, daysAgo: int, body: string) =
  let stamp =
    DateTimeOffset.UtcNow
      .AddDays(-float daysAgo)
      .ToString("yyyy-MM-ddTHH:mm:ssZ")

  commitJson
    .Replace(
      "\"created_at\":\"2026-10-20T04:17:19Z\"",
      "\"created_at\":\"" + stamp + "\""
    )
    .Replace("018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d", id)
    .Replace("\"body\":\"It was DNS.\"", "\"body\":\"" + body + "\"")

/// The search api of the latest tests: the palette's people settle,
/// and the commit answer rides the test's own dated rows.
let latestSearchApi(rows: string list) =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/search/profiles" then
      ok searchProfilesJson
    elif request.Target.StartsWith "/api/v1/search/commits" then
      ok(sprintf "{\"data\":[%s]}" (String.concat "," rows))
    elif request.Target.StartsWith "/api/v1/log/" then
      ok logPageJson
    elif request.Target.Contains "/replies" then
      ok
        "{\"data\":[],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The answer in server (meaning) order: a two-day row, the
/// twenty-day row, and a one-day row — Latest must re-sort.
let mixedRows() = [
  datedCommitJson("018f4c1e-a111-7c3a-9b2d-5e6f7a8b9c0d", 2, "Recent match.")
  datedCommitJson("018f4c1e-b222-7c3a-9b2d-5e6f7a8b9c0d", 20, "Old match.")
  datedCommitJson("018f4c1e-c333-7c3a-9b2d-5e6f7a8b9c0d", 1, "Fresh match.")
]

/// Searches "shippin" from the palette onto the results page, and
/// expects the answer's counted words to have arrived.
let openResultsAsync
  (api: FakeApi, store: SessionStore, counted: string)
  : Async<Window> = async {
  let! window = startShell(store, api :> IHttp)

  let! shown = showsText(window, "The Octocat")

  Expect.isTrue shown "the log never rendered"

  do! clickWhenShown(window, "Search")

  do!
    onUiAsync(fun () ->
      controlByName(window, "SearchInput")
      |> Option.iter(fun control -> (control :?> TextBox).Text <- "shippin"))

  let! settled = seesVisible(window, "@kai-kou")

  Expect.isTrue settled "the search never settled"

  do! clickWhenShown(window, "See all matching commits")

  let! counted = seesVisible(window, counted)

  Expect.isTrue counted "the results page never counted its matches"

  return window
}

/// Clicks one results tab by its words inside the tab strip, the
/// strip the results carry above their rows.
let clickSearchTabAsync(window: Window, label: string) : Async<unit> =
  onUiAsync(fun () ->
    controlByName(window, "SearchTabs")
    |> Option.iter(fun host ->
      buttonWithText(host, label) |> Option.iter click))

/// The row carry's vertical middle: whether one body stands above
/// another in the rendered page.
let aboveAsync(window: Window, upper: string, lower: string) : Async<bool> =
  probeUiAsync(fun () ->
    match buttonWithText(window, upper), buttonWithText(window, lower) with
    | Some upperRow, Some lowerRow ->
      let upperAt =
        upperRow.TranslatePoint(
          Point(upperRow.Bounds.Width / 2.0, 12.0),
          window
        )

      let lowerAt =
        lowerRow.TranslatePoint(
          Point(lowerRow.Bounds.Width / 2.0, 12.0),
          window
        )

      upperAt.HasValue && lowerAt.HasValue && upperAt.Value.Y < lowerAt.Value.Y
    | _ -> false)

let tests =
  testList "search latest tab" [
    testCaseAsync
      "Most relevant shows the answer whole, Latest the past week newest first"
      (async {
        let api = latestSearchApi(mixedRows())

        let! window = openResultsAsync(api, signedInStore(), "3 matches")

        let! recent = seesVisible(window, "Recent match.")
        let! old = seesVisible(window, "Old match.")
        let! fresh = seesVisible(window, "Fresh match.")

        Expect.isTrue (recent && old && fresh) "Most relevant lost a row"

        let commitFetches =
          api.Requests
          |> List.filter(fun request ->
            request.Target.StartsWith "/api/v1/search/commits")
          |> List.length

        Expect.isTrue (commitFetches > 0) "the page never fetched"

        do! clickSearchTabAsync(window, "Latest")

        let! heading = seesVisible(window, "Latest commits \u00b7 past week")

        Expect.isTrue heading "the Latest heading never showed"

        let! stillOld = seesVisible(window, "Old match.")

        Expect.isFalse stillOld "Latest kept an old row"

        let! ordered = aboveAsync(window, "Fresh match.", "Recent match.")

        Expect.isTrue ordered "Latest did not sort newest first"

        do! settleAsync()

        let settled =
          api.Requests
          |> List.filter(fun request ->
            request.Target.StartsWith "/api/v1/search/commits")
          |> List.length

        Expect.equal commitFetches settled "switching the tab refetched"

        do! clickSearchTabAsync(window, "Most relevant")

        let! restored = seesVisible(window, "Old match.")

        Expect.isTrue restored "Most relevant did not restore after Latest"
      })

    testCaseAsync
      "Latest empty in the past week speaks the past-week words"
      (async {
        let api =
          latestSearchApi [
            datedCommitJson(
              "018f4c1e-d444-7c3a-9b2d-5e6f7a8b9c0d",
              20,
              "Only old."
            )
          ]

        let! window = openResultsAsync(api, signedInStore(), "1 match")

        do! clickSearchTabAsync(window, "Latest")

        let! words =
          seesVisible(
            window,
            "No matches from the past week. Try Most relevant for older matches, or search for another idea."
          )

        Expect.isTrue words "the past-week words never showed"
      })
  ]
