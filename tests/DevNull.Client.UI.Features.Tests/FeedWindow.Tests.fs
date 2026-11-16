module DevNull.Client.UI.Tests.FeedWindow

open System
open Expecto
open DevNull.Client.FeedWindow

type Row = { Key: string; Value: int }

let inline row(key, value) = { Key = key; Value = value }

let opsNewest: Ops<Row> = { KeyOf = (fun item -> item.Key) }

let opsOldest: Ops<Row> = { KeyOf = (fun item -> item.Key) }

let keys(window: Window<Row>) =
  items window |> List.map(fun item -> item.Key)

let values(window: Window<Row>) =
  items window |> List.map(fun item -> item.Value)

let page(request: string option, rows: Row list) : Chunk<Row> = {
  Request = ValueOption.ofOption request
  Items = Array.ofList rows
  Older = ValueNone
  Newer = ValueNone
}

let follow
  (request: string option, older: string option, rows: Row list)
  : Chunk<Row> =
  let chunk = page(request, rows)

  {
    chunk with
        Older = ValueOption.ofOption older
  }

let withNewer(cursor: string option, chunk: Chunk<Row>) : Chunk<Row> = {
  chunk with
      Newer = ValueOption.ofOption cursor
}

// Three newest-first pages of two rows under a limit of four.
let threePages() =
  let pages = [
    follow(Some "cursor-1", Some "cursor-2", [ row("a", 1); row("b", 2) ])
    follow(Some "cursor-2", Some "cursor-3", [ row("c", 3); row("d", 4) ])
    follow(Some "cursor-3", None, [ row("e", 5); row("f", 6) ])
  ]

  pages
  |> List.fold
    (fun window next -> addOlder(opsNewest, next, window))
    (empty(NewestFirst, 4))

