module DevNull.Client.Components.Completion

open System
open System.Reactive.Subjects
open Avalonia
open Avalonia.Controls
open Avalonia.Threading
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Components.Avatar
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache
open DevNull.Client.Theme

[<Struct>]
type View = {
  Query: string
  Caption: bool
  Users: User list
  Organizations: Organization list
  Issue: Issue option
  Searching: bool
  Throttled: bool
  Active: int
}

[<Struct>]
type Actions = {
  PickPerson: User -> unit
  PickOrganization: Organization -> unit
  PickIssue: Issue -> unit
  Hovered: int -> unit
}

/// The flyout's live surface: the scroller the popup shows, the row
/// controls the keys walk, and the release of the row stream.
type Surface = {
  Control: Control
  Rows: IObservable<Control array>
  Dispose: unit -> unit
}

/// Everything one row of the flyout carries: where it sits among the
/// rows the keys walk, the name a test finds it by, what a pick
/// answers, and what it shows.
[<Struct>]
type Row = {
  Index: int
  Name: string
  Pick: unit -> unit
  Content: Control
}

/// The site's issue card label: `owner/repo#number`, read from the
/// address the answer carries.
let issueLabel(issue: Issue) =
  let parts = issue.Url.Split '/'

  if parts.Length >= 5 then
    $"{parts[3]}/{parts[4]}#{issue.Number}"
  else
    $"#{issue.Number}"

/// The quiet mono status line a state asks for, the site's own words.
let statusLine(text: string) : Control =
  TextBlock()
    .Name("CompletionStatus")
    .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
    .FontSize(13.0)
    .Margin(10.0, 8.0, 10.0, 8.0)
    .Text(text)
    .TextTrimming(TextTrimming.CharacterEllipsis)

/// One row: the site's list entry, armed with the row the keys hold.
/// The row never takes the focus, so a pick keeps the caret where the
/// completion lands it.
let rowOf(state: IObservable<View>, actions: Actions, row: Row) : Control =
  let armed =
    state
    |> Observable.map(fun current -> current.Active = row.Index)
    |> Observable.distinctUntilChanged

  let chip =
    Border()
      .Classes(Theme.name Theme.SurfaceBack)
      .Opacity(armed |> Observable.map(fun on -> if on then 1.0 else 0.0))
      .VerticalAlignment(VerticalAlignment.Center)
      .Child(
        TextBlock().Classes(Theme.name Theme.Mono).FontSize(11.0).Text("↵")
      )

  Button()
    .Name(row.Name)
    .Classes(Theme.name Theme.GhostButton)
    .Focusable(false)
    .HorizontalAlignment(HorizontalAlignment.Stretch)
    .OnClickHandler(fun _ args ->
      args.Handled <- true
      row.Pick())
    .OnPointerEnteredHandler(fun _ _ -> actions.Hovered row.Index)
    .Content(
      DockPanel()
        .LastChildFill(true)
        .Children(chip.DockRight(), row.Content.DockLeft())
    )

/// One person to mention: the avatar on their lane, the name, and the
/// handle on the quiet mono line.
let personContent(images: ImageCacheService, user: User) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(8.0)
    .Children(
      Avatar.view {
        Name = user.Name
        Url = user.AvatarUrl
        Lane = user.Lane
        Size = 28.0
        Cache = images
      },
      StackPanel()
        .VerticalAlignment(VerticalAlignment.Center)
        .Spacing(1.0)
        .Children(
          TextBlock()
            .FontSize(13.0)
            .FontWeight(FontWeight.SemiBold)
            .Text(user.Name)
            .TextTrimming(TextTrimming.CharacterEllipsis),
          TextBlock()
            .Classes(Theme.name Theme.Mono, Theme.name Theme.Muted)
            .FontSize(12.0)
            .Text("@" + user.Handle)
            .TextTrimming(TextTrimming.CharacterEllipsis)
        )
    )

