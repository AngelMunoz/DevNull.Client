module DevNull.Client.Components.GuildRow

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache

/// One directory card, the site's own: the icon or its emoji as the
/// round avatar, the name, the `#handle` on the mono line, the
/// description clipped to two lines, the member count, and the
/// read-only state chips. The click is the host's; the chips render
/// state the viewer cannot change here — the signed API offers no
/// join or mute write.
[<Struct>]
type Props = {
  Guild: Guild
  Cache: ImageCacheService
  OnOpen: Guild -> unit
}

/// The read-only state chips a card wears: `Joined` filled, `Muted`
/// and `Paused` muted.
val stateChips: guild: Guild -> Control array

/// One directory card.
val view: props: Props -> Control
