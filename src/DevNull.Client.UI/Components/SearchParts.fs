module DevNull.Client.Components.SearchParts

open System
open System.Text.RegularExpressions
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Documents
open Avalonia.Controls.Shapes
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.Components.Avatar
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Icons
open DevNull.Client.ImageCache

module Intent =

  type Reading =
    | Developer
    | Either
    | Topic

  // GitHub usernames run long, but people rarely type more than this
  // before it is clearly a word.
  let longestLikelyHandle = 12

  let handleCharacters = Regex("^[a-z0-9-]+$", RegexOptions.IgnoreCase)

  let separators = Regex("\s")

  // True when the words read as a person's name: two or three of
  // them, each starting a capital.
  let looksLikeFullName(text: string) =
    let words = separators.Split text

    let capitalized = Regex("^\\p{Lu}[\\p{L}'-]*$")

    words.Length >= 2
    && words.Length <= 3
    && Array.forall (fun (word: string) -> capitalized.IsMatch word) words

  // A lowercase word made only of letters, long enough to be a topic
  // ("caching", "sharding"), is as likely a subject as a username.
  // Digits, dashes, or capitals tip it towards a handle.
  let dictionaryShaped(word: string) = Regex("^[a-z]{6,}$").IsMatch word

  let looksLikeHandle(word: string) =
    handleCharacters.IsMatch word
    && String.length word <= longestLikelyHandle
    && not(dictionaryShaped word)

  let ofTerm(term: string) =
    let trimmed = term.Trim()

    if trimmed.StartsWith("@", StringComparison.Ordinal) then
      Developer
    elif looksLikeFullName trimmed then
      Either
    elif separators.IsMatch trimmed then
      Topic
    elif looksLikeHandle trimmed then
      Developer
    else
      Either

  let inline searchesCommits(reading: Reading) = reading <> Developer

  let inline awaitsDeveloperName(term: string) = term.Trim() = "@"

let suggestions: string list = [
  "shipping a side project"
  "debugging in production"
  "testing strategies that actually work"
  "learning a new programming language"
  "speeding up a slow app"
  "working with AI coding tools"
]

[<Struct>]
type RowCtx = {
  Images: ImageCacheService
  Flyout: Source voption
  Now: DateTimeOffset
}

let heading(text: string) : Control =
  TextBlock()
    .Classes(Theme.name Theme.Mono)
    .FontSize(12.0)
    .Margin(12.0, 8.0, 12.0, 4.0)
    .Text(text)

// The person's avatar, with the hover card when the host gave a
// source.
let avatarOf(ctx: RowCtx, user: User, size: float) : Control =
  // Avalonia centers a sized control under Stretch, so the avatar
  // pins to the row's left lane like the organization avatars.
  let plain =
    (Avatar.view {
      Name = user.Name
      Url = user.AvatarUrl
      Lane = user.Lane
      Size = size
      Cache = ctx.Images
    })
      .HorizontalAlignment(HorizontalAlignment.Left)

  match ctx.Flyout with
  | ValueSome source -> UserFlyout.attach(source, user, plain)
  | ValueNone -> plain

// The lane dot as an inline of the line around it, the six-dip gap
// before the handle the site's lane handle carries. A side-by-side
// panel measures its children without bound and never trims; inline,
// the line ellipsizes as one text.
let laneInline(lane: Lane option) : Inline =
  let dot = Ellipse().Width(6.0).Height(6.0).Margin(0.0, 0.0, 6.0, 0.0)

  Theme.laneFill(dot, lane)

  InlineUIContainer(Child = dot, BaselineAlignment = BaselineAlignment.Center)

let peopleIdentity(ctx: RowCtx, user: User) : Control =
  let identity =
    let block =
      TextBlock()
        .Classes(Theme.name Theme.Subtitle)
        .VerticalAlignment(VerticalAlignment.Center)
        .TextTrimming(TextTrimming.CharacterEllipsis)

    let name = Run user.Name

    let handle = Run("@" + user.Handle)

    handle.FontFamily <- Theme.Fonts.mono
    handle.FontSize <- 13.0

    let inlines = InlineCollection()
    inlines.Add name
    inlines.Add(Run " ")
    inlines.Add(laneInline user.Lane)
    inlines.Add handle

    block.Inlines <- inlines
    block

  let bio =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .FontSize(13.0)
      .Text(user.Bio |> Option.defaultValue "")
      .TextTrimming(TextTrimming.CharacterEllipsis)
      .IsVisible(
        user.Bio
        |> Option.map(fun bio -> bio <> "")
        |> Option.defaultValue false
      )

  StackPanel().Spacing(2.0).Children(avatarOf(ctx, user, 36.0), identity, bio)

