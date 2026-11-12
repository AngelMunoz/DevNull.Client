namespace DevNull.Client.Layouts.Workbench

open System
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Types
open DevNull.Client.Errors

module WorkbenchSettings =

  let initial: WorkbenchState = Build.state(Build.history Following, None)

  let validHandle(handle: string) =
    handle.Length > 0
    && handle.Length <= 64
    && handle
       |> Seq.forall(fun c -> not(Char.IsWhiteSpace c) && c <> '|' && c <> ':')

  let parseHandle(raw: string) =
    let handle = raw.Trim().TrimStart('@')

    if validHandle handle then Some handle else None

  let parseRef(token: string) =
    match LogKinds.tryParse token with
    | Some log -> Some(Build.history log)
    | None ->
      match token with
      | "saved" -> Some Build.saved
      | "scheduled" -> Some Build.scheduled
      | "notifications" -> Some Build.notifications
      | _ when token.StartsWith("repository:", StringComparison.Ordinal) ->
        token.Substring("repository:".Length)
        |> parseHandle
        |> Option.map Build.repository
      | _ -> None

  let parseCheckout(token: string) =
    match CommitIds.parse<AppError> token with
    | Ok id -> Some(CommitIds.value id)
    | Error _ -> None

  let parse(raw: string) : WorkbenchState =
    if String.IsNullOrWhiteSpace raw then
      initial
    else
      match raw.Trim().Split([| '|' |]) |> Array.toList with
      | [ refToken ] ->
        parseRef(refToken.Trim())
        |> Option.map(fun ref -> Build.state(ref, None))
        |> Option.defaultValue initial
      | [ refToken; checkoutToken ] ->
        match
          parseRef(refToken.Trim()), parseCheckout(checkoutToken.Trim())
        with
        | Some ref, Some checkoutId -> Build.state(ref, Some checkoutId)
        | _ -> initial
      | _ -> initial

  let store(state: WorkbenchState) =
    let refToken = Ref.stored(WorkbenchState.refOf state)

    match WorkbenchState.checkoutId state with
    | Some checkoutId -> refToken + "|" + checkoutId
    | None -> refToken
