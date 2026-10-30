module DevNull.Client.Components.SearchParts

open System
open Avalonia
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache
open DevNull.Client.Components.UserFlyout

/// What the term rules say about a search: who it reads like, and
/// which channels it reaches. The site's own few rules, not a model —
/// a wrong guess only changes which section comes first.
module Intent =

  /// The three readings a term can have.
  type Reading =
    | Developer
    | Either
    | Topic

  /// Reads a term as the site's dialog does: a leading `@` is a
  /// developer, two or three capitalised words could be either, spaces
  /// make a topic, a short handle-shaped token is a developer, and
  /// anything else could be either.
  val ofTerm: term: string -> Reading

  /// True when the reading reaches the commit channel.
  val searchesCommits: reading: Reading -> bool

  /// True when a lone `@` announces a developer search but has nothing
  /// to search for yet.
  val awaitsDeveloperName: term: string -> bool

/// The site's canned suggestions, the words its empty dialog offers.
val suggestions: string list

/// Everything the shared search rows read.
[<Struct>]
type RowCtx = {
  Images: ImageCacheService
  Flyout: Source voption
  Now: DateTimeOffset
}

/// One section heading, the site's mono label.
val heading: text: string -> Control

/// One person as a row's content: the avatar on its lane, the name,
/// the handle with its lane dot, and the bio under them.
val peopleIdentity: ctx: RowCtx * user: User -> Control

/// One commit as a row's content: the avatar, the handle with its lane
/// dot beside its age, and two lines of the body.
val commitIdentity: ctx: RowCtx * commit: Commit -> Control

/// One term as a row's content: the words and the arrow that follows
/// them on hover.
val termIdentity: term: string -> Control

/// The see-all row's content: the accent words and their arrow.
val seeAllIdentity: unit -> Control

/// Rides the armed row into view: every armed index brings the row it
/// names into view, searched from the root the caller reads at each
/// step — the palette's scroller stands in one tree, while a popup's
/// rows live under the window's overlay only while it stands.
val followArmed:
  armed: IObservable<int> * prefix: string * root: (unit -> Visual voption) ->
    IDisposable

/// One status line, the site's quiet mono sentence.
val statusLine: text: string -> Control
