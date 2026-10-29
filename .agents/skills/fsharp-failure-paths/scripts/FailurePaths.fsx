let mutable failures = 0

let check name (expected: 'a) (actual: 'a) =
    if expected = actual then
        printfn "PASS %s" name
    else
        failures <- failures + 1
        printfn "FAIL %s: expected %A, got %A" name expected actual

let outcome f =
    try Ok(f ()) with ex -> Error(ex.GetType().Name)

check "List.head throws on an empty list" (Error "ArgumentException") (outcome (fun () -> List.head ([]: int list)))
check "Map.find throws on a missing key" (Error "KeyNotFoundException") (outcome (fun () -> Map.find "b" (Map.empty |> Map.add "a" 1)))
check "Seq.find throws when nothing matches" (Error "KeyNotFoundException") (outcome (fun () -> Seq.find ((<) 5) [ 1; 2 ]))
check "Option.Value throws on None" (Error "NullReferenceException") (outcome (fun () -> (None: int option).Value))

check "List.tryHead returns None on an empty list" None (List.tryHead ([]: int list))
check "Map.tryFind returns None on a missing key" None (Map.tryFind "b" (Map.empty |> Map.add "a" 1))
check "Seq.tryFind returns None when nothing matches" None (Seq.tryFind ((<) 5) [ 1; 2 ])

let regionHidden (settings: Map<string, string>) = defaultArg (Map.tryFind "region" settings) "us-east"

let regionReported (settings: Map<string, string>) =
    match Map.tryFind "region" settings with
    | Some c -> Ok c
    | None -> Error "missing setting: region"

check "defaultArg turns a missing setting into a plausible value" "us-east" (regionHidden Map.empty)
check "Map.tryFind with match reports the missing setting" (Error "missing setting: region") (regionReported Map.empty)

let fetchRates () : Map<string, decimal> = failwith "rate service unavailable"
let swallowed () = try fetchRates () with _ -> Map.empty

let kept () =
    try Ok(fetchRates ()) with ex -> Error $"rates unavailable: {ex.Message}"

check "try ... with _ -> Map.empty hides the failure" 0 (swallowed () |> Map.count)
check "try ... with ex -> Error keeps the cause" (Error "rates unavailable: rate service unavailable") (kept ())

let checkQuantity (sku: string, qty: int) =
    if qty > 0 then Ok(sku, qty) else Error $"{sku}: quantity must be positive"

let firstOnly lines =
    let errors = lines |> List.map checkQuantity |> List.choose (function Error e -> Some e | Ok _ -> None)
    if List.isEmpty errors then Ok lines else Error [ List.head errors ]

let allErrors lines =
    let results = lines |> List.map checkQuantity
    match results |> List.choose (function Error e -> Some e | Ok _ -> None) with
    | [] -> Ok(results |> List.choose (function Ok l -> Some l | Error _ -> None))
    | errors -> Error errors

let twoBad = [ "item-1", 0; "item-2", -3 ]
check "List.head errors drops the second error" (Error [ "item-1: quantity must be positive" ]) (firstOnly twoBad)
check "Returning the error list keeps both errors" (Error [ "item-1: quantity must be positive"; "item-2: quantity must be positive" ]) (allErrors twoBad)

let requireReversed condition error = if condition then Error error else Ok()
let require condition error = if condition then Ok() else Error error
check "A reversed guard rejects a true condition" (Error "must be positive") (requireReversed (3 > 0) "must be positive")
check "A correct guard accepts a true condition" (Ok()) (require (3 > 0) "must be positive")

if failures > 0 then exit 1
