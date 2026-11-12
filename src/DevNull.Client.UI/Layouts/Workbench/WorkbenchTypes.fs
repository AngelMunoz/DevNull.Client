namespace DevNull.Client.Workbench

open DevNull.Client.Core.ApiTypes

type Ref =
  | HistoryRef of LogKind
  | SavedRef
  | ScheduledRef
  | NotificationsRef
  | RepositoryRef of handle: string

type Checkout = {
  CommitId: string
  Page: CommitPage option
}

[<Struct>]
type WorkbenchState = { Ref: Ref; CheckoutId: string option }

module Ref =

  let label(ref: Ref) =
    match ref with
    | HistoryRef Latest -> "Latest"
    | HistoryRef Following -> "Following"
    | HistoryRef Trending -> "Trending"
    | SavedRef -> "Saved"
    | ScheduledRef -> "Scheduled"
    | NotificationsRef -> "Notifications"
    | RepositoryRef handle -> "@" + handle

  let stored(ref: Ref) =
    match ref with
    | HistoryRef log -> LogKinds.wire log
    | SavedRef -> "saved"
    | ScheduledRef -> "scheduled"
    | NotificationsRef -> "notifications"
    | RepositoryRef handle -> "repository:" + handle

  let log(ref: Ref) =
    match ref with
    | HistoryRef log -> Some log
    | SavedRef
    | ScheduledRef
    | NotificationsRef
    | RepositoryRef _ -> None

  let repository(ref: Ref) =
    match ref with
    | RepositoryRef handle -> Some handle
    | _ -> None

  let notifications(ref: Ref) =
    match ref with
    | NotificationsRef -> true
    | _ -> false

module Checkout =

  let commitId(checkout: Checkout) = checkout.CommitId

  let page(checkout: Checkout) = checkout.Page

module WorkbenchState =

  let refOf(state: WorkbenchState) = state.Ref

  let checkoutId(state: WorkbenchState) = state.CheckoutId

module Build =

  let history(log: LogKind) : Ref = HistoryRef log

  let saved: Ref = SavedRef

  let scheduled: Ref = ScheduledRef

  let notifications: Ref = NotificationsRef

  let repository(handle: string) : Ref = RepositoryRef handle

  let state(ref: Ref, checkoutId: string option) : WorkbenchState = {
    Ref = ref
    CheckoutId = checkoutId
  }

  let checkout(commitId: string, page: CommitPage option) : Checkout = {
    CommitId = commitId
    Page = page
  }
