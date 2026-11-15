module DevNull.Client.Core.Emoji

/// One catalog group: its name and its emoji cells in catalog order.
[<Struct>]
type Group = { Name: string; Emoji: string array }

/// One search result: the emoji, the shortcode that matched it, and the
/// rank the shortcode earned — 0 for a prefix, 1 for a later word
/// prefix, 2 for a substring.
[<Struct>]
type Match = {
  Emoji: string
  Name: string
  Rank: int
}

/// One span of a plain text value: text keeps the app font; emoji takes
/// the emoji font.
type Run =
  | Text of string
  | Emoji of string

/// One insertion: the body, the selection it replaces, the emoji to
/// place there. Start and Finish may arrive in either order. The caret
/// stays a UTF-16 index for Avalonia.
[<Struct>]
type Insert = {
  Text: string
  Start: int
  Finish: int
  Emoji: string
}

/// The text and caret an insertion leaves.
[<Struct>]
type Inserted = { Text: string; Caret: int }

/// The eight groups in catalog order: Smileys & people, Animals &
/// nature, Food & drink, Travel & places, Activities, Objects,
/// Symbols, and Flags. The catalog is embedded in the assembly and
/// parsed once; a cell can share its emoji with another group.
val groups: Group array

/// The shortcode table in catalog order: every name and the emoji it
/// names. An emoji can carry more than one name.
val names: (string * string) array

/// The shortcode for an emoji: the first name in table order, or the
/// emoji itself when the table has no name for it.
val nameOf: emoji: string -> string

/// The query as search reads it: trimmed, lowercased, and stripped of
/// leading and trailing colons.
val normalize: raw: string -> string

/// The emoji that match the query, one per emoji, ranked by the best
/// name it carries: prefix names first, then later word prefixes, then
/// substrings; within a rank the shortest name wins and equal lengths
/// break by ordinal name. A blank query answers no results.
val search: query: string -> Match array

/// Inserts the emoji over the range between Start and Finish, or at
/// the caret when the range is empty, and answers the text with the
/// caret after the emoji. The range is ordered and clamped into the
/// text, so a selection dragged in either direction is replaced.
val insertAt: insert: Insert -> Inserted

/// Splits plain text into text and emoji runs by grapheme. A grapheme
/// with the emoji selector, a keycap, a ZWJ, a skin tone, a regional
/// indicator, or a default emoji-presentation code point is emoji.
val runs: text: string -> Run list
