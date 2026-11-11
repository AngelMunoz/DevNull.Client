module DevNull.Client.Core.RemotesText

open System

/// The links one draft carries, first-wins order.
let linksOf(text: string) : string list =
  text.Split(' ', '\n', '\r', '\t')
  |> Array.choose(fun word ->
    match Uri.TryCreate(word.Trim(), UriKind.Absolute) with
    | true, NonNull uri when uri.Scheme = "http" || uri.Scheme = "https" ->
      Some(uri.ToString())
    | _ -> None)
  |> Array.distinct
  |> List.ofArray

type TextPiece =
  | Prose of string
  | Link of string

let inline isTrailingPunct(c: char) =
  c = '.' || c = ',' || c = ';' || c = ':' || c = '!' || c = '?'

let countChar(c: char, text: string) : int =
  text |> Seq.filter(fun held -> held = c) |> Seq.length

// The address without the prose punctuation that rides its end.
let stripLinkTail(candidate: string) : string =
  let rec strip(text: string) : string =
    if text = "" then
      text
    else
      let last = text[text.Length - 1]

      if isTrailingPunct last then
        strip(text.Substring(0, text.Length - 1))
      elif last = ')' && countChar(')', text) > countChar('(', text) then
        strip(text.Substring(0, text.Length - 1))
      else
        text

  strip candidate

// The link walk: every round takes the first scheme and recurses
// on what is left, a tail call.
[<TailCall>]
let rec splitLinksFrom(rest: string, pieces: TextPiece list) : TextPiece list =
  let httpsAt = rest.IndexOf("https://", StringComparison.OrdinalIgnoreCase)
  let httpAt = rest.IndexOf("http://", StringComparison.OrdinalIgnoreCase)

  let at =
    match httpsAt, httpAt with
    | -1, other -> other
    | other, -1 -> other
    | first, second -> min first second

  if at < 0 then
    (if rest = "" then pieces else Prose rest :: pieces)
  else
    let candidateEnd = rest.IndexOfAny([| ' '; '\n'; '\r'; '\t' |], at)

    let candidateEnd = if candidateEnd < 0 then rest.Length else candidateEnd
    let candidate = rest.Substring(at, candidateEnd - at)
    let link = stripLinkTail candidate

    let valid =
      match Uri.TryCreate(link, UriKind.Absolute) with
      | true, NonNull uri -> uri.Scheme = "http" || uri.Scheme = "https"
      | _ -> false

    if valid then
      let before = rest.Substring(0, at)

      let after =
        candidate.Substring(link.Length) + rest.Substring(candidateEnd)

      splitLinksFrom(
        after,
        Link link :: (if before = "" then pieces else Prose before :: pieces)
      )
    else
      splitLinksFrom(
        rest.Substring(at + 1),
        Prose(rest.Substring(0, at + 1)) :: pieces
      )

/// Splits one body into prose and links.
let splitLinks(text: string) : TextPiece list =
  splitLinksFrom(text, []) |> List.rev

let inline isWordChar(c: char) = Char.IsLetterOrDigit c || c = '_'

/// Where the word characters around the caret begin: the first index
/// past the run of letters, digits, and underscores that ends just
/// before the caret.
let tokenStartOf(text: string, caret: int) : int =
  let rec back(at: int) =
    if at >= 0 && isWordChar text[at] then
      back(at - 1)
    else
      at + 1

  back(caret - 1)

/// The `@token` the caret stands in, when one does.
let mentionOf(text: string, caret: int) : string option =
  let tokenStart = tokenStartOf(text, caret)

  let opens =
    tokenStart > 0
    && text[tokenStart - 1] = '@'
    && (tokenStart - 1 = 0 || Char.IsWhiteSpace text[tokenStart - 2])

  if opens then
    let upto = min caret text.Length

    Some(text.Substring(tokenStart, upto - tokenStart))
  else
    None

/// One handle's completion of the mention at the caret.
let mentionCompleted(text: string, caret: int, handle: string) : string * int =
  let tokenStart = tokenStartOf(text, caret)

  let from =
    if tokenStart > 0 && text[tokenStart - 1] = '@' then
      tokenStart - 1
    else
      tokenStart

  let replacement = "@" + handle + " "
  let cut = min caret text.Length

  (text.Substring(0, from) + replacement + text.Substring(cut),
   from + replacement.Length)

/// One link as the picker names it: the address without its scheme.
let plainLink(url: string) : string =
  url
  |> fun url ->
    if url.StartsWith "https://" then url.Substring 8
    elif url.StartsWith "http://" then url.Substring 7
    else url

/// What the typing line says: one name, two names, or the crowd.
let typingText(who: string list) : string =
  match who with
  | [] -> ""
  | [ only ] -> $"{only} is typing"
  | [ one; two ] -> $"{one} and {two} are typing"
  | crowd -> $"{List.length crowd} people are typing"
