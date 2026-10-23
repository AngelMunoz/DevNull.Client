module DevNull.Client.Views.LogView

open System
open System.Collections.Generic
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open IcedTasks
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open Navs
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.Navigation
open DevNull.Client.Profiles
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.Timeline
open DevNull.Client.UiClock

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLastVisit
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideSshKeys> = {
  Env: 'env
  Log: LogKind
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

/// One row of the log feed: a day separator, a commit on the rail, a
/// deletion marker, or the last-visit separator.
type LogRow =
  | Day of string
  | Node of Commit
  | TombstoneRow of Tombstone
  | LastVisitRow

/// The visit the marker reads: when it was and who is reading.
[<Struct>]
type VisitMarker = {
  VisitedAt: DateTimeOffset
  ViewerId: string
}

/// The page's state logic: model, messages, update, and the
/// subscriptions that feed it from outside.
module State =

  /// One thread preview the page grew past the payload slice: the
  /// replies shown and the cursor the next page asks for.
  [<Struct>]
  type PreviewState = {
    Shown: CommitNode list
    Cursor: string option
  }

  type Model = {
    Loading: bool
    Error: AppError option
    Items: CommitNode list
    NextCursor: string option
    Head: string option
    NewCount: int option
    Ranked: bool
    Attached: bool
    Now: DateTimeOffset
    Pasteis: bool
    ReplyingTo: string option
    /// The reply drafts the page holds for commits whose row recycled,
    /// by commit id. An empty text never lands here.
    ReplyDrafts: Map<string, string>
    /// The visit captured when the page mounted; the rendered marker
    /// keeps pointing there for the session even as the store advances.
    Marker: DateTimeOffset option
    /// The thread previews the page grew past the payload slice, by
    /// parent id.
    Previews: Map<string, PreviewState>
  }

  type Msg =
    | Fetched of Result<LogPage, AppError>
    | ReloadRequested
    | Appended of Result<LogPage, AppError>
    | MoreRequested
    | StarToggled of Commit
    | StarDone of Result<Commit, AppError>
    | PastelGiven of Commit
    | PastelDone of Result<PastelResult, AppError>
    | CheckNew
    | NewChecked of Result<LogPage, AppError>
    | NewRequested
    | NewFetched of Result<LogPage, AppError>
    | AttachedChanged of attached: bool
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState
    | ReplyToggled of Commit
    | ReplyPosted of commitId: string
    | ReplyDraftChanged of commitId: string * text: string
    | PollVoteGiven of Commit * PollOption
    | PollVotedDone of Result<Commit, AppError>
    | PreviewMoreRequested of Commit
    | PreviewLoaded of commitId: string * Result<Page<CommitNode>, AppError>

  let initial = {
    Loading = true
    Error = None
    Items = []
    NextCursor = None
    Head = None
    NewCount = None
    Ranked = false
    Attached = false
    Now = DateTimeOffset.UtcNow
    Pasteis = false
    ReplyingTo = None
    ReplyDrafts = Map.empty
    Marker = None
    Previews = Map.empty
  }

  /// The id of the newest commit in a page: the head the since-poll
  /// checks against.
  let headOf(items: CommitNode list) : string option =
    items
    |> List.tryPick(function
      | LiveCommit commit -> Some commit.Id
      | Tombstone _ -> None)

  let fetch(props: Props<'env>, cursor: string option) =
    Logs.get(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        Log = props.Log
        Since = None
        Cursor = cursor
      }
    )

  let checkNew(props: Props<'env>, head: string) =
    Logs.get(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        Log = props.Log
        Since = Some head
        Cursor = None
      }
    )

  let init (props: Props<'env>) () =
    // The visit is read once per mount, the way the site holds the
    // marker's position for the whole session.
    let marker =
      props.Env.LastVisit.LastVisit(
        props.Session.Session.User.Id,
        LogKinds.wire props.Log
      )

    {
      initial with
          Pasteis = props.Session.Session.PasteisMode
          Marker = marker
    },
    Cmd.OfAsync.perform fetch (props, None) Fetched

  /// Records this visit's newest commit as the next marker, once the
  /// feed has items to take it from.
  let private markVisit(props: Props<'env>, items: CommitNode list) : Cmd<Msg> =
    match items with
    | [] -> Cmd.none
    | first :: _ ->
      let newest =
        match first with
        | LiveCommit commit -> commit.CreatedAt
        | Tombstone _ -> DateTimeOffset.UtcNow

      Cmd.ofEffect(fun _ ->
        props.Env.LastVisit.Mark(
          props.Session.Session.User.Id,
          LogKinds.wire props.Log,
          newest
        ))

  let replaceNode(items: CommitNode list, updated: Commit) : CommitNode list =
    items
    |> List.map(function
      | LiveCommit commit when commit.Id = updated.Id -> LiveCommit updated
      | node -> node)

  let update (props: Props<'env>) msg model =
    match msg with
    | Fetched result ->
      match result with
      | Ok page ->
        {
          model with
              Loading = false
              Error = None
              Items = page.Page.Items
              NextCursor = page.Page.NextCursor
              Head = headOf page.Page.Items
              NewCount = None
              Ranked = page.IsRanked
        },
        markVisit(props, page.Page.Items)
      | Error error ->
        {
          model with
              Loading = false
              Error = Some error
        },
        Cmd.none
    | CheckNew ->
      // A ranked log is score ordered, so "newer than head" means
      // nothing; the site skips the poll there too.
      match model.Head, model.Ranked with
      | Some head, false ->
        model, Cmd.OfAsync.perform checkNew (props, head) NewChecked
      | _ -> model, Cmd.none
    | NewChecked result ->
      match result with
      | Ok page ->
        // The since read answers the same first page; only its count is
        // news. The commits themselves come with the pill's refresh.
        {
          model with
              NewCount =
                page.NewCommitsCount |> Option.filter(fun count -> count > 0)
        },
        Cmd.none
      | Error _ -> model, Cmd.none
    | NewRequested ->
      { model with NewCount = None },
      Cmd.OfAsync.perform fetch (props, None) NewFetched
    | NewFetched result ->
      match result with
      | Ok page ->
        // The pill's refresh replaces the feed with the fresh first page,
        // the way the site reloads its log; no duplicate can survive.
        {
          model with
              Items = page.Page.Items
              NextCursor = page.Page.NextCursor
              Head = headOf page.Page.Items
              Ranked = page.IsRanked
        },
        Cmd.none
      | Error error -> { model with Error = Some error }, Cmd.none
    | ReloadRequested ->
      {
        model with
            Loading = true
            Error = None
      },
      Cmd.OfAsync.perform fetch (props, None) Fetched
    | ReplyToggled commit ->
      let replyingTo =
        if model.ReplyingTo = Some commit.Id then
          None
        else
          Some commit.Id

      { model with ReplyingTo = replyingTo }, Cmd.none
    | ReplyPosted commitId ->
      // A landed reply closes its composer and refreshes the feed, so
      // the new reply arrives through the same read as everything
      // else. Its draft is spent.
      {
        model with
            ReplyingTo = None
            ReplyDrafts = model.ReplyDrafts |> Map.remove commitId
      },
      Cmd.ofEffect(fun dispatch' -> dispatch' ReloadRequested)
    | ReplyDraftChanged(commitId, text) ->
      // A recycled row rebuilds its composer from this map, so a draft
      // outlives the row; an emptied text leaves no entry behind.
      let drafts =
        if text = "" then
          model.ReplyDrafts |> Map.remove commitId
        else
          model.ReplyDrafts |> Map.add commitId text

      { model with ReplyDrafts = drafts }, Cmd.none
    | PollVoteGiven(commit, option) ->
      let vote() =
        Commits.vote(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            CommitId = commit.Id
            OptionId = option.Id
          }
        )

      { model with Error = None }, Cmd.OfAsync.perform vote () PollVotedDone
    | PollVotedDone result ->
      match result with
      | Ok updated ->
        {
          model with
              Items = replaceNode(model.Items, updated)
        },
        Cmd.none
      | Error error -> { model with Error = Some error }, Cmd.none
    | PreviewMoreRequested commit ->
      // The first ask reads the newest page whole, the way the site's
      // preview does; the next ones walk the cursor it left.
      let cursor =
        model.Previews |> Map.tryFind commit.Id |> Option.bind _.Cursor

      model,
      Cmd.OfAsync.perform
        Commits.replies
        (props.Env,
         {
           KeyFile = props.Session.KeyFile
           CommitId = commit.Id
           Newest = true
           Cursor = cursor
         })
        (fun page -> PreviewLoaded(commit.Id, page))
    | PreviewLoaded(commitId, result) ->
      match result with
      | Ok page ->
        let known =
          model.Previews
          |> Map.tryFind commitId
          |> Option.map _.Shown
          |> Option.defaultValue []

        let seen = known |> List.map FeedParts.nodeKey |> Set.ofList

        let fresh =
          page.Items
          |> List.filter(fun node ->
            seen |> Set.contains(FeedParts.nodeKey node) |> not)

        let grown = {
          Shown = known @ fresh
          Cursor = page.NextCursor
        }

        {
          model with
              Previews = model.Previews |> Map.add commitId grown
        },
        Cmd.none
      | Error error -> { model with Error = Some error }, Cmd.none
    | MoreRequested ->
      model, Cmd.OfAsync.perform fetch (props, model.NextCursor) Appended
    | Appended result ->
      match result with
      | Ok page ->
        {
          model with
              Items = model.Items @ page.Page.Items
              NextCursor = page.Page.NextCursor
        },
        Cmd.none
      | Error error -> { model with Error = Some error }, Cmd.none
    | StarToggled commit ->
      { model with Error = None },
      Cmd.OfAsync.perform
        CommitActions.toggleStar
        (props.Env, props.Session, commit)
        StarDone
    | StarDone result ->
      match result with
      | Ok updated ->
        {
          model with
              Items = replaceNode(model.Items, updated)
        },
        Cmd.none
      | Error error -> { model with Error = Some error }, Cmd.none
    | PastelGiven commit ->
      let give() =
        Pasteis.give(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            CommitId = commit.Id
          }
        )

      { model with Error = None }, Cmd.OfAsync.perform give () PastelDone
    | PastelDone result ->
      match result with
      | Ok pastel ->
        {
          model with
              Items = replaceNode(model.Items, pastel.Commit)
        },
        Cmd.none
      | Error error -> { model with Error = Some error }, Cmd.none
    | AttachedChanged attached -> { model with Attached = attached }, Cmd.none
    | ClockTicked moment -> { model with Now = moment }, Cmd.none
    | SessionChanged state ->
      let pasteis =
        match state with
        | SignedIn entry -> entry.Session.PasteisMode
        | SignedOut -> model.Pasteis

      { model with Pasteis = pasteis }, Cmd.none

  /// The page's inlets from outside: the shared clock, the session
  /// store, and — while the page sits in the tree — the since-poll and
  /// the shared chrome's requests. The keyed engine stops and starts
  /// sources as the model moves.
  let subscribe (props: Props<'env>) (model: Model) : Sub<Msg> =
    let chromeRequests =
      props.Env.Timeline.Requests
      |> Observable.choose(function
        | Timeline.Reload
        | Timeline.CommitLanded -> Some ReloadRequested
        | Timeline.ScrollToTop -> None)

    [
      Sub.fromObservable
        "clock"
        (props.Env.UiClock.Minute |> Observable.map ClockTicked)

      Sub.fromObservable
        "session"
        (props.Env.Session.Observe |> Observable.map SessionChanged)

      if model.Attached then
        Sub.interval "since-poll" (TimeSpan.FromMinutes 1.0) (fun () ->
          CheckNew)

      if model.Attached then
        Sub.fromObservable "chrome" chromeRequests
    ]
    |> Sub.batch

  /// The draft a recycled row's composer seeds from. The program's
  /// model stream is the live subject, so the row build — which runs
  /// on realize, between emissions — reads the page's current state.
  let draftOf(model: IObservable<Model>, commitId: string) : string =
    match box model with
    | :? BehaviorSubject<Model> as subject ->
      subject.Value.ReplyDrafts
      |> Map.tryFind commitId
      |> Option.defaultValue ""
    | _ -> ""

  /// Interleaves day separators between the commits of different days,
  /// carrying the day across tombstones. A ranked log is score ordered,
  /// its days interleave, so it rides without separators the way the
  /// site's ranked feed does. The last-visit marker rides above the
  /// first already-seen commit, and only when someone else's newer
  /// commit sits above it — the site's own rules.
  let toRows
    (items: CommitNode list, ranked: bool, marker: VisitMarker option)
    : LogRow list =
    let now = DateTimeOffset.UtcNow

    let nodeRow node =
      match node with
      | LiveCommit commit -> LogRow.Node commit
      | Tombstone tombstone -> LogRow.TombstoneRow tombstone

    let baseRows =
      if ranked then
        [ for node in items -> nodeRow node ]
      else
        let dayOf node =
          match node with
          | LiveCommit commit -> Some(dayLabel(now, commit.CreatedAt))
          | Tombstone _ -> None

        // The day seen before each item, carried across tombstones: one
        // carried state per item, seeded with the empty one.
        let carry =
          items
          |> List.scan (fun day node -> dayOf node |> Option.orElse day) None
          |> List.truncate items.Length

        [
          for prevDay, node in List.zip carry items do
            match node with
            | LiveCommit commit ->
              let day = dayLabel(now, commit.CreatedAt)

              if prevDay <> Some day then
                LogRow.Day day
                LogRow.Node commit
              else
                LogRow.Node commit
            | Tombstone tombstone -> LogRow.TombstoneRow tombstone
        ]

    let markerTarget =
      match ranked, marker with
      | false, Some visit ->
        items
        |> List.tryFindIndex(function
          | LiveCommit commit -> commit.CreatedAt <= visit.VisitedAt
          | Tombstone _ -> false)
        |> Option.bind(fun seenAt ->
          let newerFromOthers =
            items
            |> List.truncate seenAt
            |> List.exists(function
              | LiveCommit commit -> commit.Author.Id <> visit.ViewerId
              | Tombstone _ -> false)

          match newerFromOthers with
          | true ->
            (match List.item seenAt items with
             | LiveCommit commit -> Some commit.Id
             | Tombstone _ -> None)
          | false -> None)
      | _ -> None

    match markerTarget with
    | None -> baseRows
    | Some seenId ->
      baseRows
      |> List.collect(fun row ->
        match row with
        | LogRow.Node commit when commit.Id = seenId -> [
            LogRow.LastVisitRow
            row
          ]
        | _ -> [ row ])

  let keyOf(logRow: LogRow) : string =
    match logRow with
    | LogRow.Day label -> "day:" + label
    | LogRow.Node commit -> commit.Id
    | LogRow.TombstoneRow tombstone -> tombstone.Id
    | LogRow.LastVisitRow -> "last-visit"

/// The page's semantic regions; the view only composes these.
module Parts =

  /// The hover card source every log card shares.
  let flyoutOf(props: Props<'env>) : UserFlyout.Source voption =
    ValueSome {
      UserFlyout.Fetch = props.Env.Profiles.Fetch
      OnOpen =
        fun username -> Navigation.go(props.Nav, "/profile/" + username, ignore)
      Cache = props.Env.ImageCache
    }

  /// The row context every log card shares: navigation for opens and
  /// quotes, messages for the star and the pastel, the live flags.
  let commitCtx
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : CommitCard.RowProps = {
    OnEvent =
      function
      | CommitCard.Opened c ->
        Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
      | CommitCard.ThreadRequested c ->
        Navigation.go(props.Nav, "/commit/" + c.Id, ignore)
      | CommitCard.MoreRepliesRequested c ->
        dispatch(State.PreviewMoreRequested c)
      | CommitCard.QuoteRequested c ->
        Navigation.go(props.Nav, "/quote/" + c.Id, ignore)
      | CommitCard.ReplyToggled c -> dispatch(State.ReplyToggled c)
      | CommitCard.StarToggled c -> dispatch(State.StarToggled c)
      | CommitCard.PastelGiven c -> dispatch(State.PastelGiven c)
      | CommitCard.PollVoted(commit, option) ->
        dispatch(State.PollVoteGiven(commit, option))
    Pasteis = model |> Observable.map _.Pasteis
    ViewerId = props.Session.Session.User.Id
    Now = model |> Observable.map _.Now
    Dense = false
    Images = props.Env.ImageCache
    Flyout = flyoutOf props
  }

  /// One feed row: a separator, a commit card with its revealed reply
  /// composer, or a deletion marker.
  let row
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    )
    (logRow: LogRow)
    : Control =
    let ctx = commitCtx(props, dispatch, model)

    match logRow with
    | LogRow.Day label -> FeedParts.dayRow label
    | LogRow.LastVisitRow -> FeedParts.lastVisitRow()
    | LogRow.Node commit ->
      // The inline reply composer seeds from the page's draft map, so
      // a recycled row rebuilds with the typed text. The program dies
      // with its control on the first detach; the draft does not.
      let composer =
        Composer.Page {
          Env = props.Env
          Session = props.Session
          Target = Composer.ReplyTo commit.Id
          OnCommitted = fun _ -> dispatch(State.ReplyPosted commit.Id)
          Register = ValueNone
          Draft =
            ValueSome {
              Seed = State.draftOf(model, commit.Id)
              Changed =
                fun text -> dispatch(State.ReplyDraftChanged(commit.Id, text))
            }
        }

      let reply =
        composer.IsVisible(
          model
          |> Observable.map(fun current -> current.ReplyingTo = Some commit.Id)
        )

      // The thread preview rides under the card: the payload slice,
      // or the list the page grew. It rebuilds only when the shown
      // list itself changed.
      let preview =
        match commit.ReplyPreview with
        | None -> Panel() :> Control
        | Some _ ->
          ContentControl()
            .Content(
              model
              |> Observable.map(fun current ->
                current.Previews |> Map.tryFind commit.Id)
              |> Observable.distinctUntilChanged
              |> Observable.map(function
                | Some grown ->
                  CommitCard.replyBlock(ctx, commit, ValueSome grown.Shown)
                | None -> CommitCard.replyBlock(ctx, commit, ValueNone))
              |> _.ToBinding()
            )

      StackPanel()
        .Children(CommitCard.feedRow ctx (LiveCommit commit), preview, reply)

    | LogRow.TombstoneRow tombstone ->
      CommitCard.feedRow ctx (Tombstone tombstone)

  /// The new-commits pill: a count over the feed, one click to take it.
  let newPill
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    Button()
      .Classes(Theme.name Theme.AccentButton)
      .CornerRadius(999.0)
      .Padding(16.0, 6.0, 16.0, 6.0)
      .Margin(0.0, 8.0, 0.0, 0.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .HorizontalContentAlignment(HorizontalAlignment.Center)
      .Content(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            Icons.icon Glyph.ArrowUp,
            TextBlock()
              .Text(
                model
                |> Observable.map(fun current ->
                  match current.NewCount with
                  | Some count -> $"{count} new commits"
                  | None -> "")
              )
          )
      )
      .IsVisible(model |> Observable.map _.NewCount.IsSome)
      .OnClickHandler(fun _ _ -> dispatch State.NewRequested)

  /// The timeline itself: the virtualized feed over the page's rows.
  /// The shared chrome's return-to-top signal reaches the feed's own
  /// scroller.
  let feed
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    Feed.view {
      Items =
        model
        |> Observable.map(fun current ->
          State.toRows(
            current.Items,
            current.Ranked,
            current.Marker
            |> Option.map(fun visitedAt -> {
              VisitedAt = visitedAt
              ViewerId = props.Session.Session.User.Id
            })
          ))
      KeyOf = State.keyOf
      Row = row(props, dispatch, model)
      Footer =
        ValueSome(
          PageChrome.loadMore(
            model |> Observable.map _.NextCursor.IsSome,
            fun () -> dispatch State.MoreRequested
          )
        )
      ScrollTop =
        ValueSome(
          props.Env.Timeline.Requests
          |> Observable.choose(function
            | Timeline.ScrollToTop -> Some()
            | _ -> None)
        )
    }

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  // The pinned area takes no horizontal inset: the shared chrome above
  // the outlet carries the band and the composer rail, so the page
  // starts a hair under it, on the reading column's left edge.
  let pinnedPage =
    PageChrome.page {
      Model = model
      TitleOf = fun _ -> ""
      Padding = Thickness(0.0, 8.0, 0.0, 0.0)
      ErrorOf = _.Error
      Busy = fun current -> current.Loading, "Loading the log…"
      Empty =
        fun current ->
          not current.Loading && current.Error.IsNone && current.Items.IsEmpty,
          "Nothing here yet. Commit something."
      Header = [ Parts.newPill(model, dispatch) ]
      Body = Parts.feed(props, dispatch, model)
    }

  // The cached log page reports its tree presence, so the since-poll
  // and the chrome subscription pause while the page is stashed and
  // resume on the way back.
  pinnedPage.AttachedToVisualTree.Add(fun _ ->
    dispatch(State.AttachedChanged true))

  pinnedPage.DetachedFromVisualTree.Add(fun _ ->
    dispatch(State.AttachedChanged false))

  pinnedPage

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLastVisit
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideSshKeys>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root
    props.Context.AddDisposable lifetime
