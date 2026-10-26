module DevNull.Client.Theme

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Chrome
open Avalonia.Controls.Documents
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Markup.Xaml.MarkupExtensions
open Avalonia.Media
open Avalonia.Styling
open NXUI.Extensions
open DevNull.Client.Core.ApiTypes
open DevNull.Client.SettingsStore

module Keys =

  let background = "AppBackground"
  let surface = "AppSurface"
  let card = "AppCard"
  let border = "AppBorder"
  let text = "AppText"
  let muted = "AppMuted"
  let accent = "AppAccent"
  let accentHover = "AppAccentHover"
  let accentPressed = "AppAccentPressed"
  let onAccent = "AppOnAccent"
  let danger = "AppDanger"
  let dangerText = "AppDangerText"
  let success = "AppSuccess"
  let popover = "AppPopover"
  let rail = "AppRail"

  // The input fill rides the rail token until a surface of its own is
  // needed.
  let input = rail
  let primarySoft = "AppPrimarySoft"
  let laneFallback = "AppLaneFallback"

  let lane(index: int) = $"AppLane{abs index % 6}"

module Fonts =

  let sans = FontFamily("avares://DevNull.Client.UI/Assets/Fonts#Geist")

  let mono = FontFamily("avares://DevNull.Client.UI/Assets/Fonts#Geist Mono")

let solid(variant: ThemeVariant, key: string) : IBrush =
  match Application.Current with
  | null -> Brushes.Transparent
  | app ->
    match app.TryFindResource(key, variant) with
    | true, (:? IBrush as brush) -> brush
    | _ -> Brushes.Transparent

module Brushes =

  let themed(key: string) : IObservable<IBrush> =
    match Application.Current with
    | null ->
      System.Reactive.Linq.Observable.Return(Brushes.Transparent :> IBrush)
    | app ->
      app.GetResourceObservable key
      |> Observable.map(fun value ->
        match value with
        | :? IBrush as brush -> brush
        | _ -> Brushes.Transparent)

  let background = themed Keys.background
  let surface = themed Keys.surface
  let card = themed Keys.card
  let border = themed Keys.border
  let text = themed Keys.text
  let muted = themed Keys.muted
  let accent = themed Keys.accent
  let accentHover = themed Keys.accentHover
  let accentPressed = themed Keys.accentPressed
  let onAccent = themed Keys.onAccent
  let danger = themed Keys.danger
  let success = themed Keys.success
  let popover = themed Keys.popover
  let rail = themed Keys.rail
  let input = themed Keys.input
  let primarySoft = themed Keys.primarySoft
  let laneFallback = themed Keys.laneFallback

  let transparent: IObservable<IBrush> =
    System.Reactive.Linq.Observable.Return(Brushes.Transparent :> IBrush)

  let lane(index: int) = themed(Keys.lane index)

  /// One brush that never changes, as an observable.
  let constant(brush: IBrush) : IObservable<IBrush> =
    System.Reactive.Linq.Observable.Return brush

  /// The brush of one lane value: a palette index, a user hex, or the
  /// neutral fallback when the user carries no lane.
  let laneOf(lane: Lane option) : IObservable<IBrush> =
    match lane with
    | Some(LaneIndex index) -> themed(Keys.lane index)
    | Some(LaneColor hex) ->
      constant(SolidColorBrush(Color.Parse hex) :> IBrush)
    | None -> laneFallback

  /// One solid brush from a hex color, for user-painted lanes.
  let solid(hex: string) : IBrush =
    SolidColorBrush(Color.Parse hex) :> IBrush

type StyleClass =
  | NavItem
  | NavItemActive
  | NavCommit
  | RailItem
  | Card
  | Title
  | Headline
  | Subtitle
  | Muted
  | StatNum
  | AccentButton
  | GhostButton
  | DangerButton
  | NavButton
  | CodeBlock
  | Chip
  | PollOption
  | PollRemove
  | FilterPill
  | FilterPillActive
  | TabButton
  | TabButtonActive
  | CommitRow
  | Divider
  | ChromeTitle
  | CaptionButton
  | Mono
  | FgAccent
  | FgMuted
  | FgDanger
  | FgOnAccent
  | SurfaceBack
  | PrimarySoftBack
  | AccentBack
  | CardBack
  | RailBack
  | Hairline
  | AccentEdge
  | TextEdge
  | CardFill
  | CardEdge
  | RailEdge
  | MutedEdge
  | AheadPill
  | DayMarker
  | HeadRule
  | ReactionChip
  | ReactionChipMine
  | BubbleShadow

/// The height of the drag band the shell lays over the drawn window
/// chrome.
let bandHeight = 32.0

