module DevNull.Client.Components.Completion

open System
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Components.Avatar
open DevNull.Client.Components.Palette
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache

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

/// Everything one button row of the flyout carries: where it sits among
/// the rows the keys walk, the name a test finds it by, what a pick
/// answers, and what it shows.
[<Struct>]
type ButtonRow = {
  Index: int
  Name: string
  Pick: unit -> unit
  Content: Control
}

/// One row of the flyout: the caption, a person or organization to
/// mention, the issue card, or the quiet state that stands in for
/// them.
type Row =
  | Caption
  | Person of index: int * user: User
  | Organization of index: int * organization: Organization
  | IssueRow of issue: Issue
  | Quiet of text: string

/// The site's issue card label: `owner/repo#number`, read from the
/// address the answer carries.
let issueLabel(issue: Issue) =
  let parts = issue.Url.Split '/'

  if parts.Length >= 5 then
    $"{parts.[3]}/{parts.[4]}#{issue.Number}"
  else
    $"#{issue.Number}"

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

/// The completion flyout's list.
let view
  (state: IObservable<View>, actions: Actions, images: ImageCacheService)
  : Control =

  let captionRow() =
    TextBlock()
      .Name("CompletionCaption")
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .FontSize(11.0)
      .Margin(10.0, 6.0, 10.0, 2.0)
      .Text("People you follow")

  let buttonRow(row: ButtonRow) : Control =
    let armed =
      state
      |> Observable.map(fun current -> current.Active = row.Index)
      |> Observable.distinctUntilChanged

    let chip =
      SearchParts
        .kbdChip(SearchParts.KbdTone.Armed, "↵")
        .Opacity(armed |> Observable.map(fun on -> if on then 1.0 else 0.0))
        .VerticalAlignment(VerticalAlignment.Center)

    Button()
      .Name(row.Name)
      .Classes(Theme.name Theme.GhostButton)
      .Focusable(false)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        row.Pick())
      .Content(
        DockPanel()
          .LastChildFill(true)
          .Children(chip.DockRight(), row.Content.DockLeft())
      )

  let keyOf(row: Row) =
    match row with
    | Caption -> box "caption"
    | Person(index, user) -> box(index, user)
    | Organization(index, organization) -> box(index, organization)
    | IssueRow issue -> box issue
    | Quiet text -> box text

  let rowsOf(current: View) : Row list =
    let mentions = [
      for index, user in List.indexed current.Users -> Person(index, user)

      for index, organization in List.indexed current.Organizations ->
        let at = List.length current.Users + index

        Organization(at, organization)
    ]

    match current.Issue with
    | Some issue -> [ IssueRow issue ]
    | None -> [
        if current.Caption then
          Caption

        yield! mentions

        if mentions.IsEmpty && not current.Caption then
          Quiet(
            match current.Searching, current.Throttled with
            | true, _ -> "Searching…"
            | _, true -> "error: too many searches, give it a few seconds"
            | _ -> $"fatal: no developers match '{current.Query}'"
          )
      ]

  let render(row: Row) : RenderedRow =
    match row with
    | Caption -> {
        Leading = ValueNone
        Control = captionRow()
        Followed = ValueNone
      }
    | Quiet text -> {
        Leading = ValueNone
        Control = SearchParts.statusLine text
        Followed = ValueNone
      }
    | Person(index, user) ->
      let control =
        buttonRow {
          Index = index
          Name = $"CompletionRow{index}"
          Pick = fun () -> actions.PickPerson user
          Content = personContent(images, user)
        }

      {
        Leading = ValueNone
        Control = control
        Followed = ValueSome control
      }
    | Organization(index, organization) ->
      let control =
        buttonRow {
          Index = index
          Name = $"CompletionRow{index}"
          Pick = fun () -> actions.PickOrganization organization
          Content = orgContent(images, organization)
        }

      {
        Leading = ValueNone
        Control = control
        Followed = ValueSome control
      }
    | IssueRow issue ->
      let control =
        buttonRow {
          Index = 0
          Name = "CompletionIssueRow"
          Pick = fun () -> actions.PickIssue issue
          Content = issueContent issue
        }

      {
        Leading = ValueNone
        Control = control
        Followed = ValueSome control
      }

  Palette.view(
    {
      Name = "CompletionScroller"
      MaxHeight = 320.0
    },
    {
      Rows = state |> Observable.map rowsOf
      KeyOf = keyOf
      Render = render
      Active = state |> Observable.map _.Active
      OnHover = ValueSome actions.Hovered
    }
  )