[<Tests>]
let tests =
  testList "feed window" [
    testCase "an older page appends for newest first" (fun () ->
      let first =
        addOlder(
          opsNewest,
          page(None, [ row("c", 3); row("d", 4) ]),
          empty(NewestFirst, 300)
        )

      let second =
        addOlder(
          opsNewest,
          follow(Some "cursor-2", Some "cursor-3", [ row("e", 5) ]),
          first
        )

      Expect.equal [ "c"; "d"; "e" ] (keys second) "rows in display order"

      Expect.equal
        (ValueSome "cursor-3")
        (older second)
        "the older cursor follows the newest page")

    testCase "an older page prepends for oldest first" (fun () ->
      let first =
        addOlder(
          opsOldest,
          page(None, [ row("c", 3); row("d", 4) ]),
          empty(OldestFirst, 300)
        )

      let second =
        addOlder(
          opsOldest,
          follow(
            Some "cursor-2",
            Some "cursor-3",
            [ row("a", 1); row("b", 2) ]
          ),
          first
        )

      Expect.equal [ "a"; "b"; "c"; "d" ] (keys second) "rows in display order"

      Expect.equal
        (ValueSome "cursor-3")
        (older second)
        "the older cursor sits at the front")

    testCase "a newer page prepends for newest first" (fun () ->
      let first =
        addNewer(
          opsNewest,
          page(None, [ row("c", 3); row("d", 4) ]),
          empty(NewestFirst, 300)
        )

      let second =
        addNewer(
          opsNewest,
          follow(Some "cursor-0", None, [ row("a", 1); row("b", 2) ]),
          first
        )

      Expect.equal [ "a"; "b"; "c"; "d" ] (keys second) "rows in display order"

      Expect.equal 4 (count second) "every row landed"

      Expect.equal
        (ValueSome "cursor-0")
        (entryOf(opsNewest, "a", second))
        "the fresh row keeps its fetch cursor"

      Expect.equal
        ValueNone
        (entryOf(opsNewest, "c", second))
        "page one has no entry")

    testCase "a newer page appends for oldest first" (fun () ->
      let first =
        addNewer(
          opsOldest,
          page(None, [ row("a", 1); row("b", 2) ]),
          empty(OldestFirst, 300)
        )

      let second =
        addNewer(
          opsOldest,
          follow(Some "cursor-0", None, [ row("c", 3); row("d", 4) ]),
          first
        )

      Expect.equal [ "a"; "b"; "c"; "d" ] (keys second) "rows in display order")

    testCase "a boundary row keeps its place and takes the new value" (fun () ->
      let first =
        addOlder(
          opsNewest,
          page(None, [ row("c", 3); row("d", 4) ]),
          empty(NewestFirst, 300)
        )

      let second =
        addOlder(
          opsNewest,
          follow(Some "cursor-2", None, [ row("d", 40); row("e", 5) ]),
          first
        )

      Expect.equal [ "c"; "d"; "e" ] (keys second) "no repeated row"

      Expect.equal
        [ 3; 40; 5 ]
        (values second)
        "the repeated row took the page value"

      Expect.equal
        (ValueSome "cursor-2")
        (entryOf(opsNewest, "e", second))
        "the new row keeps its fetch cursor"

      Expect.equal ValueNone (older second) "the page ended the older chain")

    testCase "a page one refresh with overlap joins the head span" (fun () ->
      let first =
        addNewer(
          opsNewest,
          page(None, [ row("b", 2); row("c", 3) ]),
          empty(NewestFirst, 300)
        )

      let refreshed =
        mergeNewerPage(
          opsNewest,
          page(None, [ row("a", 1); row("b", 20) ]),
          first
        )

      Expect.equal
        [ "a"; "b"; "c" ]
        (keys refreshed)
        "the new row sits at the head"

      Expect.equal
        [ 1; 20; 3 ]
        (values refreshed)
        "the overlap took the new value"

      Expect.equal 3 (count refreshed) "every row landed"

      Expect.equal
        ValueNone
        (entryOf(opsNewest, "a", refreshed))
        "the page one rows share the head entry")

    testCase
      "a page one refresh with overlap joins the tail span for oldest first"
      (fun () ->
        let first =
          addOlder(
            opsOldest,
            page(None, [ row("b", 2); row("c", 3) ]),
            empty(OldestFirst, 300)
          )

        let refreshed =
          mergeNewerPage(
            opsOldest,
            page(None, [ row("c", 30); row("d", 4) ]),
            first
          )

        Expect.equal
          [ "b"; "c"; "d" ]
          (keys refreshed)
          "the new row sits at the tail"

        Expect.equal
          [ 2; 30; 4 ]
          (values refreshed)
          "the overlap took the new value"

        Expect.equal 3 (count refreshed) "every row landed"

        Expect.equal
          ValueNone
          (entryOf(opsOldest, "d", refreshed))
          "the page one rows share the tail entry")

    testCase
      "a page one refresh with another request cursor adds a head span"
      (fun () ->
        let first =
          addNewer(
            opsNewest,
            follow(Some "cursor-old", None, [ row("b", 2); row("c", 3) ]),
            empty(NewestFirst, 300)
          )

        let refreshed =
          mergeNewerPage(
            opsNewest,
            page(None, [ row("a", 1); row("b", 20) ]),
            first
          )

        Expect.equal [ "a"; "b"; "c" ] (keys refreshed) "rows in display order"

        Expect.equal 3 (count refreshed) "every row landed"

        Expect.equal
          ValueNone
          (entryOf(opsNewest, "a", refreshed))
          "the new row keeps page one as its entry"

        Expect.equal
          (ValueSome "cursor-old")
          (entryOf(opsNewest, "b", refreshed))
          "the repeated row keeps its old entry")

    testCase
      "a page one refresh with another request cursor adds a tail span for oldest first"
      (fun () ->
        let first =
          addNewer(
            opsOldest,
            follow(Some "cursor-old", None, [ row("b", 2); row("c", 3) ]),
            empty(OldestFirst, 300)
          )

        let refreshed =
          mergeNewerPage(
            opsOldest,
            page(None, [ row("a", 1); row("b", 20) ]),
            first
          )

        Expect.equal
          [ "b"; "c"; "a" ]
          (keys refreshed)
          "rows in display order, where the newer edge is the tail"

        Expect.equal 3 (count refreshed) "every row landed"

        Expect.equal
          ValueNone
          (entryOf(opsOldest, "a", refreshed))
          "the new row keeps page one as its entry"

        Expect.equal
          (ValueSome "cursor-old")
          (entryOf(opsOldest, "b", refreshed))
          "the repeated row keeps its old entry")

    testCase "a page one refresh without overlap adds a head span" (fun () ->
      let first =
        addNewer(
          opsNewest,
          follow(Some "cursor-0", None, [ row("d", 4) ]),
          empty(NewestFirst, 300)
        )

      let refreshed =
        mergeNewerPage(
          opsNewest,
          page(None, [ row("a", 1); row("b", 2) ]),
          first
        )

      Expect.equal [ "a"; "b"; "d" ] (keys refreshed) "rows in display order"

      Expect.equal 3 (count refreshed) "every row landed"

      Expect.equal
        ValueNone
        (entryOf(opsNewest, "a", refreshed))
        "the new span is page one"

      Expect.equal
        (ValueSome "cursor-0")
        (entryOf(opsNewest, "d", refreshed))
        "the old head kept its cursor")

    testCase
      "a page one refresh with no new key leaves the window unchanged"
      (fun () ->
        let first =
          addNewer(
            opsNewest,
            follow(Some "cursor-0", None, [ row("d", 4) ]),
            empty(NewestFirst, 300)
          )

        let refreshed =
          mergeNewerPage(opsNewest, page(None, [ row("d", 40) ]), first)

        Expect.equal first refreshed "unchanged"
        Expect.equal [ 4 ] (values refreshed) "the row kept its value")

    testCase "entryOf returns the request cursor of the holding span" (fun () ->
      let window =
        addOlder(
          opsNewest,
          follow(
            Some "cursor-2",
            Some "cursor-3",
            [ row("c", 3); row("d", 4) ]
          ),
          addOlder(
            opsNewest,
            follow(
              Some "cursor-1",
              Some "cursor-2",
              [ row("a", 1); row("b", 2) ]
            ),
            empty(NewestFirst, 300)
          )
        )

      Expect.equal
        (ValueSome "cursor-1")
        (entryOf(opsNewest, "a", window))
        "the first page entry"

      Expect.equal
        (ValueSome "cursor-2")
        (entryOf(opsNewest, "c", window))
        "the second page entry"

      let firstPage =
        addOlder(
          opsNewest,
          page(None, [ row("a", 1) ]),
          empty(NewestFirst, 300)
        )

      Expect.equal
        ValueNone
        (entryOf(opsNewest, "a", firstPage))
        "page one has no entry"

      Expect.equal
        ValueNone
        (entryOf(opsNewest, "missing", window))
        "a missing key has no entry")

    testCase "trim keeps the window at its limit from the older edge" (fun () ->
      let trimmed =
        trim(
          {
            Ops = opsNewest
            Keep = ValueSome "a"
            Edge = OlderEdge
          },
          threePages()
        )

      Expect.equal [ "a"; "b"; "c"; "d" ] (keys trimmed) "the older span left"
      Expect.equal 4 (count trimmed) "the window fits its limit"

      Expect.equal
        (ValueSome "cursor-3")
        (older trimmed)
        "the remaining edge span carries the refetch cursor")

    testCase
      "trim removes newer spans for a reader at the older edge"
      (fun () ->
        let trimmed =
          trim(
            {
              Ops = opsNewest
              Keep = ValueSome "e"
              Edge = NewerEdge
            },
            threePages()
          )

        Expect.equal
          [ "c"; "d"; "e"; "f" ]
          (keys trimmed)
          "the newer span left"

        Expect.equal 4 (count trimmed) "the window fits its limit")

    testCase "trim never removes the span holding the anchor" (fun () ->
      let trimmed =
        trim(
          {
            Ops = opsNewest
            Keep = ValueSome "e"
            Edge = OlderEdge
          },
          threePages()
        )

      Expect.equal
        [ "a"; "b"; "c"; "d"; "e"; "f" ]
        (keys trimmed)
        "the anchor span stayed"

      Expect.equal 6 (count trimmed) "every row stayed")

    testCase
      "trim keeps one span when the span alone exceeds the limit"
      (fun () ->
        let window =
          addOlder(
            opsNewest,
            page(None, [ row("a", 1); row("b", 2) ]),
            empty(NewestFirst, 1)
          )

        let trimmed =
          trim(
            {
              Ops = opsNewest
              Keep = ValueNone
              Edge = OlderEdge
            },
            window
          )

        Expect.equal [ "a"; "b" ] (keys trimmed) "the last span stayed"
        Expect.equal 2 (count trimmed) "the kept rows stayed")

    testCase "an empty older page ends the older cursor" (fun () ->
      let first =
        addOlder(
          opsNewest,
          follow(Some "cursor-2", Some "cursor-3", [ row("a", 1) ]),
          empty(NewestFirst, 300)
        )

      let second = addOlder(opsNewest, page(Some "cursor-3", []), first)

      Expect.equal [ "a" ] (keys second) "no row arrived"
      Expect.equal ValueNone (older second) "the cursor chain ended"
      Expect.equal 1 (count second) "the loaded row stayed")

    testCase "a duplicate only page ends the older cursor" (fun () ->
      let first =
        addOlder(
          opsNewest,
          follow(Some "cursor-2", Some "cursor-3", [ row("a", 1) ]),
          empty(NewestFirst, 300)
        )

      let second =
        addOlder(
          opsNewest,
          follow(Some "cursor-3", None, [ row("a", 10) ]),
          first
        )

      Expect.equal [ "a" ] (keys second) "no repeated row"
      Expect.equal [ 10 ] (values second) "the repeated row took the new value"
      Expect.equal ValueNone (older second) "the cursor chain ended"
      Expect.equal 1 (count second) "the loaded row stayed")

    testCase "an empty newer page ends the newer cursor" (fun () ->
      let first =
        addNewer(
          opsNewest,
          withNewer(Some "cursor-0", page(Some "cursor-1", [ row("a", 1) ])),
          empty(NewestFirst, 300)
        )

      let second = addNewer(opsNewest, page(Some "cursor-0", []), first)

      Expect.equal
        (ValueSome "cursor-0")
        (newer first)
        "the head had a newer cursor"

      Expect.equal ValueNone (newer second) "the cursor chain ended")

    testCase "an ordered insert lands before the first older row" (fun () ->
      let window =
        addOlder(
          opsNewest,
          page(None, [ row("c", 3); row("d", 4) ]),
          empty(NewestFirst, 300)
        )

      let inserted =
        insert(
          {
            Ops = opsNewest
            Item = row("b", 2)
            Before = (fun existing -> existing.Value >= 3)
          },
          window
        )

      Expect.equal [ "b"; "c"; "d" ] (keys inserted) "the row landed in order")
    testCase "an ordered insert replaces a known key" (fun () ->
      let window =
        addOlder(
          opsNewest,
          page(None, [ row("c", 3); row("d", 4) ]),
          empty(NewestFirst, 300)
        )

      let inserted =
        insert(
          {
            Ops = opsNewest
            Item = row("c", 30)
            Before = (fun existing -> existing.Value >= 3)
          },
          window
        )

      Expect.equal [ "c"; "d" ] (keys inserted) "no duplicate row"
      Expect.equal [ 30; 4 ] (values inserted) "the row took the new value")
    testCase "replaceKey folds a live key to a tombstone key" (fun () ->
      let window =
        addOlder(
          opsNewest,
          page(None, [ row("live", 1); row("d", 4) ]),
          empty(NewestFirst, 300)
        )

      let folded =
        replaceKey(
          {
            Ops = opsNewest
            Key = "live"
            Item = row("deleted:live", 2)
          },
          window
        )

      Expect.equal [ "deleted:live"; "d" ] (keys folded) "the row changed key"
      Expect.equal [ 2; 4 ] (values folded) "the tombstone value landed")
    testCase "random pages keep unique keys, order, and the limit" (fun () ->
      let random = Random(20261115)
      let limit = 12
      let pool = [ for index in 1..120 -> row(sprintf "k%03d" index, index) ]

      let nextPage(index) =
        let rows = [
          for _ in 1 .. random.Next(1, 6) -> pool[random.Next(pool.Length)]
        ]

        follow(Some(sprintf "cursor-%d" index), None, rows)

      let step(window: Window<Row>, index: int) =
        let next =
          match random.Next(2) with
          | 0 -> addOlder(opsNewest, nextPage index, window)
          | _ -> addNewer(opsNewest, nextPage index, window)

        let nextKeys = keys next

        Expect.equal
          (List.length(List.distinct nextKeys))
          (List.length nextKeys)
          "unique keys"

        let before = keys window

        Expect.equal
          (before |> List.filter(fun key -> List.contains key nextKeys))
          (nextKeys |> List.filter(fun key -> List.contains key before))
          "the loaded rows keep their order"

        trim(
          {
            Ops = opsNewest
            Keep = ValueNone
            Edge = if random.Next(2) = 0 then OlderEdge else NewerEdge
          },
          next
        )

      let final =
        List.fold
          (fun window index -> step(window, index))
          (empty(NewestFirst, limit))
          [ 1..120 ]

      Expect.isLessThanOrEqual (count final) limit "the window fits its limit"

      Expect.equal
        (List.length(List.distinct(keys final)))
        (List.length(keys final))
        "unique keys")

    testCase
      "the restore entry is the holding span and a missing key falls back"
      (fun () ->
        let window = threePages()

        Expect.equal
          (ValueSome "cursor-3")
          (entryOf(opsNewest, "e", window))
          "the third page entry"

        Expect.equal
          ValueNone
          (entryOf(opsNewest, "missing", window))
          "a missing key has no entry"

        Expect.equal
          window
          (threePages())
          "the same pages build the same window")
  ]
