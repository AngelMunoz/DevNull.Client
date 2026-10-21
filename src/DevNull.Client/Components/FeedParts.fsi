module DevNull.Client.Components.FeedParts

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes

/// The key of one feed row: the commit or tombstone id.
val inline nodeKey: node: CommitNode -> string

/// One day separator: the rail runs on in the neutral tone, the label
/// rides the content column above a hairline.
val dayRow: label: string -> Control
