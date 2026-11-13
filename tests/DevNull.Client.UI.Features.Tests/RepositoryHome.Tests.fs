module DevNull.Client.UI.Tests.RepositoryHomeTests

open System
open System.Reactive.Subjects
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.Workbench
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

type Sinks = {
  Thread: Commit -> unit
  Quote: Commit -> unit
  External: string -> unit
  Repository: string -> unit
  Lists: bool -> unit
}

let noSinks: Sinks = {
  Thread = ignore
  Quote = ignore
  External = ignore
  Repository = ignore
  Lists = ignore
}

let images = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let sessionOf(env: TestEnv) =
  match (env :> IProvideSession).Session.Current with
  | SignedIn session -> session
  | SignedOut -> failwith "the repository test needs a signed-in session"

let props(env: TestEnv, sinks: Sinks) : RepositoryHome.Props<TestEnv> = {
  Env = env
  Session = Some(sessionOf env)
  Handle = "octocat"
  KeyFile = "/tmp/devnull-test-id_ed25519"
  ViewerId = viewerId
  Pasteis = true
  Images = images
  Now = Observable.single(DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero))
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  OnOpenThread = sinks.Thread
  OnQuote = sinks.Quote
  OnOpenExternal = sinks.External
  OnOpenRepository = sinks.Repository
  OnListChanged = sinks.Lists
  OnDialog = ignore
}

let profileDetails =
  let json =
    profileWithStatsJson.Replace(
      "\"pinned_commit\":null",
      "\"pinned_commit\":" + pinnedOwnCommitJson
    )

  match ProfileDetails.decode json with
  | Ok details -> details
  | Error failure ->
    failwith $"the canned profile never decoded: {failure.Message}"

let commitsPage =
  match CommitNode.decodePage logPageJson with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned commits never decoded: {failure.Message}"

let followersPage =
  match User.decodePage followersPageJson with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned followers never decoded: {failure.Message}"

let headCommit =
  match commitsPage.Items with
  | LiveCommit commit :: _ -> commit
  | _ -> failwith "the canned commits carry no live commit"

let followingPage =
  match User.decodePage followingPageJson with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned following never decoded: {failure.Message}"

let initialModel(sinks: Sinks) =
  let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
  let p = props(env, sinks)
  let model, _ = RepositoryHome.init p
  p, model

let shownModel(sinks: Sinks) =
  let p, initial = initialModel sinks

  let model, _ =
    RepositoryHome.update(p, RepositoryHome.Shown(Ok profileDetails), initial)

  p, model

let readyModel(sinks: Sinks) =
  let p, model = shownModel sinks

  let model, _ =
    RepositoryHome.update(
      p,
      RepositoryHome.CommitsFetched(Ok commitsPage),
      model
    )

  p, model

let run(cmd: Cmd<RepositoryHome.Msg>) =
  cmd (fun _ -> ()) (fun (_, ex) -> failwith ex.Message)

