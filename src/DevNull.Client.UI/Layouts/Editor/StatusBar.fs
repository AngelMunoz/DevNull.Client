module DevNull.Client.Editor.StatusBar

open System
open Avalonia
open Avalonia.Controls
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Formatting

type Props = {
  Ref: LogKind
  Lane: Lane option
  Key: string
  FetchedAt: DateTimeOffset option
  Now: DateTimeOffset
  NewCount: int
  Band: WidthBand
}

let laneText(lane: Lane option) =
  lane
  |> Option.map(function
    | LaneIndex index -> $"lane {index}"
    | LaneColor color -> $"lane {color}")
  |> Option.defaultValue "lane —"

let shortKey(key: string) =
  if String.IsNullOrEmpty key then
    "—"
  elif key.Length <= 8 then
    key
  else
    key.Substring(0, 4) + "…" + key.Substring(key.Length - 2)

let fetchedText(now: DateTimeOffset, at: DateTimeOffset option) =
  match at with
  | None -> "fetched —"
  | Some moment ->
    let ago = timeAgo(now, moment)

    if ago = "now" then "fetched now" else $"fetched {ago}"

let newText(count: int) = if count > 0 then $"●{count}" else ""

let fullText(props: Props) =
  let parts =
    [
      $"⎇ {LogLabel.ofKind props.Ref}"
      laneText props.Lane
      $"key {shortKey props.Key}"
      fetchedText(props.Now, props.FetchedAt)
    ]
    @ (if props.NewCount > 0 then
         [ newText props.NewCount ]
       else
         [])

  String.concat " · " parts

let trimmedText(props: Props) =
  let count =
    if props.NewCount > 0 then
      $" · {newText props.NewCount}"
    else
      ""

  $"⎇ {LogLabel.ofKind props.Ref}{count}"

let view(props: Props) : Control =
  let text =
    match props.Band with
    | Full
    | Folded -> fullText props
    | Trimmed -> trimmedText props

  Border()
    .Name("EditorStatusBar")
    .Classes(Theme.name Theme.SurfaceBack, Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
    .Padding(Thickness(12.0, 4.0))
    .Child(
      TextBlock()
        .Text(text)
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
    )
