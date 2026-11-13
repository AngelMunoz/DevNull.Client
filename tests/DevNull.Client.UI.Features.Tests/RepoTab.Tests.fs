module DevNull.Client.UI.Tests.RepoTab

open Expecto
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.Editor
open DevNull.Client.Stores
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.ThreadTab

let session: SignedInSession = {
  Session = sessionOf "octocat"
  KeyFile = "/tmp/devnull-test-key"
}

let pinnedStatsJson =
  profileWithStatsJson.Replace(
    "\"pinned_commit\":null",
    sprintf "\"pinned_commit\":%s" commitJson
  )

let details =
  match ProfileDetails.decode pinnedStatsJson with
  | Ok details -> details
  | Error failure ->
    failwith $"the canned profile never decoded: {failure.Message}"

let firstPage =
  match CommitNode.decodePage logPageJson with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned commits never decoded: {failure.Message}"

let secondPage =
  match CommitNode.decodePage secondLogPageJson with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned commits never decoded: {failure.Message}"

let usersPage =
  match
    User.decodePage(
      sprintf
        "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
        apiUserJson
    )
  with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned users never decoded: {failure.Message}"

let props: RepoTab.Props<FakeEnv> = {
  Env = FakeEnv(unusedHttp)
  Session = session
  Handle = "octocat"
  Zone = {
    Info = System.TimeZoneInfo.Utc
    Label = "UTC"
  }
  Now =
    fun () ->
      System.DateTimeOffset(2026, 11, 12, 12, 0, 0, System.TimeSpan.Zero)
  OnOpenCommit = ignore
  OnReply = ignore
  OnQuote = ignore
  OnOpenRepo = ignore
}

let update(msg: RepoTab.Msg, model: RepoTab.Model) =
  RepoTab.update(props, msg, model)

let tests =
  testList "editor repo tab" [
    testCase
      "the profile read lands with contributions, shields, and the pinned card"
      (fun () ->
        let next, _ =
          update(RepoTab.ProfileFetched(Ok details), RepoTab.initial)

        Expect.isFalse next.Loading "the loading flag"
        Expect.isSome next.Details "the profile"
        Expect.equal next.Details.Value.Activity.Value.Scale "week" "the scale"

        Expect.equal
          next.Details.Value.Activity.Value.Buckets.Length
          3
          "the buckets"

        Expect.equal next.Details.Value.Achievements.Value.Level 3 "the level"

        Expect.equal
          next.Details.Value.Achievements.Value.Rank
          "Wizard"
          "the rank"

        Expect.equal
          next.Details.Value.Achievements.Value.Badges.Length
          1
          "the shields"

        Expect.equal
          next.Details.Value.Streaks.Value.Current
          3
          "the current streak"

        Expect.isSome next.Details.Value.PinnedCommit "the pinned card")

    testCase "the commits page and its cursor append" (fun () ->
      let first, _ =
        update(RepoTab.CommitsFetched(Ok firstPage), RepoTab.initial)

      Expect.equal first.Commits.Length 1 "the first commits"
      Expect.equal first.CommitsCursor (Some "next") "the first cursor"

      let second, _ = update(RepoTab.CommitsAppended(Ok secondPage), first)

      Expect.equal second.Commits.Length 2 "the appended commits"
      Expect.equal second.CommitsCursor None "the last cursor")

    testCase
      "the follow state flips at once and rolls back on failure"
      (fun () ->
        let baseline = { details with IsFollowing = false }

        let started, _ =
          update(RepoTab.ProfileFetched(Ok baseline), RepoTab.initial)

        let following, _ = update(RepoTab.FollowToggled, started)

        Expect.isTrue
          following.Details.Value.IsFollowing
          "the follow did not land at once"

        Expect.equal
          following.Details.Value.Profile.FollowersCount
          (Some 12)
          "the follower count did not move"

        let failed, _ =
          update(
            RepoTab.FollowDone(
              baseline,
              Error(AppError.TransportFailed "offline")
            ),
            following
          )

        Expect.isFalse
          failed.Details.Value.IsFollowing
          "the failed follow stayed"

        Expect.equal
          failed.Details.Value.Profile.FollowersCount
          (Some 11)
          "the count stayed"

        let landed, _ = update(RepoTab.FollowDone(baseline, Ok()), following)

        let unfollowed, _ = update(RepoTab.FollowToggled, landed)

        Expect.isFalse
          unfollowed.Details.Value.IsFollowing
          "the unfollow did not land"

        Expect.equal
          unfollowed.Details.Value.Profile.FollowersCount
          (Some 11)
          "the unfollow count")

    testCase "the following count opens its own list" (fun () ->
      let opened, _ =
        update(RepoTab.ListToggled RepoTab.Following, RepoTab.initial)

      Expect.equal
        opened.List.Kind
        (Some RepoTab.Following)
        "the following list did not open"

      let loaded, _ =
        update(RepoTab.ListFetched(RepoTab.Following, Ok usersPage), opened)

      Expect.equal loaded.List.Items.Length 1 "the following rows"
      Expect.isFalse loaded.List.Loading "the list's loading flag")

    testCase
      "a repository tab is pinned and one handle opens one tab"
      (fun () ->
        let tab = EditorTab.repo(TabId.repo "ana", "ana")

        let once =
          EditorState.openTab(tab, EditorState.initial LogKind.Following)

        let twice = EditorState.openTab(tab, once)

        Expect.isTrue
          (EditorTab.isPinned tab)
          "the repository tab is not pinned"

        Expect.equal (EditorTab.title tab) "@ana (repo)" "the repository title"

        Expect.isTrue
          (TabId.isRepo(EditorTab.id tab))
          "the repository identity"

        Expect.equal
          (EditorState.tabs twice |> List.length)
          2
          "the handle made two tabs")
  ]
