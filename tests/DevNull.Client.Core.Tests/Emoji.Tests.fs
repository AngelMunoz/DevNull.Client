module Tests.Emoji

open Expecto
open DevNull.Client.Core
open DevNull.Client.Core.Emoji

let firstEmoji = groups |> Array.collect(fun group -> group.Emoji) |> Array.head

let sampleNames = [|
  names.[0]
  names.[names.Length / 2]
  names.[names.Length - 1]
|]

let emojiOf(query: string) =
  search query |> Array.map _.Emoji |> Array.sort

let tests =
  testList "emoji" [
    test "the catalog has named groups with cells" {
      Expect.isTrue (groups.Length > 0) "the catalog has a group"

      for group in groups do
        Expect.isFalse
          (System.String.IsNullOrWhiteSpace group.Name)
          "the group name is not blank"

        Expect.isTrue (group.Emoji.Length > 0) "the group has a cell"

        for emoji in group.Emoji do
          Expect.isFalse
            (System.String.IsNullOrEmpty emoji)
            "the cell is not empty"
    }

    test "every shortcode is named and points at a cell" {
      Expect.isTrue (names.Length > 0) "the catalog has a shortcode"

      for name, emoji in names do
        Expect.isFalse
          (System.String.IsNullOrWhiteSpace name)
          "the name is not blank"

        Expect.isFalse
          (System.String.IsNullOrEmpty emoji)
          "the emoji is not empty"
    }

    test "a named emoji answers one of its names" {
      let pairs = names |> Set.ofArray

      for _, emoji in names do
        let name = nameOf emoji

        Expect.isTrue
          (Set.contains (name, emoji) pairs)
          "the answer names the emoji"
    }

    test "an unnamed value answers itself" {
      Expect.equal "not-an-emoji" (nameOf "not-an-emoji") "the fallback"
    }

    test "a shortcode search answers its emoji" {
      for name, emoji in sampleNames do
        Expect.isTrue
          (search name |> Array.exists(fun found -> found.Emoji = emoji))
          "the search returned the emoji"
    }

    test "a search ignores case and surrounding colons" {
      for name, _ in sampleNames do
        Expect.equal
          (emojiOf name)
          (emojiOf(name.ToUpperInvariant()))
          "the case is ignored"

        Expect.equal
          (emojiOf name)
          (emojiOf $":{name}:")
          "the colons are ignored"
    }

    test "a search answers one result per emoji" {
      for name, _ in sampleNames do
        let found = search name

        Expect.isTrue (found.Length > 0) "the search found the emoji"

        Expect.equal
          (found |> Array.map _.Emoji |> Array.distinct |> Array.length)
          found.Length
          "the emoji comes back once"
    }

    test "a search orders its ranks and every name carries the query" {
      for name, _ in sampleNames do
        let query = normalize name
        let found = search name

        found
        |> Array.pairwise
        |> Array.iter(fun (left, right) ->
          Expect.isTrue (left.Rank <= right.Rank) "the ranks are ordered")

        for found in found do
          Expect.stringContains
            (found.Name.ToLowerInvariant())
            query
            "the result name carries the query"
    }

    test "a blank or colon-only query answers nothing" {
      Expect.isEmpty (search "") "the blank query"

      Expect.isEmpty (search "::") "the colon query"

      Expect.isEmpty (search "   ") "the spaces"
    }

    test "an unknown query answers nothing" {
      Expect.isEmpty (search "zzzz-not-a-shortcode") "the unknown query"
    }

    test "plain text stays one text run" {
      Expect.equal [ Run.Text "hello" ] (runs "hello") "the plain text"
    }

    test "text and emoji split by grapheme" {
      Expect.equal
        [ Run.Text "a"; Run.Emoji "😀"; Run.Text "b" ]
        (runs "a😀b")
        "the runs"
    }

    test "an emoji sequence stays one run" {
      for emoji in [ "😀"; "👩‍💻"; "👋🏽"; "🇪🇸"; "1️⃣" ] do
        Expect.equal [ Run.Emoji emoji ] (runs emoji) "the sequence"
    }

    test "a text-default symbol becomes emoji with the selector" {
      Expect.equal [ Run.Text "☺" ] (runs "☺") "without the selector"
      Expect.equal [ Run.Emoji "☺️" ] (runs "☺️") "with the selector"
    }

    test "a heart becomes emoji with the selector" {
      Expect.equal [ Run.Text "❤" ] (runs "❤") "without the selector"
      Expect.equal [ Run.Emoji "❤️" ] (runs "❤️") "with the selector"
    }

    test "an insertion at the caret leaves the caret after the emoji" {
      let text = "shipped it"
      let at = 8

      let inserted =
        insertAt {
          Text = text
          Start = at
          Finish = at
          Emoji = firstEmoji
        }

      Expect.equal
        (text.Substring(0, at) + firstEmoji + text.Substring(at))
        inserted.Text
        "the text"

      Expect.equal (at + firstEmoji.Length) inserted.Caret "the caret"
    }

    test "an insertion replaces the selection" {
      let text = "shipped it"

      let inserted =
        insertAt {
          Text = text
          Start = 8
          Finish = 10
          Emoji = firstEmoji
        }

      Expect.equal (text.Substring(0, 8) + firstEmoji) inserted.Text "the text"

      Expect.equal (8 + firstEmoji.Length) inserted.Caret "the caret"
    }

    test "an insertion replaces a selection dragged backwards" {
      let text = "shipped it"

      let inserted =
        insertAt {
          Text = text
          Start = 10
          Finish = 8
          Emoji = firstEmoji
        }

      Expect.equal (text.Substring(0, 8) + firstEmoji) inserted.Text "the text"

      Expect.equal (8 + firstEmoji.Length) inserted.Caret "the caret"
    }

    test "an insertion at the end appends" {
      let text = "shipped"

      let inserted =
        insertAt {
          Text = text
          Start = text.Length
          Finish = text.Length
          Emoji = firstEmoji
        }

      Expect.equal (text + firstEmoji) inserted.Text "the text"

      Expect.equal (text.Length + firstEmoji.Length) inserted.Caret "the caret"
    }

    test "a selection outside the text is clamped" {
      let text = "ok"

      let inserted =
        insertAt {
          Text = text
          Start = 9
          Finish = 20
          Emoji = firstEmoji
        }

      Expect.equal (text + firstEmoji) inserted.Text "the text"

      Expect.equal (text.Length + firstEmoji.Length) inserted.Caret "the caret"
    }
  ]
