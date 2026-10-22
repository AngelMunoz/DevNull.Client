module DevNull.Client.Components.UserFlyout

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache

/// What one hover card needs from its host: the profile fetch, the
/// profile navigation, and the image cache.
[<Struct>]
type Source = {
  Fetch: string -> Async<ProfileDetails option>
  OnOpen: string -> unit
  Cache: ImageCacheService
}

/// Wraps one child (the avatar). After a 450 ms hover a card with the
/// user's identity opens beside it; it closes 200 ms after the pointer
/// leaves the trigger or the card, and a click opens the profile. The
/// card is read-only: follow and mute stay on the profile page.
val attach: source: Source * user: User * child: Control -> Control
