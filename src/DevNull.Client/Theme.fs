module DevNull.Client.Theme

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Chrome
open Avalonia.Layout
open Avalonia.Markup.Xaml.MarkupExtensions
open Avalonia.Media
open Avalonia.Styling
open NXUI.Extensions
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
  let rail = "AppRail"
  let primarySoft = "AppPrimarySoft"
  let laneFallback = "AppLaneFallback"

  let lane(index: int) = $"AppLane{abs index % 6}"

module Fonts =

  let sans = FontFamily("avares://DevNull.Client/Assets/Fonts#Geist")

  let mono = FontFamily("avares://DevNull.Client/Assets/Fonts#Geist Mono")

let solid(variant: ThemeVariant, key: string) : IBrush =
  match Application.Current with
  | null -> Brushes.Transparent
  | app ->
    match app.TryFindResource(key, variant) with
    | true, (:? IBrush as brush) -> brush
    | _ -> Brushes.Transparent

module Brushes =

  let private themed(key: string) : IObservable<IBrush> =
    match Application.Current with
    | null -> failwith "the theme is not installed yet"
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
  let rail = themed Keys.rail
  let primarySoft = themed Keys.primarySoft
  let laneFallback = themed Keys.laneFallback

  let transparent = {
    new IObservable<IBrush> with
      member _.Subscribe(observer) =
        observer.OnNext Brushes.Transparent

        {
          new IDisposable with
            member _.Dispose() = ()
        }
  }

  let lane(index: int) = themed(Keys.lane index)

  /// One solid brush from a hex color, for user-painted lanes.
  let solid(hex: string) : IBrush =
    SolidColorBrush(Color.Parse hex) :> IBrush

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
  | CaptionButton
  | Mono
  | Wordmark

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
  | CaptionButton -> "caption-button"
  | Mono -> "mono"
  | Wordmark -> "wordmark"

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
let private themed(key: string) = box(DynamicResourceExtension key)

let style class' =
  match class' with
  | NavItem ->
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name NavItem)),
      [|
        Button.BackgroundProperty, box Brushes.Transparent
        Button.BorderThicknessProperty, box(Thickness(0.0))
        Button.ForegroundProperty, themed Keys.muted
        Button.CornerRadiusProperty, box(CornerRadius(8.0))
        Button.PaddingProperty, box(Thickness(12.0, 9.0, 12.0, 9.0))
        Button.HorizontalAlignmentProperty, box HorizontalAlignment.Stretch
        Button.HorizontalContentAlignmentProperty, box HorizontalAlignment.Left
      |]
    )
  | NavItemActive ->
    // The site's active navigation: a primary-soft wash with primary
    // text, never a gray fill.
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class(name NavItem).Class(name NavItemActive)),
      [|
        Button.BackgroundProperty, themed Keys.primarySoft
        Button.ForegroundProperty, themed Keys.accent
      |]
    )
  | Card ->
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
  | Title ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Title)),
      [|
        TextBlock.FontSizeProperty, box 17.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
        TextBlock.ForegroundProperty, themed Keys.text
      |]
    )
  | Subtitle ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Subtitle)),
      [|
        TextBlock.FontSizeProperty, box 14.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
        TextBlock.ForegroundProperty, themed Keys.text
      |]
    )
  | Muted ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Muted)),
      [| TextBlock.ForegroundProperty, themed Keys.muted |]
    )
  | AccentButton ->
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
  | GhostButton ->
    styleOf(
      (fun selector -> selector.OfType<Button>().Class(name GhostButton)),
      [|
        Button.BackgroundProperty, box Brushes.Transparent
        Button.ForegroundProperty, themed Keys.muted
        Button.BorderThicknessProperty, box(Thickness(0.0))
      |]
    )
  | DangerButton ->
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
  | NavButton ->
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
  | CodeBlock ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name CodeBlock)),
      [|
        TextBlock.FontFamilyProperty, box Fonts.mono
        TextBlock.FontSizeProperty, box 13.0
        TextBlock.ForegroundProperty, themed Keys.text
      |]
    )
  | Chip ->
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
  | Divider ->
    styleOf(
      (fun selector -> selector.OfType<Border>().Class(name Divider)),
      [|
        Border.BackgroundProperty, themed Keys.border
        Control.HeightProperty, box 1.0
      |]
    )
  | Mono ->
    // The machine voice: hashes, handles, times, and counts.
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Mono)),
      [|
        TextBlock.FontFamilyProperty, box Fonts.mono
        TextBlock.FontSizeProperty, box 13.0
        TextBlock.ForegroundProperty, themed Keys.muted
      |]
    )
  | Wordmark ->
    // The lowercase brand: bold, tight, with the second slash painted
    // in the lane color by the shell.
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Wordmark)),
      [|
        TextBlock.FontSizeProperty, box 24.0
        TextBlock.FontWeightProperty, box FontWeight.Bold
        TextBlock.LetterSpacingProperty, box -0.6
        TextBlock.ForegroundProperty, themed Keys.text
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
  | CaptionButton ->
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
    CaptionButton
    Mono
    Wordmark
  ]
  |> List.iter(fun class' -> styles.Add(style class'))

  // The caption hover rides as a sibling pseudo-class style: a nested
  // style on an app-level style stalls the style system.
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

  styles

/// The dark palette: the site's `.dark` tokens from
/// dev-null/resources/css/app.css.
let private darks() =
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

/// The light palette: the site's `:root` tokens from
/// dev-null/resources/css/app.css.
let private lights() =
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
