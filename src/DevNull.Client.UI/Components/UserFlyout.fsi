module DevNull.Client.Components.UserFlyout

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache

/// What one hover card needs from its host: the profile fetch, the
/// profile navigation, the organization navigation a body's
/// org-kind mention rides, and the image cache.
[<Struct>]
type Source = {
  Fetch: string -> Async<ProfileDetails option>
  OnOpen: string -> unit
  OnOpenOrg: string -> unit
  Cache: ImageCacheService
}

/// Wraps one child (the avatar). After a 450 ms hover a card with the
/// user's identity opens beside it; it closes 200 ms after the pointer
/// leaves the trigger or the card, and a click on either opens the
/// profile. Follow and mute stay on the profile page.
///
/// The flyout rides a TextBlock inline, and a TextBlock clears and
/// rebuilds its inline children on every arrange pass, so the flyout
/// detaches and re-attaches with layout: no lifecycle logic may key on
/// the detach. The timers own the lifetime instead — every tick and
/// every pointer path stops them — and a recycled row's popup closes
/// with its tree.
val attach: source: Source * user: User * child: Control -> Control