// One small label chip, the site's "Follows you" and "Muted" marks.
let followMark() : Control =
  Border()
    .Classes(Theme.name Theme.Chip)
    .CornerRadius(999.0)
    .Child(
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .FontSize(11.0)
        .Text("Following")
    )

let orgIdentity(ctx: RowCtx, organization: Organization) : Control =
  let identity =
    let block =
      TextBlock()
        .Classes(Theme.name Theme.Subtitle)
        .VerticalAlignment(VerticalAlignment.Center)
        .TextTrimming(TextTrimming.CharacterEllipsis)

    let name = Run organization.Name

    let handle = Run("@" + organization.Handle)

    handle.FontFamily <- Theme.Fonts.mono
    handle.FontSize <- 13.0

    let inlines = InlineCollection()
    inlines.Add name
    inlines.Add(Run " ")
    inlines.Add(laneInline organization.Lane)
    inlines.Add handle

    block.Inlines <- inlines
    block

  let bio =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .FontSize(13.0)
      .Text(organization.Bio |> Option.defaultValue "")
      .TextTrimming(TextTrimming.CharacterEllipsis)
      .IsVisible(
        organization.Bio
        |> Option.map(fun bio -> bio <> "")
        |> Option.defaultValue false
      )

  // Avalonia centers a sized control under Stretch, so the avatar pins
  // to the row's left lane, the column the name line starts at.
  let avatar =
    (Avatar.view {
      Name = organization.Name
      Url = organization.AvatarUrl
      Lane = organization.Lane
      Size = 36.0
      Cache = ctx.Images
    })
      .HorizontalAlignment(HorizontalAlignment.Left)

  StackPanel()
    .Spacing(2.0)
    .Children(
      avatar,
      identity,
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(followMark().IsVisible(organization.IsFollowed), bio)
    )

let commitIdentity(ctx: RowCtx, commit: Commit) : Control =
  let meta =
    let block =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .FontSize(13.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .TextTrimming(TextTrimming.CharacterEllipsis)

    let inlines = InlineCollection()
    inlines.Add(laneInline commit.Author.Lane)
    inlines.Add(Run("@" + commit.Author.Handle))
    inlines.Add(Run " · ")
    inlines.Add(Run(Formatting.timeAgo(ctx.Now, commit.CreatedAt)))

    block.Inlines <- inlines
    block

  StackPanel()
    .Spacing(2.0)
    .Children(
      avatarOf(ctx, commit.Author, 36.0),
      meta,
      TextBlock()
        .FontSize(13.0)
        .TextWrapping(TextWrapping.Wrap)
        .MaxLines(2)
        .Text(commit.Body)
    )

let termIdentity(term: string) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(6.0)
    .Children(
      TextBlock()
        .FontSize(13.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Text(term)
        .TextTrimming(TextTrimming.CharacterEllipsis),
      Icons.iconAt(Glyph.ArrowRight, 16.0)
    )

let seeAllIdentity() : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(6.0)
    .Children(
      TextBlock()
        .Classes(Theme.name Theme.FgAccent)
        .Text("See all matching commits"),
      Icons.iconAt(Glyph.ArrowRight, 16.0)
    )

[<Struct>]
type KbdTone =
  | Outline
  | Armed

let kbdChip(tone: KbdTone, key: string) : Control =
  let label = TextBlock().Classes(Theme.name Theme.Mono).Text(key)

  match tone with
  | Outline ->
    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(4.0)
      .Padding(6.0, 1.0, 6.0, 1.0)
      .Child(label.FontSize(12.0))
  | Armed ->
    Border().Classes(Theme.name Theme.SurfaceBack).Child(label.FontSize(11.0))

let statusBlock() : TextBlock =
  TextBlock()
    .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
    .FontSize(13.0)
    .Margin(20.0, 14.0, 20.0, 14.0)
    .TextTrimming(TextTrimming.CharacterEllipsis)

let statusLine(text: string) : Control = statusBlock().Text(text)

let statusLineOf(text: IObservable<string>) : Control =
  let line = text |> Slices.gate id |> Slices.share

  statusBlock()
    .Text(line |> _.ToBinding())
    .IsVisible(line |> Observable.map(String.IsNullOrWhiteSpace >> not))
