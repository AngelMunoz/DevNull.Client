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
open DevNull.Client
open DevNull.Client.Components.Avatar
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Theme

/// What the term rules say about a search.
module Intent =

  /// The three readings a term can have.
  type Reading =
    | Developer
    | Either
    | Topic

  // GitHub usernames run long, but people rarely type more than this
  // before it is clearly a word.
  let longestLikelyHandle = 12

  let handleCharacters = Regex("^[a-z0-9-]+$", RegexOptions.IgnoreCase)

  let separators = Regex("\s")

  /// True when the words read as a person's name: two or three of
  /// them, each starting a capital.
  let looksLikeFullName(text: string) =
    let words = separators.Split text

    let capitalized = Regex("^\\p{Lu}[\\p{L}'-]*$")

    words.Length >= 2
    && words.Length <= 3
    && Array.forall (fun (word: string) -> capitalized.IsMatch word) words

  /// A lowercase word made only of letters, long enough to be a topic
  /// ("caching", "sharding"), is as likely a subject as a username.
  /// Digits, dashes, or capitals tip it towards a handle.
  let dictionaryShaped(word: string) = Regex("^[a-z]{6,}$").IsMatch word

  let looksLikeHandle(word: string) =
    handleCharacters.IsMatch word
    && String.length word <= longestLikelyHandle
    && not(dictionaryShaped word)

  /// Reads a term as the site's dialog does.
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

  /// True when the reading reaches the commit channel.
  let searchesCommits(reading: Reading) = reading <> Developer

  /// True when a lone `@` announces a developer search but has nothing
  /// to search for yet.
  let awaitsDeveloperName(term: string) = term.Trim() = "@"

/// The site's canned suggestions.
let suggestions: string list = [
  "shipping a side project"
  "debugging in production"
  "testing strategies that actually work"
  "learning a new programming language"
  "speeding up a slow app"
  "working with AI coding tools"
]

/// Everything the shared search rows read.
[<Struct>]
type RowCtx = {
  Images: ImageCacheService
  Flyout: Source voption
  Now: DateTimeOffset
}

/// One section heading, the site's mono label.
let heading(text: string) : Control =
  TextBlock()
    .Classes(Theme.name Theme.Mono)
    .FontSize(12.0)
    .Margin(12.0, 8.0, 12.0, 4.0)
    .Text(text)

/// The person's avatar, with the hover card when the host gave a
/// source.
let avatarOf(ctx: RowCtx, user: User, size: float) : Control =
  let plain =
    Avatar.view {
      Name = user.Name
      Url = user.AvatarUrl
      Lane = user.Lane
      Size = size
      Cache = ctx.Images
    }

  match ctx.Flyout with
  | ValueSome source -> UserFlyout.attach(source, user, plain)
  | ValueNone -> plain

/// The lane dot as an inline of the line around it. A side-by-side
/// panel measures its children without bound and never trims; inline,
/// the line ellipsizes as one text.
let laneInline(user: User) : Inline =
  let dot = Ellipse().Width(6.0).Height(6.0)

  Theme.laneFill(dot, user.Lane)

  InlineUIContainer(Child = dot, BaselineAlignment = BaselineAlignment.Center)

/// One person as a row's content: the avatar on its lane, the name,
/// the handle with its lane dot, and the bio under them.
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
    inlines.Add(laneInline user)
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

/// One commit as a row's content: the avatar, the handle with its lane
/// dot beside its age, and two lines of the body.
let commitIdentity(ctx: RowCtx, commit: Commit) : Control =
  let meta =
    let block =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .FontSize(13.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .TextTrimming(TextTrimming.CharacterEllipsis)

    let inlines = InlineCollection()
    inlines.Add(laneInline commit.Author)
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

/// One term as a row's content: the words and the arrow that follows
/// them on hover.
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

/// The see-all row's content: the accent words and their arrow.
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

/// One status line, the site's quiet mono sentence.
let statusLine(text: string) : Control =
  TextBlock()
    .Classes(Theme.name Theme.Mono)
    .FontSize(13.0)
    .Margin(20.0, 14.0, 20.0, 14.0)
    .Text(text)
    .TextTrimming(TextTrimming.CharacterEllipsis)
