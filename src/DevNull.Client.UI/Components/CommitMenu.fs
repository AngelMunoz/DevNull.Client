module DevNull.Client.Components.CommitMenu

open System
open Avalonia
open Avalonia.Automation
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Input
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Components.MenuPicks
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Theme

[<Struct>]
type AuthorRelation = {
  IsFollowing: bool
  IsMuting: bool
  IsBlocking: bool
}

[<Struct>]
type MenuSlot = {
  Share: (Commit -> unit) voption
  Report: (Commit -> unit) voption
  Delete: (Commit -> unit) voption
  ToggleFollow: Commit -> unit
  ToggleMute: Commit -> unit
  Block: Commit -> unit
  Relations: IObservable<Commit -> AuthorRelation voption>
  Busy: IObservable<string option>
}

[<Struct>]
type MenuProps = {
  Commit: Commit
  ViewerId: string
  Slot: MenuSlot
  OnOpened: Commit -> unit
  CopyLink: Control -> unit
}

// The site's block ask, word for word: what a block ends, what neither
// side is told, and the way back.
let blockWords =
  "You'll stop following each other. Neither of you will be able to message, follow, reply to, or star the other, or see each other's commits. They won't be told, but they'll see your profile as unavailable. You can unblock them any time from your settings."

// The site's delete ask: the undo window is the whole body.
let deleteTitle = "Delete this commit?"
let deleteWords = "You can undo for 30 seconds."

// The relation the author items speak before the page's read lands:
// the site's default words, held disabled until the read answers.
let unknownRelation = {
  IsFollowing = false
  IsMuting = false
  IsBlocking = false
}

// True when one commit's menu action is the one in flight.
let inFlight(commit: Commit, busy: string option) : bool =
  Option.contains commit.Id busy

// The menu's rows for one relation read, in the site's order. Every
// row keeps its click handled — `menuPick` and `confirmPick` already
// do — so no row ever opens the card behind the menu.
let rowsFor
  (props: MenuProps, relation: AuthorRelation voption, source: Control)
  : Control array =
  let commit = props.Commit
  let slot = props.Slot

  // The command rows wait while this commit's own action runs.
  let idle =
    slot.Busy |> Observable.map(fun busy -> inFlight(commit, busy) |> not)

  let confirmBusy =
    slot.Busy |> Observable.map(fun busy -> inFlight(commit, busy))

  // The author items wait on top of that until the page's read lands.
  let relationReady = relation |> ValueOption.isSome

  let enabled = idle |> Observable.map(fun ready -> ready && relationReady)

  let copyRow = menuPick("Copy link", fun () -> props.CopyLink source)

  let shareRow: Control =
    match slot.Share with
    | ValueSome command when commit.Status = Approved ->
      menuPick("Send in Remotes", fun () -> command commit).IsEnabled(idle)
    | _ -> Panel()

  let authorRows: Control list =
    if
      (Author.id commit.Author) = props.ViewerId
      || Author.isOrganization commit.Author
    then
      []
    else
      let handle = (Author.handle commit.Author)
      let current = relation |> ValueOption.defaultValue unknownRelation

      let followRow =
        if current.IsBlocking then
          None
        else
          let word =
            if current.IsFollowing then
              $"Unfollow @{handle}"
            else
              $"Follow @{handle}"

          Some(
            menuPick(word, fun () -> slot.ToggleFollow commit)
              .IsEnabled(enabled)
          )

      let muteWord =
        if current.IsMuting then
          $"Unmute @{handle}"
        else
          $"Mute @{handle}"

      let muteRow =
        menuPick(muteWord, fun () -> slot.ToggleMute commit).IsEnabled(enabled)

      let blockRow =
        if current.IsBlocking then
          menuPick($"Unblock @{handle}", fun () -> slot.Block commit)
            .IsEnabled(enabled)
        else
          (confirmPick {
            Label = $"Block @{handle}"
            Title = $"Block @{handle}?"
            Words = blockWords
            Verb = "Block"
            OnConfirm = fun () -> slot.Block commit
            Busy = confirmBusy
          })
            .IsEnabled(enabled)

      [ followRow; Some muteRow; Some blockRow ] |> List.choose id

  let authorGroup = StackPanel().Children(authorRows |> Array.ofList) :> Control

  let reportRow: Control =
    match slot.Report with
    | ValueSome command when
      commit.Status = Approved
      && (Author.id commit.Author) <> props.ViewerId
      && not(Author.isOrganization commit.Author)
      ->
      menuPick("Report commit", fun () -> command commit).IsEnabled(idle)
    | _ -> Panel() :> Control

  let deleteRow: Control =
    match slot.Delete with
    | ValueSome command when
      (Author.id commit.Author) = props.ViewerId
      || Author.isAdmin(commit.Author, props.ViewerId)
      ->
      (confirmPick {
        Label = "Delete commit"
        Title = deleteTitle
        Words = deleteWords
        Verb = "Delete"
        OnConfirm = fun () -> command commit
        Busy = confirmBusy
      })
        .IsEnabled(idle)
    | _ -> Panel() :> Control

  [| copyRow; shareRow; authorGroup; reportRow; deleteRow |]

let view(props: MenuProps) : Control =
  let commit = props.Commit

  // The popup card binds its rows to the relation read, so the words
  // flip the moment the page's answer lands, menu open or not.
  let flyout = Popup()
  let content = ContentControl()

  // The focusable card receives the keys while the menu stands, so
  // Escape closes it here and hands focus back to the trigger.
  let card =
    Border()
      .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
      .BorderThickness(1.0)
      .CornerRadius(6.0)
      .Padding(4.0)
      .Focusable(true)
      .Child(content)

  flyout.Child <- card

  let more =
    Button()
      .Name("CommitMenuButton")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(6.0, 2.0, 6.0, 2.0)
      .Content("···")
      .OnClickHandler(fun _ args ->
        args.Handled <- true

        // The page's relation read rides the open edge, the way the
        // site's menu asks once it opens.
        let opening = not flyout.IsOpen
        flyout.IsOpen <- opening

        if opening then
          props.OnOpened commit)

  AutomationProperties.SetName(more, "More actions")
  ToolTip.SetTip(more, "More")

  flyout
    .Name("CommitMenuFlyout")
    .PlacementTarget(more)
    .Placement(PlacementMode.BottomEdgeAlignedRight)
  |> ignore

  card.AddHandler(
    InputElement.KeyDownEvent,
    fun _ args ->
      if args.Key = Key.Escape then
        args.Handled <- true
        flyout.IsOpen <- false
        more.Focus() |> ignore
  )

  flyout.Opened.Add(fun _ -> card.Focus() |> ignore)

  content.Content(
    props.Slot.Relations
    |> Observable.map(fun lookup -> lookup commit)
    |> Observable.distinctUntilChanged
    |> Observable.map(fun relation ->
      StackPanel().Children(rowsFor(props, relation, more)))
    |> _.ToBinding()
  )
  |> ignore

  Panel().Children(more, flyout)
