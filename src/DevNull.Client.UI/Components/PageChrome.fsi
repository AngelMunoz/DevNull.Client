module DevNull.Client.Components.PageChrome

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Media
open DevNull.Client.Errors
open DevNull.Client.Theme

/// One line of page text: off, or the words it says.
[<Struct>]
type Line =
  | Off
  | Words of string

/// One gated slice with one upstream subscription, replayed to a
/// binding that joins after the last emission.
val share: source: IObservable<'T> -> IObservable<'T>

/// One line of text in the given foreground class, following an
/// observable source.
val messagePanel: text: IObservable<string> * fg: StyleClass -> Control

/// The page's error card: one plain line per failure, in the danger
/// tone, shown only while the page carries an error.
val errorPanel: error: IObservable<AppError option> -> Control

/// The feed's load-more button, shown while the page holds a cursor.
val loadMore: visible: IObservable<bool> * onClick: (unit -> unit) -> Control

/// Everything one list page renders: the pinned header built from the
/// page's own pieces, and the scrolling body built once. `Padding` is
/// the pinned area's inset, so a page whose rows run edge to edge keeps
/// its own gutter.
type PageProps<'m> = {
  Model: IObservable<'m>
  /// The title line, as a projection of the model; empty hides it.
  TitleOf: 'm -> string
  Padding: Thickness
  ErrorOf: 'm -> AppError option
  /// The busy line, when the page has one.
  Busy: 'm -> Line
  /// The empty line, when the feed has one.
  Empty: 'm -> Line
  /// The page's own header pieces, after the title, before the error.
  Header: Control list
  /// The scrolling body.
  Body: Control
}

/// Builds one list page: the pinned header over the body.
val page: props: PageProps<'m> -> Control
