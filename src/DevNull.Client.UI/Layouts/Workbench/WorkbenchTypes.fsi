namespace DevNull.Client.Workbench

open DevNull.Client.Core.ApiTypes

/// One workbench ref: one of the three histories, the saved and
/// scheduled lists, the notifications list, or a user's repository.
type Ref =
  private
  | HistoryRef of LogKind
  | SavedRef
  | ScheduledRef
  | NotificationsRef
  | RepositoryRef of handle: string

/// One checked-out commit: its id always, the page once the inspector
/// has read it.
type Checkout = private {
  CommitId: string
  Page: CommitPage option
}

/// The part of the workbench that outlives a restart: the selected
/// ref and the checked-out commit id, when one is held.
[<Struct>]
type WorkbenchState = private {
  Ref: Ref
  CheckoutId: string option
}

module Ref =

  /// The name a reader sees: a log's title, a list's title, or the
  /// repository handle with its `@`.
  val label: ref: Ref -> string

  /// The stored token of one ref, the one `WorkbenchSettings.store`
  /// writes.
  val stored: ref: Ref -> string

  /// The log behind a history ref, when the ref is one.
  val log: ref: Ref -> LogKind option

  /// The handle behind a repository ref, when the ref is one.
  val repository: ref: Ref -> string option

module Checkout =

  /// The checked-out commit's id.
  val inline commitId: checkout: Checkout -> string

  /// The commit page once the inspector has read it, or none.
  val inline page: checkout: Checkout -> CommitPage option

module WorkbenchState =

  /// The selected ref.
  val inline refOf: state: WorkbenchState -> Ref

  /// The checked-out commit id, or none.
  val inline checkoutId: state: WorkbenchState -> string option

module internal Build =

  val history: log: LogKind -> Ref

  val saved: Ref

  val scheduled: Ref

  val notifications: Ref

  val repository: handle: string -> Ref

  val state: ref: Ref * checkoutId: string option -> WorkbenchState

  val checkout: commitId: string * page: CommitPage option -> Checkout
