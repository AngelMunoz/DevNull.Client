module DevNull.Client.Components.LiveBadge

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes

/// The live chip beside a live user's name: the red chip with the
/// white dot and the word. The chip opens the stream page outside
/// the app; without a stream the chip does not show.
val view: live: LiveStream option -> Control
