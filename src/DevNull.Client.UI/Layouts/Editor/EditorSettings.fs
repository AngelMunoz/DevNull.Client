namespace DevNull.Client.Layouts.Editor

open DevNull.Client.Core.ApiTypes

type StoredState = StoredState of LogKind

module StoredState =

  let selectedRef(StoredState log) = log

module EditorSettings =

  let initial() = StoredState Following

  let parse(raw: string | null) =
    raw
    |> Option.ofObj
    |> Option.bind LogKinds.tryParse
    |> Option.defaultValue Following
    |> StoredState

  let store(StoredState log) = LogKinds.wire log
