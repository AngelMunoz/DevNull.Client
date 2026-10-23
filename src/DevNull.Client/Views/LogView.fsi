module DevNull.Client.Views.LogView

open System
open Avalonia.Controls
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.Profiles
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Timeline
open DevNull.Client.UiClock
open MVUish.Avalonia
open Navs

/// The log page: one of the three logs, its timeline, and its pager.
/// The page needs the signed request workers and the navigation target.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLastVisit
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideSshKeys> = {
  Env: 'env
  Log: LogKind
  Session: SignedInSession
  Nav: INavigable
  /// The route the page lives in; the page's program lifetime lands
  /// in the route's disposable bag, so leaving or evicting the route
  /// stops the program.
  Context: RouteContext
}

/// One row of the log feed: a day separator, a commit on the rail, a
/// deletion marker, or the last-visit separator.
type LogRow =
  | Day of string
  | Node of Commit
  | TombstoneRow of Tombstone
  | LastVisitRow

/// The visit the marker reads: when it was and who is reading.
[<Struct>]
type VisitMarker = {
  VisitedAt: DateTimeOffset
  ViewerId: string
}

/// The page's state logic, exported so the update stays testable
/// without a running page.
module State =

  /// One thread preview the page grew past the payload slice: the
  /// replies shown and the cursor the next page asks for.
  [<Struct>]
  type PreviewState = {
    Shown: CommitNode list
    Cursor: string option
  }

  /// The state of one log page: the loaded items, the pager cursor, the
  /// head the since-poll checks against, the new-commit count the pill
  /// holds, and whether the log is ranked.
  type Model = {
    Loading: bool
    Error: AppError option
    Items: CommitNode list
    NextCursor: string option
    Head: string option
    NewCount: int option
    Ranked: bool
    /// Whether the page sits in the visual tree: the since-poll pauses
    /// while it does not.
    Attached: bool
    /// The shared clock's last tick, for the relative times.
    Now: DateTimeOffset
    /// Whether the viewer's pastel controls show, live from the session.
    Pasteis: bool
    /// The commit whose inline reply composer is open, by id.
    ReplyingTo: string option
    /// The visit captured when the page mounted, for the marker row.
    Marker: DateTimeOffset option
    /// The thread previews the page grew past the payload slice, by
    /// parent id.
    Previews: Map<string, PreviewState>
  }

  /// The messages of the log page, including the since-poll pair.
  type Msg =
    | Fetched of Result<LogPage, AppError>
    | ReloadRequested
    | Appended of Result<LogPage, AppError>
    | MoreRequested
    | StarToggled of Commit
    | StarDone of Result<Commit, AppError>
    | PastelGiven of Commit
    | PastelDone of Result<PastelResult, AppError>
    | CheckNew
    | NewChecked of Result<LogPage, AppError>
    | NewRequested
    | NewFetched of Result<LogPage, AppError>
    | AttachedChanged of attached: bool
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState
    | ReplyToggled of Commit
    | ReplyPosted of commitId: string
    | PollVoteGiven of Commit * PollOption
    | PollVotedDone of Result<Commit, AppError>
    | PreviewMoreRequested of Commit
    | PreviewLoaded of commitId: string * Result<Page<CommitNode>, AppError>

  /// The fresh page state.
  val initial: Model

  /// Interleaves day separators between the commits of different days,
  /// carrying the day across tombstones. A ranked log is score ordered,
  /// its days interleave, so it rides without separators. The
  /// last-visit marker rides above the first already-seen commit.
  val toRows:
    items: CommitNode list * ranked: bool * marker: VisitMarker option ->
      LogRow list

  /// The stable identity of one feed row.
  val keyOf: logRow: LogRow -> string

  /// Applies one message.
  val update:
    props: Props<'env> -> msg: Msg -> model: Model -> Model * Cmd<Msg>
      when 'env :> IProvideClock
      and 'env :> IProvideNonceSource
      and 'env :> IProvideSigner
      and 'env :> IProvideHttp
      and 'env :> IProvideSession
      and 'env :> IProvideUiClock
      and 'env :> IProvideImageCache
      and 'env :> IProvideAttachmentSources
      and 'env :> IProvideLastVisit
      and 'env :> IProvideProfiles
      and 'env :> IProvideTimeline
      and 'env :> IProvideSshKeys

/// One log timeline, self-contained: mounts the MVUish program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideLastVisit
  and 'env :> IProvideProfiles
  and 'env :> IProvideTimeline
  and 'env :> IProvideSshKeys> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
