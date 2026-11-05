module Tests.Emoji

open Expecto
open DevNull.Client.Core
open DevNull.Client.Core.Emoji

let searchNames(query: string) = search query |> Array.map _.Name

let countInGroup(name: string) =
  groups
  |> Array.tryFind(fun group -> group.Name = name)
  |> Option.map(fun group -> group.Emoji.Length)
  |> Option.defaultValue -1

let tests =
  testList "emoji" [
    test "the catalog carries the eight groups in order" {
      Expect.equal
        (groups |> Array.map _.Name |> List.ofArray)
        [
          "Smileys & people"
          "Animals & nature"
          "Food & drink"
          "Travel & places"
          "Activities"
          "Objects"
          "Symbols"
          "Flags"
        ]
        "group order"

      Expect.equal 529 (countInGroup "Smileys & people") "people"
      Expect.equal 157 (countInGroup "Animals & nature") "nature"
      Expect.equal 128 (countInGroup "Food & drink") "food"
      Expect.equal 218 (countInGroup "Travel & places") "travel"
      Expect.equal 85 (countInGroup "Activities") "activities"
      Expect.equal 261 (countInGroup "Objects") "objects"
      Expect.equal 223 (countInGroup "Symbols") "symbols"
      Expect.equal 269 (countInGroup "Flags") "flags"

      Expect.equal
        1870
        (groups |> Array.sumBy(fun group -> group.Emoji.Length))
        "cells"

      Expect.equal 1919 names.Length "shortcodes"
    }

    test "the first shortcode names the emoji" {
      Expect.equal "+1" (nameOf "👍") "thumbs up"
      Expect.equal "boat" (nameOf "⛵") "sailboat"
      Expect.equal "no-such-emoji" (nameOf "no-such-emoji") "no fallback"
    }

    test "a prefix query ranks short names first" {
      Expect.equal 34 (search "heart" |> Array.length) "heart count"

      Expect.equal
        [ "smile"; "smirk"; "smiley" ]
        (searchNames "smi" |> Array.take 3 |> List.ofArray)
        "the prefix block"
    }

    test "a word prefix and a substring follow the prefix block" {
      Expect.equal
        [ "apple"; "green_apple"; "pineapple" ]
        (searchNames "apple" |> List.ofArray)
        "apple order"

      Expect.equal "🪨" (search ":rock" |> Array.head |> _.Emoji) "the rock"
    }

    test "one emoji comes back once whatever its names" {
      let boats = search "boat"

      Expect.equal
        [ "boat"; "motor_boat"; "rowboat"; "speedboat" ]
        (boats |> Array.map _.Name |> List.ofArray)
        "boat order"

      Expect.equal
        1
        (boats |> Array.filter(fun match' -> match'.Emoji = "⛵") |> Array.length)
        "one sailboat"
    }

    test "a shortcode search answers its emoji" {
      let found = search "+1"

      Expect.equal 1 (Array.length found) "one result"
      Expect.equal "👍" found.[0].Emoji "the emoji"
      Expect.equal "+1" found.[0].Name "the name"
    }

    test "an unknown query answers nothing" {
      Expect.equal 0 (search "zzqq" |> Array.length) "no results"
      Expect.equal 0 (search "" |> Array.length) "blank"
      Expect.equal 0 (search "::" |> Array.length) "colons"
    }

    test "one emoji per best name" {
      let ar = search "ar"
      Expect.equal 253 (Array.length ar) "ar results"

      Expect.equal
        (ar |> Array.map _.Emoji |> Array.distinct |> Array.length)
        (Array.length ar)
        "one per emoji"
    }

    test "an insertion at the caret leaves the caret after the emoji" {
      let inserted =
        insertAt {
          Text = "shipped it"
          Start = 8
          Finish = 8
          Emoji = "🚀"
          MaxLength = 40000
        }

      match inserted with
      | ValueSome result ->
        Expect.equal "shipped 🚀it" result.Text "the text"

        Expect.equal 10 result.Caret "the caret"
      | ValueNone -> failtest "the insertion was refused"
    }

    test "an insertion replaces the selection" {
      let inserted =
        insertAt {
          Text = "shipped it"
          Start = 8
          Finish = 10
          Emoji = "🎉"
          MaxLength = 40000
        }

      match inserted with
      | ValueSome result ->
        Expect.equal "shipped 🎉" result.Text "the text"
        Expect.equal 10 result.Caret "the caret"
      | ValueNone -> failtest "the insertion was refused"
    }

    test "an insertion replaces a selection dragged backwards" {
      let inserted =
        insertAt {
          Text = "shipped it"
          Start = 10
          Finish = 8
          Emoji = "🎉"
          MaxLength = 40000
        }

      match inserted with
      | ValueSome result ->
        Expect.equal "shipped 🎉" result.Text "the text"
        Expect.equal 10 result.Caret "the caret"
      | ValueNone -> failtest "the insertion was refused"
    }

    test "an insertion at the end appends" {
      let inserted =
        insertAt {
          Text = "shipped"
          Start = 7
          Finish = 7
          Emoji = "🔥"
          MaxLength = 40000
        }

      match inserted with
      | ValueSome result ->
        Expect.equal "shipped🔥" result.Text "the text"
        Expect.equal 9 result.Caret "the caret"
      | ValueNone -> failtest "the insertion was refused"
    }

    test "an insertion over the raw cap is refused" {
      let text = String.replicate 39999 "a"

      let refused =
        insertAt {
          Text = text
          Start = text.Length
          Finish = text.Length
          Emoji = "🚀"
          MaxLength = 40000
        }

      match refused with
      | ValueNone -> ()
      | ValueSome _ -> failtest "the cap accepted the insertion"
    }

    test "a selection outside the text is clamped" {
      let inserted =
        insertAt {
          Text = "ok"
          Start = 9
          Finish = 20
          Emoji = "🎉"
          MaxLength = 40000
        }

      match inserted with
      | ValueSome result ->
        Expect.equal "ok🎉" result.Text "the text"
        Expect.equal 4 result.Caret "the caret"
      | ValueNone -> failtest "the insertion was refused"
    }
  ]
