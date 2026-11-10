module DevNull.Client.Components.ThreadMenus

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Components.ThreadChrome
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache
open DevNull.Client.RemotesState

[<Struct>]
type MenuActions = {
  ViewProfile: User -> unit
  Pin: bool -> unit
  Archive: bool -> unit
  Mute: MuteDuration -> unit
  Block: User -> unit
  Leave: unit -> unit
}

[<Struct>]
type ActionsMenuProps = {
  Target: ChatTarget
  Pinned: IObservable<bool>
  Archived: IObservable<bool>
  Busy: IObservable<bool>
  Actions: MenuActions
}

[<Struct>]
type OrgActions = {
  RenameChanged: string -> unit
  Rename: unit -> unit
  MemberChanged: string -> unit
  Add: unit -> unit
  ShareChanged: bool -> unit
  Role: string * OrgRole -> unit
  Remove: string * bool -> unit
}

[<Struct>]
type OrgMenuProps = {
  Org: OrgRef
  ViewerId: string
  ViewerHandle: string
  Thread: IObservable<ThreadState>
  Rename: IObservable<string>
  Picked: IObservable<string list>
  Shared: IObservable<bool>
  Busy: IObservable<bool>
  Actions: OrgActions
}

// The target's own name as the menu title.
let menuTitle(target: ChatTarget) : string =
  match target with
  | Partner partner -> partner.Name
  | Org org -> org.Name |> Option.defaultValue "the org"

// The partner's profile pick.
let profilePick(partner: User, viewProfile: User -> unit) : Control =
  Button()
    .Classes(Theme.name Theme.GhostButton)
    .Padding(6.0, 2.0, 6.0, 2.0)
    .FontSize(12.0)
    .HorizontalContentAlignment(HorizontalAlignment.Left)
    .Content("view profile")
    .OnClickHandler(fun _ args ->
      args.Handled <- true
      viewProfile partner)

// One menu pick that follows a live state slice.
let inline visiblePick
  (label: string, onPick: unit -> unit, shown: IObservable<bool>)
  : Control =
  menuPick(label, onPick).IsVisible(shown)

// The mute choices, each one a quiet pick that waits while a call
// stands.
let mutePicks(actions: MenuActions, busy: IObservable<bool>) : Control array = [|
  for label, how in
    [
      "mute 1 hour", MuteHour
      "mute until tomorrow", MuteTomorrow
      "mute 1 week", MuteWeek
      "mute forever", MuteForever
      "unmute", MuteOff
    ] ->
    menuPick(label, fun () -> actions.Mute how)
      .IsEnabled(busy |> Observable.map not)
|]

// The partner's block pick.
let blockPick
  (actions: MenuActions, partner: User, busy: IObservable<bool>)
  : Control =
  confirmPick {
    Label = $"block @{partner.Handle}"
    Title = $"Block {partner.Name}?"
    Words =
      "This conversation goes away for both of you, and neither of you can message, follow, reply to, or star the other. They won't be told. You can unblock them any time from your settings."
    Verb = "Block"
    OnConfirm = fun () -> actions.Block partner
    Busy = busy
  }

// The org's leave pick.
let leavePick
  (actions: MenuActions, title: string, busy: IObservable<bool>)
  : Control =
  confirmPick {
    Label = $"leave {title}"
    Title = $"Leave {title}?"
    Words =
      "You stop seeing the org and its history. A maintainer would have to add you back. If you are its only maintainer, the longest-standing member takes over."
    Verb = "Leave"
    OnConfirm = fun () -> actions.Leave()
    Busy = busy
  }

// The menu's rows in the order the flyout carries them.
let actionRows(props: ActionsMenuProps) : Control array =
  let inline waiting(control: Control) =
    control.IsEnabled(props.Busy |> Observable.map not)

  [|
    match props.Target with
    | Partner partner -> profilePick(partner, props.Actions.ViewProfile)
    | Org _ -> ()
    visiblePick(
      "pin",
      (fun () -> props.Actions.Pin true),
      props.Pinned |> Observable.map not
    )
    |> waiting
    visiblePick("unpin", (fun () -> props.Actions.Pin false), props.Pinned)
    |> waiting
    visiblePick(
      "stash",
      (fun () -> props.Actions.Archive true),
      props.Archived |> Observable.map not
    )
    |> waiting
    visiblePick("pop", (fun () -> props.Actions.Archive false), props.Archived)
    |> waiting
    yield! mutePicks(props.Actions, props.Busy)
    match props.Target with
    | Partner partner -> blockPick(props.Actions, partner, props.Busy)
    | Org _ -> leavePick(props.Actions, menuTitle props.Target, props.Busy)
  |]

