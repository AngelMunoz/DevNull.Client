module DevNull.Client.Components.Completion

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache

/// What the completion flyout shows right now: the query as typed,
/// whether the quiet caption belongs above the rows, the people and
/// organizations to mention, the issue a number names while it holds
/// one, the states between — searching, asked to slow down — and the
/// row the keys hold.
[<Struct>]
type View = {
  Query: string
  Caption: bool
  Users: User list
  Organizations: Organization list
  Issue: Issue option
  Searching: bool
  Throttled: bool
  Active: int
}

/// What the flyout's rows answer to.
[<Struct>]
type Actions = {
  PickPerson: User -> unit
  PickOrganization: Organization -> unit
  PickIssue: Issue -> unit
  Hovered: int -> unit
}

/// The flyout's live surface: the scroller the popup shows, the row
/// controls the keys walk, and the release of the row stream.
type Surface = {
  Control: Control
  Rows: IObservable<Control array>
  Dispose: unit -> unit
}

/// The completion flyout's list: the caption, the person and
/// organization rows, the issue card, or the quiet state that stands
/// in for them — each row armed with the one the keys hold. Pure view
/// over the state above.
val view:
  state: IObservable<View> * actions: Actions * images: ImageCacheService ->
    Surface
