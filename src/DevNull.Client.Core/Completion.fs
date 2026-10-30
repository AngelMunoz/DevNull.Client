module DevNull.Client.Core.Completion

open System
open System.Text.RegularExpressions

[<Struct>]
type Trigger =
  | Mention of start: int * query: string
  | IssueRef of start: int * digits: string

// The "@handle" being typed right before the caret, kept in step with
// the server's own mention rules: an `@` that starts the text or
// follows anything but a letter, digit, underscore, dot, slash, at, or
// dash, with up to the thirty-nine characters a handle holds.
let mentionBeforeCaret =
  Regex("(?:^|[^\\p{L}\\p{N}_./@-])@([A-Za-z0-9][A-Za-z0-9-]{0,38}|)$")

// A fenced block or inline span of code that has settled, kept in
// step with the server's own code-span rules: a block runs from a
// line that starts a fence to the line that closes it, a span runs
// between two backticks on one line.
let committedCode =
  "(?<![^\\n])(`{3,})[^`\\r\\n]*\\r?\\n(?:[\\s\\S]*?\\r?\\n)?\\1`*[ \\t]*(?![^\\r\\n])|`[^`\\r\\n]+`"

let codeSplitter = Regex(committedCode)

// The same split while the text is being typed: a block or span still
// open runs to the end of the text, so code being written reads as
// code too.
let typedSplitter =
  Regex(committedCode + "|(?<![^\\n])```[\\s\\S]*$|`[^`\\r\\n]*$")

// The prose chunks of a split: the even parts, code spans left out.
// The chunk nearest the caret holds what typing just placed there.
let proseChunks(splitter: Regex, text: string) : string list = [
  for at, part in Array.indexed(splitter.Split text) do
    if at % 2 = 0 then
      part
]

// The text with its settled code blanked to spaces of the same length,
// so what the prose holds keeps its place in the text.
let withoutCode(text: string) =
  codeSplitter.Split text
  |> Array.indexed
  |> Array.map(fun (at, part) ->
    if at % 2 = 0 then part else String(' ', part.Length))
  |> String.concat ""

// A hex color, kept in step with the site's own rules: three, four,
// six, or eight hex digits, where one of three or four also needs a
// letter or a leading zero — so "#123" stays an issue reference while
// "#000" is black — a whole word like a hashtag, with only punctuation
// allowed before the next space.
let colorPattern =
  Regex(
    "(?:(?<!\\S)|(?<=\\n>))[#\\uFF03]([0-9a-f]{8}|[0-9a-f]{6}|(?=[0-9]*[a-f]|0)[0-9a-f]{3,4})(?=\\p{P}*(?:[\\s\\u00AD\\u2060\\u200A-\\u200D\\u20E2]|$))",
    RegexOptions.IgnoreCase
  )

// The `#number` at the end of a prose chunk: digits one to ten long,
// none opening with a zero.
let issueReference = Regex("(?:^|[\\s([{])#([1-9][0-9]{0,9})$")

let wordCharacter = Regex("^[\\p{L}\\p{N}_/#-]")

let mentionAt(text: string, caret: int) =
  let matched = mentionBeforeCaret.Match(text.Substring(0, caret))

  if matched.Success then
    let query = matched.Groups[1].Value
    ValueSome(Mention(caret - query.Length - 1, query))
  else
    ValueNone

let issueAt(text: string, caret: int) =
  let nearest =
    proseChunks(typedSplitter, text.Substring(0, caret))
    |> List.tryLast
    |> Option.defaultValue ""

  let matched = issueReference.Match nearest

  let colored(start: int) =
    withoutCode text
    |> colorPattern.Matches
    |> Seq.exists(fun found -> found.Index = start)

  if not matched.Success then
    ValueNone
  else
    let followed =
      caret < text.Length && wordCharacter.IsMatch(text.Substring(caret, 1))

    let digits = matched.Groups[1].Value
    let start = caret - digits.Length - 1

    if followed || colored start then
      ValueNone
    else
      ValueSome(IssueRef(start, digits))

let triggerAt(text: string, caret: int) =
  match mentionAt(text, caret) with
  | ValueSome found -> ValueSome found
  | ValueNone -> issueAt(text, caret)

/// One completion's placement: where the trigger starts, the query it
/// holds, the token that replaces it, and how far past the token the
/// caret lands.
[<Struct>]
type Placement = {
  Start: int
  Query: string
  Token: string
  CaretPast: int
}

/// One completion's surgery: the token takes the trigger's place, one
/// spacing character follows unless whitespace already follows, and
/// the caret lands `CaretPast` characters past the token.
let completed(place: Placement, text: string) : string * int =
  let triggerEnd = place.Start + 1 + place.Query.Length
  let after = text.Substring triggerEnd

  let spacing =
    match Seq.tryHead after with
    | Some head when Char.IsWhiteSpace head -> ""
    | _ -> " "

  let next = text.Substring(0, place.Start) + place.Token + spacing + after

  next, place.Start + place.Token.Length + place.CaretPast

/// One trigger's own opening: the mark that opens it, where it starts,
/// and the query it holds.
let opening(trigger: Trigger) : char * int * string =
  match trigger with
  | Mention(start, query) -> '@', start, query
  | IssueRef(start, digits) -> '#', start, digits

/// True when the index sits inside the text.
let (|Inside|_|) (text: string) (index: int) =
  if index >= 0 && index < text.Length then
    Some index
  else
    None

/// True when the mark sits at the index.
let (|Marked|_|) (text: string) (mark: char) (index: int) =
  if index >= 0 && index < text.Length && text[index] = mark then
    Some mark
  else
    None

/// True when the query sits right after the index.
let (|QueryAt|_|) (text: string) (query: string) (index: int) =
  if
    index >= 0
    && index + 1 + query.Length <= text.Length
    && text.Substring(index + 1, query.Length) = query
  then
    Some index
  else
    None

/// A trigger the text still holds: its mark at the start and its query
/// right after it.
let (|Held|_|) (text: string) (trigger: Trigger) =
  let mark, start, query = opening trigger

  match start with
  | Inside text at & Marked text mark _ & QueryAt text query _ ->
    Some(at, query)
  | _ -> None

let mentionCompleted(text: string, trigger: Trigger, handle: string) =
  match trigger with
  | Mention _ & Held text (start, query) ->
    completed(
      {
        Start = start
        Query = query
        Token = "@" + handle
        CaretPast = 1
      },
      text
    )
  | IssueRef _ // an issue reference never completes as a mention
  | Mention _ -> // the text no longer holds the trigger
    text, text.Length

let issueCompleted(text: string, trigger: Trigger, url: string) =
  match trigger with
  | IssueRef _ & Held text (start, digits) ->
    completed(
      {
        Start = start
        Query = digits
        Token = url
        CaretPast = 0
      },
      text
    )
  | Mention _ // a mention never completes as an issue reference
  | IssueRef _ -> // the text no longer holds the trigger
    text, text.Length