let actionsMenu(props: ActionsMenuProps) : Control =
  let title = menuTitle props.Target

  let flyout =
    Popup()
      .Child(
        Border()
          .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
          .BorderThickness(1.0)
          .CornerRadius(6.0)
          .Padding(4.0)
          .Child(StackPanel().Children(actionRows props))
      )

  let more =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(6.0)
      .Content("···")
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        flyout.IsOpen <- not flyout.IsOpen)

  ToolTip.SetTip(more, $"More options for {title}")

  let placed =
    flyout
      .Name("ActionsMenuFlyout")
      .PlacementTarget(more)
      .Placement(PlacementMode.BottomEdgeAlignedRight)

  Panel().Children(more, placed).HorizontalAlignment(HorizontalAlignment.Right)

// One org member's avatar in the stack.
let memberAvatar(cache: ImageCacheService, member': ThreadMember) : Control =
  (Avatar.view {
    Name = member'.User.Name
    Url = member'.User.AvatarUrl
    Lane = member'.User.Lane
    Size = 26.0
    Cache = cache
    Square = false
  })
    .Margin(-6.0, 0.0, 0.0, 0.0)

let memberStack
  (cache: ImageCacheService, members: ThreadMember list)
  : Control =
  StackPanel().Orientation(Orientation.Horizontal).Spacing(0.0).Children [|
    for member' in members -> memberAvatar(cache, member')
  |]

// The rename field: the name bound to the model slice.
let renameBox
  (rename: IObservable<string>, onChanged: string -> unit)
  : TextBox =
  TextBox()
    .Name("OrgRename")
    .PlaceholderText("name")
    .MaxLength(60)
    .MinHeight(30.0)
    .FontSize(12.0)
    .MaxWidth(180.0)
    .Text(rename |> Observable.distinctUntilChanged |> _.ToBinding())
    .OnTextChangedHandler(fun box _ ->
      box.Text |> Option.ofObj |> Option.defaultValue "" |> onChanged)

// The rename field's save command.
let saveButton(onSave: unit -> unit) : Control =
  Button()
    .Classes(Theme.name Theme.GhostButton)
    .Padding(6.0, 2.0, 6.0, 2.0)
    .FontSize(11.0)
    .Content("save")
    .OnClickHandler(fun _ args ->
      args.Handled <- true
      onSave())

// The rename row: the field and its save command.
let renameRow
  (rename: IObservable<string>, onChanged: string -> unit, onSave: unit -> unit)
  : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(4.0)
    .Children(renameBox(rename, onChanged), saveButton onSave)

// The org member field: Enter reports the trimmed handle.
let memberBox(onChanged: string -> unit) : TextBox =
  TextBox()
    .Name("OrgMember")
    .PlaceholderText("@handle")
    .MinHeight(30.0)
    .FontSize(12.0)
    .MaxWidth(180.0)
    .OnKeyDownHandler(fun box args ->
      if args.Key = Key.Enter then
        args.Handled <- true

        let typed =
          box.Text |> Option.ofObj |> Option.defaultValue "" |> _.Trim()

        onChanged typed)

