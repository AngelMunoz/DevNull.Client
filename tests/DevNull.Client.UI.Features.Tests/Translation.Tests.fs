module DevNull.Client.UI.Tests.Translation

open System
open System.IO
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open Avalonia.Controls
open DevNull.Client
open DevNull.Client.Core
open DevNull.Client.Components.TranslationModel
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Types
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Shell
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness
open DevNull.Client.UI.Tests.App

let settingsJson =
  """{"data":{"timezone":"Europe/Budapest","show_local_time":true,"translation_language":"ja","automatic_translation":true,"translation_languages":[{"code":"ja","name":"Japanese","native_name":"日本語"},{"code":"fr","name":"French","native_name":"français"}],"is_admin":false}}"""

let manualSettingsJson =
  """{"data":{"timezone":"Europe/Budapest","show_local_time":true,"translation_language":"ja","automatic_translation":false,"translation_languages":[{"code":"ja","name":"Japanese","native_name":"日本語"},{"code":"fr","name":"French","native_name":"français"}],"is_admin":false}}"""

let translationJson =
  """{"translation":{"language":"en","source_language":"de","source_name":"German","available":true,"translated":{"body_segments":[{"type":"text","text":"Good morning"}],"preview_segments":null}}}"""

let translatedPageJson = logPageJson

let german: CommitTranslation = {
  Language = "en"
  SourceLanguage = Some "de"
  SourceName = Some "German"
  Available = true
  Translated =
    Some {
      BodySegments = [ TextSegment "Good morning" ]
      PreviewSegments = None
    }
}

let user: User = {
  Id = "01a151db-7fee-727b-a7ce-c911a7556242"
  Handle = "octocat"
  Name = "The Octocat"
  AvatarUrl = None
  Bio = None
  Lane = None
  CommitsCount = None
  FollowersCount = None
  FollowingCount = None
  FollowedByFollowingCount = None
  IsFollowed = None
  FollowsYou = None
  Live = None
}

let commitOf(translation: CommitTranslation option) : Commit = {
  Id = "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
  ShortHash = "b8b9c0d"
  Body = "It was DNS."
  BodySegments = []
  PreviewSegments = None
  Mentions = []
  ParentId = None
  Parent = None
  HtmlUrl = "https://dev-null.sh/@octocat/018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d"
  CreatedAt = DateTimeOffset.UnixEpoch
  StarsCount = None
  RepliesCount = None
  Status = CommitStatus.Approved
  Author = Person user
  Images = []
  Starred = None
  IsPinned = false
  PasteisCount = 0
  QuotedCommitId = None
  QuotedCommit = None
  Poll = None
  LinkPreview = None
  ReplyPreview = None
  Translation = translation
}

let startShellWithTranslations
  (store: SessionStore, http: IHttp, translations: TranslationCache)
  : Async<Window> =
  let env = TestEnv(store, http, translations = translations)

  async {
    let! window = probeUiAsync(fun () -> Shell(env, None) :> Window)
    return! showAsync window
  }

let settingsApi() =
  FakeApi(fun request ->
    match request.Method, request.Target with
    | "GET", "/api/v1/me/settings" -> ok settingsJson
    | "GET", "/api/v1/me" -> ok meJson
    | "GET", "/api/v1/me/devices" -> ok "{\"data\":[]}"
    | "PUT", "/api/v1/me/translation-language" -> ok meJson
    | "PUT", "/api/v1/me/timezone" -> ok meJson
    | "PUT", "/api/v1/me/automatic-translation" -> ok meJson
    | "GET", target when target.StartsWith "/api/v1/log/" ->
      ok translatedPageJson
    | "POST", _ -> ok translationJson
    | _ -> ok "{}")

let translatedApi() =
  FakeApi(fun request ->
    match request.Method, request.Target with
    | "GET", target when target.StartsWith "/api/v1/log/" ->
      ok translatedPageJson
    | "POST", target when target.EndsWith "/translation" -> ok translationJson
    | "GET", "/api/v1/me" -> ok meJson
    | "GET", "/api/v1/me/settings" -> ok manualSettingsJson
    | _ -> ok "{}")

let automaticApi() =
  FakeApi(fun request ->
    match request.Method, request.Target with
    | "GET", target when target.StartsWith "/api/v1/log/" ->
      ok translatedPageJson
    | "POST", target when target.EndsWith "/translation" -> ok translationJson
    | "GET", "/api/v1/me" -> ok meJson
    | "GET", "/api/v1/me/settings" -> ok settingsJson
    | _ -> ok "{}")

