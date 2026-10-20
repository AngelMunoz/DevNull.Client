module DevNull.Client.Status

open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media

/// The busy panel: one spinner line of text, centered.
val busy: message: string -> Control

/// The error panel: one line of plain text, centered.
val error: message: string -> Control

/// The empty panel: one line of muted text, centered.
val empty: message: string -> Control

/// The placeholder panel for a route the API does not serve yet.
val soon: message: string -> Control
