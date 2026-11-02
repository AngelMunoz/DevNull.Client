module DevNull.Client.Components.PageChrome

open System
open Avalonia
open Avalonia.Controls
open DevNull.Client.Errors
open DevNull.Client.Theme

/// One line of page text: off, or the words it says.
[<Struct>]
type Line =
  | Off
  | Words of string

/// One content builder gated by the identity of its inputs: the source
/// passes only when `keyOf` changes, and each pass builds the control.
///
/// `keyOf` MUST cover every value `build` reads. An input left out of
/// the key leaves the built control stale until another keyed input
/// moves; an input wider than the build needs rebuilds the subtree
/// without a reason. A key compares by value when it is a string or a
/// value type, by reference when it is any other reference value
/// (records, lists, unions), and part by part when it is a tuple, so
/// several identities can travel as one key.
val rebuildOn:
  source: IObservable<'a> * keyOf: ('a -> 'k) * build: ('a -> Control) ->
    IObservable<Control>

/// One line of text in the given foreground class, following an
/// observable source.
val inline messagePanel: text: IObservable<string> * fg: StyleClass -> Control

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
