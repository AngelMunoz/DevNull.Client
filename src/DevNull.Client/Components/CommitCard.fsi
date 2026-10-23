module DevNull.Client.Components.CommitCard

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache
open DevNull.Client.Components.UserFlyout

/// What one card reports up: open the thread, toggle the star, give a
/// pastel, start a quote, reveal the reply composer, vote a poll
/// option, or walk more replies of a thread preview.
[<Struct>]
type Event =
  | Opened of Commit
  | ReplyToggled of Commit
  | ThreadRequested of Commit
  | MoreRepliesRequested of Commit
  | StarToggled of Commit
  | PastelGiven of Commit
  | QuoteRequested of Commit
  | PollVoted of Commit * PollOption

/// One commit row. `OnEvent` receives every interaction. Every card
/// rides the graph rail with its author's avatar; dense rows (the
/// replies of a thread) shrink the node. `ViewerId` is the signed-in
/// user: the author of their own commit sees a star count, never a
/// toggle — the server refuses the star. `Pastels` says whether the
/// viewer's pastel controls show at all, live so the flag can move
/// under a built card; `Now` is the clock the relative time follows;
/// `Images` loads the author avatar and the commit's images; `Flyout`
/// is the hover card source for the author, when the host has one.
[<Struct>]
type Props = {
  Commit: Commit
  OnEvent: Event -> unit
  ViewerId: string
  Pastels: IObservable<bool>
  Now: IObservable<DateTimeOffset>
  Dense: bool
  Images: ImageCacheService
  Flyout: Source voption
}

/// Renders one commit as a row of the log.
val view: props: Props -> Control

/// Renders a deleted commit placeholder.
val tombstone: unit -> Control

/// Everything one card row needs from its host page: where its events
/// go, the signed-in viewer, the live pastéis flag and clock, the
/// row's density, the image cache, and the hover card source.
[<Struct>]
type RowProps = {
  OnEvent: Event -> unit
  ViewerId: string
  Pasteis: IObservable<bool>
  Now: IObservable<DateTimeOffset>
  Dense: bool
  Images: ImageCacheService
  Flyout: Source voption
}

/// One commit row over a feed node: the card with the host's event
/// routing, or the deletion marker. Every feed page shares this;
/// only the routing differs.
val feedRow: props: RowProps -> node: CommitNode -> Control

/// The thread preview under a feed card: the replies to show (the
/// payload slice, or the list a host grew), as the site's slim chain
/// of ringed avatars, with the link that walks the rest of the
/// thread.
val replyBlock:
  props: RowProps * commit: Commit * shown: CommitNode list voption -> Control

/// The shared context of one reply chain: the row props, the nesting
/// depth, the lane and handle of the parent the chain hangs from, and
/// the host's own host for a parent's children (the thread page grows
/// its nodes through the cursor; the feed reads the payload slice).
[<Struct>]
type ChainCtx = {
  Props: RowProps
  Depth: int
  ParentLane: Lane option
  ParentHandle: string
  Grow: (ChainCtx -> Commit -> Control) voption
}

/// One run of sibling replies as the site's slim chain: ringed
/// avatars, branch elbows, sibling stubs, and lane spines below every
/// row the chain continues through.
val chainRows: ctx: ChainCtx * nodes: CommitNode list -> Control
