module DevNull.Client.Workbench.RepositoryHome

open System
open Avalonia.Controls
open MVUish.Avalonia
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Env
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore

/// One people list a repository home opens in the inspector's place.
type PeopleKind =
  | Followers
  | Following

/// One open people list: the kind it shows, its loaded rows, its
/// cursor, whether a read is in flight, and the failure it carries.
type PeopleList = {
  Kind: PeopleKind
  Loading: bool
  Error: AppError option
  Items: User list
  Cursor: string option
}

/// The repository home's state: the handle, the profile read and the
/// viewer's relation to it, the follow call, the user's commits with
/// their cursor, the open people list, and the clock the rows read
/// against.
type Model = {
  Handle: string
  Loading: bool
  Error: AppError option
  Details: ProfileDetails option
  Commits: CommitNode list
  CommitsCursor: string option
  CommitsLoading: bool
  CommitsError: AppError option
  FollowBusy: bool
  FollowError: AppError option
  People: PeopleList option
  PollChoices: Map<string, string>
  Now: DateTimeOffset
}

/// The home's messages: the profile and commit reads, the follow call
/// and its answer, the people lists and their cursors, the back and
/// open intents, and the clock.
type Msg =
  | Shown of Result<ProfileDetails, AppError>
  | CommitsFetched of Result<Page<CommitNode>, AppError>
  | MoreCommitsRequested
  | CommitsAppended of Result<Page<CommitNode>, AppError>
  | FollowToggled
  | FollowDone of wanted: bool * Result<unit, AppError>
  | FollowersRequested
  | FollowingRequested
  | PeopleFetched of kind: PeopleKind * Result<Page<User>, AppError>
  | MorePeopleRequested
  | PeopleAppended of kind: PeopleKind * Result<Page<User>, AppError>
  | PeopleClosed
  | OpenRepository of handle: string
  | Opened of Commit
  | QuoteRequested of Commit
  | ExternalOpened of url: string
  | PollChoiceChanged of Commit * PollOption * chosen: bool
  | PollVoteRequested of Commit
  | PollVotedDone of Result<Commit, AppError>
  | CommitUpdated of Commit
  | CommitDeleted of string
  | ClockTicked of moment: DateTimeOffset

/// What one repository home needs from its host: the protocol workers,
/// the signed key file and viewer, the pastéis flag, the image cache,
/// the clock and zone, and the host's thread, quote, external, and
/// repository sinks plus the people-list visibility report.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession option
  Handle: string
  KeyFile: string
  ViewerId: string
  Pasteis: bool
  Images: ImageCacheService
  Now: IObservable<DateTimeOffset>
  Zone: Zone
  OnOpenThread: Commit -> unit
  OnQuote: Commit -> unit
  OnOpenExternal: string -> unit
  OnOpenRepository: string -> unit
  OnListChanged: bool -> unit
  OnDialog: RowActions.Dialog -> unit
}

/// The home's two regions: the center profile page and the people list
/// the host places in the inspector's place.
[<Struct>]
type View = { Center: Control; Lists: Control }

/// True when the viewer is not the profile's own user.
val inline canFollow: viewerId: string * details: ProfileDetails -> bool

/// The home's first state, its profile read, and its first commit page.
val init: props: Props<'env> -> Model * Cmd<Msg>

/// The home's one update function.
val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The home's inlet: the host's clock ticks.
val subscribe: props: Props<'env> -> model: Model -> Sub<Msg>

/// The home's center profile page and its people list.
val view:
  props: Props<'env> ->
  model: IObservable<Model> ->
  dispatch: Dispatch<Msg> ->
    View

/// The home's program. The host starts it with `Programs.runPage` and
/// keeps the returned handle.
val program: props: Props<'env> -> Program<unit, Model, Msg, View>
