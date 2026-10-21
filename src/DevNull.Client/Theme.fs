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

  /// One brush that never changes, as an observable.
  let constant(brush: IBrush) : IObservable<IBrush> = {
    new IObservable<IBrush> with
      member _.Subscribe(observer) =
        observer.OnNext brush

        {
          new IDisposable with
            member _.Dispose() = ()
        }
  }

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
  | NavCommit -> "nav-commit"
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
  | FilterPill -> "filter-pill"
  | FilterPillActive -> "filter-pill-active"
  | TabButton -> "tab-button"
  | TabButtonActive -> "tab-button-active"
  | CommitRow -> "commit-row"
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
    // The site's nav entry: one 44-high row, icon and label at 15px
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
  | NavCommit ->
    // The compose entry: the nav row shape filled with the primary
    // color, the way the site's Commit button sits inside the nav.
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
  | Headline ->
    // The site's identity heading: 24px semibold, tight tracking.
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Headline)),
      [|
        TextBlock.FontSizeProperty, box 24.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
        TextBlock.ForegroundProperty, themed Keys.text
      |]
    )
  | Subtitle ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Subtitle)),
      [|
        TextBlock.FontSizeProperty, box 15.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
        TextBlock.ForegroundProperty, themed Keys.text
      |]
    )
  | Muted ->
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class(name Muted)),
      [| TextBlock.ForegroundProperty, themed Keys.muted |]
    )
  | StatNum ->
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
  | FilterPill ->
    // The site's notification filter: a full-round pill that fills
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
  | FilterPillActive ->
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
  | TabButton ->
    // The site's page tab: a full-width row of centered titles over a
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
  | TabButtonActive ->
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
  | CommitRow ->
    // One commit card: transparent over the feed, a surface wash on
    // hover, the way the site's rows wash with accent/50. The padding
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
    NavCommit
    Card
    Title
    Headline
    Subtitle
    Muted
    StatNum
    AccentButton
    GhostButton
    DangerButton
    NavButton
    CodeBlock
    Chip
    FilterPill
    FilterPillActive
    TabButton
    TabButtonActive
    CommitRow
    Divider
    ChromeTitle
    CaptionButton
    Mono
    Wordmark
  ]
  |> List.iter(fun class' -> styles.Add(style class'))

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
