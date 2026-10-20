module DevNull.Client.Theme

open Avalonia
open Avalonia.Media
open Avalonia.Styling

/// The named brushes of the app. Dark is the only variant; the instances
/// are module values, so every consumer sees the same brush.
module Brushes =

  /// The window background, near black.
  val background: SolidColorBrush

  /// The sidebar and chrome surface, one step above the background.
  val surface: SolidColorBrush

  /// The card fill for commits, forms, and panels.
  val card: SolidColorBrush

  /// The hairline around cards and separators.
  val border: SolidColorBrush

  /// The primary text color.
  val text: IBrush

  /// The secondary text color: handles, timestamps, hints.
  val muted: IBrush

  /// The accent: the commit button, the active navigation item, links.
  val accent: IBrush

  /// The accent one step down: the hover fill.
  val accentHover: IBrush

  /// The accent two steps down: the pressed fill.
  val accentPressed: IBrush

  /// The text on top of accent and danger fills.
  val onAccent: IBrush

  /// The lane dot when a user carries no lane.
  val laneFallback: IBrush

  /// The destructive color: delete account, report.
  val danger: IBrush

  /// No fill at all.
  val transparent: IBrush

  /// One lane dot color for the palette indexes 0 to 5, cycling on
  /// out-of-range indexes; no lane gives the fallback.
  val lane: index: int -> IBrush

/// One class name in the style vocabulary.
type StyleClass =
  | NavItem
  | NavItemActive
  | Card
  | Title
  | Subtitle
  | Muted
  | AccentButton
  | GhostButton
  | DangerButton
  | NavButton
  | CodeBlock
  | Chip
  | Divider
  | ChromeTitle

/// The height of the drag band the shell lays over the drawn window
/// chrome.
val bandHeight: float

/// The insets every page scrolls under: the window band plus the page
/// gutter.
val pagePadding: Thickness

/// The readable name of one class, for `.Classes(...)`.
val name: class': StyleClass -> string

/// One app-level style: a selector plus its setters, as code.
val styleOf:
  build: (Selector | null -> Selector) *
  setters: (AvaloniaProperty * obj | null) array ->
    Style

/// One style for the given class.
val style: class': StyleClass -> Style

/// Every style of the app, in order.
val styles: unit -> Styles

/// Forces the dark variant and installs `styles` after the Fluent theme.
val install: app: Application -> unit
