module DevNull.Client.Layouts.Editor.ListTab

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
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.PageChrome
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Stores
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
  and 'env :> IProvideImageCache> = {
  Env: 'env
  Session: SignedInSession
  Kind: Kind
  Zone: Zone
  Now: unit -> DateTimeOffset
  OnOpenThread: Commit -> unit
}

let title(kind: Kind) =
  match kind with
  | Saved -> "SAVED"
  | Queue -> "QUEUE"

module State =

  type Model = {
    Loading: bool
    Error: AppError option
    Saved: CommitNode list
    SavedCursor: string option
    Queue: ScheduledCommit list
    QueuePage: int
    QueueLast: int
    Now: DateTimeOffset
  }

  type Msg =
    | SavedFetched of Result<Page<CommitNode>, AppError>
    | SavedAppended of Result<Page<CommitNode>, AppError>
    | QueueFetched of Result<ScheduledPage, AppError>
    | QueueAppended of Result<ScheduledPage, AppError>
    | MoreSaved
    | MoreQueue

  let initial(now: DateTimeOffset) = {
    Loading = true
    Error = None
    Saved = []
    SavedCursor = None
    Queue = []
    QueuePage = 1
    QueueLast = 1
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

  let init(props: Props<'env>) =
    let model = initial(props.Now())

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
         {
           model with
               Loading = false
               Error = None
               Saved = page.Items
               SavedCursor = page.NextCursor
         },
         Cmd.none
       | Error error ->
         {
           model with
               Loading = false
               Error = Some error
         },
         Cmd.none)
    | SavedAppended result ->
      (match result with
       | Ok page ->
         {
           model with
               Loading = false
               Error = None
               Saved = model.Saved @ page.Items
               SavedCursor = page.NextCursor
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
      match model.SavedCursor with
      | Some cursor ->
        { model with Loading = true },
        Cmd.OfAsync.perform
          Stars.index
          (props.Env, savedRequest(props, Some cursor))
          SavedAppended
      | None -> model, Cmd.none
    | MoreQueue ->
      if model.QueuePage < model.QueueLast then
        { model with Loading = true },
        Cmd.OfAsync.perform
          ScheduledCommits.list
          (props.Env, queueRequest(props, model.QueuePage + 1))
          QueueAppended
      else
        model, Cmd.none

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

let savedView
  (props: Props<'env>, model: State.Model, dispatch: Dispatch<State.Msg>)
  : Control =
  let rowProps: RowProps = {
    OnEvent =
      function
      | Opened commit -> props.OnOpenThread commit
      | _ -> ()
    OnOpenExternal = ignore
    ViewerId = props.Session.Session.User.Id
    Pasteis = Observable.single props.Session.Session.PasteisMode
    Now = Observable.single model.Now
    PollChoices = Observable.single(Map.empty<string, string>)
    Dense = true
    Images = props.Env.ImageCache
    Flyout = ValueNone
    Pin = ValueNone
    Star = ValueNone
    Stars = ValueNone
    Vote = ValueNone
    Pastel = ValueNone
    Menu = ValueNone
    Translation = Observable.single(fun _ -> ValueNone)
    Zone = props.Zone
  }

  let rows = [|
    for node in model.Saved do
      feedRow rowProps node
  |]

  let empty: Control =
    if model.Loading && model.Saved.IsEmpty then
      TextBlock().Text("Loading saved commits…").Classes(Theme.name Theme.Muted)
    elif model.Saved.IsEmpty then
      TextBlock().Text("No saved commits.").Classes(Theme.name Theme.Muted)
    else
      Panel()

  let error = errorPanel(Observable.single model.Error)

  let more =
    loadMore(
      Observable.single(model.SavedCursor.IsSome),
      fun () -> dispatch State.MoreSaved
    )

  more.Name <- "ListTabMoreSaved"

  StackPanel()
    .Spacing(6.0)
    .Children(
      TextBlock()
        .Text("STARS.INDEX")
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted),
      empty,
      error,
      StackPanel().Spacing(4.0).Children(rows),
      more
    )

let queueView
  (props: Props<'env>, model: State.Model, dispatch: Dispatch<State.Msg>)
  : Control =
  let rows = [|
    for entry in model.Queue do
      queueRow(props, model.Now, entry)
  |]

  let empty: Control =
    if model.Loading && model.Queue.IsEmpty then
      TextBlock().Text("Loading the queue…").Classes(Theme.name Theme.Muted)
    elif model.Queue.IsEmpty then
      TextBlock().Text("No scheduled commits.").Classes(Theme.name Theme.Muted)
    else
      Panel()

  let error = errorPanel(Observable.single model.Error)

  let more =
    loadMore(
      Observable.single(model.QueuePage < model.QueueLast),
      fun () -> dispatch State.MoreQueue
    )

  more.Name <- "ListTabMoreQueue"

  StackPanel()
    .Spacing(6.0)
    .Children(
      TextBlock()
        .Text("SCHEDULED")
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted),
      empty,
      error,
      StackPanel().Spacing(6.0).Children(rows),
      more
    )

let view
  (
    props: Props<'env>,
    model: IObservable<State.Model>,
    dispatch: Dispatch<State.Msg>
  ) : Control =
  let body =
    rebuildOn(
      model,
      (fun current -> current),
      fun current ->
        match props.Kind with
        | Saved -> savedView(props, current, dispatch)
        | Queue -> queueView(props, current, dispatch)
    )

  ScrollViewer()
    .Name($"EditorListTab-{title props.Kind}")
    .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
    .Content(
      ContentControl()
        .Content(body |> _.ToBinding())
        .Margin(Thickness(16.0, 12.0))
    )

let program
  (props: Props<'env>)
  : Program<unit, State.Model, State.Msg, Control> =
  Program.mkProgram
    (fun () -> State.init props)
    (fun msg model -> State.update(props, msg, model))
    (fun model dispatch -> view(props, model, dispatch))

let mount(dispatcher: IDispatcher, props: Props<'env>) =
  DevNull.Client.Programs.runPage(dispatcher, program props)
