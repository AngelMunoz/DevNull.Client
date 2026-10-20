module DevNull.Client.Theme

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Chrome
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Styling
open NXUI.Extensions

module Brushes =

  let solid(color: Color) = SolidColorBrush(color)

  let background = solid(Color.Parse "#0B0C0E")
  let surface = solid(Color.Parse "#121418")
  let card = solid(Color.Parse "#191C21")
  let border = solid(Color.Parse "#262B33")
  let text = solid(Color.Parse "#E7E9EC") :> IBrush
  let muted = solid(Color.Parse "#98A0AB") :> IBrush
  let accent = solid(Color.Parse "#5B8DEF") :> IBrush
  let accentHover = solid(Color.Parse "#4A7BDB") :> IBrush
  let accentPressed = solid(Color.Parse "#3D6BC7") :> IBrush
  let onAccent = Brushes.White :> IBrush
  let laneFallback = solid(Color.Parse "#3A404B") :> IBrush
  let danger = solid(Color.Parse "#E5534B") :> IBrush
  let transparent = Brushes.Transparent :> IBrush

  let laneColors: IBrush array =
    [| "#5FA843"; "#4A7BDB"; "#D2622F"; "#8E5BBF"; "#B8912C"; "#2F8A8F" |]
    |> Array.map(fun hex -> solid(Color.Parse hex) :> IBrush)

  let lane index =
    laneColors
    |> Array.tryItem(abs index % laneColors.Length)
    |> Option.defaultValue laneFallback

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
let bandHeight = 32.0

/// The insets every page scrolls under: the window band plus the page
/// gutter.
let pagePadding = Thickness(24.0, 40.0, 24.0, 0.0)

let name class' =
  match class' with
  | NavItem -> "nav-item"
  | NavItemActive -> "nav-item-active"
  | Card -> "card"
  | Title -> "title"
  | Subtitle -> "subtitle"
  | Muted -> "muted"
  | AccentButton -> "accent-button"
  | GhostButton -> "ghost-button"
  | DangerButton -> "danger-button"
  | NavButton -> "nav-button"
  | CodeBlock -> "code-block"
  | Chip -> "chip"
  | Divider -> "divider"
  | ChromeTitle -> "chrome-title"

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

let style class' =
  match class' with
  | NavItem ->
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name NavItem)),
      [|
        Button.BackgroundProperty, box Brushes.Transparent
        Button.BorderThicknessProperty, box(Thickness(0.0))
        Button.ForegroundProperty, box Brushes.muted
        Button.CornerRadiusProperty, box(CornerRadius(8.0))
        Button.PaddingProperty, box(Thickness(12.0, 9.0, 12.0, 9.0))
        Button.HorizontalAlignmentProperty, box HorizontalAlignment.Stretch
        Button.HorizontalContentAlignmentProperty, box HorizontalAlignment.Left
      |]
    )
  | NavItemActive ->
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class(name NavItem).Class(name NavItemActive)),
      [|
        Button.BackgroundProperty, box Brushes.card
        Button.ForegroundProperty, box Brushes.text
      |]
    )
  | Card ->
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name Card)),
      [|
        Border.BackgroundProperty, box Brushes.card
        Border.BorderBrushProperty, box Brushes.border
        Border.BorderThicknessProperty, box(Thickness(1.0))
        Border.CornerRadiusProperty, box(CornerRadius(10.0))
        Border.PaddingProperty, box(Thickness(14.0))
      |]
    )
  | Title ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Title)),
      [|
        TextBlock.FontSizeProperty, box 17.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
        TextBlock.ForegroundProperty, box Brushes.text
      |]
    )
  | Subtitle ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Subtitle)),
      [|
        TextBlock.FontSizeProperty, box 14.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
        TextBlock.ForegroundProperty, box Brushes.text
      |]
    )
  | Muted ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Muted)),
      [| TextBlock.ForegroundProperty, box Brushes.muted |]
    )
  | AccentButton ->
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name AccentButton)),
      [|
        Button.BackgroundProperty, box Brushes.accent
        Button.ForegroundProperty, box Brushes.onAccent
        Button.BorderThicknessProperty, box(Thickness(0.0))
        Button.CornerRadiusProperty, box(CornerRadius(8.0))
        Button.FontWeightProperty, box FontWeight.SemiBold
        Button.PaddingProperty, box(Thickness(16.0, 9.0, 16.0, 9.0))
      |]
    )
  | GhostButton ->
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name GhostButton)),
      [|
        Button.BackgroundProperty, box Brushes.Transparent
        Button.ForegroundProperty, box Brushes.muted
        Button.BorderThicknessProperty, box(Thickness(0.0))
      |]
    )
  | DangerButton ->
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name DangerButton)),
      [|
        Button.BackgroundProperty, box Brushes.danger
        Button.ForegroundProperty, box Brushes.onAccent
        Button.BorderThicknessProperty, box(Thickness(0.0))
        Button.CornerRadiusProperty, box(CornerRadius(8.0))
        Button.PaddingProperty, box(Thickness(16.0, 9.0, 16.0, 9.0))
      |]
    )
  | NavButton ->
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name NavButton)),
      [|
        Button.BackgroundProperty, box Brushes.surface
        Button.ForegroundProperty, box Brushes.text
        Button.BorderThicknessProperty, box(Thickness(1.0))
        Button.BorderBrushProperty, box Brushes.border
        Button.CornerRadiusProperty, box(CornerRadius(8.0))
      |]
    )
  | CodeBlock ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name CodeBlock)),
      [|
        TextBlock.FontFamilyProperty,
        box(FontFamily "Cascadia Code, Consolas, monospace")
        TextBlock.FontSizeProperty, box 13.0
        TextBlock.ForegroundProperty, box Brushes.text
      |]
    )
  | Chip ->
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name Chip)),
      [|
        Border.BackgroundProperty, box Brushes.surface
        Border.BorderBrushProperty, box Brushes.border
        Border.BorderThicknessProperty, box(Thickness(1.0))
        Border.CornerRadiusProperty, box(CornerRadius(10.0))
        Border.PaddingProperty, box(Thickness(8.0, 2.0, 8.0, 2.0))
      |]
    )
  | Divider ->
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name Divider)),
      [|
        Border.BackgroundProperty, box Brushes.border
        Control.HeightProperty, box 1.0
      |]
    )
  | ChromeTitle ->
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

let styles() =
  let styles = Styles()

  [
    NavItem
    NavItemActive
    Card
    Title
    Subtitle
    Muted
    AccentButton
    GhostButton
    DangerButton
    NavButton
    CodeBlock
    Chip
    Divider
    ChromeTitle
  ]
  |> List.iter(fun class' -> styles.Add(style class'))

  styles

let install(app: Application) =
  app.RequestedThemeVariant <- ThemeVariant.Dark
  app.Styles.Add(styles())
