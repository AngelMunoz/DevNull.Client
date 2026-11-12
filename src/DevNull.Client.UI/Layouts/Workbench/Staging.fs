module DevNull.Client.Workbench.Staging

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia.Collections
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.Theme

[<Struct>]
type Queue = {
  Items: ScheduledCommit list
  Total: int
  Loading: bool
  Error: AppError option
}

[<Struct>]
type Model = {
  Draft: string
  Quote: Commit option
  Queue: Queue
}

[<Struct>]
type View = {
  Open: bool
  Draft: string
  Quote: Commit option
  Queue: Queue
}

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
  Session: SignedInSession option
  OnToggle: unit -> unit
  OnDraftChanged: string -> unit
  OnPosted: Commit -> unit
  OnScheduled: ScheduledCommit -> unit
}

let initial: Model = {
  Draft = ""
  Quote = None
  Queue = {
    Items = []
    Total = 0
    Loading = false
    Error = None
  }
}

let inline draftLabel(draft: string) =
  if String.IsNullOrWhiteSpace draft then
    "no draft"
  else
    "1 draft"

let inline scheduledLabel(total: int) =
  if total = 1 then "1 scheduled" else $"{total} scheduled"

let stripText(current: View) =
  let queue =
    match current.Queue.Error with
    | Some _ -> "queue unavailable"
    | None -> scheduledLabel current.Queue.Total

  $"STAGING: {draftLabel current.Draft} · {queue}"

let queueMoment(entry: ScheduledCommit) =
  let at = entry.SubmittedAt |> Option.defaultValue entry.ScheduledAt
  at.ToLocalTime().ToString("MMM d, h:mm tt")

let queueStatus(entry: ScheduledCommit) =
  match entry.Status with
  | StillScheduled -> "Scheduled"
  | SubmittedAs _ -> "Submitted"
  | CommitDeleted -> "Deleted"

let queueBody(entry: ScheduledCommit) =
  entry.Body |> Option.defaultValue "(the body is hidden after submission)"

let queueNote(current: View) =
  match current.Queue.Error with
  | Some error -> "The queue could not be read: " + describe error
  | None when current.Queue.Loading && current.Queue.Items.IsEmpty ->
    "Reading the queue…"
  | None when current.Queue.Items.IsEmpty -> "The queue is empty."
  | None when List.length current.Queue.Items < current.Queue.Total ->
    $"Showing {List.length current.Queue.Items} of {current.Queue.Total} queued commits."
  | None -> ""

let queueRow(entry: ScheduledCommit) : Control =
  Border()
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
    .Padding(Thickness(0.0, 6.0, 0.0, 6.0))
    .Child(
      StackPanel()
        .Spacing(2.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Mono, Theme.name Theme.Muted)
            .Text($"{queueStatus entry} · {queueMoment entry}"),
          TextBlock().TextWrapping(TextWrapping.Wrap).Text(queueBody entry)
        )
    )

let queue(model: IObservable<View>) : Control =
  let rows =
    ItemsControl()
      .ItemTemplate(
        FuncDataTemplate<ScheduledCommit>(
          (fun entry _ -> queueRow entry),
          false
        )
      )
      .BindItemsSource(
        model
        |> Observable.map(fun current ->
          current.Queue.Items :> IReadOnlyList<ScheduledCommit>),
        fun entry -> entry.Id
      )

  let note =
    TextBlock()
      .Name("StagingQueueNote")
      .Classes(Theme.name Theme.Muted)
      .TextWrapping(TextWrapping.Wrap)
      .Text(
        model |> Observable.map queueNote |> Observable.distinctUntilChanged
      )

  let children: Control array = [|
    TextBlock().Classes(Theme.name Theme.Mono).Text("QUEUE") :> Control
    note
    ScrollViewer()
      .MaxHeight(240.0)
      .VerticalScrollBarVisibility(ScrollBarVisibility.Auto)
      .Content(rows)
    :> Control
  |]

  StackPanel().Spacing(6.0).Children(children)

let strip(props: Props<'env>, model: IObservable<View>) : Control =
  let counts =
    TextBlock()
      .Name("StagingCounts")
      .Classes(Theme.name Theme.Mono)
      .VerticalAlignment(VerticalAlignment.Center)
      .Text(
        model |> Observable.map stripText |> Observable.distinctUntilChanged
      )

  let toggle =
    Button()
      .Name("StagingToggle")
      .Classes(Theme.name Theme.GhostButton)
      .Content(
        TextBlock()
          .Text(
            model
            |> Observable.map(fun current ->
              if current.Open then "close" else "open staging ▸")
            |> Observable.distinctUntilChanged
          )
      )
      .OnClickHandler(fun _ _ -> props.OnToggle())

  let head = Grid()
  head.ColumnDefinitions <- ColumnDefinitions("*,Auto")
  head.Children(counts.Column(0), toggle.Column(1)) |> ignore

  Border().Padding(Thickness(16.0, 6.0, 16.0, 6.0)).Child(head)

let composerFor
  (props: Props<'env>, current: View, openQueue: unit -> unit)
  : Control =
  match props.Session with
  | None ->
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Text("Sign in to stage a commit.")
  | Some session ->
    let target =
      current.Quote
      |> Option.map(fun commit -> Composer.Quoting commit.Id)
      |> Option.defaultValue Composer.Fresh

    Composer.Page {
      Env = props.Env
      Session = session
      Target = target
      Participants = ValueNone
      OnCommitted = props.OnPosted
      OnScheduled = ValueSome props.OnScheduled
      OnOpenQueue = ValueSome openQueue
      Draft =
        ValueSome {
          Seed = current.Draft
          Changed = props.OnDraftChanged
        }
      OnBodyReady = ValueNone
    }
    :> Control

let openArea(props: Props<'env>, model: IObservable<View>) : Control =
  let queueView = queue model

  let composer =
    ContentControl()
      .Name("StagingComposer")
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .Content(
        model
        |> Observable.distinctUntilChangedKey(fun current ->
          current.Open, current.Quote |> Option.map _.Id)
        |> Observable.map(fun current ->
          if current.Open then
            composerFor(props, current, fun () -> queueView.BringIntoView())
          else
            Panel() :> Control)
        |> _.ToBinding()
      )

  Border()
    .Name("StagingArea")
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
    .Padding(Thickness(16.0, 10.0, 16.0, 12.0))
    .IsVisible(
      model |> Observable.map _.Open |> Observable.distinctUntilChanged
    )
    .Child(
      let parts: Control array = [| composer; queueView |]
      StackPanel().Spacing(10.0).Children(parts)
    )

let view (props: Props<'env>) (model: IObservable<View>) : Control =
  Border()
    .Name("StagingRegion")
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
    .Child(
      let parts: Control array = [|
        strip(props, model)
        openArea(props, model)
      |]

      StackPanel().Children(parts)
    )
