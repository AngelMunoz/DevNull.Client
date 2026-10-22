module DevNull.Client.Theme

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Styling
open DevNull.Client.Core.ApiTypes
open DevNull.Client.SettingsStore

/// The named brushes of the app. Every brush is an observable of the
/// themed resource: the value follows the light and dark dictionaries
/// as the variant changes, so views bind it once and never re-style.
module Brushes =

  /// The window background.
  val background: IObservable<IBrush>

  /// The chrome surface one step above the background.
  val surface: IObservable<IBrush>

  /// The card fill for commits, forms, and panels.
  val card: IObservable<IBrush>

  /// The hairline around cards and separators.
  val border: IObservable<IBrush>

  /// The primary text color.
  val text: IObservable<IBrush>

  /// The secondary text color: handles, timestamps, hints.
  val muted: IObservable<IBrush>

  /// The primary: the commit button, links, the active states.
  val accent: IObservable<IBrush>

  /// The primary one step down: the hover fill.
  val accentHover: IObservable<IBrush>

  /// The primary two steps down: the pressed fill.
  val accentPressed: IObservable<IBrush>

  /// The text on top of accent and danger fills.
  val onAccent: IObservable<IBrush>

  /// The destructive color: delete account, report.
  val danger: IObservable<IBrush>

  /// The rail line color.
  val rail: IObservable<IBrush>

  /// The soft primary wash behind active elements.
  val primarySoft: IObservable<IBrush>

  /// No fill at all.
  val transparent: IObservable<IBrush>

  /// One lane color for the palette indexes 0 to 5, cycling on
  /// out-of-range indexes.
  val lane: index: int -> IObservable<IBrush>

  /// One brush that never changes, as an observable.
  val constant: brush: IBrush -> IObservable<IBrush>

  /// The brush of one lane value: a palette index, a user hex, or the
  /// neutral fallback when the user carries no lane.
  val laneOf: lane: Lane option -> IObservable<IBrush>

  /// One solid brush from a hex color, for user-painted lanes.
  val solid: hex: string -> IBrush

  /// The lane color when a user carries no lane.
  val laneFallback: IObservable<IBrush>

/// The bundled Geist fonts of the site.
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
  | FilterPill
  | FilterPillActive
  | TabButton
  | TabButtonActive
  | CommitRow
  | Divider
  | ChromeTitle
  | CaptionButton
  | Mono

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

/// Moves one class on and off a control as the observable flips. The
/// class list is the only thing the subscription touches.
val bindClass:
  control: Control * class': StyleClass * active: IObservable<bool> -> unit

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
/// non-bindable spots read through this; views bind the observables.
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

  /// The rail line color.
  val rail: string

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
