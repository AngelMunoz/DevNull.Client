module DevNull.Client.Layouts.Editor.RepoTab

open System
open DevNull.Client.Editor
open Avalonia.Controls
open Avalonia.Threading
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Env
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore
open MVUish.Avalonia

/// Which list a profile's count opens.
type FollowedList =
  | Followers
  | Following

/// One follower or following list inside the tab.
type ListState = {
  Kind: FollowedList option
  Items: User list
  Cursor: string option
  Loading: bool
  Error: AppError option
}

/// The repository tab's state: the profile, the handle's commits,
/// the open list, the follow call, and the clock.
type Model = {
  Loading: bool
  Error: AppError option
  Details: ProfileDetails option
  Commits: CommitNode list
  CommitsCursor: string option
  CommitsLoading: bool
  CommitsError: AppError option
  List: ListState
  FollowBusy: bool
  Cards: EditorCards.State
  Now: DateTimeOffset
}

/// The messages of one repository tab.
type Msg =
  | ProfileFetched of Result<ProfileDetails, AppError>
  | CommitsFetched of Result<Page<CommitNode>, AppError>
  | CommitsAppended of Result<Page<CommitNode>, AppError>
  | MoreCommits
  | FollowToggled
  | FollowDone of ProfileDetails * Result<unit, AppError>
  | ListToggled of FollowedList
  | ListFetched of FollowedList * Result<Page<User>, AppError>
  | ListAppended of FollowedList * Result<Page<User>, AppError>
  | MoreListRequested
  | CardMsg of EditorCards.Msg
  | ClockTicked of moment: DateTimeOffset

/// What one repository tab needs from its host: the signed workers,
/// the signed-in session, the handle, the zone, the shared clock, and
/// the two intents the tab reports.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideDispatcher
  and 'env :> IProvideRemotes
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideBrowser> = {
  Env: 'env
  Session: SignedInSession
  Handle: string
  Zone: Zone
  Now: unit -> DateTimeOffset
  OnOpenCommit: Commit -> unit
  OnReply: Commit -> unit
  OnQuote: Commit -> unit
  OnOpenRepo: string -> unit
}

/// The fresh state: the profile and the first commit page are on
/// their way.
val initial: Model

/// Starts the profile and first commit page reads.
val init: props: Props<'env> -> Model * Cmd<Msg>

/// Applies one message.
val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The tab's inlets from outside: the shared clock.
val subscribe: props: Props<'env> * model: Model -> Sub<Msg>

/// The tab's view: the identity header, the shields, the lists, the
/// pinned README, the contributions, and the history rows.
val view:
  props: Props<'env> * model: IObservable<Model> * dispatch: Dispatch<Msg> ->
    Control

/// The tab's program, with its update loop bound to the caller's
/// dispatcher.
val program: props: Props<'env> -> Program<unit, Model, Msg, Control>

/// One repository tab program, with its own lifecycle counter.
module Live =

  /// The programs mounted now, across the process.
  val current: unit -> int

  /// The process-wide stream of that count.
  val observe: IObservable<int>

/// Mounts one repository tab. The returned handle disposes the
/// program once; the layout calls it when the tab's host unmounts.
val mount: dispatcher: IDispatcher * props: Props<'env> -> Control * IDisposable