let tests =
  testList "workbench repository home" [
    testCase "the profile read lands the header, activity, streaks, and README"
    <| fun () ->
      let p, initial = initialModel noSinks

      Expect.isTrue initial.Loading "the profile starts reading"
      Expect.isTrue initial.CommitsLoading "the commits start reading"

      let model, _ =
        RepositoryHome.update(
          p,
          RepositoryHome.Shown(Ok profileDetails),
          initial
        )

      let details = Option.get model.Details

      Expect.isFalse model.Loading "the profile read settled"
      Expect.isNone model.Error "no profile error"
      Expect.equal details.Profile.User.Handle "octocat" "the handle"

      Expect.equal
        details.Profile.Location
        (Some "San Francisco")
        "the location"

      Expect.equal details.Profile.FollowersCount (Some 11) "the followers"

      Expect.equal
        (details.Activity |> Option.map _.Scale)
        (Some "week")
        "the activity scale"

      Expect.equal
        (details.Activity |> Option.map(fun activity -> activity.Buckets.Length))
        (Some 3)
        "the bucket count"

      Expect.equal
        (details.Streaks |> Option.map(fun streaks -> streaks.Current))
        (Some 3)
        "the current streak"

      Expect.equal
        (details.Achievements
         |> Option.map(fun achievements -> achievements.Rank))
        (Some "Wizard")
        "the rank"

      Expect.isTrue
        details.PinnedCommit.IsSome
        "the README has no pinned commit"

      let model, _ =
        RepositoryHome.update(
          p,
          RepositoryHome.CommitsFetched(Ok commitsPage),
          model
        )

      Expect.equal model.Commits commitsPage.Items "the commits rows"

      Expect.equal
        model.CommitsCursor
        commitsPage.NextCursor
        "the commits cursor"

    testCase "the commits cursor lands and appends"
    <| fun () ->
      let p, model = shownModel noSinks

      let firstPage = {
        commitsPage with
            NextCursor = Some "next"
      }

      let model, _ =
        RepositoryHome.update(
          p,
          RepositoryHome.CommitsFetched(Ok firstPage),
          model
        )

      Expect.equal model.Commits firstPage.Items "the first page"
      Expect.equal model.CommitsCursor (Some "next") "the cursor"

      let model, _ =
        RepositoryHome.update(p, RepositoryHome.MoreCommitsRequested, model)

      Expect.isTrue model.CommitsLoading "the next page started"

      let spent = { commitsPage with NextCursor = None }

      let model, _ =
        RepositoryHome.update(
          p,
          RepositoryHome.CommitsAppended(Ok spent),
          model
        )

      Expect.equal
        model.Commits.Length
        (firstPage.Items.Length + spent.Items.Length)
        "the appended rows"

      Expect.isNone model.CommitsCursor "the cursor is spent"

    testCase "follow lands at once and rolls back on refusal"
    <| fun () ->
      let p, model = readyModel noSinks
      let p = { p with ViewerId = "another-viewer" }
      let before = (Option.get model.Details).Profile.FollowersCount

      let model, _ =
        RepositoryHome.update(p, RepositoryHome.FollowToggled, model)

      let followed = Option.get model.Details

      Expect.isTrue followed.IsFollowing "the follow did not land"
      Expect.isTrue model.FollowBusy "the follow did not start"

      Expect.equal
        followed.Profile.FollowersCount
        (before |> Option.map((+) 1))
        "the follower count did not rise"

      let model, _ =
        RepositoryHome.update(
          p,
          RepositoryHome.FollowDone(true, Error(TransportFailed "offline")),
          model
        )

      let rolled = Option.get model.Details

      Expect.isFalse rolled.IsFollowing "the refusal did not roll back"

      Expect.equal
        rolled.Profile.FollowersCount
        before
        "the follower count did not roll back"

      Expect.isFalse model.FollowBusy "the follow stayed busy"
      Expect.isSome model.FollowError "the refusal is quiet"

      Expect.isFalse
        (RepositoryHome.canFollow(viewerId, profileDetails))
        "the self profile can follow itself"

      Expect.isTrue
        (RepositoryHome.canFollow("another-viewer", profileDetails))
        "another viewer cannot follow the profile"

      let followedDetails = {
        (Option.get model.Details) with
            IsFollowing = true
      }

      let model, _ =
        RepositoryHome.update(
          p,
          RepositoryHome.FollowToggled,
          {
            model with
                Details = Some followedDetails
                FollowBusy = false
                FollowError = None
          }
        )

      let unfollowed = Option.get model.Details

      Expect.isFalse unfollowed.IsFollowing "the unfollow did not land"

      Expect.equal
        unfollowed.Profile.FollowersCount
        (before |> Option.map(fun count -> count - 1))
        "the follower count did not fall"

    testCase "the follower and following lists open with their own cursor"
    <| fun () ->
      let changes = ResizeArray<bool>()

      let p, model = readyModel { noSinks with Lists = changes.Add }

      let model, cmd =
        RepositoryHome.update(p, RepositoryHome.FollowersRequested, model)

      run cmd

      let followers = Option.get model.People

      Expect.equal followers.Kind RepositoryHome.Followers "the followers kind"
      Expect.isTrue followers.Loading "the follower read did not start"
      Expect.equal (changes |> Seq.toList) [ true ] "the list did not open"

      let firstPage = {
        followersPage with
            NextCursor = Some "next"
      }

      let model, _ =
        RepositoryHome.update(
          p,
          RepositoryHome.PeopleFetched(RepositoryHome.Followers, Ok firstPage),
          model
        )

      Expect.equal
        (Option.get model.People).Items
        firstPage.Items
        "the follower rows"

      Expect.equal
        (Option.get model.People).Cursor
        (Some "next")
        "the follower cursor"

      let model, _ =
        RepositoryHome.update(p, RepositoryHome.MorePeopleRequested, model)

      Expect.isTrue
        (Option.get model.People).Loading
        "the follower append started"

      let model, _ =
        RepositoryHome.update(
          p,
          RepositoryHome.PeopleAppended(
            RepositoryHome.Followers,
            Ok followersPage
          ),
          model
        )

      Expect.equal
        (Option.get model.People).Items.Length
        (firstPage.Items.Length + followersPage.Items.Length)
        "the appended followers"

      Expect.isNone
        (Option.get model.People).Cursor
        "the follower cursor is spent"

      let model, cmd =
        RepositoryHome.update(p, RepositoryHome.FollowingRequested, model)

      run cmd

      let following = Option.get model.People

      Expect.equal following.Kind RepositoryHome.Following "the following kind"
      Expect.isTrue following.Items.IsEmpty "the old followers stayed"

      let model, _ =
        RepositoryHome.update(
          p,
          RepositoryHome.PeopleFetched(
            RepositoryHome.Followers,
            Ok followersPage
          ),
          model
        )

      Expect.isTrue
        (Option.get model.People).Items.IsEmpty
        "the stale follower page landed"

      let model, _ =
        RepositoryHome.update(
          p,
          RepositoryHome.PeopleFetched(
            RepositoryHome.Following,
            Ok followingPage
          ),
          model
        )

      Expect.equal
        (Option.get model.People).Items
        followingPage.Items
        "the following rows"

      let model, cmd =
        RepositoryHome.update(p, RepositoryHome.PeopleClosed, model)

      run cmd

      Expect.isNone model.People "the list stayed open"

      Expect.equal
        (changes |> Seq.toList)
        [ true; true; false ]
        "the close was quiet"

    testCaseAsync
      "a self profile carries no follow control"
      (async {
        let! visible =
          probeUiAsync(fun () ->
            let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)

            let p = {
              props(env, noSinks) with
                  ViewerId = viewerId
            }

            let model, _ = RepositoryHome.init p

            let model, _ =
              RepositoryHome.update(
                p,
                RepositoryHome.Shown(Ok profileDetails),
                model
              )

            let subject = new BehaviorSubject<RepositoryHome.Model>(model)

            let view =
              RepositoryHome.view
                p
                (subject :> IObservable<RepositoryHome.Model>)
                (fun (_: RepositoryHome.Msg) -> ())

            let window = Window(Content = view.Center)
            window.Show()

            let follow =
              window.GetVisualDescendants()
              |> Seq.choose(function
                | :? Button as button when button.Name = "RepositoryFollow" ->
                  Some button
                | _ -> None)
              |> Seq.tryHead

            let visible =
              follow |> Option.map _.IsVisible |> Option.defaultValue false

            window.Close()
            visible)

        Expect.isFalse
          visible
          "the self profile still offered the follow control"
      })

    testCase
      "the thread, quote, external, and repository intents leave through their sinks"
    <| fun () ->
      let threads = ResizeArray<Commit>()
      let quoted = ResizeArray<Commit>()
      let external = ResizeArray<string>()
      let repositories = ResizeArray<string>()

      let sinks = {
        Thread = threads.Add
        Quote = quoted.Add
        External = external.Add
        Repository = repositories.Add
        Lists = ignore
      }

      let p, model = readyModel sinks

      let model, threadCmd =
        RepositoryHome.update(p, RepositoryHome.Opened headCommit, model)

      run threadCmd
      Expect.equal (threads |> Seq.toList) [ headCommit ] "the thread sink"

      let model, quoteCmd =
        RepositoryHome.update(
          p,
          RepositoryHome.QuoteRequested headCommit,
          model
        )

      run quoteCmd
      Expect.equal (quoted |> Seq.toList) [ headCommit ] "the quote sink"

      let model, externalCmd =
        RepositoryHome.update(
          p,
          RepositoryHome.ExternalOpened "https://github.com/octocat",
          model
        )

      run externalCmd

      Expect.equal
        (external |> Seq.toList)
        [ "https://github.com/octocat" ]
        "the external sink"

      let model, repositoryCmd =
        RepositoryHome.update(p, RepositoryHome.OpenRepository "dora", model)

      run repositoryCmd
      Expect.equal (repositories |> Seq.toList) [ "dora" ] "the repository sink"
  ]