/// The insets every page scrolls under: the page gutter.
let pagePadding = Thickness(24.0, 12.0, 24.0, 0.0)

/// The inset a pinned header takes: the band caption row floats above
/// the client, so the header clears it.
let pinnedPadding = Thickness(24.0, bandHeight + 8.0, 24.0, 0.0)

let name class' =
  match class' with
  | NavItem -> "nav-item"
  | NavItemActive -> "nav-item-active"
  | NavCommit -> "nav-commit"
  | RailItem -> "rail-item"
  | Card -> "card"
  | Title -> "title"
  | Headline -> "headline"
  | Subtitle -> "subtitle"
  | Muted -> "muted"
  | StatNum -> "stat-num"
  | AccentButton -> "accent-button"
  | GhostButton -> "ghost-button"
  | DangerButton -> "danger-button"
  | NavButton -> "nav-button"
  | CodeBlock -> "code-block"
  | Chip -> "chip"
  | PollOption -> "poll-option"
  | PollRemove -> "poll-remove"
  | FilterPill -> "filter-pill"
  | FilterPillActive -> "filter-pill-active"
  | TabButton -> "tab-button"
  | TabButtonActive -> "tab-button-active"
  | CommitRow -> "commit-row"
  | Divider -> "divider"
  | ChromeTitle -> "chrome-title"
  | CaptionButton -> "caption-button"
  | Mono -> "mono"
  | FgAccent -> "fg-accent"
  | FgMuted -> "fg-muted"
  | FgDanger -> "fg-danger"
  | FgOnAccent -> "fg-on-accent"
  | SurfaceBack -> "surface-back"
  | PrimarySoftBack -> "primary-soft-back"
  | AccentBack -> "accent-back"
  | CardBack -> "card-back"
  | RailBack -> "rail-back"
  | Hairline -> "hairline"
  | AccentEdge -> "accent-edge"
  | TextEdge -> "text-edge"
  | CardFill -> "card-fill"
  | CardEdge -> "card-edge"
  | RailEdge -> "rail-edge"
  | MutedEdge -> "muted-edge"
  | AheadPill -> "ahead-pill"
  | DayMarker -> "day-marker"
  | HeadRule -> "head-rule"
  | ReactionChip -> "reaction-chip"
  | ReactionChipMine -> "reaction-chip-mine"
  | BubbleShadow -> "bubble-shadow"

