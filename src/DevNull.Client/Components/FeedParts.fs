module DevNull.Client.Components.FeedParts

open Avalonia
open Avalonia.Controls
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
      .Background(Brushes.rail)

  let label' =
    Border()
      .BorderBrush(Brushes.border)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Child(
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .Margin(0.0, 8.0, 16.0, 8.0)
          .Text(label)
      )

  grid.Children(line.Column(0), label'.Column(1))
