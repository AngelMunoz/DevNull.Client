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