let retryApi() =
  let attempts = ref 0

  FakeApi(fun request ->
    match request.Method, request.Target with
    | "GET", target when target.StartsWith "/api/v1/log/" ->
      ok translatedPageJson
    | "POST", target when target.EndsWith "/translation" ->
      attempts.Value <- attempts.Value + 1

      if attempts.Value = 1 then
        {
          Status = 503
          Headers = []
          Body =
            """{"message":"Could not translate this commit. Try again."}"""
        }
      else
        ok translationJson
    | "GET", "/api/v1/me" -> ok meJson
    | "GET", "/api/v1/me/settings" -> ok manualSettingsJson
    | _ -> ok "{}")

let tests =
  testList "translation" [
    testCaseAsync
      "a 503 waits and tries again"
      (async {
        let attempts = ref 0

        let attempt() = async {
          attempts.Value <- attempts.Value + 1

          return
            if attempts.Value < 3 then
              Error(
                Refused(
                  503,
                  None,
                  "Could not translate this commit. Try again."
                )
              )
            else
              Ok {
                Translation = Some german
                Json = translationJson
              }
        }

        let! result = execute([ 0; 0 ], attempt)

        Expect.equal 3 attempts.Value "attempts"
        Expect.isOk result "answer"
      })

    testCaseAsync
      "a 429 comes back at once"
      (async {
        let attempts = ref 0

        let attempt() = async {
          attempts.Value <- attempts.Value + 1
          return Error(Refused(429, None, "Too Many Attempts."))
        }

        let! _ = execute([ 0; 0 ], attempt)

        Expect.equal 1 attempts.Value "attempts"
      })

    testCaseAsync
      "the automatic choice shows the translated body"
      (async {
        let card =
          view {
            Automatic = true
            Book = initial
            Now = DateTimeOffset.UnixEpoch
            Commit = commitOf(Some german)
          }

        match card with
        | ValueSome card -> Expect.isSome card.Body "translated body"
        | ValueNone -> failtest "expected a translation view"
      })

    testCaseAsync
      "showing the original hides the translated body"
      (async {
        let commit = commitOf(Some german)
        let book = showOriginal(initial, commit.Id)

        let card =
          view {
            Automatic = true
            Book = book
            Now = DateTimeOffset.UnixEpoch
            Commit = commit
          }

        match card with
        | ValueSome card ->
          Expect.isNone card.Body "translated body"
          Expect.isTrue card.CanTranslate "translate button"
        | ValueNone -> failtest "expected a translation view"
      })

    testCaseAsync
      "an approved commit offers Translate"
      (async {
        let card =
          view {
            Automatic = true
            Book = initial
            Now = DateTimeOffset.UnixEpoch
            Commit = commitOf None
          }

        match card with
        | ValueSome card ->
          Expect.isTrue card.CanTranslate "translate button"
          Expect.isNone card.Body "original body"
        | ValueNone -> failtest "expected a translate line"
      })

    testCaseAsync
      "a commit with nothing to translate shows nothing"
      (async {
        let commit = {
          commitOf None with
              Status = CommitStatus.Rejected
        }

        let card =
          view {
            Automatic = true
            Book = initial
            Now = DateTimeOffset.UnixEpoch
            Commit = commit
          }

        Expect.isTrue (card.IsNone) "no translation line"
      })

    testCaseAsync
      "a not-found answer drops the button"
      (async {
        let commit = commitOf None

        let book =
          finish {
            Book = initial
            Now = DateTimeOffset.UnixEpoch
            CommitId = commit.Id
            Result = Error(Refused(404, None, "Not Found"))
          }

        let card =
          view {
            Automatic = true
            Book = book
            Now = DateTimeOffset.UnixEpoch
            Commit = commit
          }

        Expect.isTrue (card.IsNone) "no translation line"
      })

    testCaseAsync
      "the rate limit blocks the button for a minute"
      (async {
        let now = DateTimeOffset.UnixEpoch
        let commit = commitOf(Some german)

        let book =
          finish {
            Book = initial
            Now = now
            CommitId = commit.Id
            Result = Error(Refused(429, None, "Too Many Attempts."))
          }

        let during =
          view {
            Automatic = false
            Book = book
            Now = now.AddSeconds 30.0
            Commit = commit
          }

        let after =
          view {
            Automatic = false
            Book = book
            Now = now.AddSeconds 61.0
            Commit = commit
          }

        Expect.isTrue (during |> ValueOption.exists _.Waiting) "waiting"
        Expect.isFalse (after |> ValueOption.exists _.Waiting) "armed"

        Expect.isTrue
          (after |> ValueOption.exists _.CanTranslate)
          "translate button"
      })

    testCaseAsync
      "the language follows the commit, the choice, the device, then English"
      (async {
        let commit = commitOf(Some german)

        Expect.equal
          "en"
          (languageFor(None, commit) |> LanguageCodes.value)
          "the commit's own language"

        let settings language languages = {
          Timezone = FollowDevice
          TranslationLanguage = language
          AutomaticTranslation = true
          Languages = languages
        }

        let japanese = {
          Code = "ja"
          Name = "Japanese"
          NativeName = "日本語"
        }

        Expect.equal
          "ja"
          (languageFor(
            Some(settings (Some japanese) [ japanese ]),
            commitOf None
           )
           |> LanguageCodes.value)
          "the chosen language"

        Expect.equal
          "en"
          (languageFor(None, commitOf None) |> LanguageCodes.value)
          "the fallback"
      })

    testCaseAsync
      "the settings page lists the timezone and translation rows and saves a language"
      (async {
        let api = settingsApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "The Octocat")
        Expect.isTrue shown "the log never rendered"

        do! clickWhenShown(window, "Settings")

        let! timezone = showsText(window, "Timezone")
        Expect.isTrue timezone "the timezone row never rendered"

        let! language = showsText(window, "Translation language")
        Expect.isTrue language "the language row never rendered"

        let! automatic = showsText(window, "Automatically show translations")
        Expect.isTrue automatic "the automatic row never rendered"

        do! clickWhenShown(window, "French · français")

        do! clickWhenShown(window, "Save translation language")

        let! saved =
          waitForAsync(
            5000,
            (fun () ->
              api.Requests
              |> List.filter(fun request ->
                request.Target = "/api/v1/me/translation-language")
              |> List.filter(fun request ->
                match request.Body with
                | Text body -> body.Contains "\"fr\""
                | _ -> false)),
            fun requests -> not requests.IsEmpty
          )

        Expect.isTrue saved "the language was never saved"
      })

    testCaseAsync
      "a commit translates and shows its original again"
      (async {
        let api = translatedApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")
        Expect.isTrue shown "the log never rendered"

        let! button =
          waitForAsync(
            5000,
            (fun () -> buttonWithText(window, "Translate")),
            Option.isSome
          )

        Expect.isTrue button "the translate button never appeared"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Translate") |> Option.iter click)

        let! translated = showsText(window, "Translated from German")
        Expect.isTrue translated "the translated body never showed"

        let! original =
          waitForAsync(
            5000,
            (fun () -> buttonWithText(window, "Show original")),
            Option.isSome
          )

        Expect.isTrue original "the original link never appeared"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Show original") |> Option.iter click)

        let! restored =
          waitForAsync(
            5000,
            (fun () -> window),
            (fun root -> not(hasText(root, "Translated from German")))
          )

        Expect.isTrue restored "the translated body stayed"
      })
    testCaseAsync
      "a refused translation tries again and lands"
      (async {
        let api = retryApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown = showsText(window, "It was DNS.")
        Expect.isTrue shown "the log never rendered"

        let! button =
          waitForAsync(
            5000,
            (fun () -> buttonWithText(window, "Translate")),
            Option.isSome
          )

        Expect.isTrue button "the translate button never appeared"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Translate") |> Option.iter click)

        let! translated = showsText(window, "Translated from German")
        Expect.isTrue translated "the retry never landed"
      })
    testCaseAsync
      "a fetched translation stays when the log returns"
      (async {
        let api = automaticApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! translated = showsText(window, "Translated from German")
        Expect.isTrue translated "the automatic translation never showed"
        Expect.isTrue translated "the translation never showed"

        do! clickWhenShown(window, "Following")
        let! _ = showsText(window, "It was DNS.")

        do! clickWhenShown(window, "Latest")

        let! again = showsText(window, "Translated from German")
        Expect.isTrue again "the translation did not stay"

        let calls =
          api.Requests
          |> List.filter(fun request ->
            request.Method = "POST" && request.Target.EndsWith "/translation")

        Expect.equal 1 calls.Length "translation calls"
      })
    testCaseAsync
      "a translation is written to the disk and read cold"
      (async {
        let tier = MemoryFiles()
        let commit = commitOf None

        let payload: TranslationPayload = {
          Translation = Some german
          Json = translationJson
        }

        let first =
          TranslationCache.createWith("cache", tier.Files, NullLogger.Instance)

        do! first.Set(commit.Id, payload)

        let paths = tier.Files.List()
        Expect.equal 1 paths.Length "file writes"

        let! stored = tier.Files.Read paths[0]

        Expect.equal
          (Some translationJson)
          (stored |> Option.map fst)
          "file contents"

        let second =
          TranslationCache.createWith("cache", tier.Files, NullLogger.Instance)

        let! fromDisk = second.TryGet commit.Id
        Expect.equal (Some german) fromDisk "file read"

        let! found =
          waitForAsync(
            5000,
            (fun () -> second.Snapshot()),
            fun seed -> not seed.IsEmpty
          )

        Expect.isTrue found "the seed never arrived"

        let seed = second.Snapshot()

        Expect.equal (Some german) (seed |> Map.tryFind commit.Id) "seed"

        let! book = recall(second, initial, commit.Id)

        match book with
        | Some book ->
          let card =
            view {
              Automatic = false
              Book = book
              Now = DateTimeOffset.UnixEpoch
              Commit = commit
            }

          Expect.isTrue
            (card |> ValueOption.exists _.Body.IsSome)
            "manual cache read"
        | None -> failtest "the cache never recalled the translation"

        let empty =
          TranslationCache.createWith(
            "empty",
            MemoryFiles().Files,
            NullLogger.Instance
          )

        let! missed = recall(empty, initial, commit.Id)
        Expect.isNone missed "cache miss"
      })
    testCaseAsync
      "a foreign file leaves with a deletion"
      (async {
        let tier = MemoryFiles()
        let commit = commitOf None

        let cache =
          TranslationCache.createWith("cache", tier.Files, NullLogger.Instance)

        do!
          cache.Set(
            commit.Id,
            {
              Translation = Some german
              Json = translationJson
            }
          )

        do! tier.Files.Keep(Path.Combine("cache", "not a commit id.json"), "{}")

        do! cache.Forget commit.Id

        let! gone =
          waitForAsync(
            5000,
            (fun () ->
              tier.Files.List() |> List.map Path.GetFileNameWithoutExtension),
            List.isEmpty
          )

        Expect.isTrue gone "the foreign file left"
      })
    testCaseAsync
      "a translation older than seven days is dropped"
      (async {
        let tier = MemoryFiles()
        let commit = commitOf None

        let first =
          TranslationCache.createWith("cache", tier.Files, NullLogger.Instance)

        do!
          first.Set(
            commit.Id,
            {
              Translation = Some german
              Json = translationJson
            }
          )

        let path = tier.Files.List() |> List.exactlyOne

        tier.Age(path, DateTime.UtcNow.AddDays(-8.0))

        let second =
          TranslationCache.createWith("cache", tier.Files, NullLogger.Instance)

        let! expired = second.TryGet commit.Id
        Expect.isNone expired "expired translation"

        let! after = tier.Files.Read path
        Expect.isNone after "expired file"
      })
    testCaseAsync
      "a fetched translation is not posted again after a restart"
      (async {
        let tier = MemoryFiles()
        let firstApi = translatedApi()

        let first =
          TranslationCache.createWith("cache", tier.Files, NullLogger.Instance)

        let! window =
          startShellWithTranslations(signedInStore(), firstApi :> IHttp, first)

        let! shown = showsText(window, "It was DNS.")
        Expect.isTrue shown "the log never rendered"

        let! button =
          waitForAsync(
            5000,
            (fun () -> buttonWithText(window, "Translate")),
            Option.isSome
          )

        Expect.isTrue button "the translate button never appeared"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Translate") |> Option.iter click)

        let! translated = showsText(window, "Translated from German")
        Expect.isTrue translated "the translated body never showed"

        let! written =
          waitForAsync(
            5000,
            (fun () -> tier.Files.List()),
            fun paths -> paths.Length = 1
          )

        Expect.isTrue written "the file was never written"

        let secondApi = automaticApi()

        let second =
          TranslationCache.createWith("cache", tier.Files, NullLogger.Instance)

        let! loaded =
          waitForAsync(
            5000,
            (fun () -> second.Snapshot()),
            fun seed -> not seed.IsEmpty
          )

        Expect.isTrue loaded "the disk was never read"

        let! remounted =
          startShellWithTranslations(
            signedInStore(),
            secondApi :> IHttp,
            second
          )

        let! cached = showsText(remounted, "Translated from German")
        Expect.isTrue cached "the cached translation never showed"

        let! body = showsText(remounted, "Good morning")
        Expect.isTrue body "the cached body never showed"

        let posts =
          secondApi.Requests
          |> List.filter(fun request ->
            request.Method = "POST" && request.Target.EndsWith "/translation")

        Expect.equal 0 posts.Length "translation calls after restart"
      })
  ]
