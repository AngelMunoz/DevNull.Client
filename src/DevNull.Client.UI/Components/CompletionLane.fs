module DevNull.Client.Components.CompletionLane

open System
open System.Reactive.Subjects
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Input
open Avalonia.Interactivity
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Completion
open DevNull.Client.ImageCache
open DevNull.Client.Search
open DevNull.Client.Theme

module Flyout = DevNull.Client.Components.Completion

[<Struct>]
type Choice =
  | PersonChosen of handle: string
  | IssueChosen of number: int

type Event =
  | Stepped of step: int
  | Hovered of index: int
  | Closed
  | Applied of text: string * caret: int
  | Picked of choice: Choice

[<Struct>]
type Snapshot = {
  Trigger: Trigger voption
  Flyout: Flyout.View
  Open: bool
}

[<Struct>]
type Props = {
  Session: CompletionSession
  Trigger: IObservable<Trigger voption>
  Flyout: IObservable<Flyout.View>
  Open: IObservable<bool>
  Body: TextBox
  Events: Event -> unit
  Images: ImageCacheService
}

let closedFlyout: Flyout.View = {
  Query = ""
  Caption = false
  Users = []
  Organizations = []
  Issue = None
  Searching = false
  Throttled = false
  Active = 0
}

let closedSnapshot: Snapshot = {
  Trigger = ValueNone
  Flyout = closedFlyout
  Open = false
}

/// How many rows the keys walk: the people and organizations together,
/// or the one issue card.
let countOf(flyout: Flyout.View) : int =
  match flyout.Issue with
  | Some _ -> 1
  | None -> List.length flyout.Users + List.length flyout.Organizations

/// The armed row, clamped into the rows the newest state holds.
let activeOf(flyout: Flyout.View) : int =
  let count = countOf flyout

  if count = 0 then 0 else min flyout.Active (count - 1)

/// The completion lands in the box itself, so the change rides the
/// same text-changed path typing does, and the caret the surgery
/// computed follows it in the same beat.
let applied(props: Props, text: string, caret: int) : unit =
  props.Body.Text <- text
  props.Body.CaretIndex <- caret
  props.Events(Applied(text, caret))

let completeMention(props: Props, snapshot: Snapshot, handle: string) : unit =
  match snapshot.Trigger with
  | ValueSome(Mention _ as trigger) ->
    let text = props.Body.Text |> Option.ofObj |> Option.defaultValue ""

    let next, caret = mentionCompleted(text, trigger, handle)

    applied(props, next, caret)
    props.Events(Picked(PersonChosen handle))
  // The caret already left the mention behind.
  | ValueSome(IssueRef _)
  | ValueNone -> ()

let completeIssue(props: Props, snapshot: Snapshot, issue: Issue) : unit =
  match snapshot.Trigger with
  | ValueSome(IssueRef _ as trigger) ->
    let text = props.Body.Text |> Option.ofObj |> Option.defaultValue ""

    let next, caret = issueCompleted(text, trigger, issue.Url)

    applied(props, next, caret)
    props.Events(Picked(IssueChosen issue.Number))
  // The caret already left the reference behind.
  | ValueSome(Mention _)
  | ValueNone -> ()

/// The pick the keys answer: the armed row.
let picked(props: Props, snapshot: Snapshot) : unit =
  let flyout = snapshot.Flyout
  let active = activeOf flyout

  match snapshot.Trigger, active with
  | ValueSome(IssueRef _), _ when flyout.Issue.IsSome ->
    flyout.Issue
    |> Option.iter(fun issue -> completeIssue(props, snapshot, issue))
  | ValueSome(Mention _), at when at >= 0 ->
    match List.tryItem at flyout.Users with
    | Some user -> completeMention(props, snapshot, user.Handle)
    | None ->
      flyout.Organizations
      |> List.tryItem(at - List.length flyout.Users)
      |> Option.iter(fun organization ->
        completeMention(props, snapshot, organization.Handle))
  // Nothing stands at the armed place.
  | _ -> ()

