module DevNull.Client.UI.Tests.Guilds

// The guilds shared base through real surfaces: the log feed's guild
// chip, the composer's guild picker and its tag completion, the
// directory over its own reads and the sidebar that opens it, and the
// identity card's board button.

open System
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.VisualTree
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open MVUish.Avalonia
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client

module ComposerPage = DevNull.Client.Components.Composer
module DirectoryPage = DevNull.Client.Components.GuildDirectory

open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Formatting
open DevNull.Client.Errors
open DevNull.Client.GuildBoard

module CommitCard = DevNull.Client.Components.CommitCard
module Feed = DevNull.Client.Components.Feed
module CoreTypes = DevNull.Client.Core.ApiTypes

open DevNull.Client.UI.Tests.App
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

/// One directory row as the wire carries it.
let rowJson(guild: Guild) : string =
  sprintf
    """{"handle":"%s","name":"%s","description":%s,"emoji":"%s","icon_url":%s,"member_count":%d,"is_joined":%b,"is_muted":%b,"is_paused":%b,"html_url":"%s"}"""
    guild.Handle
    guild.Name
    (match guild.Description with
     | Some text -> sprintf "\"%s\"" text
     | None -> "null")
    guild.Emoji
    (match guild.IconUrl with
     | Some url -> sprintf "\"%s\"" url
     | None -> "null")
    guild.MemberCount
    guild.IsJoined
    guild.IsMuted
    guild.IsPaused
    guild.HtmlUrl

let guildsPageOf(rows: Guild list, page: int, lastPage: int) : string =
  sprintf
    """{"data":[%s],"links":{},"meta":{"current_page":%d,"last_page":%d,"total":74}}"""
    (rows |> List.map rowJson |> String.concat ",")
    page
    lastPage

let devGuild = {
  Handle = "dev"
  Name = "Dev Guild"
  Emoji = "🛠"
  IconUrl = None
  Description = None
  MemberCount = 1204
  IsJoined = true
  IsMuted = true
  IsPaused = false
  HtmlUrl = "https://dev-null.sh/g/dev"
}

let opsGuild = {
  Handle = "ops"
  Name = "Ops Room"
  Emoji = "🚨"
  IconUrl = None
  Description = Some "On-call and outages."
  MemberCount = 87
  IsJoined = true
  IsMuted = false
  IsPaused = false
  HtmlUrl = "https://dev-null.sh/g/ops"
}

let soloGuild = {
  Handle = "solo"
  Name = "Solo Builders"
  Emoji = "🧱"
  IconUrl = None
  Description = None
  MemberCount = 3
  IsJoined = false
  IsMuted = false
  IsPaused = false
  HtmlUrl = "https://dev-null.sh/g/solo"
}

/// The directory the fake serves: the term picks the page, every
/// read answers one numbered page of the offset sequence.
let directoryApi(pages: Map<string * int, Guild list * int>) =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/guilds" then
      let term =
        match request.Target with
        | _ when request.Target.Contains("q=") ->
          let at = request.Target.IndexOf "q=" + 2

          let finish =
            let hit = request.Target.IndexOf('&', at)

            if hit < 0 then request.Target.Length else hit

          Uri.UnescapeDataString(request.Target.Substring(at, finish - at))
        | _ -> ""

      let page =
        match request.Target.IndexOf "page=" with
        | -1 -> 1
        | at ->
          int(request.Target.Substring(at + 5).TrimEnd('&').Split('&').[0])

      match Map.tryFind (term, page) pages with
      | Some(rows, lastPage) -> ok(guildsPageOf(rows, page, lastPage))
      | None -> ok(guildsPageOf([], page, 1))
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The log page whose first commit carries its guild and whose second
/// carries none: the chip must tell the two apart on one screen.
let guildLogJson =
  let guildCommit =
    commitJson
      .Replace(
        "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d",
        "018f4c1e-9999-7c3a-9b2d-5e6f7a8b9c0d"
      )
      .Replace(
        "\"status\":\"approved\"",
        "\"status\":\"approved\",\"guild\":{\"handle\":\"dev\",\"name\":\"Dev Guild\",\"emoji\":\"🛠\",\"icon_url\":null}"
      )

  sprintf
    "{\"data\":[%s,%s],\"links\":{},\"meta\":{\"per_page\":20,\"next_cursor\":null,\"prev_cursor\":null},\"log\":\"latest\",\"is_ranked\":false,\"new_commits_count\":null,\"suggestions\":[]}"
    guildCommit
    commitJson

let guildLogApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok guildLogJson
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The search the picker and the tag completion ask: one directory
/// page carrying the joined ops room and an unjoined builder room.
let pickerApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/guilds" then
      ok(guildsPageOf([ opsGuild; soloGuild ], 1, 1))
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The commit the server creates for a guild post, guild object
/// riding beside the body as every feed read carries it.
let guildCreatedCommitJson =
  commitJson.Replace(
    "\"status\":\"approved\"",
    "\"status\":\"approved\",\"guild\":{\"handle\":\"ops\",\"name\":\"Ops Room\",\"emoji\":\"🚨\",\"icon_url\":null}"
  )

let pickerComposerApi() =
  FakeApi(fun request ->
    if request.Method = "POST" && request.Target = "/api/v1/commits" then
      ok(sprintf "{\"data\":%s}" guildCreatedCommitJson)
    elif request.Target.StartsWith "/api/v1/guilds" then
      ok(guildsPageOf([ opsGuild; soloGuild ], 1, 1))
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// Mounts one real composer page over the given env, aimed the way a
/// guild host aims it.
let mountComposer
  (
    env: TestEnv,
    target: ComposerPage.Target,
    guilds: (unit -> Guild list) voption,
    guild: string voption
  ) : Async<Window> = async {
  // The control lives on the session thread: it is born inside a
  // dispatched probe, like every control a test touches.
  let posted = ResizeArray<Commit>()

  let! composer =
    probeUiAsync(fun () ->
      ComposerPage.Page {
        Env = env
        Session = signedInSession()
        Target = target
        Participants = ValueNone
        Guilds = guilds
        Guild = guild
        OnCommitted = fun commit -> posted.Add commit
        OnScheduled = ValueNone
        OnOpenQueue = ValueNone
        Draft = ValueNone
        OnBodyReady = ValueNone
      }
      :> Control)

  let! window = mountAsync(composer)
  return window
}

/// Types into the composer's body and leaves the caret past the text.
let typeIntoBody(window: Window, text: string) : Async<unit> = async {
  let body() =
    controlByName(window, "ComposerBody")
    |> Option.bind(function
      | :? TextBox as box -> Some box
      | _ -> None)

  let! typed = waitForAsync(5000, body, Option.isSome)

  Expect.isTrue typed "the composer body never appeared"

  do!
    onUiAsync(fun () ->
      body()
      |> Option.iter(fun box ->
        box.Text <- text
        box.CaretIndex <- text.Length))
}

/// The board's own guild row: the handle the board read names.
let boardGuild = {
  devGuild with
      Handle = "rust"
      Name = "Rust Guild"
      IsMuted = false
}

/// The second page's commit: another id and another author, so the
/// grown feed shows a row the first page never carried.
let boardSecondCommitJson =
  commitJson
    .Replace(
      "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d",
      "018f4c1e-7777-7c3a-9b2d-5e6f7a8b9c0d"
    )
    .Replace("\"name\":\"The Octocat\"", "\"name\":\"The Second\"")

let boardJsonOf
  (guild: Guild, rows: string list, page: int, next: string option)
  =
  sprintf
    """{"data":[%s],"links":{"first":"https://dev-null.sh/api/v1/guilds/%s?page=1","last":null,"prev":null,"next":%s},"meta":{"current_page":%d,"per_page":30,"from":1,"to":1,"path":"https://dev-null.sh/api/v1/guilds/%s"},"guild":%s,"pinned_commit":%s,"can_moderate":false}"""
    (rows |> String.concat ",")
    guild.Handle
    (match next with
     | Some url -> sprintf "\"%s\"" url
     | None -> "null")
    page
    guild.Handle
    (rowJson guild)
    commitJson

let boardPageJson(rows: string list, page: int, next: string option) =
  boardJsonOf(boardGuild, rows, page, next)

/// The board the fake serves: page one holds one commit and a next
/// link, page two holds another and no next.
let boardApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/guilds/rust" then
      if request.Target.Contains("page=2") then
        ok(boardPageJson([ boardSecondCommitJson ], 2, None))
      else
        ok(
          boardPageJson(
            [ commitJson ],
            1,
            Some "https://dev-null.sh/api/v1/guilds/rust?page=2"
          )
        )
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The api of the sidebar walk: the log answers its guild-carrying
/// page, the directory answers both guilds on page one, and the dev
/// board answers two pages.
let guildsNavApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/log/" then
      ok guildLogJson
    elif request.Target.StartsWith "/api/v1/guilds/dev" then
      if request.Target.Contains("page=2") then
        ok(boardJsonOf(devGuild, [ boardSecondCommitJson ], 2, None))
      else
        ok(
          boardJsonOf(
            devGuild,
            [ commitJson ],
            1,
            Some "https://dev-null.sh/api/v1/guilds/dev?page=2"
          )
        )
    elif request.Target.StartsWith "/api/v1/guilds" then
      ok(guildsPageOf([ devGuild; opsGuild ], 1, 1))
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The board whose every read the flag-off server refuses.
let refusedBoardApi() =
  FakeApi(fun request ->
    if request.Target.StartsWith "/api/v1/guilds/rust" then
      refused(404, "Not found.")
    else
      (match accountAnswer request with
       | ValueSome answer -> answer
       | ValueNone -> ok "{}"))

/// The board mechanism through the standard surfaces: a minimal host
/// program owns the board state in its model, folds the pure
/// transitions, and projects the identity card, the commit rows in a
/// virtualized feed, and the load-more footer — the presentation each
/// layout builds its own version of.
module BoardHost =

  type Model = { Board: BoardState }

  type Msg =
    | LoadBoard
    | MoreBoard
    | BoardArrived of page: int * Result<CoreTypes.GuildBoard, AppError>

  let showPage(env: TestEnv, page: int) =
    Guilds.show(
      env,
      {
        KeyFile = (signedInSession()).KeyFile
        Handle = "rust"
        Page = page
      }
    )

  let init (env: TestEnv) () : Model * Cmd<Msg> =
    {
      Board = GuildBoard.load GuildBoard.empty
    },
    Cmd.OfAsync.perform showPage (env, 1) (fun outcome ->
      BoardArrived(1, outcome))

  let update (env: TestEnv) msg model : Model * Cmd<Msg> =
    match msg with
    | LoadBoard ->
      {
        model with
            Board = GuildBoard.load model.Board
      },
      Cmd.OfAsync.perform showPage (env, 1) (fun outcome ->
        BoardArrived(1, outcome))
    | MoreBoard ->
      let next = GuildBoard.more model.Board

      let cmd =
        match next.Wanted with
        | ValueSome page when next.Wanted <> model.Board.Wanted ->
          Cmd.OfAsync.perform showPage (env, page) (fun outcome ->
            BoardArrived(page, outcome))
        | _ -> Cmd.none

      { model with Board = next }, cmd
    | BoardArrived(page, outcome) ->
      {
        model with
            Board = GuildBoard.arrived(model.Board, page, outcome)
      },
      Cmd.none

  let rowProps(env: TestEnv) : CommitCard.RowProps =
    let now = new BehaviorSubject<DateTimeOffset>(DateTimeOffset.UtcNow)
    let pastels = new BehaviorSubject<bool>(false)
    let choices = new BehaviorSubject<Map<string, string>>(Map.empty)

    let translation =
      new BehaviorSubject<Commit -> CommitCard.TranslationView voption>(fun _ ->
        ValueNone)

    {
      OnEvent = ignore
      OnOpenExternal = ignore
      ViewerId = ""
      Pasteis = pastels
      Now = now
      PollChoices = choices
      Dense = false
      Images = (env :> DevNull.Client.ImageCache.IProvideImageCache).ImageCache
      Flyout = ValueNone
      Pin = ValueNone
      Star = ValueNone
      Stars = ValueNone
      Vote = ValueNone
      Pastel = ValueNone
      Menu = ValueNone
      Translation = translation
      Zone = zoneOf None
    }

  let nodeId(node: CommitNode) =
    match node with
    | LiveCommit commit -> commit.Id
    | Tombstone stone -> stone.Id

  let view
    (env: TestEnv)
    (model: IObservable<Model>)
    (dispatch: Dispatch<Msg>)
    : Control =
    let props = rowProps env

    // The identity card rebuilds only when the guild row changes.
    let identity =
      ContentControl()
        .Content(
          model
          |> Observable.map _.Board.Guild
          |> Observable.distinctUntilChanged
          |> Observable.map(fun guild ->
            match guild with
            | Some guild ->
              TextBlock()
                .Classes(Theme.name Theme.Title)
                .Margin(16.0, 16.0, 16.0, 0.0)
                .Text(guild.Name)
              :> Control
            | None -> Panel() :> Control)
          |> _.ToBinding()
        )

    let pinned =
      TextBlock()
        .Name("BoardPinned")
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
        .Margin(16.0, 0.0, 16.0, 8.0)
        .Text("Pinned")
        .IsVisible(
          model
          |> Observable.map _.Board.Pinned.IsSome
          |> Observable.distinctUntilChanged
        )

    let refused =
      TextBlock()
        .Name("BoardRefused")
        .Classes(Theme.name Theme.Muted)
        .Margin(16.0)
        .Text("Guilds are not available.")
        .IsVisible(
          model
          |> Observable.map _.Board.Refused
          |> Observable.distinctUntilChanged
        )

    let loadMore =
      Button()
        .Name("BoardLoadMore")
        .Classes(Theme.name Theme.GhostButton)
        .Margin(16.0, 4.0, 16.0, 12.0)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .Content("Load more")
        .OnClickHandler(fun _ _ -> dispatch MoreBoard)
        .IsVisible(
          model
          |> Observable.map _.Board.HasMore
          |> Observable.distinctUntilChanged
        )

    Feed.view {
      Header = ValueSome(StackPanel().Children(identity, pinned, refused))
      Items =
        model
        |> Observable.map _.Board.Commits
        |> Observable.distinctUntilChanged
      KeyOf = nodeId
      Row = CommitCard.feedRow props
      Footer = ValueSome loadMore
      HasMore =
        model
        |> Observable.map _.Board.HasMore
        |> Observable.distinctUntilChanged
      ScrollTo = ValueNone
      Ride = false
      Restore = ValueNone
      OnScroll = ValueNone
      OnRide = ValueNone
      OnNeedMore = ValueNone
    }

/// Mounts one board host over the pages the fake answers for.
let mountBoard(env: TestEnv) : Async<Window * IDisposable> = async {
  let! control, handle =
    probeUiAsync(fun () ->
      Programs.runPage(
        Harness.ui,
        Program.mkProgram
          (BoardHost.init env)
          (BoardHost.update env)
          (BoardHost.view env)
      ))

  let! window = mountAsync control
  return window, handle
}

let tests =
  testList "guilds shared base" [
    testCaseAsync
      "the log feed shows the guild chip on guild commits alone"
      (async {
        let api = guildLogApi()
        let! window = startShell(signedInStore(), api :> IHttp)

        let! shown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "The Octocat")
          )

        Expect.isTrue shown "the shell never rendered"

        // The guild-less commit beside it carries no chip: the count
        // settles at one, read on the session thread like every probe.
        do! settleAsync()

        let! count =
          probeUiAsync(fun () ->
            window.GetVisualDescendants()
            |> Seq.filter(fun visual -> visual.Name = "GuildTag")
            |> Seq.length)

        Expect.equal count 1 "one chip for one guild commit"

        let! named =
          probeUiAsync(fun () ->
            controlByName(window, "GuildTag")
            |> Option.map(fun chip -> hasText(chip, "Dev Guild"))
            |> Option.defaultValue false)

        Expect.isTrue named "the chip names the guild"
      })

    testCaseAsync
      "the composer's picker lists the joined guilds first and the search beneath"
      (async {
        let api = pickerApi()
        let env = TestEnv(signedInStore(), api :> IHttp)

        let! window =
          mountComposer(
            env,
            ComposerPage.Fresh,
            ValueSome(fun () -> [ devGuild; opsGuild ]),
            ValueNone
          )

        let! trigger =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, "GuildPickerTrigger")),
            Option.isSome
          )

        Expect.isTrue trigger "the picker trigger never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "GuildPickerTrigger")
            |> Option.iter(function
              | :? Button as button -> click button
              | _ -> ()))

        let! opened =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "Dev Guild") && hasText(root, "Ops Room")
          )

        // The search box rides the card; typing asks the fake, whose
        // answers carry the unjoined room after the joined one.
        do!
          onUiAsync(fun () ->
            controlByName(window, "GuildPickerSearch")
            |> Option.iter(function
              | :? TextBox as box -> box.Text <- "gu"
              | _ -> ()))

        let! searched =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "Solo Builders")
          )

        Expect.isTrue searched "the search answer never arrived"

        // The joined room keeps its place above the unjoined one.
        let! ordered =
          probeUiAsync(fun () ->
            let yOf(text: string) =
              buttonWithText(window, text)
              |> Option.bind(fun button ->
                button.TranslatePoint(Point(0.0, 0.0), window)
                |> Option.ofNullable
                |> Option.map _.Y)

            match yOf "Ops Room", yOf "Solo Builders" with
            | Some above, Some below -> above < below
            | _ -> false)

        Expect.isTrue ordered "the joined row did not lead"
      })

    testCaseAsync
      "picking a guild strips in the target and the post lands on its board"
      (async {
        let api = pickerComposerApi()
        let env = TestEnv(signedInStore(), api :> IHttp)

        let! window =
          mountComposer(
            env,
            ComposerPage.Fresh,
            ValueSome(fun () -> [ devGuild; opsGuild ]),
            ValueNone
          )

        let! trigger =
          waitForAsync(
            8000,
            (fun () -> controlByName(window, "GuildPickerTrigger")),
            Option.isSome
          )

        Expect.isTrue trigger "the picker trigger never appeared"

        do!
          onUiAsync(fun () ->
            controlByName(window, "GuildPickerTrigger")
            |> Option.iter(function
              | :? Button as button -> click button
              | _ -> ()))

        let! opened =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "Ops Room")
          )

        Expect.isTrue opened "the picker never opened"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Ops Room") |> Option.iter click)

        let! stripped =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "Posting to #ops")
          )

        Expect.isTrue stripped "the target strip never showed"

        // Clearing returns the draft to no guild.
        do!
          onUiAsync(fun () ->
            controlByName(window, "ComposerTargetClear")
            |> Option.iter(function
              | :? Button as button -> click button
              | _ -> ()))

        let! cleared =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> not(hasText(root, "Posting to #ops"))
          )

        Expect.isTrue cleared "the strip never cleared"

        // The draft aims again before the send: the board rides it.
        do!
          onUiAsync(fun () ->
            controlByName(window, "GuildPickerTrigger")
            |> Option.iter(function
              | :? Button as button -> click button
              | _ -> ()))

        let! again =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "Ops Room")
          )

        Expect.isTrue again "the picker never reopened"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Ops Room") |> Option.iter click)

        let! stripped =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "Posting to #ops")
          )

        Expect.isTrue stripped "the target strip never showed again"

        do! typeIntoBody(window, "Ship the guild board")

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Commit") |> Option.iter click)

        let! landed =
          waitForAsync(
            8000,
            (fun () -> api.Requests),
            fun requests ->
              requests
              |> List.exists(fun request ->
                request.Method = "POST" && request.Target = "/api/v1/commits")
          )

        Expect.isTrue landed "the post never left"

        // The wire call is the behavior here: the board rode the post,
        // beside the rendered strip the person saw.
        let rodeBoard =
          api.Requests
          |> List.exists(fun request ->
            request.Method = "POST"
            && request.Target = "/api/v1/commits"
            && (match request.Body with
                | Text body -> body.Contains("\"guild_handle\":\"ops\"")
                | _ -> false))

        Expect.isTrue rodeBoard "the post never named its board"
      })

    testCaseAsync
      "a host that opens the composer aimed at a guild starts on the strip"
      (async {
        let api = pickerComposerApi()
        let env = TestEnv(signedInStore(), api :> IHttp)

        let! window =
          mountComposer(
            env,
            ComposerPage.Fresh,
            ValueSome(fun () -> [ opsGuild ]),
            ValueSome "ops"
          )

        let! stripped =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              hasText(root, "Posting to #ops")
              && hasText(root, "What do you want to share with the guild?")
          )

        Expect.isTrue stripped "the aimed draft never showed its strip"
      })

    testCaseAsync
      "a reply target hides the guild picker"
      (async {
        let api = pickerComposerApi()
        let env = TestEnv(signedInStore(), api :> IHttp)

        let! window =
          mountComposer(
            env,
            ComposerPage.ReplyTo "018f4c1e-6f8a-7c3a-9b2d-5e6f7a8b9c0d",
            ValueSome(fun () -> [ opsGuild ]),
            ValueNone
          )

        do! settleAsync()

        let! gone =
          probeUiAsync(fun () -> controlByName(window, "GuildPickerTrigger"))

        Expect.isNone gone "the picker rode a reply draft"
      })

    testCaseAsync
      "typing a guild tag offers the rows and Enter completes into the body"
      (async {
        let api = pickerComposerApi()
        let env = TestEnv(signedInStore(), api :> IHttp)

        let! window =
          mountComposer(
            env,
            ComposerPage.Fresh,
            ValueSome(fun () -> [ devGuild; opsGuild ]),
            ValueNone
          )

        do! typeIntoBody(window, "ship it to #op")

        let! offered =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "Ops Room")
          )

        Expect.isTrue offered "the tag rows never arrived"

        do!
          onUiAsync(fun () ->
            textBoxWithWatermark(window, "What did you ship?")
            |> Option.iter(fun box ->
              box.RaiseEvent(
                KeyEventArgs(
                  RoutedEvent = InputElement.KeyDownEvent,
                  Key = Key.Enter
                )
              )))

        let! completed =
          waitForAsync(
            8000,
            (fun () ->
              textBoxWithWatermark(window, "What did you ship?")
              |> Option.bind(fun box -> box.Text |> Option.ofObj)
              |> Option.defaultValue ""),
            fun text -> text = "ship it to #ops "
          )

        Expect.isTrue completed "the tag never completed into the body"
      })

    testCaseAsync
      "the directory pages and searches the site's own way"
      (async {
        let api =
          directoryApi(
            Map [
              ("", 1), ([ devGuild; opsGuild ], 2)
              ("", 2), ([ soloGuild ], 2)
              ("dev", 1), ([ devGuild ], 1)
            ]
          )

        let env = TestEnv(signedInStore(), api :> IHttp)

        let opened = ResizeArray<string>()

        let! directory =
          probeUiAsync(fun () ->
            DirectoryPage.Page {
              Env = env
              SignedIn = signedInSession()
              OnOpen = fun guild -> opened.Add guild.Handle
              Title = "Guilds"
            }
            :> Control)

        let! window = mountAsync(directory)

        // The count bar: the fake's directory holds seventy-four
        // guilds on two pages.
        let! listed =
          waitForAsync(
            10000,
            (fun () -> window),
            fun root ->
              hasText(root, "Dev Guild")
              && hasText(root, "Ops Room")
              && hasText(root, "74 guilds")
              && hasText(root, "Page 1 of 2")
          )

        Expect.isTrue listed "the first page never rendered"

        // Typing alone asks nothing; the Search button does.
        do!
          onUiAsync(fun () ->
            controlByName(window, "GuildSearch")
            |> Option.iter(function
              | :? TextBox as box -> box.Text <- "dev"
              | _ -> ()))

        do! settleAsync()

        let! unasked = probeUiAsync(fun () -> hasText(window, "Ops Room"))

        Expect.isTrue unasked "typing alone searched"

        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Search") |> Option.iter click)

        let! searched =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              hasText(root, "Dev Guild") && not(hasText(root, "Ops Room"))
          )

        Expect.isTrue searched "the search never narrowed the cards"

        // A pick answers the host's own callback, never a route.
        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Dev Guild") |> Option.iter click)

        let! picked =
          waitForAsync(
            8000,
            (fun () -> opened |> Seq.toList),
            fun handles -> handles = [ "dev" ]
          )

        Expect.isTrue picked "the open never reached the host"

        // The pager: the next chevron walks to page two, whose cards
        // replace page one's.
        let api2 =
          directoryApi(
            Map [
              ("", 1), ([ devGuild ], 2)
              ("", 2), ([ opsGuild; soloGuild ], 2)
            ]
          )

        let env2 = TestEnv(signedInStore(), api2 :> IHttp)

        let! directory2 =
          probeUiAsync(fun () ->
            DirectoryPage.Page {
              Env = env2
              SignedIn = signedInSession()
              OnOpen = ignore
              Title = "Guilds"
            }
            :> Control)

        let! window2 = mountAsync(directory2)

        let! first =
          waitForAsync(
            10000,
            (fun () -> window2),
            fun root ->
              hasText(root, "Dev Guild") && hasText(root, "Page 1 of 2")
          )

        Expect.isTrue first "the pager never rendered"

        do!
          onUiAsync(fun () ->
            controlByName(window2, "GuildNextPage")
            |> Option.iter(function
              | :? Button as button -> click button
              | _ -> ()))

        let! paged =
          waitForAsync(
            8000,
            (fun () -> window2),
            fun root ->
              hasText(root, "Ops Room")
              && hasText(root, "Solo Builders")
              && hasText(root, "Page 2 of 2")
              && not(hasText(root, "Dev Guild"))
          )

        Expect.isTrue paged "the next page never replaced the cards"
      })

    testCaseAsync
      "a flag-off server leaves the directory at its one line"
      (async {
        let refusedApi =
          FakeApi(fun request ->
            if request.Target.StartsWith "/api/v1/guilds" then
              refused(404, "Not found.")
            else
              (match accountAnswer request with
               | ValueSome answer -> answer
               | ValueNone -> ok "{}"))

        let env = TestEnv(signedInStore(), refusedApi :> IHttp)

        let! directory =
          probeUiAsync(fun () ->
            DirectoryPage.Page {
              Env = env
              SignedIn = signedInSession()
              OnOpen = ignore
              Title = "Guilds"
            }
            :> Control)

        let! window = mountAsync(directory)

        let! refusedShown =
          waitForAsync(
            10000,
            (fun () ->
              let line = controlByName(window, "GuildsRefused")

              let box = controlByName(window, "GuildSearch")

              (line |> Option.map effectiveVisible |> Option.defaultValue false)
              && (box
                  |> Option.map(fun found -> not(effectiveVisible found))
                  |> Option.defaultValue true)),
            id
          )

        Expect.isTrue refusedShown "the refusal never took the surface over"
      })

    testCaseAsync
      "the website layout's sidebar lands on the guild directory"
      (async {
        let! window, env =
          startShellWithBrowser(signedInStore(), guildsNavApi() :> IHttp)

        do! clickWhenShown(window, "Guilds")

        let! listed =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "Dev Guild") && hasText(root, "Ops Room")
          )

        Expect.isTrue listed "the directory page never rendered"

        // A row opens the guild's board inside the app.
        do!
          onUiAsync(fun () ->
            buttonWithText(window, "Dev Guild") |> Option.iter click)

        let! boarded =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              hasText(root, "Pinned to board")
              && hasText(root, "It was DNS.")
              && hasText(root, "Posting to #dev")
          )

        Expect.isTrue boarded "the board never landed"

        // The next numbered page joins the rows.
        do!
          onUiAsync(fun () ->
            controlByName(window, "BoardLoadMore")
            |> Option.iter(function
              | :? Button as button -> click button
              | _ -> ()))

        let! grown =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root -> hasText(root, "The Second")
          )

        Expect.isTrue grown "the next page never joined"
      })

    testCaseAsync
      "the board session loads and pages through the standard surfaces"
      (async {
        let env = TestEnv(signedInStore(), boardApi() :> IHttp)
        let! window, handle = mountBoard env

        let! loaded =
          waitForAsync(
            10000,
            (fun () -> window),
            fun root ->
              hasText(root, "The Octocat")
              && hasText(root, "Rust Guild")
              && hasText(root, "Pinned")
          )

        Expect.isTrue loaded "the board's first page never rendered"

        do!
          onUiAsync(fun () ->
            controlByName(window, "BoardLoadMore")
            |> Option.iter(function
              | :? Button as button -> click button
              | _ -> ()))

        let! grew =
          waitForAsync(
            8000,
            (fun () -> window),
            fun root ->
              hasText(root, "The Octocat") && hasText(root, "The Second")
          )

        Expect.isTrue grew "the next page never joined the rows"

        let! gone =
          probeUiAsync(fun () ->
            controlByName(window, "BoardLoadMore")
            |> Option.map effectiveVisible
            |> Option.defaultValue false)

        Expect.isFalse gone "the last page still offered more"

        handle.Dispose()
      })

    testCaseAsync
      "a flag-off board leaves the refusal line and no rows"
      (async {
        let env = TestEnv(signedInStore(), refusedBoardApi() :> IHttp)
        let! window, handle = mountBoard env

        let! refusedShown =
          waitForAsync(
            10000,
            (fun () ->
              controlByName(window, "BoardRefused")
              |> Option.map effectiveVisible
              |> Option.defaultValue false),
            id
          )

        Expect.isTrue refusedShown "the refusal never took the surface over"

        let! rows = probeUiAsync(fun () -> hasText(window, "The Octocat"))

        Expect.isFalse rows "a refused board rendered rows"

        handle.Dispose()
      })
  ]