/// Moves one class on and off a control as the observable flips. The
/// class list is the only thing the subscription touches.
let bindClass
  (control: Control, class': StyleClass, active: IObservable<bool>)
  : unit =
  active
  |> Observable.add(fun flag ->
    if flag then
      control.Classes.Add(name class') |> ignore
    else
      control.Classes.Remove(name class') |> ignore)

/// The lane slot one control paints. Each slot names the property the
/// lane class styles, so no control ever inherits a second lane brush
/// it never asked for.
type LaneRole =
  | LaneFill
  | LaneStroke
  | LaneBorder
  | LaneBack
  | LaneFg

/// The lane class of one slot: the palette index, or the neutral
/// fallback when the user carries no lane.
let laneClass(role: LaneRole, lane: Lane option) : string =
  let slot =
    match role with
    | LaneFill -> "lane-fill"
    | LaneStroke -> "lane-stroke"
    | LaneBorder -> "lane-border"
    | LaneBack -> "lane-back"
    | LaneFg -> "lane-fg"

  let key =
    match lane with
    | Some(LaneIndex index) -> string index
    | _ -> "fallback"

  $"{slot}-{key}"

/// Paints one shape's lane fill: the palette class for an index, the
/// constant brush for a user hex, the fallback class for no lane.
let laneFill(shape: Shapes.Shape, lane: Lane option) : unit =
  match lane with
  | Some(LaneColor hex) -> shape.Fill(Brushes.solid hex) |> ignore
  | _ -> shape.Classes(laneClass(LaneFill, lane)) |> ignore

/// Paints one shape's lane stroke, the same three ways.
let laneStroke(shape: Shapes.Shape, lane: Lane option) : unit =
  match lane with
  | Some(LaneColor hex) -> shape.Stroke(Brushes.solid hex) |> ignore
  | _ -> shape.Classes(laneClass(LaneStroke, lane)) |> ignore

/// Paints one border's lane ring.
let laneBorder(border: Border, lane: Lane option) : unit =
  match lane with
  | Some(LaneColor hex) -> border.BorderBrush(Brushes.solid hex) |> ignore
  | _ -> border.Classes(laneClass(LaneBorder, lane)) |> ignore

/// Paints one border's lane body.
let laneBack(border: Border, lane: Lane option) : unit =
  match lane with
  | Some(LaneColor hex) -> border.Background(Brushes.solid hex) |> ignore
  | _ -> border.Classes(laneClass(LaneBack, lane)) |> ignore

/// Paints one templated control's lane foreground: the rail icons and
/// the level chip.
let laneForeground
  (control: Primitives.TemplatedControl, lane: Lane option)
  : unit =
  match lane with
  | Some(LaneColor hex) -> control.Foreground(Brushes.solid hex) |> ignore
  | _ -> control.Classes(laneClass(LaneFg, lane)) |> ignore

/// Paints one text block's lane foreground.
let laneText(block: TextBlock, lane: Lane option) : unit =
  match lane with
  | Some(LaneColor hex) -> block.Foreground(Brushes.solid hex) |> ignore
  | _ -> block.Classes(laneClass(LaneFg, lane)) |> ignore

/// One app-level style: a selector plus its setters, as code.
let styleOf
  (
    build: Selector | null -> Selector,
    setters: (AvaloniaProperty * obj | null) array
  ) : Style =
  Style(build)
    .Setters(
      setters |> Array.map(fun (property, value) -> Setter(property, value))
    )

/// One themed setter value: a dynamic resource the variant switch
/// re-resolves.
let themed(key: string) = box(DynamicResourceExtension key)

/// Every class and the style it carries, in cascade order.
let styleTable: (StyleClass * (unit -> Style)) list = [
  NavItem,
  fun () ->
    // The nav entry: one 44-high row, icon and label at 15px
    // medium, rounded corners.
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name NavItem)),
      [|
        Button.BackgroundProperty, box Brushes.Transparent
        Button.BorderThicknessProperty, box(Thickness(0.0))
        Button.ForegroundProperty, themed Keys.muted
        Button.CornerRadiusProperty, box(CornerRadius(8.0))
        Button.HeightProperty, box 44.0
        Button.PaddingProperty, box(Thickness(16.0, 0.0, 12.0, 0.0))
        Button.FontSizeProperty, box 15.0
        Button.FontWeightProperty, box FontWeight.Medium
        Button.HorizontalAlignmentProperty, box HorizontalAlignment.Stretch
        Button.HorizontalContentAlignmentProperty, box HorizontalAlignment.Left
        Button.VerticalContentAlignmentProperty, box VerticalAlignment.Center
      |]
    )
  NavItemActive,
  fun () ->
    // The active navigation: a primary-soft wash with primary
    // text, never a gray fill.
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class(name NavItem).Class(name NavItemActive)),
      [|
        Button.BackgroundProperty, themed Keys.primarySoft
        Button.ForegroundProperty, themed Keys.accent
      |]
    )
  NavCommit,
  fun () ->
    // The compose entry: the nav row shape filled with the primary
    // color.
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name NavCommit)),
      [|
        Button.BackgroundProperty, themed Keys.accent
        Button.ForegroundProperty, themed Keys.onAccent
        Button.BorderThicknessProperty, box(Thickness(0.0))
        Button.CornerRadiusProperty, box(CornerRadius(8.0))
        Button.HeightProperty, box 44.0
        Button.PaddingProperty, box(Thickness(16.0, 0.0, 12.0, 0.0))
        Button.FontSizeProperty, box 15.0
        Button.FontWeightProperty, box FontWeight.Medium
        Button.HorizontalAlignmentProperty, box HorizontalAlignment.Stretch
        Button.HorizontalContentAlignmentProperty, box HorizontalAlignment.Left
        Button.VerticalContentAlignmentProperty, box VerticalAlignment.Center
      |]
    )
  RailItem,
  fun () ->
    // The icon rail: the row keeps its height and centers its single
    // icon.
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name RailItem)),
      [|
        Button.PaddingProperty, box(Thickness(0.0))
        Button.HorizontalContentAlignmentProperty,
        box HorizontalAlignment.Center
      |]
    )
  Card,
  fun () ->
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name Card)),
      [|
        Border.BackgroundProperty, themed Keys.card
        Border.BorderBrushProperty, themed Keys.border
        Border.BorderThicknessProperty, box(Thickness(1.0))
        Border.CornerRadiusProperty, box(CornerRadius(10.0))
        Border.PaddingProperty, box(Thickness(14.0))
      |]
    )
  Title,
  fun () ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Title)),
      [|
        TextBlock.FontSizeProperty, box 17.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
        TextBlock.ForegroundProperty, themed Keys.text
      |]
    )
  Headline,
  fun () ->
    // The identity heading: 24px semibold, tight tracking.
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Headline)),
      [|
        TextBlock.FontSizeProperty, box 24.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
        TextBlock.ForegroundProperty, themed Keys.text
      |]
    )
  Subtitle,
  fun () ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Subtitle)),
      [|
        TextBlock.FontSizeProperty, box 15.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
        TextBlock.ForegroundProperty, themed Keys.text
      |]
    )
  Muted,
  fun () ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Muted)),
      [| TextBlock.ForegroundProperty, themed Keys.muted |]
    )
  StatNum,
  fun () ->
    // The count half of one stat pair: mono, medium, reading color.
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name StatNum)),
      [|
        TextBlock.FontFamilyProperty, box Fonts.mono
        TextBlock.FontSizeProperty, box 14.0
        TextBlock.FontWeightProperty, box FontWeight.Medium
        TextBlock.ForegroundProperty, themed Keys.text
      |]
    )
  AccentButton,
  fun () ->
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name AccentButton)),
      [|
        Button.BackgroundProperty, themed Keys.accent
        Button.ForegroundProperty, themed Keys.onAccent
        Button.BorderThicknessProperty, box(Thickness(0.0))
        Button.CornerRadiusProperty, box(CornerRadius(8.0))
        Button.FontWeightProperty, box FontWeight.SemiBold
        Button.PaddingProperty, box(Thickness(16.0, 9.0, 16.0, 9.0))
      |]
    )
  GhostButton,
  fun () ->
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name GhostButton)),
      [|
        Button.BackgroundProperty, box Brushes.Transparent
        Button.ForegroundProperty, themed Keys.muted
        Button.BorderThicknessProperty, box(Thickness(0.0))
      |]
    )
  DangerButton,
  fun () ->
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name DangerButton)),
      [|
        Button.BackgroundProperty, themed Keys.danger
        Button.ForegroundProperty, themed Keys.dangerText
        Button.BorderThicknessProperty, box(Thickness(0.0))
        Button.CornerRadiusProperty, box(CornerRadius(8.0))
        Button.PaddingProperty, box(Thickness(16.0, 9.0, 16.0, 9.0))
      |]
    )
  NavButton,
  fun () ->
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name NavButton)),
      [|
        Button.BackgroundProperty, themed Keys.surface
        Button.ForegroundProperty, themed Keys.text
        Button.BorderThicknessProperty, box(Thickness(1.0))
        Button.BorderBrushProperty, themed Keys.border
        Button.CornerRadiusProperty, box(CornerRadius(8.0))
      |]
    )
  CodeBlock,
  fun () ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name CodeBlock)),
      [|
        TextBlock.FontFamilyProperty, box Fonts.mono
        TextBlock.FontSizeProperty, box 13.0
        TextBlock.ForegroundProperty, themed Keys.text
      |]
    )
  Chip,
  fun () ->
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name Chip)),
      [|
        Border.BackgroundProperty, themed Keys.surface
        Border.BorderBrushProperty, themed Keys.border
        Border.BorderThicknessProperty, box(Thickness(1.0))
        Border.CornerRadiusProperty, box(CornerRadius(10.0))
        Border.PaddingProperty, box(Thickness(8.0, 2.0, 8.0, 2.0))
      |]
    )
  PollOption,
  fun () ->
    // One choice row of a poll: the whole row is the radio, so the
    // border and the padding live on the toggle itself.
    styleOf(
      (fun selector -> selector.OfType<ToggleButton>().Class(name PollOption)),
      [|
        Button.BackgroundProperty, box Brushes.Transparent
        Button.BorderBrushProperty, themed Keys.border
        Button.BorderThicknessProperty, box(Thickness(1.0))
        Button.CornerRadiusProperty, box(CornerRadius(6.0))
        Button.PaddingProperty, box(Thickness(12.0, 8.0, 12.0, 8.0))
        Layoutable.MinHeightProperty, box 38.0
        Button.FontSizeProperty, box 15.0
        Button.ForegroundProperty, themed Keys.text
        Button.HorizontalAlignmentProperty, box HorizontalAlignment.Stretch
        Button.HorizontalContentAlignmentProperty,
        box HorizontalAlignment.Stretch
        Button.VerticalContentAlignmentProperty, box VerticalAlignment.Center
      |]
    )
  FilterPill,
  fun () ->
    // The notification filter: a full-round pill that fills
    // with the primary-soft wash while pressed in.
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name FilterPill)),
      [|
        Button.BackgroundProperty, box Brushes.Transparent
        Button.BorderBrushProperty, themed Keys.border
        Button.BorderThicknessProperty, box(Thickness(1.0))
        Button.CornerRadiusProperty, box(CornerRadius(999.0))
        Button.ForegroundProperty, themed Keys.muted
        Button.FontSizeProperty, box 14.0
        Button.FontWeightProperty, box FontWeight.Medium
        Button.PaddingProperty, box(Thickness(12.0, 6.0, 12.0, 6.0))
      |]
    )
  FilterPillActive,
  fun () ->
    styleOf(
      (fun selector ->
        selector
          .OfType<Button>()
          .Class(name FilterPill)
          .Class(name FilterPillActive)),
      [|
        Button.BackgroundProperty, themed Keys.primarySoft
        Button.BorderBrushProperty, themed Keys.accent
        Button.ForegroundProperty, themed Keys.accent
      |]
    )
  TabButton,
  fun () ->
    // The page tab: a full-width row of centered titles over a
    // hairline, the active one underlined in primary.
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name TabButton)),
      [|
        Button.BackgroundProperty, box Brushes.Transparent
        Button.BorderThicknessProperty, box(Thickness(0.0, 0.0, 0.0, 3.0))
        Button.BorderBrushProperty, box Brushes.Transparent
        Button.CornerRadiusProperty, box(CornerRadius(0.0))
        Button.ForegroundProperty, themed Keys.muted
        Button.FontSizeProperty, box 15.0
        Button.FontWeightProperty, box FontWeight.Medium
        Button.PaddingProperty, box(Thickness(16.0, 13.0, 16.0, 11.0))
        Button.HorizontalAlignmentProperty, box HorizontalAlignment.Stretch
        Button.HorizontalContentAlignmentProperty,
        box HorizontalAlignment.Center
      |]
    )
  TabButtonActive,
  fun () ->
    styleOf(
      (fun selector ->
        selector
          .OfType<Button>()
          .Class(name TabButton)
          .Class(name TabButtonActive)),
      [|
        Button.BorderBrushProperty, themed Keys.accent
        Button.ForegroundProperty, themed Keys.text
      |]
    )
  CommitRow,
  fun () ->
    // One commit card: transparent over the feed, a surface wash on
    // hover. The padding
    // stays at zero: the Fluent template's default inset would shove
    // the whole row off the shared rail.
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name CommitRow)),
      [|
        Button.BackgroundProperty, box Brushes.Transparent
        Button.BorderThicknessProperty, box(Thickness(0.0))
        Button.PaddingProperty, box(Thickness(0.0))
        Button.ForegroundProperty, themed Keys.text
        Button.HorizontalAlignmentProperty, box HorizontalAlignment.Stretch
        Button.HorizontalContentAlignmentProperty,
        box HorizontalAlignment.Stretch
        Button.VerticalContentAlignmentProperty, box VerticalAlignment.Stretch
      |]
    )
  Divider,
  fun () ->
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name Divider)),
      [|
        Border.BackgroundProperty, themed Keys.border
        Control.HeightProperty, box 1.0
      |]
    )
  Mono,
  fun () ->
    // The machine voice: hashes, handles, times, and counts.
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Mono)),
      [|
        TextBlock.FontFamilyProperty, box Fonts.mono
        TextBlock.FontSizeProperty, box 13.0
        TextBlock.ForegroundProperty, themed Keys.muted
      |]
    )
  ChromeTitle,
  fun () ->
    // The drawn window chrome draws the window title over the client
    // area; the shell paints its own brand, so the chrome text stays
    // off.
    styleOf(
      (fun selector ->
        selector
          .OfType<WindowDrawnDecorations>()
          .Descendant()
          .OfType<TextBlock>()),
      [| Control.IsVisibleProperty, box false |]
    )
  CaptionButton,
  fun () ->
    // One app-drawn caption button; the hover state rides on a sibling
    // pseudo-class style in `styles`.
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name CaptionButton)),
      [|
        Button.BackgroundProperty, box Brushes.Transparent
        Button.BorderThicknessProperty, box(Thickness(0.0))
        Button.ForegroundProperty, themed Keys.muted
        Button.WidthProperty, box 44.0
        Button.HeightProperty, box 30.0
        Button.PaddingProperty, box(Thickness(0.0))
        Button.CornerRadiusProperty, box(CornerRadius(6.0))
      |]
    )
  AheadPill,
  fun () ->
    // One conversation's unread count: a mono count on the primary
    // fill. Geist Mono digits are monospaced, so the count holds its
    // width as it changes.
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name AheadPill)),
      [|
        Border.BackgroundProperty, themed Keys.accent
        Border.CornerRadiusProperty, box(CornerRadius(2.0))
        Border.PaddingProperty, box(Thickness(8.0, 0.0, 8.0, 0.0))
        Layoutable.MinHeightProperty, box 20.0
        TextElement.FontFamilyProperty, box Fonts.mono
        TextElement.FontSizeProperty, box 11.0
        TextElement.FontWeightProperty, box FontWeight.Medium
        TextElement.ForegroundProperty, themed Keys.onAccent
      |]
    )
  DayMarker,
  fun () ->
    // One calendar-day break in a thread: a quiet pill the host row
    // centers, a dot, and the day name. The dot rides an Ellipse
    // style in `styles`.
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name DayMarker)),
      [|
        Border.BackgroundProperty, themed Keys.surface
        Border.CornerRadiusProperty, box(CornerRadius(2.0))
        Border.PaddingProperty, box(Thickness(6.0, 2.0, 8.0, 2.0))
        TextElement.FontFamilyProperty, box Fonts.mono
        TextElement.FontSizeProperty, box 11.0
        TextElement.ForegroundProperty, themed Keys.muted
      |]
    )
  ReactionChip,
  fun () ->
    // One emoji tally under a message: card fill inside a hairline at
    // 13px. The mine variant washes primary-soft; it rides a sibling
    // style in `styles`.
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name ReactionChip)),
      [|
        Border.BackgroundProperty, themed Keys.card
        Border.BorderBrushProperty, themed Keys.border
        Border.BorderThicknessProperty, box(Thickness(1.0))
        Border.CornerRadiusProperty, box(CornerRadius(6.0))
        Border.PaddingProperty, box(Thickness(6.0, 0.0, 8.0, 0.0))
        Layoutable.MinHeightProperty, box 24.0
        TextElement.FontSizeProperty, box 13.0
      |]
    )
  BubbleShadow,
  fun () ->
    // The chat's one depth cue: on message bubbles, images in
    // messages, and link cards only.
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name BubbleShadow)),
      [|
        Border.BoxShadowProperty,
        box(
          BoxShadows(
            BoxShadow(
              OffsetY = 12.0,
              Blur = 30.0,
              Spread = -12.0,
              Color = Color.FromArgb(115uy, 0uy, 0uy, 0uy)
            )
          )
        )
      |]
    )
]

