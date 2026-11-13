module DevNull.Client.Workbench.ThreadMode

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.Theme

type Model = {
  CommitId: string
  Loading: bool
  Error: AppError option
  Commit: Commit option
  Ancestors: CommitNode list
  ReplyParticipants: ReplyParticipant list
  Replies: CommitNode list
  RepliesCursor: string option
  RepliesLoading: bool
  RepliesError: AppError option
  Now: DateTimeOffset
}

type Msg =
  | Shown of Result<CommitPage, AppError>
  | RepliesFetched of Result<Page<CommitNode>, AppError>
  | MoreRepliesRequested
  | RepliesAppended of Result<Page<CommitNode>, AppError>
  | Posted of commit: Commit
  | ClockTicked of moment: DateTimeOffset
  | BackRequested
  | Opened of Commit
  | QuoteRequested of Commit

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  CommitId: string
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnBack: unit -> unit
  OnOpenThread: Commit -> unit
  OnOpenExternal: string -> unit
  OnQuote: Commit -> unit
}

[<Struct>]
type View = { Center: Control; Parents: Control }

let show(props: Props<'env>) : Async<Result<CommitPage, AppError>> =
  Commits.show(
    props.Env,
    {
      KeyFile = props.KeyFile
      CommitId = props.CommitId
    }
  )

let fetchReplies
  (props: Props<'env>, cursor: string option)
  : Async<Result<Page<CommitNode>, AppError>> =
  Commits.replies(
    props.Env,
    {
      KeyFile = props.KeyFile
      CommitId = props.CommitId
      Newest = true
      Cursor = cursor
    }
  )

let init(props: Props<'env>) : Model * Cmd<Msg> =
  {
    CommitId = props.CommitId
    Loading = true
    Error = None
    Commit = None
    Ancestors = []
    ReplyParticipants = []
    Replies = []
    RepliesCursor = None
    RepliesLoading = false
    RepliesError = None
    Now = DateTimeOffset.UtcNow
  },
  Cmd.OfAsync.perform show props Shown

let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
  match msg with
  | Shown result ->
    match result with
    | Ok page ->
      {
        model with
            Loading = false
            Error = None
            Commit = Some page.Commit
            Ancestors = page.Ancestors
            ReplyParticipants = page.ReplyParticipants
            RepliesLoading = true
            RepliesError = None
      },
      Cmd.OfAsync.perform fetchReplies (props, None) RepliesFetched
    | Error error ->
      {
        model with
            Loading = false
            Error = Some error
      },
      Cmd.none
  | RepliesFetched result ->
    match result with
    | Ok page ->
      {
        model with
            Replies = page.Items
            RepliesCursor = page.NextCursor
            RepliesLoading = false
            RepliesError = None
      },
      Cmd.none
    | Error error ->
      {
        model with
            RepliesLoading = false
            RepliesError = Some error
      },
      Cmd.none
  | MoreRepliesRequested ->
    match model.RepliesCursor with
    | Some _ ->
      {
        model with
            RepliesLoading = true
            RepliesError = None
      },
      Cmd.OfAsync.perform
        fetchReplies
        (props, model.RepliesCursor)
        RepliesAppended
    | None -> model, Cmd.none
  | RepliesAppended result ->
    match result with
    | Ok page ->
      {
        model with
            Replies = model.Replies @ page.Items
            RepliesCursor = page.NextCursor
            RepliesLoading = false
            RepliesError = None
      },
      Cmd.none
    | Error error ->
      {
        model with
            RepliesLoading = false
            RepliesError = Some error
      },
      Cmd.none
  | Posted reply ->
    let replies =
      model.Replies
      |> List.filter(fun node ->
        match node with
        | LiveCommit existing -> existing.Id <> reply.Id
        | Tombstone tombstone -> tombstone.Id <> reply.Id)
      |> fun items -> LiveCommit reply :: items

    let commit =
      model.Commit
      |> Option.map(fun current -> {
        current with
            RepliesCount =
              current.RepliesCount
              |> Option.map((+) 1)
              |> Option.orElse(Some 1)
      })

    {
      model with
          Commit = commit
          Replies = replies
          RepliesError = None
    },
    Cmd.none
  | ClockTicked moment -> ({ model with Now = moment }, Cmd.none)
  | BackRequested -> model, Cmd.ofAction(fun () -> props.OnBack())
  | Opened commit -> model, Cmd.ofAction(fun () -> props.OnOpenThread commit)
  | QuoteRequested commit -> model, Cmd.ofAction(fun () -> props.OnQuote commit)

let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
  Sub.fromObservable "clock" (props.Now |> Observable.map ClockTicked)

let participantsOf
  (commit: Commit, others: ReplyParticipant list)
  : ReplyPicker.ParticipantRow list =
  let author = commit.Author

  let authorRow: ReplyPicker.ParticipantRow = {
    Id = Author.id author
    Handle = Author.handle author
    IsAuthor = true
  }

  let rows =
    others
    |> List.map(fun participant ->
      {
        Id = participant.Id
        Handle = participant.Handle
        IsAuthor = false
      }
      : ReplyPicker.ParticipantRow)

  authorRow :: rows

let inline nodeKey(node: CommitNode) =
  match node with
  | LiveCommit commit -> commit.Id
  | Tombstone tombstone -> tombstone.Id

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : View =
  let now = model |> Observable.map _.Now |> Slices.share
  let pasteis = Observable.single props.Pasteis
  let polls = Observable.single(Map.empty: Map<string, string>)
  let translation = Observable.single(fun (_: Commit) -> ValueNone)

  let rowProps(onEvent: Event -> unit) : RowProps = {
    OnEvent = onEvent
    OnOpenExternal = props.OnOpenExternal
    ViewerId = props.ViewerId
    Pasteis = pasteis
    Now = now
    PollChoices = polls
    Dense = true
    Images = props.Images
    Flyout = ValueNone
    Pin = ValueNone
    Star = ValueNone
    Stars = ValueNone
    Vote = ValueNone
    Pastel = ValueNone
    Menu = ValueNone
    Translation = translation
    Zone = props.Zone
  }

  let cardProps = {
    rowProps(fun event ->
      match event with
      | CommitCard.QuoteRequested commit -> dispatch(QuoteRequested commit)
      | _ -> ()) with
        Dense = false
  }

  let chainProps =
    rowProps(fun event ->
      match event with
      | CommitCard.Opened commit -> dispatch(Opened commit)
      | CommitCard.QuoteRequested commit -> dispatch(QuoteRequested commit)
      | _ -> ())

  let chainOf(commit: Commit) : ChainCtx = {
    Props = chainProps
    Depth = 0
    ParentLane = Author.lane commit.Author
    ParentHandle = Author.handle commit.Author
    Grow = ValueNone
  }

  let cardKey(current: Model) =
    match current.Commit with
    | Some commit -> $"{commit.Id}|{commit.RepliesCount}"
    | None -> ""

  let parentsKey(current: Model) =
    match current.Commit with
    | Some commit ->
      commit.Id
      + "|"
      + (current.Ancestors |> List.map nodeKey |> String.concat ",")
    | None when current.Loading -> "loading"
    | None -> "error"

  let repliesKey(current: Model) =
    current.Replies |> List.map nodeKey |> String.concat ","

  let composerKey(current: Model) =
    match current.Commit with
    | Some commit -> commit.Id
    | None when current.Loading -> "loading"
    | None -> "error"

  let buildCard(current: Model) : Control =
    match current.Commit with
    | Some commit -> CommitCard.feedRow cardProps (LiveCommit commit)
    | None -> Panel() :> Control

  let buildParents(current: Model) : Control =
    let chain =
      match current.Commit with
      | Some commit when not current.Ancestors.IsEmpty ->
        chainRows(chainOf commit, current.Ancestors)
      | Some _ -> TextBlock().Classes(Theme.name Theme.Muted).Text("(root)")
      | None ->
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .Text(
            if current.Loading then
              "Loading the thread…"
            else
              "The thread could not be read."
          )

    StackPanel()
      .Spacing(6.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Mono).Text("PARENTS"),
        chain
      )

  let buildReplies(current: Model) : Control =
    match current.Commit with
    | Some commit -> chainRows(chainOf commit, current.Replies)
    | None -> Panel() :> Control

  let buildComposer(current: Model) : Control =
    match current.Commit with
    | Some commit ->
      Composer.Page {
        Env = props.Env
        Session = props.Session
        Target = Composer.ReplyTo commit.Id
        Participants =
          ValueSome(participantsOf(commit, current.ReplyParticipants))
        OnCommitted = fun reply -> dispatch(Posted reply)
        OnScheduled = ValueNone
        OnOpenQueue = ValueNone
        Draft = ValueNone
        OnBodyReady = ValueNone
      }
      :> Control
    | None ->
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Text(
          if current.Loading then
            "Loading the thread…"
          else
            "The thread could not be read."
        )

  let back =
    Button()
      .Name("ThreadBack")
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalAlignment(HorizontalAlignment.Left)
      .Content(
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .Text(
            model
            |> Observable.map(fun current ->
              current.Commit
              |> Option.map(fun commit ->
                "Esc back · THREAD " + commit.ShortHash)
              |> Option.defaultValue "Esc back · THREAD")
            |> Observable.distinctUntilChanged
          )
      )
      .OnClickHandler(fun _ _ -> dispatch BackRequested)

  let cardHost =
    ContentControl()
      .Name("ThreadCard")
      .Content(PageChrome.rebuildOn(model, cardKey, buildCard) |> _.ToBinding())

  let repliesHost =
    ContentControl()
      .Name("ThreadReplies")
      .Content(
        PageChrome.rebuildOn(model, repliesKey, buildReplies) |> _.ToBinding()
      )

  let repliesBlock =
    StackPanel()
      .Spacing(6.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Mono).Text("REPLIES"),
        repliesHost,
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .Text("No replies yet.")
          .IsVisible(
            model
            |> Observable.map(fun current ->
              current.Commit.IsSome
              && current.Replies.IsEmpty
              && not current.RepliesLoading)
          ),
        PageChrome.collapsingMessagePanel(
          model
          |> Observable.map(fun current ->
            if current.RepliesLoading then "Loading replies…" else ""),
          Theme.Muted
        ),
        PageChrome.errorPanel(model |> Observable.map _.RepliesError),
        PageChrome.loadMore(
          model
          |> Observable.map(fun current ->
            current.RepliesCursor.IsSome && not current.RepliesLoading),
          fun () -> dispatch MoreRepliesRequested
        )
      )

  let composerHost =
    ContentControl()
      .Name("ThreadComposer")
      .Content(
        PageChrome.rebuildOn(model, composerKey, buildComposer) |> _.ToBinding()
      )

  let stack =
    StackPanel()
      .Spacing(12.0)
      .Margin(Thickness(16.0))
      .Children(
        back,
        PageChrome.collapsingMessagePanel(
          model
          |> Observable.map(fun current ->
            if current.Loading then "Reading the thread…" else ""),
          Theme.Muted
        ),
        PageChrome.errorPanel(model |> Observable.map _.Error),
        cardHost,
        repliesBlock,
        composerHost
      )

  let scroller =
    ScrollViewer()
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
      .Content(stack)

  let center =
    Border()
      .Name("ThreadCenter")
      .Focusable(true)
      .OnKeyDownHandler(fun _ args ->
        if args.Key = Key.Escape then
          args.Handled <- true
          dispatch BackRequested)
      .Child(scroller)

  center.AttachedToVisualTree.Add(fun _ -> center.Focus() |> ignore)

  let parents =
    ScrollViewer()
      .Name("ThreadParents")
      .MaxHeight(260.0)
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
      .Content(
        ContentControl()
          .Content(
            PageChrome.rebuildOn(model, parentsKey, buildParents)
            |> _.ToBinding()
          )
      )

  { Center = center; Parents = parents }

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> init props)
    (fun msg model -> update(props, msg, model))
    (view props)
  |> Program.withSubscription(subscribe props)
