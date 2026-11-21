module DevNull.Client.Editor.ListTab

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Editor
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.PageChrome
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore
open DevNull.Client.UiClock

type Kind =
  | Saved
  | Queue

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideDispatcher
  and 'env :> IProvideRemotes
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideBrowser> = {
  Env: 'env
  Session: SignedInSession
  Kind: Kind
  Zone: Zone
  Now: unit -> DateTimeOffset
  OnOpenThread: Commit -> unit
  OnReply: Commit -> unit
  OnQuote: Commit -> unit
  OnOpenRepo: string -> unit
  OnOpenGuild: string -> unit
  OnShared: User -> unit
}

let title(kind: Kind) =
  match kind with
  | Saved -> "SAVED"
  | Queue -> "QUEUE"

// One feed row with the chunk request cursor that fetched it, so
// the anchor the feed reports names the page the row came from.
[<Struct>]
type AnchoredNode = {
  Node: CommitNode
  Entry: string option
}

let entryOf(anchored: AnchoredNode) : string option = anchored.Entry

module State =

  type Model = {
    Loading: bool
    Error: AppError option
    SavedFeed: FeedWindow.Window<CommitNode>
    SavedAnchor: FeedWindow.Anchor voption
    Queue: ScheduledCommit list
    QueuePage: int
    QueueLast: int
    Cards: EditorCards.State
    Now: DateTimeOffset
  }

  type Msg =
    | SavedFetched of Result<Page<CommitNode>, AppError>
    | SavedAppended of
      cursor: string option *
      Result<Page<CommitNode>, AppError>
    | SavedAnchorMoved of FeedWindow.Anchor
    | QueueFetched of Result<ScheduledPage, AppError>
    | QueueAppended of Result<ScheduledPage, AppError>
    | MoreSaved
    | MoreQueue
    | CardMsg of EditorCards.Msg

  let initial(now: DateTimeOffset) = {
    Loading = true
    Error = None
    SavedFeed = FeedWindow.empty(FeedWindow.NewestFirst, 300)
    SavedAnchor = ValueNone
    Queue = []
    QueuePage = 1
    QueueLast = 1
    Cards = EditorCards.empty
    Now = now
  }

  let savedRequest
    (props: Props<'env>, cursor: string option)
    : Stars.ListRequest = {
    KeyFile = props.Session.KeyFile
    Cursor = cursor
  }

  let queueRequest
    (props: Props<'env>, page: int)
    : ScheduledCommits.ListRequest = {
    KeyFile = props.Session.KeyFile
    Page = page
  }

  let savedOps: FeedWindow.Ops<CommitNode> = {
    KeyOf =
      fun node ->
        match node with
        | LiveCommit commit -> commit.Id
        | Tombstone tombstone -> tombstone.Id
  }

  let savedItems(model: Model) = FeedWindow.items model.SavedFeed

  let savedOlder(model: Model) = FeedWindow.older model.SavedFeed

  let trimSaved(model: Model, feed: FeedWindow.Window<CommitNode>) =
    FeedWindow.trim(
      {
        Ops = savedOps
        Keep = model.SavedAnchor |> ValueOption.map _.Key
        Edge = FeedWindow.OlderEdge
      },
      feed
    )

  let replaceSaved(feed: FeedWindow.Window<CommitNode>, updated: Commit) =
    FeedWindow.replace(savedOps, LiveCommit updated, feed)

  let tombstoneOf(commit: Commit) : Tombstone = {
    Id = commit.Id
    ParentId = commit.ParentId
    DeletedAt = DateTimeOffset.UtcNow
    RepliesCount = commit.RepliesCount
  }

  let cardsProps(props: Props<'env>) : EditorCards.Props<'env> = {
    Env = props.Env
    Session = props.Session
    Zone = props.Zone
    Surface = EditorCards.Saved
    Now = props.Now
    OnOpenRepo = props.OnOpenRepo
    OnOpenGuild = props.OnOpenGuild
    OnShared = ignore
  }

  let init(props: Props<'env>) =
    let model = {
      initial(props.Now()) with
          Cards = EditorCards.initial(cardsProps props)
    }

    match props.Kind with
    | Saved ->
      model,
      Cmd.OfAsync.perform
        Stars.index
        (props.Env, savedRequest(props, None))
        SavedFetched
    | Queue ->
      model,
      Cmd.OfAsync.perform
        ScheduledCommits.list
        (props.Env, queueRequest(props, 1))
        QueueFetched

  let update(props: Props<'env>, msg: Msg, model: Model) =
    match msg with
    | SavedFetched result ->
      (match result with
       | Ok page ->
         let feed =
           FeedWindow.addOlder(
             savedOps,
             {
               Request = ValueNone
               Items = Array.ofList page.Items
               Older = ValueOption.ofOption page.NextCursor
               Newer = ValueNone
             },
             FeedWindow.empty(FeedWindow.NewestFirst, 300)
           )
           |> fun feed -> trimSaved(model, feed)

         {
           model with
               Loading = false
               Error = None
               SavedFeed = feed
         },
         Cmd.none
       | Error error ->
         {
           model with
               Loading = false
               Error = Some error
         },
         Cmd.none)
    | SavedAppended(cursor, result) ->
      (match result with
       | Ok page ->
         let feed =
           FeedWindow.addOlder(
             savedOps,
             {
               Request = ValueOption.ofOption cursor
               Items = Array.ofList page.Items
               Older = ValueOption.ofOption page.NextCursor
               Newer = ValueNone
             },
             model.SavedFeed
           )
           |> fun feed -> trimSaved(model, feed)

         {
           model with
               Loading = false
               Error = None
               SavedFeed = feed
         },
         Cmd.none
       | Error error ->
         {
           model with
               Loading = false
               Error = Some error
         },
         Cmd.none)
    | QueueFetched result ->
      (match result with
       | Ok page ->
         {
           model with
               Loading = false
               Error = None
               Queue = page.Items
               QueuePage = page.CurrentPage
               QueueLast = page.LastPage
         },
         Cmd.none
       | Error error ->
         {
           model with
               Loading = false
               Error = Some error
         },
         Cmd.none)
    | QueueAppended result ->
      (match result with
       | Ok page ->
         {
           model with
               Loading = false
               Error = None
               Queue = model.Queue @ page.Items
               QueuePage = page.CurrentPage
               QueueLast = page.LastPage
         },
         Cmd.none
       | Error error ->
         {
           model with
               Loading = false
               Error = Some error
         },
         Cmd.none)
    | MoreSaved ->
      match savedOlder model with
      | ValueSome cursor ->
        { model with Loading = true },
        Cmd.OfAsync.perform
          Stars.index
          (props.Env, savedRequest(props, Some cursor))
          (fun result -> SavedAppended(Some cursor, result))
      | ValueNone -> model, Cmd.none
    | SavedAnchorMoved anchor ->
      {
        model with
            SavedAnchor = ValueSome anchor
      },
      Cmd.none
    | MoreQueue ->
      if model.QueuePage < model.QueueLast then
        { model with Loading = true },
        Cmd.OfAsync.perform
          ScheduledCommits.list
          (props.Env, queueRequest(props, model.QueuePage + 1))
          QueueAppended
      else
        model, Cmd.none
    | CardMsg msg ->
      let cards, cmd, effects =
        EditorCards.update(cardsProps props, msg, model.Cards)

      let next =
        effects
        |> List.fold
          (fun current effect ->
            match effect with
            | EditorCards.CommitChanged updated -> {
                current with
                    SavedFeed = replaceSaved(current.SavedFeed, updated)
              }
            | EditorCards.CommitDeleted commitId ->
              savedItems current
              |> List.tryPick(function
                | LiveCommit commit when commit.Id = commitId ->
                  Some(Tombstone(tombstoneOf commit))
                | _ -> None)
              |> Option.map(fun item -> {
                current with
                    SavedFeed =
                      FeedWindow.replaceKey(
                        {
                          Ops = savedOps
                          Key = commitId
                          Item = item
                        },
                        current.SavedFeed
                      )
              })
              |> Option.defaultValue current
            | EditorCards.PinChanged _ -> current
            | EditorCards.Failed error -> { current with Error = Some error })
          { model with Cards = cards }

      next, EditorCards.mapCmd(CardMsg, cmd)

  let status(now: DateTimeOffset, entry: ScheduledCommit) =
    match entry.Status with
    | StillScheduled ->
      if entry.ScheduledAt <= now then
        "Waiting for submission"
      else
        "Scheduled"
    | SubmittedAs CommitStatus.Approved -> "Approved"
    | SubmittedAs CommitStatus.Pending -> "Submitted for review"
    | SubmittedAs CommitStatus.Rejected -> "Rejected"
    | SubmittedAs(CommitStatus.UnknownStatus _) -> "Submitted"
    | CommitDeleted -> "Deleted"

  let when_(zone: Zone, entry: ScheduledCommit) =
    let local = TimeZoneInfo.ConvertTime(entry.ScheduledAt, zone.Info)
    local.ToString("MMM d, yyyy, h:mm tt")

let queueRow
  (props: Props<'env>, now: DateTimeOffset, entry: ScheduledCommit)
  : Control =
  let heading =
    match entry.Body with
    | Some body -> body
    | None -> "Queued commit"

  let status = State.status(now, entry)
  let when_ = State.when_(props.Zone, entry)

  Border()
    .Name($"ScheduledRow-{entry.Id}")
    .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
    .BorderThickness(Thickness(1.0))
    .CornerRadius(8.0)
    .Padding(10.0, 8.0)
    .Child(
      StackPanel()
        .Spacing(4.0)
        .Children(
          TextBlock().Text(heading).TextWrapping(TextWrapping.Wrap),
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(
              TextBlock()
                .Name("ScheduledStatus")
                .Text(status)
                .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted),
              TextBlock()
                .Text(when_)
                .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
            )
        )
    )

let savedFeed
  (
    props: Props<'env>,
    model: IObservable<State.Model>,
    dispatch: Dispatch<State.Msg>,
    rowProps: CommitCard.RowProps
  ) : Control =
  let headerBody =
    rebuildOn(
      model,
      (fun current ->
        current.Loading, (State.savedItems current).IsEmpty, current.Error),
      fun current ->
        let empty: Control =
          if current.Loading && (State.savedItems current).IsEmpty then
            TextBlock()
              .Text("Loading saved commits…")
              .Classes(Theme.name Theme.Muted)
          elif (State.savedItems current).IsEmpty then
            TextBlock()
              .Text("No saved commits.")
              .Classes(Theme.name Theme.Muted)
          else
            Panel()

        StackPanel()
          .Spacing(6.0)
          .Children(
            TextBlock()
              .Text("STARS.INDEX")
              .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted),
            empty,
            errorPanel(Observable.single current.Error)
          )
    )

  let hasOlder =
    model
    |> Observable.map(fun current ->
      State.savedOlder current |> ValueOption.isSome)
    |> Slices.share

  let items =
    model
    |> Observable.map(fun current -> current.SavedFeed)
    |> Slices.gateByRef
    |> Observable.map(fun feed ->
      FeedWindow.items feed
      |> List.map(fun node -> {
        Node = node
        Entry =
          FeedWindow.entryOf(State.savedOps, State.savedOps.KeyOf node, feed)
          |> ValueOption.toOption
      }))
    |> Slices.share

  let feed =
    Feed.viewWith(
      {
        Header =
          ValueSome(ContentControl().Content(headerBody |> _.ToBinding()))
        Items = items
        KeyOf = fun anchored -> State.savedOps.KeyOf anchored.Node
        Row = fun anchored -> feedRow rowProps anchored.Node
        Footer = ValueNone
        HasMore = hasOlder
        ScrollTo = ValueNone
        Ride = false
        Restore = ValueNone
        OnScroll = ValueNone
        OnRide = ValueNone
        OnNeedMore = ValueNone
      },
      {
        Feed.Advanced.none() with
            AnchorKeyOf =
              ValueSome(fun anchored ->
                Some(State.savedOps.KeyOf anchored.Node))
            EntryCursorOf = ValueSome entryOf
            RestoreAnchor = ValueNone
            OnAnchor =
              ValueSome(fun anchor -> dispatch(State.SavedAnchorMoved anchor))
            Paging =
              ValueSome {
                Order = FeedWindow.NewestFirst
                HasOlder = hasOlder
                HasNewer = Observable.single false
                OnOlder = (fun () -> dispatch State.MoreSaved)
                OnNewer = ignore
              }
      }
    )

  feed.Name($"EditorListTab-{title props.Kind}")

let queueFeed
  (
    props: Props<'env>,
    model: IObservable<State.Model>,
    dispatch: Dispatch<State.Msg>
  ) : Control =
  let headerBody =
    rebuildOn(
      model,
      (fun current -> current.Loading, current.Queue.IsEmpty, current.Error),
      fun current ->
        let empty: Control =
          if current.Loading && current.Queue.IsEmpty then
            TextBlock()
              .Text("Loading the queue…")
              .Classes(Theme.name Theme.Muted)
          elif current.Queue.IsEmpty then
            TextBlock()
              .Text("No scheduled commits.")
              .Classes(Theme.name Theme.Muted)
          else
            Panel()

        StackPanel()
          .Spacing(6.0)
          .Children(
            TextBlock()
              .Text("SCHEDULED")
              .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted),
            empty,
            errorPanel(Observable.single current.Error)
          )
    )

  let more =
    loadMore(
      model
      |> Observable.map(fun current -> current.QueuePage < current.QueueLast),
      fun () -> dispatch State.MoreQueue
    )
      .Name("ListTabMoreQueue")

  (Feed.view {
    Header = ValueSome(ContentControl().Content(headerBody |> _.ToBinding()))
    Items = model |> Observable.map _.Queue
    KeyOf = fun entry -> entry.Id
    Row = fun entry -> queueRow(props, props.Now(), entry)
    Footer = ValueSome more
    HasMore =
      model
      |> Observable.map(fun current -> current.QueuePage < current.QueueLast)
    ScrollTo = ValueNone
    Ride = false
    Restore = ValueNone
    OnScroll = ValueNone
    OnRide = ValueNone
    OnNeedMore = ValueSome(fun () -> dispatch State.MoreQueue)
  })
    .Name($"EditorListTab-{title props.Kind}")

let view
  (
    props: Props<'env>,
    model: IObservable<State.Model>,
    dispatch: Dispatch<State.Msg>
  ) : Control =
  let cardDispatch msg = dispatch(State.CardMsg msg)

  let fallback =
    function
    | Opened commit -> props.OnOpenThread commit
    | ReplyToggled commit -> props.OnReply commit
    | QuoteRequested commit -> props.OnQuote commit
    | MoreRepliesRequested commit -> props.OnOpenThread commit
    | _ -> ()

  let cards = model |> Observable.map _.Cards |> Slices.share

  let rowProps =
    EditorCards.row {
      Props = State.cardsProps props
      State = cards
      Dispatch = cardDispatch
      OnEvent = EditorCards.event(cardDispatch, fallback)
    }
    |> fun row -> { row with Dense = true }

  let feed =
    match props.Kind with
    | Saved -> savedFeed(props, model, dispatch, rowProps)
    | Queue -> queueFeed(props, model, dispatch)

  Grid()
    .Children(
      feed,
      EditorCards.layer(State.cardsProps props, cards, cardDispatch)
    )

let program
  (props: Props<'env>)
  : Program<unit, State.Model, State.Msg, Control> =
  Program.mkProgram
    (fun () -> State.init props)
    (fun msg model -> State.update(props, msg, model))
    (fun model dispatch -> view(props, model, dispatch))
  |> Program.withSubscription(fun _ ->
    Sub.fromObservable
      "card-clock"
      (props.Env.UiClock.Minute
       |> Observable.map(fun tick ->
         State.CardMsg(EditorCards.ClockTicked tick))))

let mount(dispatcher: IDispatcher, props: Props<'env>) =
  DevNull.Client.Programs.runPage(dispatcher, program props)
