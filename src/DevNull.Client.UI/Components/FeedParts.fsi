module DevNull.Client.Components.FeedParts

open Avalonia.Controls
open Avalonia.Controls.Shapes
open DevNull.Client.Core.ApiTypes

/// The key of one feed row: the commit or tombstone id.
val inline nodeKey: node: CommitNode -> string

/// One day separator: the rail runs on in the neutral tone, the label
/// rides the content column above a hairline.
val dayRow: label: string -> Control

/// The unread marker: the mono HEAD label beside a dashed rule.
val lastVisitRow: unit -> Control

/// One dashed lane-colored vertical rail segment, centered at the
/// given offset from its host's left edge. The thread rail chains row
/// to row; every row draws its own segment.
val laneSpineAt: lane: Lane option * x: float -> Line

/// The dashed quarter-circle that branches one reply off its parent's
/// rail, at the given offset: down from the parent's spine, then
/// right onto the reply's avatar column.
val laneElbowAt:
  lane: Lane option * x: float * width: float * height: float -> Path
