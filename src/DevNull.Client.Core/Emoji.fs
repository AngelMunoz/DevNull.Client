module DevNull.Client.Core.Emoji

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Reflection
open System.Text
open System.Text.Json
open JDeck

[<Struct>]
type Group = { Name: string; Emoji: string array }

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

[<Struct>]
type Insert = {
  Text: string
  Start: int
  Finish: int
  Emoji: string
}

[<Struct>]
type Inserted = { Text: string; Caret: int }

[<Struct>]
type NameEntry = { Name: string; Emoji: string }

type Catalog = {
  Groups: Group array
  Names: (string * string) array
  First: Dictionary<string, string>
}

let groupDecoder(element: JsonElement) : Result<Group, JDeck.DecodeError> =
  decode {
    let! name = Required.Property.get ("name", Required.string) element
    let! emoji = Required.Property.list ("emoji", Required.string) element

    return {
      Name = name
      Emoji = Array.ofList emoji
    }
  }

let nameDecoder(element: JsonElement) : Result<NameEntry, JDeck.DecodeError> =
  decode {
    let! name = Required.Property.get ("name", Required.string) element
    let! emoji = Required.Property.get ("emoji", Required.string) element
    return { Name = name; Emoji = emoji }
  }

let makeCatalog(groups: Group list, names: NameEntry list) : Catalog =
  let entries =
    names |> List.map(fun entry -> entry.Name, entry.Emoji) |> Array.ofList

  let first = Dictionary<string, string>(StringComparer.Ordinal)

  for name, emoji in entries do
    if not(first.ContainsKey emoji) then
      first[emoji] <- name

  {
    Groups = Array.ofList groups
    Names = entries
    First = first
  }

let catalogDecoder(element: JsonElement) : Result<Catalog, JDeck.DecodeError> =
  decode {
    let! groups = Required.Property.list ("groups", groupDecoder) element
    let! names = Required.Property.list ("names", nameDecoder) element
    return makeCatalog(groups, names)
  }

let readCatalog() : Catalog =
  let assembly = Assembly.GetExecutingAssembly()

  match assembly.GetManifestResourceStream "emoji-catalog.json" with
  | null -> failwith "The embedded emoji catalog is missing."
  | stream ->
    use stream = stream
    use reader = new StreamReader(stream)
    let json = reader.ReadToEnd()

    match JDeck.Decoding.fromString(json, catalogDecoder) with
    | Ok catalog -> catalog
    | Error error ->
      failwith $"The embedded emoji catalog is invalid: {error.message}"

let catalog = lazy (readCatalog())

let groups = catalog.Value.Groups

let names = catalog.Value.Names

let nameOf(emoji: string) =
  match catalog.Value.First.TryGetValue emoji with
  | true, name -> name
  | _ -> emoji

let normalize(raw: string) = raw.Trim().ToLowerInvariant().Trim(':')

let rankOf(name: string, query: string) =
  let lower = name.ToLowerInvariant()

  let laterWordPrefix() =
    let words =
      lower.Split([| '_'; '-' |], StringSplitOptions.RemoveEmptyEntries)

    words.Length > 1
    && words
       |> Array.skip 1
       |> Array.exists(fun word ->
         word.StartsWith(query, StringComparison.Ordinal))

  let rules = [
    (fun () -> lower.StartsWith(query, StringComparison.Ordinal)), 0
    laterWordPrefix, 1
    (fun () -> lower.Contains(query, StringComparison.Ordinal)), 2
  ]

  rules
  |> List.tryPick(fun (matches, rank) -> if matches() then Some rank else None)
  |> Option.defaultValue 3

let compareMatches (a: Match) (b: Match) =
  let byRank = compare a.Rank b.Rank

  if byRank <> 0 then
    byRank
  else
    let byLength = compare a.Name.Length b.Name.Length

    if byLength <> 0 then
      byLength
    else
      String.CompareOrdinal(a.Name, b.Name)