/// One style for the given class.
let style class' =
  styleTable
  |> List.pick(fun (candidate, build) ->
    if candidate = class' then Some(build()) else None)

let styles() =
  let styles = Styles()

  for _, build in styleTable do
    styles.Add(build())

  // Hover states ride as sibling pseudo-class styles: a nested style on
  // an app-level style stalls the style system. Later styles win over
  // earlier ones, so each hover stays under its active override.
  styles.Add(
    styleOf(
      (fun selector ->
        selector
          .OfType<Button>()
          .Class(name CaptionButton)
          .Class(":pointerover")),
      [| Button.BackgroundProperty, themed Keys.card |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class(name NavItem).Class(":pointerover")),
      [|
        Button.BackgroundProperty, themed Keys.surface
        Button.ForegroundProperty, themed Keys.text
      |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class(name NavCommit).Class(":pointerover")),
      [| Button.BackgroundProperty, themed Keys.accentHover |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector
          .OfType<ToggleButton>()
          .Class(name PollOption)
          .Class(":pointerover")),
      [| Button.BackgroundProperty, themed Keys.surface |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector
          .OfType<ToggleButton>()
          .Class(name PollOption)
          .Class(":checked")),
      [|
        Button.BackgroundProperty, themed Keys.primarySoft
        Button.BorderBrushProperty, themed Keys.accent
      |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class(name PollRemove).Class(":pointerover")),
      [| Button.ForegroundProperty, themed Keys.danger |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class(name FilterPill).Class(":pointerover")),
      [| Button.BackgroundProperty, themed Keys.surface |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class(name TabButton).Class(":pointerover")),
      [| Button.BackgroundProperty, themed Keys.surface |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class(name CommitRow).Class(":pointerover")),
      [| Button.BackgroundProperty, themed Keys.surface |]
    )
  )

  // The focused commit row: the rows run edge to edge on the rail,
  // where a border ring would shift the layout on every walk, so the
  // focus rides as the same primary-soft wash the active navigation
  // wears.
  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class(name CommitRow).Class(":focus")),
      [| Button.BackgroundProperty, themed Keys.primarySoft |]
    )
  )

  // The day marker's dot: the muted text color at six tenths.
  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Shapes.Ellipse>().Class(name DayMarker)),
      [|
        Shapes.Ellipse.FillProperty, themed Keys.muted
        Layoutable.WidthProperty, box 6.0
        Layoutable.HeightProperty, box 6.0
        Visual.OpacityProperty, box 0.6
      |]
    )
  )

  // The thread's head divider: "HEAD · N new" in the mono accent, a
  // half-opacity accent hairline trailing to the row's end.
  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name HeadRule)),
      [|
        TextBlock.FontFamilyProperty, box Fonts.mono
        TextBlock.FontSizeProperty, box 11.0
        TextBlock.FontWeightProperty, box FontWeight.Medium
        TextBlock.ForegroundProperty, themed Keys.accent
      |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name HeadRule)),
      [|
        Border.BackgroundProperty, themed Keys.accent
        Control.HeightProperty, box 1.0
        Visual.OpacityProperty, box 0.5
      |]
    )
  )

  // The mine half of a reaction: the primary-soft wash with the accent
  // ring.
  styles.Add(
    styleOf(
      (fun selector ->
        selector
          .OfType<Border>()
          .Class(name ReactionChip)
          .Class(name ReactionChipMine)),
      [|
        Border.BackgroundProperty, themed Keys.primarySoft
        Border.BorderBrushProperty, themed Keys.accent
      |]
    )
  )

  // The brush classes: one property per class, resolved as dynamic
  // resources the variant switch re-resolves. They follow the base and
  // hover styles, so they win any color tie. No control binds a brush
  // observable, so no control is rooted from the app's resource event.
  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name FgAccent)),
      [| TextBlock.ForegroundProperty, themed Keys.accent |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name FgAccent)),
      [| Button.ForegroundProperty, themed Keys.accent |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name FgMuted)),
      [| Button.ForegroundProperty, themed Keys.muted |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name FgDanger)),
      [| TextBlock.ForegroundProperty, themed Keys.danger |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name FgOnAccent)),
      [| TextBlock.ForegroundProperty, themed Keys.onAccent |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name SurfaceBack)),
      [| Border.BackgroundProperty, themed Keys.surface |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name PrimarySoftBack)),
      [| Border.BackgroundProperty, themed Keys.primarySoft |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name AccentBack)),
      [| Border.BackgroundProperty, themed Keys.accent |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name CardBack)),
      [| Border.BackgroundProperty, themed Keys.card |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name RailBack)),
      [| Border.BackgroundProperty, themed Keys.rail |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name Hairline)),
      [| Border.BorderBrushProperty, themed Keys.border |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name Hairline)),
      [| Button.BorderBrushProperty, themed Keys.border |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name AccentEdge)),
      [| Button.BorderBrushProperty, themed Keys.accent |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name AccentEdge)),
      [| Border.BorderBrushProperty, themed Keys.accent |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<PathIcon>().Class(name FgAccent)),
      [| TemplatedControl.ForegroundProperty, themed Keys.accent |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name TextEdge)),
      [| Button.BorderBrushProperty, themed Keys.text |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Shapes.Ellipse>().Class(name CardFill)),
      [| Shapes.Ellipse.FillProperty, themed Keys.card |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Shapes.Ellipse>().Class(name CardEdge)),
      [| Shapes.Ellipse.StrokeProperty, themed Keys.card |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Shapes.Ellipse>().Class(name RailEdge)),
      [| Shapes.Ellipse.StrokeProperty, themed Keys.rail |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Shapes.Line>().Class(name MutedEdge)),
      [| Shapes.Line.StrokeProperty, themed Keys.muted |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Shapes.Ellipse>().Class(name MutedEdge)),
      [| Shapes.Ellipse.StrokeProperty, themed Keys.muted |]
    )
  )

  // The lane classes: one block of generated rows per palette slot, so
  // every lane-painted control resolves its brush the same dynamic
  // resource way.
  let laneKeys: (string * string) list = [
    for index in 0..5 -> string index, Keys.lane index
    yield "fallback", Keys.laneFallback
  ]

  for key, resource in laneKeys do
    styles.Add(
      styleOf(
        (fun selector ->
          selector.OfType<Shapes.Ellipse>().Class($"lane-fill-{key}")),
        [| Shapes.Ellipse.FillProperty, themed resource |]
      )
    )

    styles.Add(
      styleOf(
        (fun selector ->
          selector.OfType<Shapes.Ellipse>().Class($"lane-stroke-{key}")),
        [| Shapes.Ellipse.StrokeProperty, themed resource |]
      )
    )

    styles.Add(
      styleOf(
        (fun selector ->
          selector.OfType<Shapes.Line>().Class($"lane-stroke-{key}")),
        [| Shapes.Line.StrokeProperty, themed resource |]
      )
    )

    styles.Add(
      styleOf(
        (fun selector ->
          selector.OfType<Shapes.Path>().Class($"lane-stroke-{key}")),
        [| Shapes.Path.StrokeProperty, themed resource |]
      )
    )

    styles.Add(
      styleOf(
        (fun selector -> selector.OfType<Border>().Class($"lane-border-{key}")),
        [| Border.BorderBrushProperty, themed resource |]
      )
    )

    styles.Add(
      styleOf(
        (fun selector -> selector.OfType<Border>().Class($"lane-back-{key}")),
        [| Border.BackgroundProperty, themed resource |]
      )
    )

    styles.Add(
      styleOf(
        (fun selector -> selector.OfType<Button>().Class($"lane-back-{key}")),
        [| Button.BackgroundProperty, themed resource |]
      )
    )

    styles.Add(
      styleOf(
        (fun selector -> selector.OfType<TextBlock>().Class($"lane-fg-{key}")),
        [| TextBlock.ForegroundProperty, themed resource |]
      )
    )

    styles.Add(
      styleOf(
        (fun selector -> selector.OfType<PathIcon>().Class($"lane-fg-{key}")),
        [| Primitives.TemplatedControl.ForegroundProperty, themed resource |]
      )
    )

  styles

