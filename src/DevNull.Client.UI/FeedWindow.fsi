module DevNull.Client.FeedWindow

/// How a feed orders its rows: the first row is the newest, or the
/// oldest.
[<Struct>]
type Order =
  | NewestFirst
  | OldestFirst

/// One row the reader was on: its key, the part of the row above the
/// viewport top, and the request cursor that fetched its page.
[<Struct>]
type Anchor = {
  Key: string
  Offset: float
  Entry: string voption
}

/// One loaded page in display order. `Request` is the cursor that
/// fetched it, `ValueNone` for page 1. Rows are an array because a
/// page is a fixed snapshot and the window indexes it on every
/// operation.
[<Struct>]
type Chunk<'T> = {
  Request: string voption
  Items: 'T array
  Older: string voption
  Newer: string voption
}

/// The edge a trim removes whole chunks from.
[<Struct>]
type Edge =
  | NewerEdge
  | OlderEdge

/// The loaded chunks in display order, and the row cap.
type Window<'T> = {
  Order: Order
  Chunks: Chunk<'T> list
  Limit: int
}

/// The reading place one page parks between visits.
[<Struct>]
type Place = { Anchor: Anchor voption }

/// The key projection the window operations share. The order lives on
/// the window: every operation reads `Window.Order`, never this.
[<Struct>]
type Ops<'T> = { KeyOf: 'T -> string }

/// The inputs of one trim: the shared operations, the key the reader
/// holds, and the edge to remove from.
[<Struct>]
type Trim<'T> = {
  Ops: Ops<'T>
  Keep: string voption
  Edge: Edge
}

/// The inputs of one ordered insert: the row and the predicate that
/// names the first existing row it precedes.
[<Struct>]
type Insert<'T> = {
  Ops: Ops<'T>
  Item: 'T
  Before: 'T -> bool
}

/// The inputs of one replace by old key: the row's old identity and the
/// row that takes its place, whose key may differ.
[<Struct>]
type Replace<'T> = { Ops: Ops<'T>; Key: string; Item: 'T }

/// A window with no chunk.
val empty: order: Order * limit: int -> Window<'T>

/// Every row of every chunk, in display order, as the list the Feed
/// binding consumes.
val inline items: window: Window<'T> -> 'T list

/// The number of rows the window holds.
val inline internal count: window: Window<'T> -> int

/// The cursor that fetches the page older than the loaded rows.
val older: window: Window<'T> -> string voption

/// The cursor that fetches the page newer than the loaded rows.
val newer: window: Window<'T> -> string voption

/// The request cursor of the chunk holding the key: the cursor that
/// restores the page the key came from.
val entryOf: ops: Ops<'T> * key: string * window: Window<'T> -> string voption

/// Adds a page at the older edge. A key the window already holds keeps
/// its row and takes the page's value. A page with no new row only moves
/// the older cursor.
val addOlder: ops: Ops<'T> * chunk: Chunk<'T> * window: Window<'T> -> Window<'T>

/// Adds a page at the newer edge, with the same key rules as `addOlder`.
val addNewer: ops: Ops<'T> * chunk: Chunk<'T> * window: Window<'T> -> Window<'T>

/// Merges a fresh page 1 at the newer edge. The new rows join the edge
/// chunk only when the page carries that chunk's own request cursor and
/// overlaps it; otherwise they form a chunk at the newer edge. A page
/// with no new key leaves the window unchanged.
val mergeNewerPage:
  ops: Ops<'T> * chunk: Chunk<'T> * window: Window<'T> -> Window<'T>

/// Replaces the row with the item's key in place. A key the window does
/// not hold leaves the window unchanged.
val replace: ops: Ops<'T> * item: 'T * window: Window<'T> -> Window<'T>

/// Replaces the row whose key is Key with Item, even when Item's own
/// key differs. This is the live-commit to tombstone fold.
val replaceKey: replace: Replace<'T> * window: Window<'T> -> Window<'T>

/// Inserts the item before the first row that matches Before. An
/// existing key is replaced in place. With no matching row, the item
/// joins the older edge chunk.
val insert: insert: Insert<'T> * window: Window<'T> -> Window<'T>

/// Drops the row with the key. The chunk stays, so its cursors stay in
/// the chain.
val remove: ops: Ops<'T> * key: string * window: Window<'T> -> Window<'T>

/// Removes whole chunks from the trim's edge until the window holds at
/// most its limit rows. The chunk holding the keep key, and the last
/// chunk, stay.
val trim: trim: Trim<'T> * window: Window<'T> -> Window<'T>
