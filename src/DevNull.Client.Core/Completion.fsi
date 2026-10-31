module DevNull.Client.Core.Completion

/// The digits of an issue reference: one to ten digits, none opening
/// with a zero.
type IssueNumber

module IssueNumber =
  /// The digits as typed.
  val value: IssueNumber -> string

  /// Parses the digits the server accepts: one to ten ASCII digits,
  /// none opening with a zero.
  val parse: raw: string -> IssueNumber voption

/// One completion trigger the caret sits in: the `@mention` being
/// typed — an empty query for a bare `@` — or the `#number` reference
/// being typed.
[<Struct>]
type Trigger =
  | Mention of start: int * query: string
  | IssueRef of start: int * digits: IssueNumber

/// The completion trigger the caret sits in, by the site's own rules:
/// an `@` at the start of the text or after any character but a
/// letter, digit, underscore, dot, slash, at, or dash, its query at
/// most the thirty-nine characters a handle holds; a `#` followed by
/// digits — one to ten long, none opening with a zero — at the start
/// of the text or after whitespace or an opening bracket, never
/// inside code, never a color like `#000` or `#ff8a4c`, and never with
/// a word character right after the caret.
val triggerAt: text: string * caret: int -> Trigger voption

/// Completes a mention: the `@query` the trigger holds becomes
/// `@handle` plus one spacing character unless whitespace already
/// follows, and the answer carries the text and the caret just past
/// that spacing. A trigger the text no longer holds answers the text
/// unchanged and the caret at the trigger's end.
val mentionCompleted:
  text: string * trigger: Trigger * handle: string -> string * int

/// Completes an issue reference: the `#digits` the trigger holds
/// becomes the issue's address plus one spacing character unless
/// whitespace already follows, and the answer carries the text and the
/// caret past that spacing when one was added, or at the address end
/// when whitespace already follows. A trigger the text no longer holds
/// answers the text unchanged and the caret at the trigger's end.
val issueCompleted:
  text: string * trigger: Trigger * url: string -> string * int
