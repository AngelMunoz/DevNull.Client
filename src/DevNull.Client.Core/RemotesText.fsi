module DevNull.Client.Core.RemotesText

/// The links one draft carries, first-wins order: every absolute
/// http or https address a word spells, each once.
val linksOf: text: string -> string list

/// One piece of a body after the link split: plain prose, or one
/// absolute http or https address.
type TextPiece =
  | Prose of string
  | Link of string

/// Splits one body into prose and links. Only `http://` and
/// `https://` addresses link; a trailing `.`, `,`, `;`, `:`, `!`,
/// or `?` stays prose, and a trailing `)` stays prose unless the
/// address itself holds a balanced pair. An address with no
/// scheme, and an email address, stay prose.
val splitLinks: text: string -> TextPiece list

/// The `@token` the caret stands in, when one does: the letters after
/// an `@` that opens a word, up to the caret. None once the token
/// closed or the caret sits elsewhere.
val mentionOf: text: string * caret: int -> string option

/// One handle's completion of the mention at the caret: the rewritten
/// text and where the caret lands after it.
val mentionCompleted: text: string * caret: int * handle: string -> string * int

/// One link as the picker names it: the address without its scheme.
val plainLink: url: string -> string

/// What the typing line says: one name, two names, or the crowd. The
/// dots are the view's own beat, never part of the word.
val typingText: who: string list -> string