let search(query: string) : Match array =
  let normalized = normalize query

  if normalized = "" then
    [||]
  else
    let best = Dictionary<string, Match>(StringComparer.Ordinal)

    for name, emoji in names do
      let rank = rankOf(name, normalized)

      if rank < 3 then
        let candidate = {
          Emoji = emoji
          Name = name
          Rank = rank
        }

        match best.TryGetValue emoji with
        | true, current ->
          if compareMatches candidate current < 0 then
            best[emoji] <- candidate
        | _ -> best[emoji] <- candidate

    best.Values |> Seq.toArray |> Array.sortWith compareMatches

let insertAt(insert: Insert) : Inserted =
  let text = insert.Text

  let first = min insert.Start insert.Finish
  let last = max insert.Start insert.Finish

  let start = max 0 (min first text.Length)

  let finish = max start (min last text.Length)

  let next = text.Substring(0, start) + insert.Emoji + text.Substring(finish)

  {
    Text = next
    Caret = start + insert.Emoji.Length
  }

/// The default emoji-presentation ranges: a grapheme whose first code
/// point is in one of these ranges renders as emoji without U+FE0F.
let private defaultEmojiRanges: (int * int) array = [|
  (0x231A, 0x231B)
  (0x23E9, 0x23EC)
  (0x23F0, 0x23F0)
  (0x23F3, 0x23F3)
  (0x25FD, 0x25FE)
  (0x2614, 0x2615)
  (0x2648, 0x2653)
  (0x267F, 0x267F)
  (0x2693, 0x2693)
  (0x26A1, 0x26A1)
  (0x26AA, 0x26AB)
  (0x26BD, 0x26BE)
  (0x26C4, 0x26C5)
  (0x26CE, 0x26CE)
  (0x26D4, 0x26D4)
  (0x26EA, 0x26EA)
  (0x26F2, 0x26F3)
  (0x26F5, 0x26F5)
  (0x26FA, 0x26FA)
  (0x26FD, 0x26FD)
  (0x2705, 0x2705)
  (0x270A, 0x270B)
  (0x2728, 0x2728)
  (0x274C, 0x274C)
  (0x274E, 0x274E)
  (0x2753, 0x2755)
  (0x2757, 0x2757)
  (0x2795, 0x2797)
  (0x27B0, 0x27B0)
  (0x27BF, 0x27BF)
  (0x2B1B, 0x2B1C)
  (0x2B50, 0x2B50)
  (0x2B55, 0x2B55)
  (0x1F004, 0x1F004)
  (0x1F0CF, 0x1F0CF)
  (0x1F18E, 0x1F18E)
  (0x1F191, 0x1F19A)
  (0x1F1E6, 0x1F1FF)
  (0x1F201, 0x1F201)
  (0x1F21A, 0x1F21A)
  (0x1F22F, 0x1F22F)
  (0x1F232, 0x1F23A)
  (0x1F250, 0x1F251)
  (0x1F300, 0x1FAFF)
|]

let private isEmojiGrapheme(grapheme: string) =
  let codepoints =
    grapheme.EnumerateRunes() |> Seq.map(fun rune -> rune.Value) |> Seq.toArray

  let first = if codepoints.Length = 0 then 0 else codepoints.[0]

  let contains value = Array.contains value codepoints

  contains 0xFE0F
  || contains 0x20E3
  || contains 0x200D
  || codepoints
     |> Array.exists(fun value -> value >= 0x1F3FB && value <= 0x1F3FF)
  || (first >= 0x1F1E6 && first <= 0x1F1FF)
  || defaultEmojiRanges
     |> Array.exists(fun (low, high) -> first >= low && first <= high)

/// Splits plain text into text and emoji runs by grapheme. A grapheme
/// with the emoji selector, a keycap, a ZWJ, a skin tone, a regional
/// indicator, or a default emoji-presentation code point is emoji.
let runs(text: string) : Run list =
  let rec walk (enumerator: TextElementEnumerator) (acc: Run list) =
    if enumerator.MoveNext() then
      let grapheme = enumerator.GetTextElement()

      let run =
        if isEmojiGrapheme grapheme then
          Emoji grapheme
        else
          Text grapheme

      let next =
        match run, acc with
        | Text current, Text previous :: rest ->
          Text(previous + current) :: rest
        | Emoji current, Emoji previous :: rest ->
          Emoji(previous + current) :: rest
        | _ -> run :: acc

      walk enumerator next
    else
      List.rev acc

  walk (StringInfo.GetTextElementEnumerator text) []
