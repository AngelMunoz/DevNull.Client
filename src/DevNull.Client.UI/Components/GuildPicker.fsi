module DevNull.Client.Components.GuildPicker

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache

/// The guild picker's card: the rows on show — joined guilds while
/// the box is empty, the directory's answers under a search — the
/// live box the query rides, and the callbacks the composer answers.
[<Struct>]
type Props = {
  Rows: IObservable<Guild list>
  Query: IObservable<string>
  Images: ImageCacheService
  OnOpen: unit -> unit
  OnQuery: string -> unit
  OnPick: Guild -> unit
  OnClose: unit -> unit
}

/// The trigger line above the body box, and the card that opens from
/// it. The trigger carries the picker's own name; a host mounts it
/// only while it offers guild targeting at all.
[<Struct>]
type View = {
  Trigger: Control voption
  Layer: Control
}

/// One guild picker: the trigger opens the card, the card lists the
/// rows the host feeds it, and a pick answers once.
val view: props: Props -> View
