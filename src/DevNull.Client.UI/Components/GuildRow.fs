module DevNull.Client.Components.GuildRow

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Components.AsyncImage
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Theme

[<Struct>]
type Props = {
  Guild: Guild
  Cache: ImageCacheService
  OnOpen: Guild -> unit
}

// One chip of the state line: the word inside the site's chip frame.
// The joined chip wears the primary wash; the rest stay quiet.
let chip(word: string, filled: bool) : Control =
  let border =
    Border()
      .Classes(Theme.name Theme.Chip)
      .CornerRadius(999.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Child(
        TextBlock().Classes(Theme.name Theme.Muted).FontSize(11.0).Text(word)
      )

  if filled then
    border.Classes(Theme.name Theme.PrimarySoftBack) :> Control
  else
    border :> Control

let stateChips(guild: Guild) : Control array = [|
  if guild.IsJoined then
    chip("Joined", true)
  if guild.IsMuted then
    chip("Muted", false)
  if guild.IsPaused then
    chip("Paused", false)
|]

module Parts =

  // The avatar cell: the icon decoded small when the guild carries
  // one, the emoji on a disc otherwise.
  let avatar(cache: ImageCacheService, guild: Guild) : Control =
    match guild.IconUrl with
    | Some url ->
      let image =
        AsyncImage.view {
          Cache = cache
          Url = url
          DecodeWidth = 80
          DecodeHeight = ValueNone
          Stretch = Stretch.UniformToFill
          ToolTip = ValueSome guild.Name
        }

      Border()
        .Width(36.0)
        .Height(36.0)
        .CornerRadius(CornerRadius 18.0)
        .ClipToBounds(true)
        .Child(image)
    | None ->
      Border()
        .Classes(Theme.name Theme.SurfaceBack)
        .Width(36.0)
        .Height(36.0)
        .CornerRadius(CornerRadius 18.0)
        .Child(
          TextBlock()
            .FontSize(18.0)
            .VerticalAlignment(VerticalAlignment.Center)
            .HorizontalAlignment(HorizontalAlignment.Center)
            .Text(guild.Emoji)
        )

  // The member count on the quiet mono line.
  let members(count: int) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .FontSize(12.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Text($"{shortCount count} members")

let view(props: Props) : Control =
  let guild = props.Guild

  let description: Control =
    match guild.Description with
    | Some text ->
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .FontSize(13.0)
        .TextWrapping(TextWrapping.Wrap)
        .MaxLines(2)
        .TextTrimming(TextTrimming.CharacterEllipsis)
        .Text(text)
      :> Control
    | None -> Panel()

  let state =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(6.0)
      .Children(
        [| yield! stateChips guild; yield Parts.members guild.MemberCount |]
      )

  let card =
    Button()
      .Name("GuildCard")
      .Classes(Theme.name Theme.GhostButton)
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .Padding(14.0)
      .Content(
        StackPanel()
          .Spacing(6.0)
          .Children(
            Parts.avatar(props.Cache, guild),
            TextBlock()
              .Classes(Theme.name Theme.Subtitle)
              .TextTrimming(TextTrimming.CharacterEllipsis)
              .Text(guild.Name),
            TextBlock()
              .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
              .FontSize(12.0)
              .Text("#" + guild.Handle),
            description,
            state
          )
      )
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        props.OnOpen guild)

  Border()
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(1.0))
    .CornerRadius(CornerRadius 10.0)
    .Margin(0.0, 0.0, 10.0, 10.0)
    .Child(card)
