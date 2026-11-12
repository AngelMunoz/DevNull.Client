module DevNull.Client.Layouts.Editor.ActivityBar

open Avalonia
open Avalonia.Automation
open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Formatting
open DevNull.Client.Icons

type Activity =
  | History
  | Problems
  | Remotes
  | Account

[<Struct>]
type Badges = { Unread: int; Remotes: int }

type Props = {
  Selected: Activity
  Badges: Badges
  OnSelect: Activity -> unit
}

let glyphOf(activity: Activity) =
  match activity with
  | History -> Glyph.Log
  | Problems -> Glyph.Notifications
  | Remotes -> Glyph.Remotes
  | Account -> Glyph.Profile

let nameOf(activity: Activity) =
  match activity with
  | History -> "History"
  | Problems -> "Problems"
  | Remotes -> "Remotes"
  | Account -> "Account"

let badgeOf(props: Props, activity: Activity) =
  match activity with
  | History -> 0
  | Problems -> props.Badges.Unread
  | Remotes -> props.Badges.Remotes
  | Account -> 0

let badge(count: int) : Control array =
  if count > 0 then
    [|
      Border()
        .Classes(Theme.name Theme.TabBadge)
        .HorizontalAlignment(HorizontalAlignment.Right)
        .VerticalAlignment(VerticalAlignment.Top)
        .Child(TextBlock().Text(shortCount count))
    |]
  else
    [||]

let activityButton(props: Props, activity: Activity) : Control =
  let count = badgeOf(props, activity)

  let icon =
    iconAt(glyphOf activity, 18.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Center)

  let children: Control array = [| icon; yield! badge count |]

  let content: Control = Grid().Children(children)

  let button =
    if activity = props.Selected then
      Button().Classes(Theme.name Theme.NavItem, Theme.name Theme.NavItemActive)
    else
      Button().Classes(Theme.name Theme.NavItem)

  let button =
    button
      .Width(40.0)
      .Height(40.0)
      .Padding(Thickness(0.0))
      .Content(content)
      .OnClickHandler(fun _ _ -> props.OnSelect activity)

  AutomationProperties.SetName(button, nameOf activity)
  ToolTip.SetTip(button, nameOf activity)
  button.Name <- $"EditorActivity-{nameOf activity}"
  button

let view(props: Props) : Control =
  let children: Control array = [|
    for activity in [ History; Problems; Remotes; Account ] do
      activityButton(props, activity)
  |]

  Border()
    .Name("EditorActivityBar")
    .Classes(Theme.name Theme.SurfaceBack, Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 0.0, 1.0, 0.0))
    .Width(48.0)
    .Padding(Thickness(4.0, 8.0))
    .Child(
      StackPanel()
        .Orientation(Orientation.Vertical)
        .Spacing(6.0)
        .Children(children)
    )
