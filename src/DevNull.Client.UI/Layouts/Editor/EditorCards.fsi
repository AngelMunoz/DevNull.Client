module DevNull.Client.Editor.EditorCards

open System
open Avalonia.Controls
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.CommitMenu
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore
open DevNull.Client.Components.CommitActions
open DevNull.Client.Components.TranslationModel
open MVUish.Avalonia

/// The web card surface an editor row stands on. It selects the menu
/// rows and the pin offer that surface carries.
type Surface =
  | Feed
  | Detail
  | Saved
  | Profile

/// The card actions one editor tab keeps: busy slots, poll choices,
/// author relations, translations, and the two dialog slots.
type State

/// The parts of an action that belong to the tab's own row data.
type Effect =
  | CommitChanged of Commit
  | CommitDeleted of commitId: string
  | PinChanged of pinned: bool * commit: Commit
  | Failed of AppError

type Msg =
  | StarToggled of Commit
  | StarDone of Result<Commit, AppError>
  | PastelGiven of Commit
  | PastelDone of Result<PastelResult, AppError>
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PollVotedDone of Result<Commit, AppError>
  | TranslateRequested of Commit
  | OriginalRequested of Commit
  | TranslationRecalled of Commit * Book option
  | TranslationDone of Commit * Result<TranslationPayload, AppError>
  | ShareOpened of Commit
  | ShareClosed
  | Shared
  | ReportOpened of Commit
  | ReportClosed
  | MenuOpened of Commit
  | RelationLoaded of handle: string * AuthorRelation voption
  | AuthorFollowToggled of Commit
  | AuthorFollowDone of Commit * Result<unit, AppError>
  | AuthorMuteToggled of Commit
  | AuthorMuteDone of Commit * Result<MuteResult, AppError>
  | AuthorBlockRequested of Commit
  | AuthorBlockDone of Commit * Result<BlockResult, AppError>
  | DeleteRequested of Commit
  | Deleted of commitId: string * Result<unit, AppError>
  | PinToggled of Commit
  | PinDone of pinned: bool * Result<Commit, AppError>
  | ClockTicked of moment: DateTimeOffset

/// What one tab supplies to the shared card actions.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideRemotes
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideBrowser> = {
  Env: 'env
  Session: SignedInSession
  Zone: Zone
  Surface: Surface
  Now: unit -> DateTimeOffset
  OnOpenRepo: string -> unit
}

/// The row inlet: the surface, the live state, the dispatch, and the
/// tab's fallback for the events the shared actions do not own.
type RowArgs<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideRemotes
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideBrowser> = {
  Props: Props<'env>
  State: IObservable<State>
  Dispatch: Dispatch<Msg>
  OnEvent: Event -> unit
}

/// No action in flight, no relation read, and no translation.
val empty: State

/// No action in flight, no relation read, and every cached translation
/// the environment holds, ready for the first card.
val initial: props: Props<'env> -> State

/// Applies one card action. The returned effects belong to the tab's
/// own rows and update its commit list or error state.
val update:
  props: Props<'env> * msg: Msg * state: State -> State * Cmd<Msg> * Effect list

/// Wraps a card command into one tab's message type.
val mapCmd: map: ('msg -> 'outer) * cmd: Cmd<'msg> -> Cmd<'outer>

/// The row properties every editor surface shares: stars, votes, pastels,
/// the More menu, translations, the author flyout, and the surface's pin
/// offer.
val row: args: RowArgs<'env> -> RowProps

/// Routes the card events the shared actions own, then hands the rest to
/// the tab's own fallback.
val event: dispatch: Dispatch<Msg> * fallback: (Event -> unit) -> Event -> unit

/// The share and report dialogs over one tab.
val layer:
  props: Props<'env> * state: IObservable<State> * dispatch: Dispatch<Msg> ->
    Control
