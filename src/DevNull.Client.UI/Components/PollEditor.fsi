module DevNull.Client.Components.PollEditor

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes

/// One poll being built: two to four options and the time it stays
/// open.
[<Struct>]
type Draft = {
  Options: string list
  Duration: PollDuration
}

/// The poll builder's live draft and what its controls ask for.
[<Struct>]
type Props = {
  Draft: IObservable<Draft option>
  OnOptionChanged: int * string -> unit
  OnOptionAdded: unit -> unit
  OnOptionRemoved: int -> unit
  OnDurationPicked: PollDuration -> unit
  OnClosed: unit -> unit
}

/// The poll builder under the body: the bordered fieldset, its option
/// rows, the add button, and the footer with the length select and the
/// remove action.
val view: props: Props -> Control
