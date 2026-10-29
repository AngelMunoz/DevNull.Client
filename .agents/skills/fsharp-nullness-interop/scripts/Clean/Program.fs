module Program

open System
open System.IO
open System.Text.Json

type Order = { Id: int }

let tryHost (s: string) : string option =
    match Uri.TryCreate(s, UriKind.Absolute) with
    | true, NonNull u -> Some u.Host
    | _ -> None

let tryName (e: JsonElement) : string option =
    match e.TryGetProperty "name" with
    | true, p -> p.GetString() |> Option.ofObj
    | _ -> None

let envOr (name: string) (fallback: string) : string =
    match Environment.GetEnvironmentVariable name with
    | null -> fallback
    | v -> v

let rec rootMessage (ex: exn) : string =
    match ex.InnerException with
    | null -> ex.Message
    | inner -> rootMessage inner

let tryParent (path: string) : string option =
    match Path.GetDirectoryName path with
    | null
    | "" -> None
    | dir -> Some dir

let tryLoad (json: string) : Result<Order, string> =
    match JsonSerializer.Deserialize<Order> json with
    | null -> Error "empty document"
    | order -> Ok order

let tryFirstLine (reader: TextReader) : string option = reader.ReadLine() |> Option.ofObj

let someLeak (reader: TextReader) = Some(reader.ReadLine())

let stringCoerce (s: string | null) : string = string s

let delay (attempt: int) : TimeSpan = TimeSpan.FromSeconds(float (pown 2 (attempt - 1)))

let show (key: string) (value: string) = Console.WriteLine $"{key} | {value}"

let quote (o: string option) =
    match o with
    | Some v -> $"Some \"{v}\""
    | None -> "None"

[<EntryPoint>]
let main _ =
    use nullDoc = JsonDocument.Parse """{"name": null}"""
    use adaDoc = JsonDocument.Parse """{"name": "Ada"}"""
    let rootPath = if OperatingSystem.IsWindows() then @"C:\" else "/"
    show "tryHost-valid" (quote (tryHost "https://example.com/a"))
    show "tryHost-invalid" (quote (tryHost "not a uri"))
    show "tryName-null" (quote (tryName nullDoc.RootElement))
    show "tryName-value" (quote (tryName adaDoc.RootElement))
    show "envOr-missing" (envOr "FSHARP_NULLNESS_PROOF_UNSET" "fallback")
    show "rootMessage-single" (rootMessage (exn "outer"))
    show "rootMessage-nested" (rootMessage (exn ("outer", exn "inner")))
    show "tryParent-root" (quote (tryParent rootPath))
    show "tryParent-bare" (quote (tryParent "file.txt"))
    let bareDir = Path.GetDirectoryName "file.txt"
    show "getDirectoryName-bare" (match bareDir with null -> "null" | d -> $"\"{d}\"")
    show "tryLoad-null" (match tryLoad "null" with Ok o -> $"Ok {o.Id}" | Error e -> $"Error \"{e}\"")
    show "tryFirstLine-empty" (quote (tryFirstLine (new StringReader "")))
    show "someLeak-empty" (match someLeak (new StringReader "") with Some null -> "Some null" | Some v -> $"Some {v}" | None -> "None")
    show "stringCoerce-null" $"\"{stringCoerce null}\""
    show "delay-3" ((delay 3).ToString())
    show "fromHours-int" ((TimeSpan.FromHours 1).ToString())
    show "fromDays-int" ((TimeSpan.FromDays 1).ToString())
    show "nonNull-null" (try nonNull (null: string | null) |> ignore; "no exception" with ex -> ex.GetType().Name)
    let leaked: string = Unchecked.nonNull (null: string | null)
    show "uncheckedNonNull-null" (match box leaked with null -> "null passes" | _ -> "not null")
    0
