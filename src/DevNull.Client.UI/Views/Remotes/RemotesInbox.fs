namespace DevNull.Client.Views

open System
open System.Reactive.Disposables
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.RemotesState
open DevNull.Client.Stores

module Parts =

  // Everything one row's parts read: the dispatch, the model, the
  // image cache, and the signed-in viewer.
  type RowCtx = {
    Dispatch: Dispatch<State.Msg>
    Model: IObservable<State.Model>
    Images: ImageCacheService
    ViewerId: string
  }

  // One partner's avatar.
  let partnerAvatar(images: ImageCacheService, partner: User) : Control =
    Avatar.view {
      Name = partner.Name
      Url = partner.AvatarUrl
      Lane = partner.Lane
      Size = 40.0
      Cache = images
    }

  // An org's avatar: two of its other members, overlapping, in the
  // space of one avatar.
  let orgAvatar
    (images: ImageCacheService, viewerId: string, row: Conversation)
    : Control =
    let others =
      row.Members
      |> Option.defaultValue []
      |> List.filter(fun member' -> member'.User.Id <> viewerId)
      |> List.truncate 2

    let panel = Panel()

    panel.Width <- 40.0
    panel.Height <- 40.0

    let placed =
      others
      |> List.mapi(fun index member' ->
        let corner =
          Avatar.view {
            Name = member'.User.Name
            Url = member'.User.AvatarUrl
            Lane = member'.User.Lane
            Size = 26.0
            Cache = images
          }

        let horizontal, vertical =
          if index = 0 then
            HorizontalAlignment.Left, VerticalAlignment.Top
          else
            HorizontalAlignment.Right, VerticalAlignment.Bottom

        corner.HorizontalAlignment(horizontal).VerticalAlignment(vertical))

    panel.Children [| for avatar in placed -> avatar |] |> ignore

    panel

  // One conversation's title line: the partner's name and handle, or
  // the org's title and member count.
  let titleLine(viewerId: string, row: Conversation) : Control =
    match row.Partner with
    | Some partner ->
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Text(partner.Name)
            .TextTrimming(TextTrimming.CharacterEllipsis),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text("@" + partner.Handle)
            .TextTrimming(TextTrimming.CharacterEllipsis)
        )
    | None ->
      let others =
        row.Members
        |> Option.defaultValue []
        |> List.filter(fun member' -> member'.User.Id <> viewerId)

      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Text(RemotesRows.orgTitle(viewerId, row))
            .TextTrimming(TextTrimming.CharacterEllipsis),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text($"org · {List.length others + 1}")
        )

  // One conversation's preview line: the sender's prefix and the last
  // message's text.
  let excerptLine(excerpt: State.Excerpt) : Control =
    if excerpt.IsEvent then
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text(excerpt.Text)
        .TextTrimming(TextTrimming.CharacterEllipsis)
    else
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Text(excerpt.Prefix + excerpt.Text)
        .TextTrimming(TextTrimming.CharacterEllipsis)

  // One row's right edge: the pin and mute marks, then the ahead pill
  // or the relative time.
  let marksLine(model: IObservable<State.Model>, row: Conversation) : Control =
    let now = model |> Observable.map _.Now

    let pinned =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text("pinned")
        .IsVisible(row.PinnedAt.IsSome)

    let muted =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text("muted")
        .IsVisible(
          now
          |> Observable.map(fun moment ->
            row.MutedUntilAt
            |> Option.map(fun until -> until > moment)
            |> Option.defaultValue false)
        )

    let ahead =
      Border()
        .Classes(Theme.name Theme.AheadPill)
        .Child(
          TextBlock()
            .Text(if row.Ahead > 99 then "99+ ahead" else $"{row.Ahead} ahead")
        )
        .IsVisible(row.Ahead > 0)

    let time =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text(
          match row.LastMessageAt with
          | Some at -> now |> Observable.map(fun moment -> timeAgo(moment, at))
          | None -> now |> Observable.map(fun _ -> "")
        )
        .IsVisible(row.Ahead = 0)

    StackPanel()
      .Spacing(2.0)
      .HorizontalAlignment(HorizontalAlignment.Right)
      .Children(pinned, muted, ahead, time)

  // One fetched request's settle row: merge into the open list, or
  // drop it for good.
  [<Struct>]
  type SettleAsk = {
    Dispatch: Dispatch<State.Msg>
    Partner: User
    Merging: bool
    Label: string
  }

  let settleRow(ask: SettleAsk) : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(8.0, 2.0, 8.0, 2.0)
      .FontSize(13.0)
      .Content(ask.Label)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        ask.Dispatch(State.SettleWanted(ask.Partner, ask.Merging)))

  // One conversation row of the inbox column.
  let conversationRow(ctx: RowCtx, tab: InboxTab, row: Conversation) : Control =
    let active =
      ctx.Model
      |> Observable.map(fun current ->
        current.State.OpenTarget
        |> Option.map ChatTargets.key
        |> Option.contains(ChatTargets.summaryKey row))

    let settle: Control array = [|
      for partner in row.Partner |> Option.toList do
        settleRow {
          Dispatch = ctx.Dispatch
          Partner = partner
          Merging = true
          Label = "merge"
        }

        settleRow {
          Dispatch = ctx.Dispatch
          Partner = partner
          Merging = false
          Label = "drop"
        }
    |]

    let body: Control array = [|
      titleLine(ctx.ViewerId, row)
      excerptLine(RemotesRows.excerptOf(ctx.ViewerId, row))
      yield! settle
    |]

    let avatar: Control =
      match row.Partner with
      | Some partner -> partnerAvatar(ctx.Images, partner)
      | None -> orgAvatar(ctx.Images, ctx.ViewerId, row)

    let content =
      let grid = Grid()

      grid.ColumnDefinitions <- ColumnDefinitions("Auto,*,Auto")

      grid.Children(
        avatar
          .Column(0)
          .Margin(0.0, 0.0, 12.0, 0.0)
          .VerticalAlignment(VerticalAlignment.Center),
        StackPanel()
          .Spacing(2.0)
          .Margin(0.0, 10.0, 0.0, 10.0)
          .Children(body)
          .Column(1),
        marksLine(ctx.Model, row).Column(2).Margin(12.0, 10.0, 12.0, 10.0)
      )

    let button =
      Button()
        .Classes(Theme.name Theme.CommitRow)
        .HorizontalContentAlignment(HorizontalAlignment.Stretch)
        .Content(content)
        .OnClickHandler(fun _ _ ->
          ctx.Dispatch(State.Opened(ChatTargets.targetOf row)))

    let rail =
      Border()
        .Name("SelectedRail")
        .Classes(Theme.name Theme.AccentBack)
        .Width(3.0)
        .CornerRadius(2.0)
        .VerticalAlignment(VerticalAlignment.Stretch)
        .Margin(0.0, 8.0, 0.0, 8.0)
        .IsVisible(active)

    Theme.bindClass(button, Theme.PrimarySoftBack, active) |> ignore

    // The rail owns the row's first dock slot. A sized child left to a
    // panel's default stretch alignment centers, which drew the marker
    // over the row's middle.
    DockPanel().LastChildFill(true).Children(rail.Dock(Dock.Left), button)

  // One candidate row: a person a new remote can start with.
  let candidateRow(ctx: RowCtx, person: RemoteCandidate) : Control =
    let identity =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Text(person.User.Name)
            .TextTrimming(TextTrimming.CharacterEllipsis),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text("@" + person.User.Handle)
            .TextTrimming(TextTrimming.CharacterEllipsis)
        )

    let reason =
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Text(person.Reason |> Option.defaultValue "")
        .IsVisible(person.Reason.IsSome)

    let avatar =
      Avatar.view {
        Name = person.User.Name
        Url = person.User.AvatarUrl
        Lane = person.User.Lane
        Size = 40.0
        Cache = ctx.Images
      }

    let columns = Grid()

    columns.ColumnDefinitions <- ColumnDefinitions("Auto,*")

    Button()
      .Classes(Theme.name Theme.CommitRow)
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .Content(
        columns.Children(
          avatar
            .Column(0)
            .Margin(0.0, 0.0, 12.0, 0.0)
            .VerticalAlignment(VerticalAlignment.Center),
          StackPanel()
            .Spacing(2.0)
            .Margin(0.0, 10.0, 12.0, 10.0)
            .Children(identity, reason)
            .Column(1)
        )
      )
      .OnClickHandler(fun _ _ ->
        ctx.Dispatch(State.Opened(Partner person.User)))

  // The tabs of the inbox, the fetched one only while the account
  // takes requests; the fetched tab carries the waiting count.
  let tabRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let waiting =
      model
      |> Observable.map(fun current ->
        current.State.Requests |> Option.defaultValue 0)

    let waitingPill: Control =
      let pill =
        Border()
          .Classes(Theme.name Theme.TabBadge)
          .IsVisible(waiting |> Observable.map(fun count -> count > 0))
          .Child(
            TextBlock()
              .Text(
                waiting
                |> Observable.map(fun count ->
                  if count > 99 then "99+" else string count)
              )
          )

      let tip =
        TextBlock()
          .Text(waiting |> Observable.map(fun count -> $"{count} waiting"))

      ToolTip.SetTip(pill, tip)
      pill

    let strip =
      PageChrome.rebuildOn(
        model,
        (fun current -> current.RequestsOn),
        fun current ->
          let tabs: TabStrip.Tab list = [
            for tab, label in RemotesRows.tabsOf current.RequestsOn ->
              {
                Label = label
                Active = model |> Observable.map(fun next -> next.Tab = tab)
                Badge =
                  if tab = TabFetched then
                    ValueSome waitingPill
                  else
                    ValueNone
                Choose = fun () -> dispatch(State.TabChosen tab)
              }
          ]

          TabStrip.view tabs
      )

    ContentControl().Content(strip |> _.ToBinding())

  // The search box of the inbox.
  let searchBox
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let txt =
      TextBox()
        .Name("RemotesSearch")
        .PlaceholderText("search")
        .Text(model |> Observable.map _.Query |> _.ToBinding())

    // The quiet window belongs to the box's own attachment: a settled,
    // distinct term asks for its search, and a detach stops the watch a
    // re-attached box starts again.
    let typed = new SerialDisposable()

    txt.AttachedToVisualTree.Add(fun _ ->
      typed.Disposable <-
        txt.TextChanged
        |> Observable.map(fun _ ->
          txt.Text |> Option.ofObj |> Option.defaultValue "")
        |> Observable.filter(String.IsNullOrWhiteSpace >> not)
        |> Observable.distinctUntilChanged
        |> Observable.throttle(TimeSpan.FromMilliseconds(300.))
        |> Observable.subscribe(State.Searched >> dispatch))

    txt.DetachedFromVisualTree.Add(fun _ ->
      typed.Disposable <- Disposable.Empty)

    txt

  // The inbox column's rows: the shared feed, whose riding report acts
  // as the scroll-end probe. The update's guards keep it from asking
  // twice.
  let rows
    (
      ctx: RowCtx,
      list: IObservable<State.InboxRow list>,
      hasMore: IObservable<bool>
    ) : Control =
    (Feed.view {
      Items = list
      KeyOf = RemotesRows.rowKey
      Row =
        (fun item ->
          match item with
          | State.InboxRow.Conversation(tab, row) ->
            conversationRow(ctx, tab, row)
          | State.InboxRow.Candidate person -> candidateRow(ctx, person))
      Footer = ValueNone
      HasMore = hasMore
      ScrollTo = ValueNone
      Ride = false
      Restore = ValueNone
      OnScroll = ValueNone
      OnRide =
        ValueSome(fun ride ->
          if ride.FromEnd < 240.0 then
            ctx.Dispatch State.MoreWanted)
      OnNeedMore = ValueNone
    })
      .Name("RemotesInbox")

  // The quiet centered line the column says when its list says
  // nothing: loading, empty, or nothing found.
  let quietLine(text: IObservable<string>) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .Text(text)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Margin(12.0, 24.0, 12.0, 12.0)
      .TextWrapping(TextWrapping.Wrap)

  // How the inbox column's quiet line reads for its inputs.
  type Line =
    | NothingFound
    | EmptyTab
    | Loading
    | Quiet

  let lineState
    (found: State.SearchFound option, items: Conversation list option)
    : Line =
    match
      found
      |> Option.map(fun result ->
        result.Conversations.IsEmpty && result.Candidates.IsEmpty)
    with
    | Some true -> NothingFound
    | Some false -> Quiet
    | None ->
      match items with
      | Some [] -> EmptyTab
      | None -> Loading
      | Some _ -> Quiet

  let lineWords(tab: InboxTab, line: Line) : string =
    match line with
    | NothingFound -> "nothing found"
    | EmptyTab -> RemotesRows.emptyLine tab
    | Loading -> "loading the inbox…"
    | Quiet -> ""

  // The line the column says over its rows: nothing while rows show,
  // the loading word while the tab's first page is out, the empty
  // word once the list holds nothing, and the nothing-found word
  // while a search's answer holds nothing.
  let lineOf(model: IObservable<State.Model>) : IObservable<string> =
    model
    |> Observable.map(fun current ->
      let list = listTab(current.State, current.Tab)

      lineWords(current.Tab, lineState(current.Found, list.Items)))

  // The org-create picker: a quiet button whose flyout takes an
  // optional 60-char name and the members picked from the search's
  // candidate list. Control-owned flyout, the house pattern.
  let orgPicker
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let candidates =
      model
      |> Observable.map(fun current ->
        current.Found |> Option.map _.Candidates |> Option.defaultValue [])

    let nameBox =
      TextBox()
        .Name("OrgCreateName")
        .PlaceholderText("org name (optional, 60 chars)")
        .MaxLength(60)
        .MinHeight(30.0)
        .FontSize(12.0)
        .MaxWidth(220.0)
        .Text(
          model
          |> Observable.map _.OrgName
          |> Observable.distinctUntilChanged
          |> _.ToBinding()
        )
        .OnTextChangedHandler(fun box _ ->
          box.Text
          |> Option.ofObj
          |> Option.defaultValue ""
          |> State.OrgNameChanged
          |> dispatch)

    let picks = ContentControl()

    picks.Content(
      PageChrome.rebuildOn(
        candidates,
        id,
        fun found ->
          let rows: Control array = [|
            for person in found ->
              Button()
                .Classes(Theme.name Theme.GhostButton)
                .Padding(6.0, 2.0, 6.0, 2.0)
                .FontSize(12.0)
                .HorizontalContentAlignment(HorizontalAlignment.Left)
                .Content($"+ @{person.User.Handle}")
                .OnClickHandler(fun _ args ->
                  args.Handled <- true
                  dispatch(State.OrgMemberChanged person.User.Handle))
          |]

          StackPanel().Children rows
      )
      |> _.ToBinding()
    )
    |> ignore

    let chosen =
      ContentControl()
        .Content(
          PageChrome.rebuildOn(
            model,
            (fun current -> current.OrgMembers),
            fun current ->
              let members = current.OrgMembers

              let picked =
                if members.IsEmpty then
                  "nobody picked yet"
                else
                  let names = String.concat ", " members

                  $"members: {names} ({1 + List.length members}/100)"

              TextBlock()
                .Classes(Theme.name Theme.Mono)
                .FontSize(11.0)
                .Text(picked)
                .TextWrapping(TextWrapping.Wrap)
          )
          |> _.ToBinding()
        )

    let flyout =
      Popup()
        .Child(
          Border()
            .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
            .BorderThickness(1.0)
            .CornerRadius(6.0)
            .Padding(6.0)
            .MaxWidth(260.0)
            .Child(
              StackPanel()
                .Spacing(4.0)
                .Children(
                  nameBox,
                  TextBlock()
                    .Classes(Theme.name Theme.Mono)
                    .FontSize(11.0)
                    .Text("search people first; picks come from the results"),
                  picks,
                  chosen,
                  Button()
                    .Classes(Theme.name Theme.AccentButton)
                    .Padding(8.0, 2.0, 8.0, 2.0)
                    .FontSize(12.0)
                    .Content("create org")
                    .IsEnabled(
                      model
                      |> Observable.map(fun current ->
                        not current.OrgMembers.IsEmpty)
                    )
                    .OnClickHandler(fun _ args ->
                      args.Handled <- true
                      dispatch State.OrgCreateWanted)
                )
            )
        )

    Panel()
      .Children(
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0, 2.0, 6.0, 2.0)
          .FontSize(12.0)
          .Content("new org")
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            flyout.IsOpen <- not flyout.IsOpen),
        flyout
      )

  // The inbox's header row: the search box with the org picker beside
  // it, and the tabs under them.
  let inboxHead
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    DockPanel()
      .LastChildFill(true)
      .Children(
        orgPicker(model, dispatch).Dock(Dock.Right).Margin(0.0, 12.0, 12.0, 8.0),
        searchBox(model, dispatch).Margin(12.0, 12.0, 8.0, 8.0)
      )

  let inbox
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let viewerId = props.Session.Session.User.Id

    let ctx: RowCtx = {
      Dispatch = dispatch
      Model = model
      Images = props.Env.ImageCache
      ViewerId = viewerId
    }

    let list = model |> Observable.map RemotesRows.rowsOf

    let hasMore =
      model
      |> Observable.map(fun current ->
        let currentList = listTab(current.State, current.Tab)

        current.Found.IsNone && currentList.HasMore)

    let column =
      let grid = Grid()

      grid.RowDefinitions <- RowDefinitions("Auto,Auto,*")

      grid.Children(
        inboxHead(model, dispatch).Row(0),
        tabRow(model, dispatch).Row(1),
        rows(ctx, list, hasMore).Row(2)
      )

    let line = quietLine(lineOf model).VerticalAlignment(VerticalAlignment.Top)

    // The quiet line sits over the rows: the empty and loading words
    // own the column while no row exists to cover them.
    Panel().Children(column, line)

  let noThread() : Control =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .Text("no conversation checked out")
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Center)
      .TextWrapping(TextWrapping.Wrap)