/// The dark palette.
let darks() =
  let dictionary = ResourceDictionary()

  let colors = dictionary :> IDictionary<obj | null, obj | null>

  let color(key: string, hex: string) =
    colors[key] <- SolidColorBrush(Color.Parse hex)

  color(Keys.background, "#111112")
  color(Keys.surface, "#232326")
  color(Keys.card, "#18181a")
  color(Keys.border, "#2a2a2d")
  color(Keys.text, "#ececec")
  color(Keys.muted, "#a1a1a6")
  color(Keys.accent, "#7aa2f7")
  color(Keys.accentHover, "#94b5f9")
  color(Keys.accentPressed, "#94b5f9")
  color(Keys.onAccent, "#111112")
  color(Keys.danger, "#ff6b6b")
  color(Keys.dangerText, "#ff8787")
  color(Keys.success, "#51cf66")
  color(Keys.popover, "#1f1f22")
  color(Keys.rail, "#36363a")
  color(Keys.primarySoft, "#18233a")
  color(Keys.laneFallback, "#36363a")
  color(Keys.lane 0, "#ff8a4c")
  color(Keys.lane 1, "#51cf66")
  color(Keys.lane 2, "#da77f2")
  color(Keys.lane 3, "#3bc9db")
  color(Keys.lane 4, "#f06595")
  color(Keys.lane 5, "#fcc419")

  dictionary

