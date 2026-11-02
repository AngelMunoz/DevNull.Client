module DevNull.Client.Status

open System
open Avalonia.Controls
open DevNull.Client.Theme

/// One line of text in the given foreground class, following an
/// observable source.
val panel: text: IObservable<string> * fg: StyleClass -> Control

/// The busy panel: one quiet line of text, centered.
val busy: message: string -> Control

/// The empty panel: one line of muted text, centered.
val empty: message: string -> Control

/// The placeholder panel for a route the API does not serve yet.
val soon: message: string -> Control