/// One organization to mention: the avatar, the name, and the handle
/// on the quiet mono line.
let orgContent
  (images: ImageCacheService, organization: Organization)
  : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(8.0)
    .Children(
      Avatar.view {
        Name = organization.Name
        Url = organization.AvatarUrl
        Lane = None
        Size = 28.0
        Cache = images
      },
      StackPanel()
        .VerticalAlignment(VerticalAlignment.Center)
        .Spacing(1.0)
        .Children(
          TextBlock()
            .FontSize(13.0)
            .FontWeight(FontWeight.SemiBold)
            .Text(organization.Name)
            .TextTrimming(TextTrimming.CharacterEllipsis),
          TextBlock()
            .Classes(Theme.name Theme.Mono, Theme.name Theme.Muted)
            .FontSize(12.0)
            .Text("@" + organization.Handle)
            .TextTrimming(TextTrimming.CharacterEllipsis)
        )
    )

/// The issue card: its title over the repository label.
let issueContent(issue: Issue) : Control =
  StackPanel()
    .Spacing(1.0)
    .Children(
      TextBlock()
        .FontSize(13.0)
        .FontWeight(FontWeight.SemiBold)
        .Text(issue.Title)
        .TextTrimming(TextTrimming.CharacterEllipsis),
      TextBlock()
        .Classes(Theme.name Theme.Mono, Theme.name Theme.Muted)
        .FontSize(12.0)
        .Text(issueLabel issue)
        .TextTrimming(TextTrimming.CharacterEllipsis)
    )

/// What the list shows: the rows, the quiet state that stands in for
/// them, and the query the fatal line quotes.
[<Struct>]
type Shape = {
  Caption: bool
  Users: User list
  Organizations: Organization list
  Issue: Issue option
  Searching: bool
  Throttled: bool
  Query: string
}

/// The completion flyout's list.
let view
  (state: IObservable<View>, actions: Actions, images: ImageCacheService)
  : Surface =

  // What rows exist and which quiet state stands in for them; the
  // armed row rides its own observable, so a key move repaints one
  // chip instead of rebuilding the list.
  let shape =
    state
    |> Observable.map(fun current -> {
      Caption = current.Caption
      Users = current.Users
      Organizations = current.Organizations
      Issue = current.Issue
      Searching = current.Searching
      Throttled = current.Throttled
      Query = current.Query
    })
    |> Observable.distinctUntilChanged

  let rows = new BehaviorSubject<Control array>(Array.empty)

  let build(shape: Shape) =

    let captionRow() =
      TextBlock()
        .Name("CompletionCaption")
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
        .FontSize(11.0)
        .Margin(10.0, 6.0, 10.0, 2.0)
        .Text("People you follow")

    let mentionRows = [
      for index, user in List.indexed shape.Users ->
        {
          Index = index
          Name = $"CompletionRow{index}"
          Pick = fun () -> actions.PickPerson user
          Content = personContent(images, user)
        }

      for index, organization in List.indexed shape.Organizations ->
        let at = List.length shape.Users + index

        {
          Index = at
          Name = $"CompletionRow{at}"
          Pick = fun () -> actions.PickOrganization organization
          Content = orgContent(images, organization)
        }
    ]

    let quiet() =
      if shape.Searching then
        statusLine "Searching…"
      elif shape.Throttled then
        statusLine "error: too many searches, give it a few seconds"
      else
        statusLine $"fatal: no developers match '{shape.Query}'"

    let rowControls: Control array =
      match shape.Issue with
      | Some found -> [|
          rowOf(
            state,
            actions,
            {
              Index = 0
              Name = "CompletionIssueRow"
              Pick = fun () -> actions.PickIssue found
              Content = issueContent found
            }
          )
        |]
      | None -> [|
          for row in mentionRows do
            rowOf(state, actions, row)
        |]

    let children: Control array =
      match shape.Issue with
      | Some _ -> rowControls
      | None ->
          // The site's own rows: the quiet caption, the people and
          // organizations, or the state that stands in for them.
          [|
            if shape.Caption then
              captionRow()

            yield! rowControls

            if mentionRows.IsEmpty && not shape.Caption then
              quiet()
          |]

    let panel = StackPanel().Children(children)

    // The rows attach when the content control takes the panel. The
    // post runs after that, so the armed row can find its scroller.
    Dispatcher.UIThread.Post(fun () -> rows.OnNext rowControls)

    panel

  let scroller =
    ScrollViewer()
      .Name("CompletionScroller")
      .MaxHeight(320.0)
      .Content(
        ContentControl().Content((shape |> Observable.map build).ToBinding())
      )

  {
    Control = scroller
    Rows = rows
    Dispose = fun () -> rows.OnCompleted()
  }
