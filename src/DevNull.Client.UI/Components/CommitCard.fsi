module DevNull.Client.Components.CommitCard

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache
open DevNull.Client.Formatting
open DevNull.Client.Components.CommitMenu
open DevNull.Client.Components.UserFlyout

/// The viewer's relation to one commit's author, as the More menu
/// asks for it: none until the page's read lands.
type AuthorRelation = DevNull.Client.Components.CommitMenu.AuthorRelation

/// The More actions one host offers a card: the share, report, and
/// delete commands, the follow, mute, and block commands for another
/// author, the relations the page has read (a function of the commit,
/// the shape the translation slot uses), and the commit id whose menu
/// action is in flight.
type MenuSlot = DevNull.Client.Components.CommitMenu.MenuSlot

/// What one card reports up: open the thread, reveal the reply
/// composer, walk more replies of a thread preview, start a quote,
/// choose a poll option, show the commit translated, or open the
/// More menu — the page's cue to load the author relation.
[<Struct>]
type Event =
  | Opened of Commit
  | ReplyToggled of Commit
  | MoreRepliesRequested of Commit
  | QuoteRequested of Commit
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | TranslateRequested of Commit
  | OriginalRequested of Commit
  | MenuOpened of Commit

/// The pin affordance one host may offer: the command the toggle runs,
/// and the commit id whose pin call is in flight, so its button
/// disables while the server answers.
[<Struct>]
type PinToggle = {
  Command: Commit -> unit
  Busy: IObservable<string option>
}

/// One host-run card action: the command its control runs, and the
/// commit id whose call is in flight, so the control disables while
/// the server answers.
[<Struct>]
type ActionSlot = {
  Command: Commit -> unit
  Busy: IObservable<string option>
}

/// What one card reads about a commit's translation: the body to show, the
/// notice, the Translate button's state, and the last failure.
[<Struct>]
type TranslationView = {
  /// The translated body to show, or none for the original.
  Body: TranslatedBody option
  /// The source language's English name, for the notice.
  SourceName: string option
  /// The Translate button shows.
  CanTranslate: bool
  /// The call for this commit runs now.
  Busy: bool
  /// The server asked to wait; the button is disabled until the minute passes.
  Waiting: bool
  /// The last failure, in the server's words.
  Error: string option
}

/// One commit row. `OnEvent` receives the intent events the host does
/// not run itself. Every card rides the graph rail with its author's
/// avatar; dense rows (the replies of a thread) shrink the node.
/// `ViewerId` is the signed-in user: the author of their own commit
/// sees a star count, never a toggle — the server refuses the star.
/// `Pastels` says whether the viewer's pastel controls show at all,
/// live so the flag can move under a built card; `Now` is the clock
/// the relative time follows; `Images` loads the author avatar and
/// the commit's images; `Flyout` is the hover card source for the
/// author, when the host has one. `PollChoice` is the option the page
/// model holds for this commit; `Pin` offers the profile pin on the
/// author's own top-level commits, where the host decides to show it at
/// all; `Star`, `Vote`, and `Pastel` are the host-run actions, and a
/// slot left absent leaves its control out of the card; `Stars` arms
/// the author's own star count to open the stargazers list, and
/// leaves it read-only when absent; `Menu` is the
/// More actions the host offers the card; `Translation` is what the
/// card shows about the commit's translation.
[<Struct>]
type Props = {
  Commit: Commit
  OnEvent: Event -> unit
  /// The host's opener for one external URL, the link preview's
  /// outside resource; the card never reaches the browser itself.
  OnOpenExternal: string -> unit
  ViewerId: string
  Pastels: IObservable<bool>
  Now: IObservable<DateTimeOffset>
  PollChoice: IObservable<string option>
  Dense: bool
  Images: ImageCacheService
  Flyout: Source voption
  Pin: PinToggle voption
  Star: ActionSlot voption
  /// The stargazers list the author's own star count opens; an absent
  /// slot leaves the count read-only.
  Stars: ActionSlot voption
  Vote: ActionSlot voption
  Pastel: ActionSlot voption
  /// The More actions the host offers this card — copy link, share,
  /// report, delete, follow, mute, block — under the `···` trigger; an
  /// absent slot draws nothing at the right end of the toolbar.
  Menu: MenuSlot voption
  Translation: IObservable<TranslationView voption>
  /// The zone the header's time tooltip shows.
  Zone: Zone
}

/// One poll choice change, in the shape the page model stores it.
[<Struct>]
type ChoiceChange = {
  Choices: Map<string, string>
  CommitId: string
  OptionId: string
  Chosen: bool
}

/// The choice map after one row reports its checked state. Clearing
/// removes only the option that was actually chosen, so the model's
/// uncheck of another row cannot clear the new choice.
val choose: change: ChoiceChange -> Map<string, string>

/// The option a page still holds for a commit, when the poll carries it.
val selectedOption:
  choices: Map<string, string> * commit: Commit -> PollOption option

/// Renders a deleted commit placeholder.
val tombstone: unit -> Control

/// How a body's mentions read: the accent color over the page surface,
/// or the plain foreground over an accent fill.
type MentionTone =
  | Accent
  | OnFill

/// The mention context one body shares: where a handle's hover card
/// attaches, how its accent reads, the mentions the text confirms,
/// and the host's opener for a link in the text, when the host has
/// one. Without an opener a link stays accent text.
[<Struct>]
type BodyArgs = {
  Flyout: Source voption
  Tone: MentionTone
  Mentions: Mention list
  OnOpenExternal: (string -> unit) voption
}

/// One body from its segments — the text with its mentions wrapped,
/// the code blocks boxed — whatever carries it: a commit card or a
/// chat bubble. A mention that names an organization opens the
/// organization page through the source's navigation; an unknown kind
/// stays plain accent text.
val body: args: BodyArgs * text: string * segments: Segment list -> Control

/// Everything one card row needs from its host page: where its events
/// go, the signed-in viewer, the live pastéis flag and clock, the poll
/// choices, the row's density, the image cache, the hover card
/// source, the profile pin affordance when the host shows one at all,
/// the host-run star, vote, and pastel actions, the stargazers list
/// the author's own star count opens, and the translation
/// each commit shows.
[<Struct>]
type RowProps = {
  OnEvent: Event -> unit
  /// The host's opener for one external URL, threaded to the card's
  /// link preview; the row never reaches the browser itself.
  OnOpenExternal: string -> unit
  ViewerId: string
  Pasteis: IObservable<bool>
  Now: IObservable<DateTimeOffset>
  PollChoices: IObservable<Map<string, string>>
  Dense: bool
  Images: ImageCacheService
  Flyout: Source voption
  Pin: PinToggle voption
  Star: ActionSlot voption
  /// The stargazers list the author's own star count opens; an absent
  /// slot leaves the count read-only.
  Stars: ActionSlot voption
  Vote: ActionSlot voption
  Pastel: ActionSlot voption
  Menu: MenuSlot voption
  Translation: IObservable<Commit -> TranslationView voption>
  Zone: Zone
}

/// One commit row over a feed node: the card with the host's actions
/// and event routing, or the deletion marker. Every feed page shares
/// this; only the routing differs.
val feedRow: props: RowProps -> node: CommitNode -> Control

/// The thread preview under a feed card: the replies to show (the
/// payload slice, or the list a host grew) as the site's slim chain —
/// ringed avatars branched off the parent's lane rail, sibling stubs
/// between them, nested sub-threads one step deeper — with the link
/// that walks the rest.
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
