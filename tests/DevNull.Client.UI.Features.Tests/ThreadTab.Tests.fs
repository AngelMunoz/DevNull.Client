module DevNull.Client.UI.Tests.ThreadTab

open System
open Avalonia.Controls
open Avalonia.VisualTree
open Expecto
open FSharp.Control.Reactive
open DevNull.Client.AttachmentSources
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Layouts.Editor
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore
open DevNull.Client.Stores
open DevNull.Client.UiClock
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

type FakeEnv(http: IHttp) =

  interface IProvideClock with
    member _.Clock = {
      new IClock with
        member _.UnixNow() = 1760000000L
    }

  interface IProvideNonceSource with
    member _.NonceSource = {
      new INonceSource with
        member _.NewHex() = "00112233445566778899aabbccddeeff"
    }

  interface IProvideSigner with
    member _.Signer = {
      new ISigner with
        member _.Sign(_: SignRequest) = async { return Ok "signature" }
    }

  interface IProvideHttp with
    member _.Http = http

  interface IProvideUiClock with
    member _.UiClock = {
      new UiClockService with
        member _.Minute = Observable.empty
    }

  interface IProvideImageCache with
    member _.ImageCache = {
      new ImageCacheService with
        member _.Get(_: string) = async { return None }
    }

  interface IProvideProfiles with
    member _.Profiles = {
      new IProfiles with
        member _.Fetch(_: string) = async { return None }
    }

  interface IProvideDispatcher with
    member _.Dispatcher =
      (TestEnv(signedInStore(), http) :> IProvideDispatcher).Dispatcher

  interface IProvideRemotes with
    member _.Remotes =
      (TestEnv(signedInStore(), http) :> IProvideRemotes).Remotes

  interface IProvideTranslations with
    member _.Translations =
      (TestEnv(signedInStore(), http) :> IProvideTranslations).Translations

  interface IProvideUserSettings with
    member _.UserSettings =
      (TestEnv(signedInStore(), http) :> IProvideUserSettings).UserSettings

  interface IProvideAttachmentSources with
    member _.Attachments =
      (TestEnv(signedInStore(), http) :> IProvideAttachmentSources).Attachments

  interface IProvideBrowser with
    member _.Browser =
      (TestEnv(signedInStore(), http) :> IProvideBrowser).Browser

let session: SignedInSession = {
  Session = {
    User = {
      Id = "viewer"
      Handle = "ana"
      Name = "Ana"
      AvatarUrl = None
    }
    DeviceFingerprint = "9F3A0000C1"
    UnreadNotificationsCount = 0
    PasteisMode = true
    AutomaticTranslation = false
    TermsVersion = ""
    TermsAccepted = Some true
    Remotes = None
  }
  KeyFile = "/tmp/devnull-test-key"
}

let unusedHttp: IHttp = {
  new IHttp with
    member _.Send(_: HttpRequest) = async {
      return Error { Message = "unused" }
    }

    member _.GetBytes(_: string) = async { return Error { Message = "unused" } }
}

type Sinks = {
  Quote: Commit -> unit
  Reply: Commit -> unit
  Snapshot: ThreadTab.Snapshot -> unit
  Fetch: Result<unit, string> -> unit
}

let quiet: Sinks = {
  Quote = ignore
  Reply = ignore
  Snapshot = ignore
  Fetch = ignore
}

let props
  (env: FakeEnv, commitId: string, sinks: Sinks)
  : ThreadTab.Props<FakeEnv> = {
  Env = env
  Session = session
  CommitId = commitId
  Zone = {
    Info = TimeZoneInfo.Utc
    Label = "UTC"
  }
  Now = fun () -> DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)
  OnSnapshot = sinks.Snapshot
  OnFetch = sinks.Fetch
  OnQuote = sinks.Quote
  OnReplyRequested = sinks.Reply
  OnOpenRepo = ignore
}

let pageOf(json: string) =
  match CommitPage.decode json with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned thread never decoded: {failure.Message}"

let repliesOf(json: string) =
  match CommitNode.decodePage json with
  | Ok page -> page
  | Error failure ->
    failwith $"the canned replies never decoded: {failure.Message}"

let threadPage = pageOf threadPageJson

let repliesPage(cursor: string option) =
  let cursor =
    match cursor with
    | Some cursor -> sprintf "\"%s\"" cursor
    | None -> "null"

  repliesOf(
    sprintf
      "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":%s,\"prev_cursor\":null}}"
      replyJson
      cursor
  )

let firstReplies = repliesPage(Some "next")

let secondReplies =
  repliesOf(
    sprintf
      "{\"data\":[%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null}}"
      secondCommitJson
  )

let showModel =
  let model, _ =
    ThreadTab.State.update(
      props(FakeEnv(unusedHttp), threadPage.Commit.Id, quiet),
      ThreadTab.State.ThreadFetched(Ok threadPage),
      ThreadTab.State.initial
    )

  model

