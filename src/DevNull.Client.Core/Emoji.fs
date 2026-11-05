module DevNull.Client.Core.Emoji

open System
open System.Collections.Generic
open System.IO
open System.Reflection
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

[<Struct>]
type Insert = {
  Text: string
  Start: int
  Finish: int
  Emoji: string
  MaxLength: int
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

let insertAt(insert: Insert) : Inserted voption =
  let text = insert.Text

  let first = min insert.Start insert.Finish
  let last = max insert.Start insert.Finish

  let start = max 0 (min first text.Length)

  let finish = max start (min last text.Length)

  let next = text.Substring(0, start) + insert.Emoji + text.Substring(finish)

  if next.Length > insert.MaxLength then
    ValueNone
  else
    ValueSome {
      Text = next
      Caret = start + insert.Emoji.Length
    }
