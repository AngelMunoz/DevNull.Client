module DevNull.Client.Views.ScheduledView

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open IcedTasks
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open Navs
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Env
open DevNull.Client.Formatting
open DevNull.Client.Icons
open Microsoft.Extensions.Logging
open DevNull.Client.Navigation
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.UiClock

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

/// The page's state logic: model, messages, update, and the
/// subscription that feeds the due flip from the shared clock.
module State =

  /// Which row action is in flight; every row's buttons stay off while
  /// one is, the way the site's page holds them.
  type RowAction =
    | CommitNow
    | CancelRow

  [<Struct>]
  type Busy = { Id: string; Action: RowAction }

  type Model = {
    Loading: bool
    Error: AppError option
    /// The server's own conflict line, kept until the person moves on:
    /// the refresh the message asks for clears the error line before it
    /// could be read.
    Notice: string option
    Items: ScheduledCommit list
    CurrentPage: int
    LastPage: int
    Total: int
    Busy: Busy voption
    /// The rows the person unfolded past their preview.
    Unfolded: Set<string>
    Now: DateTimeOffset
  }

  type Msg =
    | Fetched of Result<ScheduledPage, AppError>
    | PageRequested of page: int
    | PreviousRequested
    | NextRequested
    | UnfoldToggled of id: string
    | CancelRequested of entry: ScheduledCommit
    | CancelDone of entry: ScheduledCommit * Result<unit, AppError>
    | SubmitNowRequested of entry: ScheduledCommit
    | SubmitNowDone of
      entry: ScheduledCommit *
      Result<ScheduledCommit, AppError>
    | ClockTicked of moment: DateTimeOffset

  let initial = {
    Loading = true
    Error = None
    Notice = None
    Items = []
    CurrentPage = 1
    LastPage = 1
    Total = 0
    Busy = ValueNone
    Unfolded = Set.empty
    Now = DateTimeOffset.UtcNow
  }

  let fetch(env, session: SignedInSession, page: int) =
    ScheduledCommits.list(
      env,
      {
        KeyFile = session.KeyFile
        Page = page
      }
    )

  let init (props: Props<'env>) () =
    initial, Cmd.OfAsync.perform fetch (props.Env, props.Session, 1) Fetched

  let update (props: Props<'env>) msg model =
    let log =
      props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Scheduled"

    let failed what error =
      Cmd.ofAction(fun () ->
        log.LogWarning(
          "The {What} failed: {Error}",
          what,
          bounded 300 (describe error)
        ))

    match msg with
    | Fetched result ->
      match result with
      | Ok page ->
        {
          model with
              Loading = false
              Error = None
              Items = page.Items
              CurrentPage = page.CurrentPage
              LastPage = page.LastPage
              Total = page.Total
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The queue page {Page} fetched {Count} commits.",
            page.CurrentPage,
            page.Items.Length
          ))
      | Error error ->
        {
          model with
              Loading = false
              Error = Some error
        },
        failed "the queue fetch" error
    | PageRequested page ->
      {
        model with
            Loading = true
            Error = None
            Notice = None
            Unfolded = Set.empty
      },
      Cmd.OfAsync.perform fetch (props.Env, props.Session, page) Fetched
    | PreviousRequested ->
      let page = max 1 (model.CurrentPage - 1)

      {
        model with
            Loading = true
            Error = None
            Notice = None
            Unfolded = Set.empty
      },
      Cmd.OfAsync.perform fetch (props.Env, props.Session, page) Fetched
    | NextRequested ->
      let page = min model.LastPage (model.CurrentPage + 1)

      {
        model with
            Loading = true
            Error = None
            Notice = None
            Unfolded = Set.empty
      },
      Cmd.OfAsync.perform fetch (props.Env, props.Session, page) Fetched
    | UnfoldToggled id ->
      let unfolded =
        if model.Unfolded.Contains id then
          model.Unfolded.Remove id
        else
          model.Unfolded.Add id

      { model with Unfolded = unfolded }, Cmd.none
    | CancelRequested entry ->
      let cancel() =
        ScheduledCommits.cancel(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            ScheduledCommitId = entry.Id
          }
        )

      {
        model with
            Error = None
            Busy = ValueSome { Id = entry.Id; Action = CancelRow }
      },
      Cmd.OfAsync.perform cancel () (fun result -> CancelDone(entry, result))
    | CancelDone(entry, result) ->
      match result with
      | Ok() ->
        {
          model with
              Busy = ValueNone
              Items =
                model.Items |> List.filter(fun item -> item.Id <> entry.Id)
              Total = max 0 (model.Total - 1)
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The scheduled commit {Id} was canceled.", entry.Id))
      | Error(Refused(409, _, message)) ->
        // The conflict keeps its own line: the refresh the message asks
        // for would clear the error line before it could be read.
        {
          model with
              Busy = ValueNone
              Notice = Some message
        },
        Cmd.batch [
          Cmd.ofAction(fun () ->
            log.LogWarning(
              "The cancel refused: {Message}",
              bounded 300 message
            ))

          Cmd.OfAsync.perform
            fetch
            (props.Env, props.Session, model.CurrentPage)
            Fetched
        ]
      | Error error ->
        {
          model with
              Busy = ValueNone
              Error = Some error
        },
        Cmd.batch [ failed "the cancel" error; Cmd.none ]
    | SubmitNowRequested entry ->
      let submit() =
        ScheduledCommits.submitNow(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            ScheduledCommitId = entry.Id
          }
        )

      {
        model with
            Error = None
            Busy = ValueSome { Id = entry.Id; Action = CommitNow }
      },
      Cmd.OfAsync.perform submit () (fun result -> SubmitNowDone(entry, result))
    | SubmitNowDone(entry, result) ->
      match result with
      | Ok updated ->
        // The row keeps its place and its body; only what became of it
        // moves, the way the site's row holds what was sent.
        let settled = {
          entry with
              SubmittedAt = updated.SubmittedAt
              Status = updated.Status
        }

        let items =
          model.Items
          |> List.map(fun item ->
            if item.Id = settled.Id then settled else item)

        {
          model with
              Busy = ValueNone
              Items = items
        },
        Cmd.ofAction(fun () ->
          log.LogDebug("The scheduled commit {Id} was submitted.", settled.Id))
      | Error(Refused(409, _, message)) ->
        // The conflict keeps its own line: the refresh the message asks
        // for would clear the error line before it could be read.
        {
          model with
              Busy = ValueNone
              Notice = Some message
        },
        Cmd.batch [
          Cmd.ofAction(fun () ->
            log.LogWarning(
              "The submission refused: {Message}",
              bounded 300 message
            ))

          (Cmd.OfAsync.perform
            fetch
            (props.Env, props.Session, model.CurrentPage)
            Fetched)
        ]
      | Error error ->
        {
          model with
              Busy = ValueNone
              Error = Some error
        },
        Cmd.batch [ failed "the submission" error; Cmd.none ]
    | ClockTicked moment -> { model with Now = moment }, Cmd.none

  let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
    Sub.fromObservable
      "clock"
      (props.Env.UiClock.Minute |> Observable.map ClockTicked)

