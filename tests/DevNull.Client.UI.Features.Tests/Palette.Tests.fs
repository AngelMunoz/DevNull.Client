module DevNull.Client.UI.Tests.Palette

open Expecto
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Layouts.Editor
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.ThreadTab

let session: SignedInSession = {
  Session = sessionOf "octocat"
  KeyFile = "/tmp/devnull-test-key"
}

let props: Palette.Props<FakeEnv> = {
  Env = FakeEnv(unusedHttp)
  Session = session
  OnOpenRepo = ignore
  OnOpenThread = ignore
  OnSelectRef = ignore
  OnOpenPanel = ignore
  OnUseDefaultLayout = ignore
  OnClose = ignore
}

let people =
  match SearchProfiles.decode searchProfilesJson with
  | Ok matches -> matches
  | Error failure ->
    failwith $"the canned people never decoded: {failure.Message}"

let commits =
  match SearchCommits.decode(sprintf "{\"data\":[%s]}" commitJson) with
  | Ok nodes -> nodes
  | Error failure ->
    failwith $"the canned commits never decoded: {failure.Message}"

let update(msg: Palette.State.Msg, model: Palette.State.Model) =
  Palette.update(props, msg, model)

let tests =
  testList "editor palette" [
    testCase "the query shapes into the two server terms" (fun () ->
      let short = Palette.plan "  an  "
      Expect.equal short.People (Some "an") "the people term"
      Expect.equal short.Commits None "a two-letter commit term"

      let full = Palette.plan "  kai kou "
      Expect.equal full.People (Some "kai kou") "the full people term"
      Expect.equal full.Commits (Some "kai kou") "the full commit term"

      let bare = Palette.plan " @ "
      Expect.equal bare.People None "a bare at"
      Expect.equal bare.Commits None "a bare at commit")

    testCase "the command list names only real commands" (fun () ->
      Expect.equal
        (Palette.commands |> List.map Palette.label)
        [
          "switch ref: Latest"
          "switch ref: Following"
          "switch ref: Trending"
          "open panel: PROBLEMS"
          "open panel: OUTPUT"
          "open panel: TERMINAL"
          "use default layout"
        ]
        "the command list")

    testCase "the people and commits land under their own terms" (fun () ->
      let searched, _ =
        update(Palette.State.QueryChanged "kai kou", Palette.State.initial)

      Expect.equal searched.Term "kai kou" "the normalized term"
      Expect.isTrue searched.PeopleLoading "the people read"
      Expect.isTrue searched.CommitsLoading "the commits read"

      let withPeople, _ =
        update(Palette.State.PeopleFetched("kai kou", Ok people), searched)

      Expect.equal withPeople.People.Length 1 "the people rows"
      Expect.isFalse withPeople.PeopleLoading "the people read finished"

      let withCommits, _ =
        update(Palette.State.CommitsFetched("kai kou", Ok commits), withPeople)

      Expect.equal withCommits.Commits.Length 1 "the commit rows"
      Expect.isFalse withCommits.CommitsLoading "the commit read finished"

      let stale, _ =
        update(Palette.State.PeopleFetched("old", Ok people), withCommits)

      Expect.equal
        stale.People.Length
        1
        "a stale people answer replaced the rows")
  ]
