module DevNull.Client.Components.ThreadChrome

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.RemotesState

/// The access bar's inputs: the partner, the access the thread
/// carries, and how a fetched request settles.
[<Struct>]
type AccessBarProps = {
  Partner: User
  Access: PushAccess
  OnSettle: User * bool -> unit
}

/// The pinned banner's inputs: the thread's live state and how the
/// panel opens.
[<Struct>]
type PinnedBannerProps = {
  Thread: IObservable<ThreadState>
  OnOpen: unit -> unit
}

/// The pinned panel's inputs: the thread's live state, whether it
/// shows, and how it closes.
[<Struct>]
type PinnedPanelProps = {
  Thread: IObservable<ThreadState>
  Shown: IObservable<bool>
  OnClose: unit -> unit
}

/// The first-pin dialog's inputs: whether it stands open, the call
/// in flight, and the two answers.
[<Struct>]
type PinDialogProps = {
  Open: IObservable<bool>
  Busy: IObservable<bool>
  OnConfirm: bool -> unit
  OnCancel: unit -> unit
}

/// One destructive confirm: the pick that opens it, its question and
/// consequences, the destructive verb and command, and whether a call
/// is in flight that keeps the verb disabled.
[<Struct>]
type ConfirmProps = {
  Label: string
  Title: string
  Words: string
  Verb: string
  OnConfirm: unit -> unit
  Busy: IObservable<bool>
}

/// The bar that takes the composer's place when the viewer cannot
/// push: a fetched request to settle, or the word on why. The
/// server's copy verbatim.
val accessBar: props: AccessBarProps -> Control

/// One quiet line the menus say when the server refuses, shown under
/// the pane's header.
val notice: notice: IObservable<string option> -> Control

/// The pinned banner row over an org thread: `pinned · {count}` with
/// the latest pin's author and a snippet. A click opens the pinned
/// panel — the banner and the panel are the only pin surface.
val pinnedBanner: props: PinnedBannerProps -> Control

/// The pinned panel over the reading: the org's pinned messages under
/// their count, with the close control.
val pinnedPanel: props: PinnedPanelProps -> Control

/// The first-pin dialog: the site's own question, the choice that
/// never asks again, and the two buttons. It stands over the pane
/// while one pin waits for its answer.
val pinDialog: props: PinDialogProps -> Control

/// One menu pick as a button row.
val menuPick: label: string * onPick: (unit -> unit) -> Control

/// One destructive pick: the site's own word first, so one click
/// never lands it. The flyout carries the question, the
/// consequences, and the Cancel and destructive buttons.
val confirmPick: props: ConfirmProps -> Control
