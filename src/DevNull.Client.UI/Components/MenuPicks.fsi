module DevNull.Client.Components.MenuPicks

open System
open Avalonia.Controls

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

/// One menu pick as a button row.
val menuPick: label: string * onPick: (unit -> unit) -> Control

/// One destructive pick: the site's own word first, so one click
/// never lands it. The flyout carries the question, the
/// consequences, and the Cancel and destructive buttons.
val confirmPick: props: ConfirmProps -> Control
