module DevNull.Client.Components.FeedParts

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Theme

/// The key of one feed row: the commit or tombstone id.
let inline nodeKey(node: CommitNode) : string =
  match node with
  | LiveCommit commit -> commit.Id
  | Tombstone tombstone -> tombstone.Id

/// One day separator: the rail runs on in the neutral tone, the label
/// rides the content column above a hairline.
let dayRow(label: string) : Control =
  let grid = Grid()

  grid.ColumnDefinitions <- ColumnDefinitions("64,*")

  let line =
    Border()
      .Width(2.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Stretch)
      .Classes(Theme.name Theme.RailBack)

  let label' =
    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Child(
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .Margin(0.0, 8.0, 16.0, 8.0)
          .Text(label)
      )

  grid.Children(line.Column(0), label'.Column(1))

/// The unread marker: the mono HEAD label beside a dashed rule, the
/// site's separator between the new commits and the seen ones.
let lastVisitRow() : Control =
  let rule =
    Line(
      StartPoint = Point(0.0, 0.0),
      EndPoint = Point(1.0, 0.0),
      Stretch = Stretch.Uniform,
      StrokeThickness = 1.0,
      StrokeDashArray = Avalonia.Collections.AvaloniaList<double>(4.0, 4.0),
      VerticalAlignment = VerticalAlignment.Center,
      HorizontalAlignment = HorizontalAlignment.Stretch
    )
      .Classes(Theme.name Theme.MutedEdge)

  DockPanel()
    .LastChildFill(true)
    .Margin(64.0, 8.0, 16.0, 8.0)
    .Children(
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text("HEAD@{last visit}")
        .Margin(0.0, 0.0, 8.0, 0.0)
        .DockLeft(),
      rule
    )

/// The pinned-commit caption: the rail runs on in the neutral tone,
/// the pin glyph and the mono muted label ride the content column —
/// the site's mark above the pinned commit on a profile.
let pinnedRow() : Control =
  let grid = Grid()

  grid.ColumnDefinitions <- ColumnDefinitions("64,*")

  let line =
    Border()
      .Width(2.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Stretch)
      .Classes(Theme.name Theme.RailBack)

  let caption =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(6.0)
      .Margin(0.0, 12.0, 16.0, 4.0)
      .Name("PinnedCaption")
      .Children(
        Icons.iconAt(Icons.Glyph.Pin, 12.0).Classes(Theme.name Theme.FgMuted),
        TextBlock()
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .Text("Pinned")
      )

  grid.Children(line.Column(0), caption.Column(1))

/// One dashed lane-colored vertical rail segment, centered at the
/// given offset from its host's left edge. The thread rail chains row
/// to row; every row draws its own segment.
let laneSpineAt(lane: Lane option, x: float) : Line =
  let spine =
    Line(
      StartPoint = Point(0.0, 0.0),
      EndPoint = Point(0.0, 1.0),
      Stretch = Stretch.Fill,
      StrokeThickness = 1.5,
      StrokeDashArray = Avalonia.Collections.AvaloniaList<double>(2.0, 2.0),
      HorizontalAlignment = HorizontalAlignment.Left,
      VerticalAlignment = VerticalAlignment.Stretch
    )
      .Margin(x - 0.75, 0.0, 0.0, 0.0)

  Theme.laneStroke(spine, lane)
  spine

/// The dashed quarter-circle that branches one reply off its parent's
/// rail, at the given offset: down from the parent's spine, then
/// right onto the reply's avatar column.
let laneElbowAt
  (lane: Lane option, x: float, width: float, height: float)
  : Path =
  let bend = min (width / 2.0) height

  let path =
    System.FormattableString.Invariant(
      $"M 0,0 L 0,{height - bend} Q 0,{height} {bend},{height} L {width},{height}"
    )

  let elbow =
    Path()
      .Name("ThreadElbow")
      .Margin(x, 0.0, 0.0, 0.0)
      .Width(width)
      .Height(height)
      .HorizontalAlignment(HorizontalAlignment.Left)
      .VerticalAlignment(VerticalAlignment.Top)
      .StrokeThickness(1.5)
      .StrokeDashArray(Avalonia.Collections.AvaloniaList<double>(2.0, 2.0))
      .Data(Geometry.Parse path)

  Theme.laneStroke(elbow, lane)
  elbow