/// The light palette.
let lights() =
  let dictionary = ResourceDictionary()

  let colors = dictionary :> IDictionary<obj | null, obj | null>

  let color(key: string, hex: string) =
    colors[key] <- SolidColorBrush(Color.Parse hex)

  color(Keys.background, "#f6f7f8")
  color(Keys.surface, "#eef0f2")
  color(Keys.card, "#ffffff")
  color(Keys.border, "#e1e4e8")
  color(Keys.text, "#121417")
  color(Keys.muted, "#5b616b")
  color(Keys.accent, "#3b6fe0")
  color(Keys.accentHover, "#2e5ccb")
  color(Keys.accentPressed, "#2e5ccb")
  color(Keys.onAccent, "#ffffff")
  color(Keys.danger, "#c92a2a")
  color(Keys.dangerText, "#ffffff")
  color(Keys.success, "#2f9e44")
  color(Keys.popover, "#ffffff")
  color(Keys.rail, "#d3d7dd")
  color(Keys.primarySoft, "#e8effc")
  color(Keys.laneFallback, "#d3d7dd")
  color(Keys.lane 0, "#d9480f")
  color(Keys.lane 1, "#2f9e44")
  color(Keys.lane 2, "#9c36b5")
  color(Keys.lane 3, "#0c8599")
  color(Keys.lane 4, "#c2255c")
  color(Keys.lane 5, "#b7791f")

  dictionary

let install(app: Application) =
  let root = ResourceDictionary()

  // The Fluent templates pick their type face from this token; the
  // shell sets the inherited font for plain text blocks.
  root["ContentControlThemeFontFamily"] <- Fonts.sans
  root.ThemeDictionaries[ThemeVariant.Dark] <- darks()
  root.ThemeDictionaries[ThemeVariant.Light] <- lights()
  app.Resources.MergedDictionaries.Add root

  // The starting variant: deterministic before any window shows, and
  // replaced by the stored choice once the app is up (Program.start's
  // AfterSetup). System maps back to the platform scheme.
  app.RequestedThemeVariant <- ThemeVariant.Dark

  app.Styles.Add(styles())

let apply(choice: ThemeChoice) : unit =
  match Application.Current with
  | null -> ()
  | app ->
    app.RequestedThemeVariant <-
      match choice with
      | System -> ThemeVariant.Default
      | Light -> ThemeVariant.Light
      | Dark -> ThemeVariant.Dark
