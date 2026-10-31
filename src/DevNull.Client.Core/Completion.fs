module DevNull.Client.Core.Completion

open System
open System.Text.RegularExpressions

/// The digits of an issue reference: one to ten digits, none opening
/// with a zero.
type IssueNumber = { Value: string }

module IssueNumber =
  /// The digits as typed.
  let value(number: IssueNumber) = number.Value

  /// Parses the digits the server accepts: one to ten ASCII digits,
  /// none opening with a zero.
  let parse(raw: string) : IssueNumber voption =
    let asciiDigits =
      raw.Length >= 1
      && raw.Length <= 10
      && raw[0] <> '0'
      && Seq.forall (fun character -> character >= '0' && character <= '9') raw

    if asciiDigits then ValueSome { Value = raw } else ValueNone

[<Struct>]
type Trigger =
  | Mention of start: int * query: string
  | IssueRef of start: int * digits: IssueNumber

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
      match IssueNumber.parse digits with
      | ValueSome number -> ValueSome(IssueRef(start, number))
      | ValueNone -> ValueNone

let triggerAt(text: string, caret: int) =
  if caret < 0 || caret > text.Length then
    ValueNone
  else
    match mentionAt(text, caret) with
    | ValueSome found -> ValueSome found
    | ValueNone -> issueAt(text, caret)

/// The trigger as the text must still hold it: where it starts, the
/// mark that opens it, and its query.
[<Struct>]
type Held = {
  Start: int
  Mark: char
  Query: string
}

/// True when the text still holds the trigger: its mark at the start
/// and its query right after it.
let held(text: string, value: Held) =
  value.Start >= 0
  && value.Start + 1 + value.Query.Length <= text.Length
  && text[value.Start] = value.Mark
  && text.Substring(value.Start + 1, value.Query.Length) = value.Query

/// One completion's surgery: what replaces the trigger, and how its
/// caret follows the spacing.
[<Struct>]
type Replacement = {
  Start: int
  QueryLength: int
  Token: string
  CaretAfterSpacing: bool
}

/// The token takes the trigger's place, one spacing character follows
/// unless whitespace already follows, and the caret lands after that
/// spacing for an issue, or one character past the token for a
/// mention.
let replace(text: string, place: Replacement) =
  let triggerEnd = place.Start + 1 + place.QueryLength
  let after = text.Substring triggerEnd

  let spacing =
    match Seq.tryHead after with
    | Some head when Char.IsWhiteSpace head -> ""
    | _ -> " "

  let next = text.Substring(0, place.Start) + place.Token + spacing + after

  let caret =
    if place.CaretAfterSpacing then
      place.Start + place.Token.Length + spacing.Length
    else
      place.Start + place.Token.Length + 1

  next, caret

/// The caret the trigger held: the character right after its query,
/// clamped into the text.
let triggerCaret(text: string, trigger: Trigger) =
  let caret =
    match trigger with
    | Mention(start, query) -> start + 1 + query.Length
    | IssueRef(start, digits) -> start + 1 + (IssueNumber.value digits).Length

  min text.Length (max 0 caret)

let mentionCompleted(text: string, trigger: Trigger, handle: string) =
  match trigger with
  | Mention(start, query) when
    held(
      text,
      {
        Start = start
        Mark = '@'
        Query = query
      }
    )
    ->
    replace(
      text,
      {
        Start = start
        QueryLength = query.Length
        Token = "@" + handle
        CaretAfterSpacing = false
      }
    )
  | IssueRef _ // an issue reference never completes as a mention
  | Mention _ -> // the text no longer holds the trigger
    text, triggerCaret(text, trigger)

let issueCompleted(text: string, trigger: Trigger, url: string) =
  match trigger with
  | IssueRef(start, digits) when
    held(
      text,
      {
        Start = start
        Mark = '#'
        Query = IssueNumber.value digits
      }
    )
    ->
    let number = IssueNumber.value digits

    replace(
      text,
      {
        Start = start
        QueryLength = number.Length
        Token = url
        CaretAfterSpacing = true
      }
    )
  | Mention _ // a mention never completes as an issue reference
  | IssueRef _ -> // the text no longer holds the trigger
    text, triggerCaret(text, trigger)
