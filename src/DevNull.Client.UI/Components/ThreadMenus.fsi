module DevNull.Client.Components.ThreadMenus

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache
open DevNull.Client.RemotesState

/// What the conversation menu reports up.
[<Struct>]
type MenuActions = {
  ViewProfile: User -> unit
  Pin: bool -> unit
  Archive: bool -> unit
  Mute: MuteDuration -> unit
  Block: User -> unit
  Leave: unit -> unit
}

/// The conversation menu's inputs: the target it describes, the live
/// pin and archive state, and the actions it reports.
[<Struct>]
type ActionsMenuProps = {
  Target: ChatTarget
  Pinned: IObservable<bool>
  Archived: IObservable<bool>
  Actions: MenuActions
}

/// What the org menu reports up.
[<Struct>]
type OrgActions = {
  RenameChanged: string -> unit
  Rename: unit -> unit
  MemberChanged: string -> unit
  Add: unit -> unit
  ShareChanged: bool -> unit
  Role: string * OrgRole -> unit
  Remove: string * bool -> unit
}

/// The org menu's inputs: the org, the viewer, the live thread, the
/// three model slices its fields follow, and the actions it reports.
[<Struct>]
type OrgMenuProps = {
  Org: OrgRef
  ViewerId: string
  ViewerHandle: string
  Thread: IObservable<ThreadState>
  Rename: IObservable<string>
  Picked: IObservable<string list>
  Shared: IObservable<bool>
  Actions: OrgActions
}

/// The conversation's own menu: view profile, pin, stash, mutes,
/// block or leave.
val actionsMenu: props: ActionsMenuProps -> Control

/// The org header's member stack: the org's members, ringed and
/// overlapped, two rows deep when the org is wide.
val memberStack:
  cache: ImageCacheService * members: ThreadMember list -> Control

/// The maintainer's menu on an org header: rename, add members with
/// the share-history choice, a role change per member, remove, and
/// leave.
val orgMenu: props: OrgMenuProps -> Control
