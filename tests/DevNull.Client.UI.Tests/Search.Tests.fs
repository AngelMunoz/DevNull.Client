module DevNull.Client.UI.Tests.Search

// The search session checks: one call per pause of typing, the
// latest-wins rule, the slow-down answer, and the server's own bounds.

open System
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open FSharp.Control.Reactive
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Errors
open DevNull.Client.RecentSearchStore
open DevNull.Client.Search

/// Waits past both of the session's pauses.
let settle() = Async.Sleep 700

/// One person to find, named by the term it answered.
let private personOf(term: string) : User = {
  Id = "01a15638-ae71-733c-89b3-373a1a0c538a"
  Handle = term
  Name = "found"
  AvatarUrl = None
  Bio = None
  Lane = None
  CommitsCount = None
  FollowersCount = None
  FollowingCount = None
  IsFollowed = None
  FollowsYou = None
}

/// The session's answers arrive through replaying subjects, so each
/// subscription sees the empty answer first; the settled ones are the
/// events that carry a term.
let tests =
  testList "search session" [
    testCaseAsync
      "the store remembers a first search"
      (async {
        let store = RecentSearchStore(Map.empty, ignore)

        store.Remember("user-a", "kai kou")

        Expect.equal [ "kai kou" ] (store.Recent "user-a") "the first search"

        store.Remember("user-a", "dns")

        Expect.equal
          [ "dns"; "kai kou" ]
          (store.Recent "user-a")
          "newest first, case-blind dedupe"

        store.Remember("user-a", "KAI KOU")

        Expect.equal
          [ "KAI KOU"; "dns" ]
          (store.Recent "user-a")
          "the same words in any case ask once"

        store.Clear("user-a")

        Expect.equal [] (store.Recent "user-a") "cleared"
      })

    testCaseAsync
      "one settled call per pause of typing"
      (async {
        let calls = ResizeArray<string>()
        let found = ResizeArray<PeopleFound>()

        use session =
          new Session(
            (fun term ->
              calls.Add term

              async {
                return Ok [ personOf term ]: Result<User list, AppError>
              }),
            (fun _ -> async { return Ok [] }),
            NullLogger.Instance
          )

        session.People
        |> Observable.filter(fun answer -> answer.Term <> "")
        |> Observable.add found.Add

        // The three typings land inside one pause; the duplicate asks
        // nothing and the pause answers only the last of them.
        session.Set "kai"
        session.Set "kai"
        session.Set "ka"
        do! settle()

        Expect.equal 1 calls.Count "the pause collapsed the typing"
        Expect.equal "ka" calls[0] "the last term asked"

        Expect.equal 1 found.Count "one answer published"
        Expect.equal "ka" found[0].Term "the answer carries its term"
      })

    testCaseAsync
      "an answer for an older term never shows"
      (async {
        let found = ResizeArray<CommitsFound>()

        use session =
          new Session(
            (fun _ -> async { return Ok [] }),
            (fun term -> async {
              // The first term's answer waits until the newer one
              // has already landed.
              if term = "first" then
                do! Async.Sleep 1000

              return Ok []: Result<CommitNode list, AppError>
            }),
            NullLogger.Instance
          )

        session.Commits
        |> Observable.filter(fun answer -> answer.Term <> "")
        |> Observable.add found.Add

        session.Set "first"
        session.Set "second"
        do! Async.Sleep 1600

        Expect.equal 1 found.Count "only the newer term published"
        Expect.equal "second" found[0].Term "the stale answer dropped"
      })

    testCaseAsync
      "the slow-down answer settles as throttled"
      (async {
        let found = ResizeArray<CommitsFound>()

        use session =
          new Session(
            (fun _ -> async { return Ok [] }),
            (fun _ -> async {
              return
                Error(
                  AppError.Refused(
                    429,
                    None,
                    "Slow down: too many searches. Give it a few seconds."
                  )
                )
            }),
            NullLogger.Instance
          )

        session.Commits
        |> Observable.filter(fun answer -> answer.Term <> "")
        |> Observable.add found.Add

        session.Set "rails"
        do! settle()

        Expect.equal 1 found.Count "the refusal settled"
        Expect.equal true found[0].Throttled "marked throttled"
        Expect.equal 0 found[0].Commits.Length "nothing found"
      })

    testCaseAsync
      "a commit term under the server's minimum never calls"
      (async {
        let calls = ResizeArray<string>()

        use session =
          new Session(
            (fun _ -> async { return Ok [] }),
            (fun term ->
              calls.Add term
              async { return Ok [] }),
            NullLogger.Instance
          )

        session.SetCommits "ab"
        do! settle()

        Expect.equal 0 calls.Count "a short term never asked"
      })
  ]