// The member's next role and the word that offers it.
let roleFlip(member': ThreadMember) : string * OrgRole =
  match member'.Role with
  | Some Maintainer
  | Some(UnknownRole _) -> "make contributor", Contributor
  | Some Contributor
  | None -> "make maintainer", Maintainer

// One member's role pick and removal. The removal waits while a call
// stands; the role pick follows its own route.
let memberPick(member': ThreadMember, props: OrgMenuProps) : Control =
  let label, nextRole = roleFlip member'

  StackPanel()
    .Spacing(2.0)
    .Children(
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(4.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .FontSize(11.0)
            .Text("@" + member'.User.Handle)
            .VerticalAlignment(VerticalAlignment.Center),
          menuPick(
            label,
            fun () -> props.Actions.Role(member'.User.Handle, nextRole)
          )
        ),
      menuPick(
        "Remove from org",
        fun () -> props.Actions.Remove(member'.User.Handle, false)
      )
        .IsEnabled(props.Busy |> Observable.map not)
    )
    .Margin(0.0, 2.0, 0.0, 0.0)

// The rows for every member but the viewer.
let memberRows(others: ThreadMember list, props: OrgMenuProps) : Control array = [|
  for member' in others -> memberPick(member', props)
|]

// The add-section's own count: everyone the org would hold once the
// picked names land.
let addCount
  (
    members: IObservable<ThreadMember list>,
    picked: IObservable<string list>,
    viewerId: string
  ) : IObservable<int> =
  Observable.combineLatest members picked
  |> Observable.map(fun (current, picked) ->
    let others =
      current |> List.filter(fun member' -> member'.User.Id <> viewerId)

    List.length others + 1 + List.length picked)
  |> Observable.distinctUntilChanged

// The count line under "add members".
let addCountLine(count: IObservable<int>) : Control =
  ContentControl()
    .Content(
      count
      |> Observable.map(fun count ->
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(11.0)
          .Text($"{count}/100")
        :> Control)
      |> _.ToBinding()
    )

// The live member rows under the add section.
let memberHost
  (members: IObservable<ThreadMember list>, props: OrgMenuProps)
  : Control =
  ContentControl()
    .Content(
      members
      |> Observable.map(fun current ->
        current
        |> List.filter(fun member' -> member'.User.Id <> props.ViewerId))
      |> Observable.distinctUntilChanged
      |> Observable.map(fun others ->
        StackPanel().Children(memberRows(others, props)))
      |> _.ToBinding()
    )

// The share-history choice.
let shareRow(shared: IObservable<bool>, onChanged: bool -> unit) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(4.0)
    .Children(
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .FontSize(11.0)
        .Text(
          "Share history — they can read everything pushed before they joined"
        )
        .TextWrapping(TextWrapping.Wrap)
        .VerticalAlignment(VerticalAlignment.Center),
      CheckBox()
        .IsChecked(shared |> _.ToBinding())
        .OnIsCheckedChangedHandler(fun box _ ->
          let shared =
            box.IsChecked |> Option.ofNullable |> Option.defaultValue false

          onChanged shared)
    )

// One section heading.
let sectionTitle(text: string) : Control =
  TextBlock().Classes(Theme.name Theme.Mono).FontSize(11.0).Text(text)

// The add command under the member field.
let addButton(onAdd: unit -> unit) : Control =
  Button()
    .Classes(Theme.name Theme.GhostButton)
    .Padding(6.0, 2.0, 6.0, 2.0)
    .FontSize(12.0)
    .HorizontalContentAlignment(HorizontalAlignment.Left)
    .Content("add")
    .OnClickHandler(fun _ args ->
      args.Handled <- true
      onAdd())

// The viewer's own leave pick.
let orgLeavePick(props: OrgMenuProps) : Control =
  let orgName = props.Org.Name |> Option.defaultValue "the org"

  confirmPick {
    Label = $"leave {orgName}"
    Title = $"Leave {orgName}?"
    Words =
      "You stop seeing the org and its history. A maintainer would have to add you back. If you are its only maintainer, the longest-standing member takes over."
    Verb = "Leave"
    OnConfirm = fun () -> props.Actions.Remove(props.ViewerHandle, true)
    Busy = props.Busy
  }

// The org menu's rows in the order the flyout carries them.
let orgRows(props: OrgMenuProps) : Control array =
  let members = props.Thread |> Observable.map _.Members

  [|
    sectionTitle "rename"
    renameRow(props.Rename, props.Actions.RenameChanged, props.Actions.Rename)
    (sectionTitle "add members").Margin(0.0, 6.0, 0.0, 0.0)
    addCountLine(addCount(members, props.Picked, props.ViewerId))
    memberBox props.Actions.MemberChanged
    shareRow(props.Shared, props.Actions.ShareChanged)
    memberHost(members, props)
    addButton props.Actions.Add
    orgLeavePick props
  |]

// The org menu's flyout: the scrolling card around the rows.
let orgFlyout(target: Control, rows: Control array) : Popup =
  Popup()
    .Name("OrgMenuFlyout")
    .PlacementTarget(target)
    .Placement(PlacementMode.BottomEdgeAlignedRight)
    .Child(
      Border()
        .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
        .BorderThickness(1.0)
        .CornerRadius(6.0)
        .Padding(6.0)
        .MaxWidth(280.0)
        .Child(ScrollViewer().Content(StackPanel().Children(rows)))
    )

let orgMenu(props: OrgMenuProps) : Control =
  let role = props.Thread |> Observable.map _.Role

  let members =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(6.0)
      .Content("members")
      .IsVisible(role |> Observable.map(Option.contains Maintainer))

  let flyout = orgFlyout(members, orgRows props)

  Panel()
    .Children(
      members.OnClickHandler(fun _ args ->
        args.Handled <- true
        flyout.IsOpen <- not flyout.IsOpen),
      flyout
    )
    .HorizontalAlignment(HorizontalAlignment.Right)
