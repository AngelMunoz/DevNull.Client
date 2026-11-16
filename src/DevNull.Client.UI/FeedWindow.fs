module DevNull.Client.FeedWindow

[<Struct>]
type Order =
  | NewestFirst
  | OldestFirst

[<Struct>]
type Anchor = {
  Key: string
  Offset: float
  Entry: string voption
}

[<Struct>]
type Chunk<'T> = {
  Request: string voption
  Items: 'T array
  Older: string voption
  Newer: string voption
}

[<Struct>]
type Edge =
  | NewerEdge
  | OlderEdge

type Window<'T> = {
  Order: Order
  Chunks: Chunk<'T> list
  Limit: int
}

[<Struct>]
type Place = { Anchor: Anchor voption }

[<Struct>]
type Ops<'T> = { KeyOf: 'T -> string }

[<Struct>]
type Trim<'T> = {
  Ops: Ops<'T>
  Keep: string voption
  Edge: Edge
}

[<Struct>]
type Insert<'T> = {
  Ops: Ops<'T>
  Item: 'T
  Before: 'T -> bool
}

[<Struct>]
type Replace<'T> = { Ops: Ops<'T>; Key: string; Item: 'T }

let empty(order: Order, limit: int) : Window<'T> = {
  Order = order
  Chunks = []
  Limit = limit
}