let flyoutActions
  (props: Props, standing: BehaviorSubject<Snapshot>)
  : Flyout.Actions = {
  PickPerson = fun user -> completeMention(props, standing.Value, user.Handle)
  PickOrganization =
    fun organization ->
      completeMention(props, standing.Value, organization.Handle)
  PickIssue = fun issue -> completeIssue(props, standing.Value, issue)
  Hovered = fun index -> props.Events(Hovered index)
}

/// The popup and its feeds. The lane owns both: the session queries
/// ride the trigger, the keys read one local snapshot, and the popup's
/// open state follows the model. Nothing is handed back to the
/// composer's subscription set.
let lane
  (props: Props, standing: BehaviorSubject<Snapshot>)
  : Popup * (unit -> IDisposable) =
  let flyout =
    Flyout.view(props.Flyout, flyoutActions(props, standing), props.Images)

  let popup =
    Popup()
      .Name("CompletionFlyout")
      .PlacementTarget(props.Body)
      .Placement(PlacementMode.Bottom)
      .Child(
        Border()
          .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
          .BorderThickness(1.0)
          .CornerRadius(8.0)
          .Padding(4.0)
          .MaxWidth(288.0)
          .Child(flyout)
      )

  let start() : IDisposable =
    // The keys decide against the completion state as it stands, so
    // the three model slices meet in one local lookup.
    let standingFeed =
      Observable.combineLatest
        (Observable.combineLatest props.Trigger props.Flyout)
        props.Open
      |> Observable.subscribe(fun ((trigger, flyout), isOpen) ->
        standing.OnNext {
          Trigger = trigger
          Flyout = flyout
          Open = isOpen
        })

    // The queries the session asks come from the model's own trigger,
    // so the two can never drift apart.
    let queries =
      props.Trigger
      |> Observable.map(fun trigger ->
        match trigger with
        | ValueSome(Mention(_, query)) -> ValueSome query
        | ValueSome(IssueRef _)
        | ValueNone -> ValueNone)
      |> Observable.distinctUntilChanged
      |> Observable.subscribe props.Session.Set

    let digits =
      props.Trigger
      |> Observable.map(fun trigger ->
        match trigger with
        | ValueSome(IssueRef(_, digits)) -> ValueSome(IssueNumber.value digits)
        | ValueSome(Mention _)
        | ValueNone -> ValueNone)
      |> Observable.distinctUntilChanged
      |> Observable.subscribe props.Session.SetIssue

    // The house sets a popup's open by hand; an observable binding
    // never opens it.
    let opened =
      props.Open
      |> Observable.distinctUntilChanged
      |> Observable.subscribe(fun isOpen -> popup.IsOpen <- isOpen)

    {
      new IDisposable with
        member _.Dispose() =
          for feed in [ queries; digits; standingFeed; opened ] do
            feed.Dispose()

          standing.OnCompleted()
    }

  popup, start

/// The completion lane over the body.
let view(props: Props) : Control =
  let standing = new BehaviorSubject<Snapshot>(closedSnapshot)

  props.Body.AddHandler(
    InputElement.KeyDownEvent,
    (fun _ args ->
      let snapshot = standing.Value

      if not args.Handled && snapshot.Open then
        match args.Key with
        | Key.Down
        | Key.Up when countOf snapshot.Flyout > 0 ->
          args.Handled <- true

          props.Events(Stepped(if args.Key = Key.Down then 1 else -1))
        | Key.Enter
        | Key.Tab when args.KeyModifiers = KeyModifiers.None ->
          args.Handled <- true
          picked(props, snapshot)
        | Key.Escape ->
          args.Handled <- true
          props.Events Closed
        | _ -> ()),
    RoutingStrategies.Tunnel
  )

  // Focus leaving the box closes the flyout, the site's own blur.
  props.Body.LostFocus.Add(fun _ -> props.Events Closed)

  let popup, startLane = lane(props, standing)

  let startLaneWith(_: Dispatch<obj>) : IDisposable = startLane()

  let start =
    Sub.ofId "completion-lane" startLaneWith
    |> Sub.untilDetached props.Body
    |> List.exactlyOne
    |> snd

  let _ = start ignore

  popup
