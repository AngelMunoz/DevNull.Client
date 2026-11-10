module DevNull.Client.Theme

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Styling
open DevNull.Client.Core.ApiTypes
open DevNull.Client.SettingsStore

/// The shell's surface brushes. These observables are for the shell's
/// own chrome only: views and components bind style classes, never a
/// brush observable.
module Brushes =

  /// The themed brush of one resource key, following the theme variant.
  val themed: key: string -> IObservable<IBrush>

  /// The window background.
  val background: IObservable<IBrush>

  /// The chrome surface one step above the background.
  val surface: IObservable<IBrush>

  /// The card fill for commits, forms, and panels.
  val card: IObservable<IBrush>

  /// The hairline around cards and separators.
  val border: IObservable<IBrush>

  /// The primary: the commit button, links, and active states.
  val accent: IObservable<IBrush>

  /// The text on top of accent and danger fills.
  val onAccent: IObservable<IBrush>

  /// No fill at all.
  val transparent: IObservable<IBrush>

  /// One solid brush from a hex color, for user-painted lanes.
  val solid: hex: string -> IBrush

/// The bundled Geist fonts.
module Fonts =

  /// Geist, the sans of the app.
  val sans: FontFamily

  /// Geist Mono, the face of hashes, handles, times, and counts.
  val mono: FontFamily

/// One class name in the style vocabulary.
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
  | FgWarning
  | FgOnAccent
  | SurfaceBack
  /// The modal scrim surface.
  | ScrimBack
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
  | TabBadge
  /// The live chip beside a live user's name.
  | LiveBadge
  | DayMarker
  | HeadRule
  | ReactionChip
  | ReactionChipMine
  | StagedBubble
  | StagedEdge
  | RejectedBubble
  | AccentDot
  | DangerRing
  | MsgRow
  | HoverActions
  | FlashRing
  | TypingDot1
  | TypingDot2
  | TypingDot3

/// The height of the drag band the shell lays over the drawn window
/// chrome.
val bandHeight: float

/// The insets every page scrolls under: the page gutter.
val pagePadding: Thickness

/// The inset a pinned header takes: the band caption row floats above
/// the client, so the header clears it.
val pinnedPadding: Thickness

/// The readable name of one class, for `.Classes(...)`.
val name: class': StyleClass -> string

/// Moves one class on and off a control as the observable flips and
/// returns the subscription. The class list is the only thing the
/// subscription touches; dispose it when the styled control is
/// replaced or leaves the tree.
val bindClass:
  control: Control * class': StyleClass * active: IObservable<bool> ->
    IDisposable

/// The lane slot one control paints: the property the lane class
/// styles. One slot per class keeps a lane-painted control from
/// inheriting a second lane brush it never asked for.
type LaneRole =
  | LaneFill
  | LaneStroke
  | LaneBorder
  | LaneBack
  | LaneFg

/// The lane class of one slot: the palette index, or the neutral
/// fallback when the user carries no lane.
val laneClass: role: LaneRole * lane: Lane option -> string

/// Paints one shape's lane fill: the palette class for an index, the
/// constant brush for a user hex, the fallback class for no lane.
val laneFill: shape: Shapes.Shape * lane: Lane option -> unit

/// Paints one shape's lane stroke, the same three ways.
val laneStroke: shape: Shapes.Shape * lane: Lane option -> unit

/// Paints one border's lane ring.
val laneBorder: border: Border * lane: Lane option -> unit

/// Paints one border's lane body.
val laneBack: border: Border * lane: Lane option -> unit

/// Paints one templated control's lane foreground: the rail icons and
/// the level chip.
val laneForeground:
  control: Primitives.TemplatedControl * lane: Lane option -> unit

/// Paints one text block's lane foreground.
val laneText: block: TextBlock * lane: Lane option -> unit

/// One app-level style: a selector plus its setters, as code.
val styleOf:
  build: (Selector | null -> Selector) *
  setters: (AvaloniaProperty * obj | null) array ->
    Style

/// One style for the given class.
val style: class': StyleClass -> Style

/// Every style of the app, in order.
val styles: unit -> Styles

/// The themed brush of one key as the given variant holds it. Tests and
/// non-bindable spots read through this; views and components bind
/// style classes, never brush observables.
val solid: variant: ThemeVariant * key: string -> IBrush

/// The keys of the themed resources, so callers never spell them.
module Keys =

  /// The window background.
  val background: string

  /// The chrome surface one step above the background.
  val surface: string

  /// The card fill.
  val card: string

  /// The hairline color.
  val border: string

  /// The modal scrim over the shell's dialogs.
  val scrim: string

  /// The primary text color.
  val text: string

  /// The secondary text color.
  val muted: string

  /// The primary color.
  val accent: string

  /// The primary hover color.
  val accentHover: string

  /// The primary pressed color.
  val accentPressed: string

  /// The text on accent fills.
  val onAccent: string

  /// The destructive color.
  val danger: string

  /// The text on destructive fills.
  val dangerText: string

  /// The success color.
  val success: string

  /// The popover fill.
  val popover: string

  /// The rail line color.
  val rail: string

  /// The input fill. Aliases the rail key until an input surface of
  /// its own is needed.
  val input: string

  /// The soft primary wash behind active elements.
  val primarySoft: string

  /// The lane color when a user carries no lane.
  val laneFallback: string

  /// One lane color key for the palette indexes 0 to 5.
  val lane: index: int -> string

/// Installs the themed resources and the styles, and follows the system
/// variant until a setting picks one.
val install: app: Application -> unit

/// Applies one theme choice to the running app.
val apply: choice: ThemeChoice -> unit