let inline items(window: Window<'T>) : 'T list = [
  for chunk in window.Chunks do
    yield! chunk.Items
]

let rowsOf(chunks: Chunk<'T> list) : int =
  chunks |> List.sumBy(fun chunk -> Array.length chunk.Items)

let inline count(window: Window<'T>) : int = rowsOf window.Chunks

let chunkAt
  (order: Order, edge: Edge, chunks: Chunk<'T> list)
  : Chunk<'T> voption =
  match order, edge with
  | NewestFirst, NewerEdge
  | OldestFirst, OlderEdge -> chunks |> List.tryHead |> ValueOption.ofOption
  | NewestFirst, OlderEdge
  | OldestFirst, NewerEdge -> chunks |> List.tryLast |> ValueOption.ofOption

let dropAt(order: Order, edge: Edge, chunks: Chunk<'T> list) : Chunk<'T> list =
  match order, edge with
  | NewestFirst, NewerEdge
  | OldestFirst, OlderEdge -> List.tail chunks
  | NewestFirst, OlderEdge
  | OldestFirst, NewerEdge -> List.truncate (List.length chunks - 1) chunks

let atHead
  (change: Chunk<'T> -> Chunk<'T>, chunks: Chunk<'T> list)
  : Chunk<'T> list =
  match chunks with
  | [] -> []
  | chunk :: rest -> change chunk :: rest

let atTail
  (change: Chunk<'T> -> Chunk<'T>, chunks: Chunk<'T> list)
  : Chunk<'T> list =
  match List.rev chunks with
  | [] -> []
  | chunk :: rest -> List.rev(change chunk :: rest)

let older(window: Window<'T>) : string voption =
  chunkAt(window.Order, OlderEdge, window.Chunks)
  |> ValueOption.bind(fun chunk -> chunk.Older)

let newer(window: Window<'T>) : string voption =
  chunkAt(window.Order, NewerEdge, window.Chunks)
  |> ValueOption.bind(fun chunk -> chunk.Newer)

let entryOf(ops: Ops<'T>, key: string, window: Window<'T>) : string voption =
  window.Chunks
  |> List.tryFind(fun chunk ->
    chunk.Items |> Array.exists(fun item -> ops.KeyOf(item) = key))
  |> ValueOption.ofOption
  |> ValueOption.bind(fun chunk -> chunk.Request)

let keysOf(ops: Ops<'T>, chunks: Chunk<'T> list) : Set<string> =
  chunks
  |> List.fold
    (fun keys chunk ->
      chunk.Items
      |> Array.fold (fun keys item -> Set.add (ops.KeyOf(item)) keys) keys)
    Set.empty

let valuesOf(ops: Ops<'T>, page: 'T array) : Map<string, 'T> =
  page
  |> Array.fold
    (fun values item -> Map.add (ops.KeyOf(item)) item values)
    Map.empty

let freshOf(ops: Ops<'T>, known: Set<string>, page: 'T array) : 'T array =
  page
  |> Array.filter(fun item -> not(Set.contains (ops.KeyOf(item)) known))
  |> Array.distinctBy ops.KeyOf

let applyValues
  (ops: Ops<'T>, values: Map<string, 'T>, chunks: Chunk<'T> list)
  : Chunk<'T> list =
  chunks
  |> List.map(fun chunk -> {
    chunk with
        Items =
          chunk.Items
          |> Array.map(fun item ->
            values |> Map.tryFind(ops.KeyOf(item)) |> Option.defaultValue item)
  })

let overlaps(ops: Ops<'T>, page: Chunk<'T>, chunk: Chunk<'T>) : bool =
  let keys = chunk.Items |> Array.map ops.KeyOf |> Set.ofArray
  page.Items |> Array.exists(fun item -> Set.contains (ops.KeyOf(item)) keys)

let setOlder
  (cursor: string voption, order: Order, chunks: Chunk<'T> list)
  : Chunk<'T> list =
  let change = fun chunk -> { chunk with Older = cursor }

  match order with
  | NewestFirst -> atTail(change, chunks)
  | OldestFirst -> atHead(change, chunks)

let setNewer
  (cursor: string voption, order: Order, chunks: Chunk<'T> list)
  : Chunk<'T> list =
  let change = fun chunk -> { chunk with Newer = cursor }

  match order with
  | NewestFirst -> atHead(change, chunks)
  | OldestFirst -> atTail(change, chunks)

let addOlder(ops: Ops<'T>, chunk: Chunk<'T>, window: Window<'T>) : Window<'T> =
  let fresh = freshOf(ops, keysOf(ops, window.Chunks), chunk.Items)
  let chunks = applyValues(ops, valuesOf(ops, chunk.Items), window.Chunks)

  match fresh with
  | [||] -> {
      window with
          Chunks = setOlder(chunk.Older, window.Order, chunks)
    }
  | _ -> {
      window with
          Chunks =
            match window.Order with
            | NewestFirst -> [
                yield! chunks
                yield { chunk with Items = fresh }
              ]
            | OldestFirst -> { chunk with Items = fresh } :: chunks
    }

let addNewer(ops: Ops<'T>, chunk: Chunk<'T>, window: Window<'T>) : Window<'T> =
  let fresh = freshOf(ops, keysOf(ops, window.Chunks), chunk.Items)
  let chunks = applyValues(ops, valuesOf(ops, chunk.Items), window.Chunks)

  match fresh with
  | [||] -> {
      window with
          Chunks = setNewer(chunk.Newer, window.Order, chunks)
    }
  | _ -> {
      window with
          Chunks =
            match window.Order with
            | NewestFirst -> { chunk with Items = fresh } :: chunks
            | OldestFirst -> [
                yield! chunks
                yield { chunk with Items = fresh }
              ]
    }

let mergeNewerPage
  (ops: Ops<'T>, chunk: Chunk<'T>, window: Window<'T>)
  : Window<'T> =
  let fresh = freshOf(ops, keysOf(ops, window.Chunks), chunk.Items)

  match fresh with
  | [||] -> window
  | _ ->
    let chunks = applyValues(ops, valuesOf(ops, chunk.Items), window.Chunks)
    let placed = { chunk with Items = fresh }

    let merged =
      match chunkAt(window.Order, NewerEdge, window.Chunks), window.Order with
      | ValueSome edge, NewestFirst when
        edge.Request = chunk.Request && overlaps(ops, chunk, edge)
        ->
        atHead(
          (fun head -> {
            head with
                Items = [| yield! fresh; yield! head.Items |]
                Newer = chunk.Newer
          }),
          chunks
        )
      | ValueSome edge, OldestFirst when
        edge.Request = chunk.Request && overlaps(ops, chunk, edge)
        ->
        atTail(
          (fun tail -> {
            tail with
                Items = [| yield! tail.Items; yield! fresh |]
                Newer = chunk.Newer
          }),
          chunks
        )
      | _, NewestFirst -> placed :: chunks
      | _, OldestFirst -> [ yield! chunks; yield placed ]

    { window with Chunks = merged }

let replace(ops: Ops<'T>, item: 'T, window: Window<'T>) : Window<'T> =
  let key = ops.KeyOf(item)

  {
    window with
        Chunks =
          window.Chunks
          |> List.map(fun chunk -> {
            chunk with
                Items =
                  chunk.Items
                  |> Array.map(fun existing ->
                    if ops.KeyOf(existing) = key then item else existing)
          })
  }

let insert(insert: Insert<'T>, window: Window<'T>) : Window<'T> =
  let key = insert.Ops.KeyOf insert.Item

  let known =
    window.Chunks
    |> List.exists(fun chunk ->
      chunk.Items |> Array.exists(fun item -> insert.Ops.KeyOf item = key))

  if known then
    replace(insert.Ops, insert.Item, window)
  else
    let rec place(chunks: Chunk<'T> list) : Chunk<'T> list option =
      match chunks with
      | [] -> None
      | chunk :: rest ->
        match chunk.Items |> Array.tryFindIndex insert.Before with
        | Some index ->
          Some(
            {
              chunk with
                  Items = Array.insertAt index insert.Item chunk.Items
            }
            :: rest
          )
        | None ->
          match place rest with
          | Some next -> Some(chunk :: next)
          | None -> None

    match place window.Chunks with
    | Some chunks -> { window with Chunks = chunks }
    | None ->
      let change =
        fun chunk -> {
          chunk with
              Items = [| yield! chunk.Items; yield insert.Item |]
        }

      {
        window with
            Chunks =
              match window.Order with
              | NewestFirst -> atTail(change, window.Chunks)
              | OldestFirst -> atHead(change, window.Chunks)
      }

let replaceKey(replace: Replace<'T>, window: Window<'T>) : Window<'T> = {
  window with
      Chunks =
        window.Chunks
        |> List.map(fun chunk -> {
          chunk with
              Items =
                chunk.Items
                |> Array.map(fun existing ->
                  if replace.Ops.KeyOf existing = replace.Key then
                    replace.Item
                  else
                    existing)
        })
}

let remove(ops: Ops<'T>, key: string, window: Window<'T>) : Window<'T> = {
  window with
      Chunks =
        window.Chunks
        |> List.map(fun chunk -> {
          chunk with
              Items =
                chunk.Items |> Array.filter(fun item -> ops.KeyOf(item) <> key)
        })
}

let holds(ops: Ops<'T>, keep: string voption, chunk: Chunk<'T>) : bool =
  keep
  |> ValueOption.exists(fun key ->
    chunk.Items |> Array.exists(fun item -> ops.KeyOf(item) = key))

[<TailCall>]
let rec trimChunks
  (window: Window<'T>, trim: Trim<'T>, chunks: Chunk<'T> list)
  : Chunk<'T> list =
  if rowsOf chunks <= window.Limit then
    chunks
  else
    match List.length chunks, chunkAt(window.Order, trim.Edge, chunks) with
    | 0, _
    | 1, _
    | _, ValueNone -> chunks
    | _, ValueSome far when holds(trim.Ops, trim.Keep, far) -> chunks
    | _, ValueSome _ ->
      trimChunks(window, trim, dropAt(window.Order, trim.Edge, chunks))

let trim(trim: Trim<'T>, window: Window<'T>) : Window<'T> = {
  window with
      Chunks = trimChunks(window, trim, window.Chunks)
}