let update(msg: ThreadTab.State.Msg, model: ThreadTab.State.Model) =
  ThreadTab.State.update(
    props(FakeEnv(unusedHttp), threadPage.Commit.Id, quiet),
    msg,
    model
  )

let controlByName(root: Control, name: string) =
  root.GetVisualDescendants()
  |> Seq.tryFind(fun visual -> visual.Name = name)
  |> Option.map(fun visual -> visual :?> Control)

let tests =
  testList "editor thread tab" [
    testCase "the thread show lands with its replies count" (fun () ->
      let next, _ =
        update(
          ThreadTab.State.ThreadFetched(Ok threadPage),
          ThreadTab.State.initial
        )

      Expect.isFalse next.Loading "the loading flag"
      Expect.isSome next.Page "the thread page"
      Expect.equal next.Page.Value.Ancestors.Length 0 "the ancestors"

      let snapshot =
        ThreadTab.State.snapshot(
          props(FakeEnv(unusedHttp), threadPage.Commit.Id, quiet),
          next
        )

      Expect.equal snapshot.RepliesCount 3 "the replies count"

      Expect.equal
        (snapshot.Participants |> List.map(fun row -> row.Handle))
        [ "octocat"; "mona"; "hubot" ]
        "the reply participants"

      Expect.isTrue
        snapshot.Participants.Head.IsAuthor
        "the author is not first")

    testCase "the card row carries the web action slots per surface" (fun () ->
      let env = FakeEnv(unusedHttp)

      let cardProps surface : EditorCards.Props<FakeEnv> = {
        Env = env
        Session = session
        Zone = {
          Info = TimeZoneInfo.Utc
          Label = "UTC"
        }
        Surface = surface
        Now = fun () -> DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)
        OnOpenRepo = ignore
      }

      let rowOf surface =
        let props = cardProps surface

        EditorCards.row {
          Props = props
          State = Observable.single(EditorCards.initial props)
          Dispatch = fun _ -> ()
          OnEvent = ignore
        }

      let detail = rowOf EditorCards.Detail

      let isSome(value: 'a voption) =
        match value with
        | ValueSome _ -> true
        | ValueNone -> false

      let isNone(value: 'a voption) =
        match value with
        | ValueSome _ -> false
        | ValueNone -> true

      Expect.isTrue (isSome detail.Star) "the detail star slot"
      Expect.isTrue (isSome detail.Vote) "the detail vote slot"
      Expect.isTrue (isSome detail.Pastel) "the detail pastel slot"
      Expect.isTrue (isSome detail.Menu) "the detail menu slot"
      Expect.isTrue (isSome detail.Pin) "the detail pin slot"

      let profile = rowOf EditorCards.Profile
      Expect.isTrue (isSome profile.Pin) "the profile pin slot"

      Expect.isTrue (isSome profile.Menu) "the profile menu slot"

      profile.Menu
      |> ValueOption.iter(fun menu ->
        Expect.isTrue (isNone menu.Share) "the profile share slot"
        Expect.isTrue (isNone menu.Report) "the profile report slot")

      let saved = rowOf EditorCards.Saved
      Expect.isTrue (isNone saved.Pin) "the saved pin slot"
      Expect.isTrue (isSome saved.Menu) "the saved menu slot"

      saved.Menu
      |> ValueOption.iter(fun menu ->
        Expect.isTrue (isNone menu.Share) "the saved share slot")

      let feed = rowOf EditorCards.Feed
      Expect.isTrue (isNone feed.Pin) "the feed pin slot"
      Expect.isTrue (isSome feed.Menu) "the feed menu slot")

    testCase "the replies append by cursor" (fun () ->
      let first, _ =
        update(ThreadTab.State.RepliesFetched(Ok firstReplies), showModel)

      Expect.equal first.Replies.Length 1 "the first replies page"
      Expect.equal first.RepliesCursor (Some "next") "the first cursor"

      let second, _ =
        update(ThreadTab.State.RepliesFetched(Ok secondReplies), first)

      Expect.equal second.Replies.Length 2 "the appended replies"
      Expect.equal second.RepliesCursor None "the second cursor")

    testCase "the star runs under one busy slot and lands" (fun () ->
      let commit = threadPage.Commit

      let busy, _ = update(ThreadTab.State.StarToggled commit, showModel)

      Expect.equal busy.Busy (Some commit.Id) "the star did not busy"

      let updated = {
        commit with
            Starred = Some true
            StarsCount = Some 8
      }

      let next, _ = update(ThreadTab.State.StarDone(Ok updated), busy)

      Expect.equal next.Busy None "the star kept the slot"
      Expect.equal next.Page.Value.Commit.Starred (Some true) "the star flag")

    testCase "the pastel runs under the same busy slot and lands" (fun () ->
      let commit = threadPage.Commit

      let busy, _ = update(ThreadTab.State.PastelGiven commit, showModel)

      Expect.equal busy.Busy (Some commit.Id) "the pastel did not busy"

      let updated = { commit with PasteisCount = 1 }

      let result: PastelResult = {
        Commit = updated
        PasteisRemaining = 6
        PasteisResetsAt = DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero)
        AuthorPasteisReceived = 1
      }

      let next, _ = update(ThreadTab.State.PastelDone(Ok result), busy)

      Expect.equal next.Busy None "the pastel kept the slot"
      Expect.equal next.Page.Value.Commit.PasteisCount 1 "the pastel count")

    testCase "the vote runs under the same busy slot and lands" (fun () ->
      let option: PollOption = {
        Id = "yes"
        Label = "Yes"
        VotesCount = Some 1
        Voted = false
        Percentage = Some 25
      }

      let poll: Poll = {
        Id = "poll"
        ClosesAt = None
        IsClosed = false
        HasVoted = false
        TotalVotes = Some 4
        Options = [ option ]
      }

      let commit = {
        threadPage.Commit with
            Poll = Some poll
      }

      let model = {
        showModel with
            Page =
              showModel.Page
              |> Option.map(fun page -> { page with Commit = commit })
      }

      let chosen, _ =
        update(
          ThreadTab.State.CardEvent(PollChoiceChanged(commit, option, true)),
          model
        )

      let busy, _ = update(ThreadTab.State.PollVoteRequested commit, chosen)

      Expect.equal busy.Busy (Some commit.Id) "the vote did not busy"

      let next, _ = update(ThreadTab.State.PollVotedDone(Ok commit), busy)

      Expect.equal next.Busy None "the vote kept the slot"
      Expect.isTrue (next.PollChoices.IsEmpty) "the choice stayed")

    testCase "a thread tab's reply delta clears when selected" (fun () ->
      let id = TabId.thread "commit"
      let tab = EditorTab.thread(id, "a1b2 @ana", 3)

      let opened = EditorState.openTab(tab, EditorState.initial Following)
      let counted = EditorState.setReplies(id, 7, opened)

      let before =
        counted
        |> EditorState.tabs
        |> List.find(fun tab -> EditorTab.id tab = id)

      Expect.equal (EditorTab.dirty before) 4 "the unseen delta"
      Expect.equal (EditorTab.seenReplies before) 3 "the seen count"

      let selected = EditorState.selectTab(id, counted)

      let after =
        selected
        |> EditorState.tabs
        |> List.find(fun tab -> EditorTab.id tab = id)

      Expect.equal (EditorTab.dirty after) 0 "the dot stayed"
      Expect.equal (EditorTab.seenReplies after) 7 "the watermark moved")

    testCase
      "the breadcrumb cache holds one entry and skips a held read"
      (fun () ->
        let id = threadPage.Commit.Id

        Expect.isTrue
          (ThreadTab.State.needsPeek(showModel, id))
          "the empty cache thought it held the commit"

        let cached, _ =
          update(ThreadTab.State.Peeked(id, Ok threadPage), showModel)

        Expect.isSome
          (ThreadTab.State.peekCached(cached, id))
          "the peek never landed"

        Expect.isFalse
          (ThreadTab.State.needsPeek(cached, id))
          "the held peek asked for another read"

        let replaced, _ =
          update(ThreadTab.State.Peeked("other", Ok threadPage), cached)

        Expect.isNone
          (ThreadTab.State.peekCached(replaced, id))
          "the cache kept two entries"

        Expect.isSome
          (ThreadTab.State.peekCached(replaced, "other"))
          "the new entry never landed")

    testCaseAsync
      "the quote and reply intents leave through their sinks"
      (async {
        let quotes = ResizeArray<Commit>()
        let replies = ResizeArray<Commit>()

        let api = threadApi()

        let sinks = {
          Quote = fun commit -> quotes.Add commit
          Reply = fun commit -> replies.Add commit
          Snapshot = ignore
          Fetch = ignore
        }

        let! window, handle =
          probeUiAsync(fun () ->
            let control, handle =
              ThreadTab.mount(
                ui,
                props(FakeEnv(api :> IHttp), threadPage.Commit.Id, sinks)
              )

            let window =
              Window(Width = 900.0, Height = 800.0, Content = control)

            window.Show()
            window, handle)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "It was DNS.")
          )

        Expect.isTrue shown "the checked-out commit never rendered"

        do!
          onUiAsync(fun () ->
            controlByName(window, "QuoteButton")
            |> Option.iter(fun control -> click(control :?> Button)))

        let! quoted =
          waitForAsync(5000, (fun () -> quotes.Count), fun count -> count = 1)

        Expect.isTrue quoted "the quote sink never fired"

        do!
          onUiAsync(fun () ->
            controlByName(window, "ReplyButton")
            |> Option.iter(fun control -> click(control :?> Button)))

        let! replied =
          waitForAsync(5000, (fun () -> replies.Count), fun count -> count = 1)

        Expect.isTrue replied "the reply sink never fired"

        Expect.equal quotes[0].Id threadPage.Commit.Id "the quoted commit"
        Expect.equal replies[0].Id threadPage.Commit.Id "the replied commit"

        do!
          onUiAsync(fun () ->
            window.Close()
            handle.Dispose())
      })
  ]