/// The page's pure labels, as the site's page words them.
module Labels =

  let status(now: DateTimeOffset, entry: ScheduledCommit) : string =
    match entry.Status with
    | StillScheduled ->
      if entry.ScheduledAt <= now then
        "Waiting for submission"
      else
        "Scheduled"
    | SubmittedAs Approved -> "Approved"
    | SubmittedAs Pending -> "Submitted for review"
    | SubmittedAs Rejected -> "Rejected"
    | SubmittedAs(UnknownStatus _) -> "Submitted"
    | CommitDeleted -> "Deleted"

  let footnote(now: DateTimeOffset, entry: ScheduledCommit) : string =
    match entry.Status with
    | StillScheduled ->
      if entry.ScheduledAt <= now then
        "Waiting for the scheduler. If submission fails, it is retried automatically."
      else
        "Private until submitted. You can commit it now or cancel it before submission."
    | _ -> ""

  let when_(entry: ScheduledCommit) : string =
    entry.ScheduledAt.ToLocalTime().ToString("MMM d, yyyy, h:mm tt")

/// The page's semantic regions; the view only composes these.
module Parts =

  /// One segment of a queued body, plain: the queue carries no mention
  /// positions, so nothing links.
  let segmentView(segment: Segment) : Control =
    match segment with
    | TextSegment text ->
      TextBlock().TextWrapping(TextWrapping.Wrap).Text(text) :> Control
    | CodeSegment code ->
      Border()
        .Classes(Theme.name Theme.SurfaceBack)
        .CornerRadius(6.0)
        .Padding(10.0)
        .Child(
          TextBlock()
            .Classes(Theme.name Theme.CodeBlock)
            .Text(code)
            .TextWrapping(TextWrapping.Wrap)
        )
      :> Control
    | BlockSegment(_, html) ->
      Border()
        .Classes(Theme.name Theme.SurfaceBack)
        .CornerRadius(6.0)
        .Padding(10.0)
        .Child(
          TextBlock()
            .Classes(Theme.name Theme.CodeBlock)
            .Text(plainText html)
            .TextWrapping(TextWrapping.Wrap)
        )
      :> Control

  let segmentsPanel(segments: Segment list) : Control =
    StackPanel().Spacing(4.0).Children [|
      for segment in segments -> segmentView segment
    |]

  /// The queued body, folded to its preview until unfolded, with the
  /// site's Show more.
  let body
    (
      dispatch: Dispatch<State.Msg>,
      unfolded: IObservable<bool>,
      entry: ScheduledCommit
    ) : Control =
    let shown(segments: Segment list) = segmentsPanel segments

    let full = shown entry.BodySegments

    let folded: Control =
      match entry.PreviewSegments with
      | Some preview ->
        StackPanel()
          .Spacing(4.0)
          .Children(
            shown preview,
            Button()
              .Name("ShowMore")
              .Classes(Theme.name Theme.GhostButton, Theme.name Theme.FgAccent)
              .Padding(0.0)
              .HorizontalAlignment(HorizontalAlignment.Left)
              .Content("Show more")
              .OnClickHandler(fun _ _ -> dispatch(State.UnfoldToggled entry.Id))
          )
      | None -> full

    ContentControl()
      .Content(
        unfolded
        |> Observable.distinctUntilChanged
        |> Observable.map(fun isOpen -> if isOpen then full else folded)
        |> _.ToBinding()
      )

  /// The poll options the queue keeps, one bordered row each.
  let pollOptions(options: string list) : Control =
    StackPanel().Spacing(4.0).Children [|
      for option in options ->
        Border()
          .Classes(Theme.name Theme.Hairline)
          .BorderThickness(Thickness(1.0))
          .CornerRadius(6.0)
          .Padding(10.0, 6.0, 10.0, 6.0)
          .Child(TextBlock().TextWrapping(TextWrapping.Wrap).Text(option))
        :> Control
    |]

  /// One queue row: the status and its moment, the actions while it
  /// still waits, the folded body, the poll, the image count, and the
  /// footnote.
  let row
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>,
      entry: ScheduledCommit
    ) : Control =
    let busyWith(current: State.Model) =
      current.Busy
      |> ValueOption.map(fun busy -> busy.Id = entry.Id, busy.Action)
      |> ValueOption.defaultValue(false, State.CancelRow)

    let actions =
      Button()
        .Name("CommitNow")
        .Classes(Theme.name Theme.GhostButton)
        .Padding(10.0, 4.0, 10.0, 4.0)
        .Content(
          TextBlock()
            .Text(
              model
              |> Observable.map(fun current ->
                let mine, action = busyWith current

                if mine && action = State.CommitNow then
                  "Committing…"
                else
                  "Commit now")
              |> Observable.distinctUntilChanged
            )
        )
        .IsEnabled(model |> Observable.map _.Busy.IsNone)
        .OnClickHandler(fun _ _ -> dispatch(State.SubmitNowRequested entry))

    let cancel =
      Button()
        .Name("CancelScheduled")
        .Classes(Theme.name Theme.GhostButton)
        .Width(30.0)
        .Height(30.0)
        .Padding(Thickness(0.0))
        .Content(Icons.iconAt(Glyph.Trash, 16.0))
        .IsEnabled(model |> Observable.map _.Busy.IsNone)
        .OnClickHandler(fun _ _ -> dispatch(State.CancelRequested entry))

    let actionRow =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8.0)
        .Children(actions, cancel)

    let heading =
      StackPanel()
        .Spacing(2.0)
        .Children(
          TextBlock()
            .Name("ScheduledStatus")
            .Text(
              model
              |> Observable.map(fun current ->
                Labels.status(current.Now, entry))
              |> Observable.distinctUntilChanged
            ),
          TextBlock()
            .Classes(Theme.name Theme.Mono, Theme.name Theme.Muted)
            .FontSize(11.0)
            .Text(Labels.when_ entry)
        )

    let head =
      DockPanel()
        .LastChildFill(false)
        .Children(
          actionRow
            .DockRight()
            .IsVisible(
              model
              |> Observable.map(fun current ->
                entry.Status = StillScheduled && current.Busy.IsNone
                || (current.Busy
                    |> ValueOption.map(fun busy -> busy.Id = entry.Id)
                    |> ValueOption.defaultValue false))
            ),
          heading
        )

    let children: Control list = [
      head
      match entry.Body with
      | Some _ ->
        body(
          dispatch,
          model
          |> Observable.map(fun current -> current.Unfolded.Contains entry.Id)
          |> Observable.distinctUntilChanged,
          entry
        )
      | None -> ()
      match entry.PollOptions with
      | Some options when not options.IsEmpty -> pollOptions options
      | _ -> ()
      if entry.ImageCount > 0 then
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .FontSize(12.0)
          .Text(
            if entry.ImageCount = 1 then
              "1 image"
            else
              $"{entry.ImageCount} images"
          )
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .FontSize(12.0)
        .TextWrapping(TextWrapping.Wrap)
        .Text(
          model
          |> Observable.map(fun current -> Labels.footnote(current.Now, entry))
          |> Observable.distinctUntilChanged
        )
    ]

    let row =
      StackPanel().Spacing(8.0).Children([| for child in children -> child |])

    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Padding(0.0, 16.0, 0.0, 16.0)
      .Child(row)

  /// The offset pager: the site's Previous and Next, each only while
  /// its page exists.
  let pager
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    let previous =
      Button()
        .Name("PreviousPage")
        .Classes(Theme.name Theme.GhostButton)
        .Content("Previous")
        .IsVisible(
          model |> Observable.map(fun current -> current.CurrentPage > 1)
        )
        .OnClickHandler(fun _ _ -> dispatch State.PreviousRequested)

    let next =
      Button()
        .Name("NextPage")
        .Classes(Theme.name Theme.GhostButton)
        .Content("Next")
        .IsVisible(
          model
          |> Observable.map(fun current ->
            current.CurrentPage < current.LastPage)
        )
        .OnClickHandler(fun _ _ -> dispatch State.NextRequested)

    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(16.0)
      .Margin(0.0, 12.0, 0.0, 24.0)
      .Children(previous, next)

  /// The scrolling queue: the rows, then the pager.
  let queue
    (
      props: Props<'env>,
      dispatch: Dispatch<State.Msg>,
      model: IObservable<State.Model>
    ) : Control =
    let rows =
      ItemsControl()
        .ItemTemplate(
          FuncDataTemplate<ScheduledCommit>(
            (fun entry _ -> row(props, dispatch, model, entry)),
            false
          )
        )
        .BindItemsSource(
          model
          |> Observable.map(fun current ->
            current.Items :> IReadOnlyList<ScheduledCommit>),
          fun entry -> entry.Id
        )

    // The queue keeps the page gutter the pinned header already uses,
    // so the rows, their hairlines, and the title line up on one edge.
    ScrollViewer()
      .Padding(Theme.pagePadding)
      .Content(
        StackPanel().Spacing(8.0).Children(rows, pager(props, dispatch, model))
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  // The intro separates from the title above and the list below; the
  // pinned column itself stacks its children with no spacing.
  let intro =
    StackPanel()
      .Spacing(6.0)
      .Margin(0.0, 4.0, 0.0, 12.0)
      .Children(
        TextBlock()
          .TextWrapping(TextWrapping.Wrap)
          .Text(
            "Only you can see this list. At the scheduled time, each commit goes through the usual review process. Scheduling is not approval."
          ),
        TextBlock()
          .Classes(Theme.name Theme.Mono, Theme.name Theme.Muted)
          .FontSize(11.0)
          .Text($"Times in {TimeZoneInfo.Local.Id}")
      )

  let notice =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .FontSize(12.0)
      .TextWrapping(TextWrapping.Wrap)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Notice |> Option.defaultValue "")
      )
      .IsVisible(model |> Observable.map _.Notice.IsSome)

  let header = StackPanel().Spacing(6.0).Children(intro, notice)

  PageChrome.page {
    Model = model
    TitleOf = fun _ -> "Scheduled commits"
    Padding = Theme.pinnedPadding
    ErrorOf = _.Error
    Busy = fun current -> current.Loading, "Loading the queue…"
    Empty =
      fun current ->
        not current.Loading && current.Error.IsNone && current.Items.IsEmpty,
        "No scheduled commits. Choose Schedule in the composer to submit later."
    Header = [ header ]
    Body = Parts.queue(props, dispatch, model)
  }

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root
    props.Context.AddDisposable lifetime
