module DevNull.Client.SettingsStore

open System
open System.IO
open System.Reactive.Subjects
open System.Text.Json
open System.Text.Json.Nodes
open FSharp.Control.Reactive
open IcedTasks

type ThemeChoice =
  | System
  | Light
  | Dark

let parseTheme(name: string) : ThemeChoice option =
  match name.Trim().ToLowerInvariant() with
  | "system" -> Some System
  | "light" -> Some Light
  | "dark" -> Some Dark
  | _ -> None

let themeString(choice: ThemeChoice) : string =
  match choice with
  | System -> "system"
  | Light -> "light"
  | Dark -> "dark"

type LayoutChoice =
  | CurrentLayout
  | WorkbenchLayout
  | EditorLayout

let parseLayout(name: string) : LayoutChoice option =
  match name.Trim().ToLowerInvariant() with
  | "current" -> Some CurrentLayout
  | "workbench" -> Some WorkbenchLayout
  | "editor" -> Some EditorLayout
  | _ -> None

let layoutString(choice: LayoutChoice) : string =
  match choice with
  | CurrentLayout -> "current"
  | WorkbenchLayout -> "workbench"
  | EditorLayout -> "editor"

type AppSettings = {
  CloseToTray: bool
  Theme: ThemeChoice
  Layout: LayoutChoice
  WorkbenchState: string
  EditorState: string
  SidebarExpanded: bool
  ChatDrafts: Map<string, string>
  VoiceVolume: float
  PinPromptHidden: bool
}

type AppSettingsStore =
  abstract Current: AppSettings
  abstract Observe: IObservable<AppSettings>
  abstract SetCloseToTray: enabled: bool -> unit
  abstract SetTheme: choice: ThemeChoice -> unit
  abstract SetLayout: choice: LayoutChoice -> unit
  abstract SetWorkbenchState: state: string -> unit
  abstract SetEditorState: state: string -> unit
  abstract SetSidebarExpanded: expanded: bool -> unit
  abstract SetChatDraft: key: string * text: string -> unit
  abstract SetVoiceVolume: volume: float -> unit
  abstract SetPinPromptHidden: hidden: bool -> unit

type IProvideSettings =
  abstract Settings: AppSettingsStore

module AppSettingsStore =

  type State = {
    Settings: BehaviorSubject<AppSettings>
    Disk: IObserver<AppSettings>
    Parks: IObserver<unit>
  }

  let defaults: AppSettings = {
    CloseToTray = false
    Theme = System
    Layout = CurrentLayout
    WorkbenchState = ""
    EditorState = ""
    SidebarExpanded = true
    ChatDrafts = Map.empty
    VoiceVolume = 1.0
    PinPromptHidden = false
  }

  let publish(state: State, value: AppSettings) =
    if state.Settings.Value <> value then
      state.Settings.OnNext value

  let change(state: State, value: AppSettings) =
    state.Disk.OnNext value
    publish(state, value)

  let current(state: State) : AppSettings = state.Settings.Value

  let observe(state: State) : IObservable<AppSettings> = state.Settings

  let setCloseToTray(state: State, enabled: bool) =
    change(
      state,
      {
        state.Settings.Value with
            CloseToTray = enabled
      }
    )

  let setTheme(state: State, choice: ThemeChoice) =
    change(
      state,
      {
        state.Settings.Value with
            Theme = choice
      }
    )

  let setLayout(state: State, choice: LayoutChoice) =
    change(
      state,
      {
        state.Settings.Value with
            Layout = choice
      }
    )

  let setWorkbenchState(state: State, value: string) =
    change(
      state,
      {
        state.Settings.Value with
            WorkbenchState = value
      }
    )

  let setEditorState(state: State, value: string) =
    change(
      state,
      {
        state.Settings.Value with
            EditorState = value
      }
    )

  let setSidebarExpanded(state: State, expanded: bool) =
    change(
      state,
      {
        state.Settings.Value with
            SidebarExpanded = expanded
      }
    )

  let setChatDraft(state: State, key: string, text: string) =
    let drafts =
      if String.IsNullOrWhiteSpace text then
        Map.remove key state.Settings.Value.ChatDrafts
      else
        Map.add key text state.Settings.Value.ChatDrafts

    publish(
      state,
      {
        state.Settings.Value with
            ChatDrafts = drafts
      }
    )

    state.Parks.OnNext()

  let setVoiceVolume(state: State, volume: float) =
    change(
      state,
      {
        state.Settings.Value with
            VoiceVolume = volume
      }
    )

  let setPinPromptHidden(state: State, hidden: bool) =
    change(
      state,
      {
        state.Settings.Value with
            PinPromptHidden = hidden
      }
    )

  let create(settings: AppSettings option, persist: AppSettings -> unit) =
    let state =
      new BehaviorSubject<AppSettings>(settings |> Option.defaultValue defaults)

    // The file's own stream: one writer at the end of it, and the
    // subject's serialized hand-off means the thread a change came from
    // stops mattering.
    let disk = new System.Reactive.Subjects.Subject<AppSettings>()

    disk |> Observable.add persist

    // The drafts arrive at typing speed, so their writes settle once
    // the typing rests: each park starts a settle, a newer park
    // cancels it, and the surviving settle writes the settings as they
    // stand then.
    let parks = new System.Reactive.Subjects.Subject<unit>()

    parks
    |> Observable.map(fun () -> asyncEx {
      do! Async.Sleep 500
      return state.Value
    })
    |> Observable.switchAsync
    |> Observable.add disk.OnNext

    let context: State = {
      Settings = state
      Disk = disk
      Parks = parks
    }

    {
      new AppSettingsStore with
        member _.Current = current context
        member _.Observe = observe context
        member _.SetCloseToTray(enabled) = setCloseToTray(context, enabled)
        member _.SetTheme(choice) = setTheme(context, choice)
        member _.SetLayout(choice) = setLayout(context, choice)

        member _.SetWorkbenchState(state) = setWorkbenchState(context, state)

        member _.SetEditorState(state) = setEditorState(context, state)

        member _.SetSidebarExpanded(expanded) =
          setSidebarExpanded(context, expanded)

        member _.SetChatDraft(key, text) = setChatDraft(context, key, text)
        member _.SetVoiceVolume(volume) = setVoiceVolume(context, volume)

        member _.SetPinPromptHidden(hidden) =
          setPinPromptHidden(context, hidden)
    }

let directory =
  Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DevNull"
  )

let settingsPath = Path.Combine(directory, "settings.json")

let save(settings: AppSettings) : Result<unit, string> =
  try
    Directory.CreateDirectory directory |> ignore
    let json = JsonObject()
    json.["close_to_tray"] <- JsonValue.Create settings.CloseToTray
    json["theme"] <- JsonValue.Create(themeString settings.Theme)
    json["layout"] <- JsonValue.Create(layoutString settings.Layout)
    json["workbench_state"] <- JsonValue.Create settings.WorkbenchState
    json["editor_state"] <- JsonValue.Create settings.EditorState
    json["sidebar_expanded"] <- JsonValue.Create settings.SidebarExpanded

    let drafts = JsonObject()

    for key, text in Map.toList settings.ChatDrafts do
      drafts[key] <- JsonValue.Create text

    json.["chat_drafts"] <- drafts
    json.["voice_volume"] <- JsonValue.Create settings.VoiceVolume
    json.["pin_prompt_hidden"] <- JsonValue.Create settings.PinPromptHidden
    File.WriteAllText(settingsPath, json.ToJsonString())
    Ok()
  with ex ->
    Error ex.Message

let load() : Result<AppSettings option, string> =
  try
    use document = JsonDocument.Parse(File.ReadAllText settingsPath)
    let root = document.RootElement

    let closeToTray =
      match root.TryGetProperty "close_to_tray" with
      | true, value when value.ValueKind = JsonValueKind.True -> true
      | _ -> false

    let theme =
      match root.TryGetProperty "theme" with
      | true, value when value.ValueKind = JsonValueKind.String ->
        value.GetString() |> Option.ofObj |> Option.bind parseTheme
      | _ -> None
      |> Option.defaultValue System

    let layout =
      match root.TryGetProperty "layout" with
      | true, value when value.ValueKind = JsonValueKind.String ->
        value.GetString() |> Option.ofObj |> Option.bind parseLayout
      | _ -> None
      |> Option.defaultValue CurrentLayout

    let workbenchState =
      match root.TryGetProperty "workbench_state" with
      | true, value when value.ValueKind = JsonValueKind.String ->
        value.GetString() |> Option.ofObj |> Option.defaultValue ""
      | _ -> ""

    let editorState =
      match root.TryGetProperty "editor_state" with
      | true, value when value.ValueKind = JsonValueKind.String ->
        value.GetString() |> Option.ofObj |> Option.defaultValue ""
      | _ -> ""

    let sidebarExpanded =
      match root.TryGetProperty "sidebar_expanded" with
      | true, value when value.ValueKind = JsonValueKind.False -> false
      | _ -> true

    let chatDrafts =
      match root.TryGetProperty "chat_drafts" with
      | true, value when value.ValueKind = JsonValueKind.Object ->
        value.EnumerateObject()
        |> Seq.choose(fun property ->
          match property.Value.ValueKind with
          | JsonValueKind.String ->
            property.Value.GetString()
            |> Option.ofObj
            |> Option.map(fun text -> property.Name, text)
          | _ -> None)
        |> Map.ofSeq
      | _ -> Map.empty

    let voiceVolume =
      match root.TryGetProperty "voice_volume" with
      | true, value when value.ValueKind = JsonValueKind.Number ->
        let read = value.GetDouble()

        if Double.IsNaN read || Double.IsInfinity read then
          1.0
        else
          Math.Clamp(read, 0.0, 1.0)
      | _ -> 1.0

    let pinPromptHidden =
      match root.TryGetProperty "pin_prompt_hidden" with
      | true, value when value.ValueKind = JsonValueKind.True -> true
      | _ -> false

    Ok(
      Some {
        CloseToTray = closeToTray
        Theme = theme
        Layout = layout
        WorkbenchState = workbenchState
        EditorState = editorState
        SidebarExpanded = sidebarExpanded
        ChatDrafts = chatDrafts
        VoiceVolume = voiceVolume
        PinPromptHidden = pinPromptHidden
      }
    )
  with
  | :? FileNotFoundException
  | :? DirectoryNotFoundException -> Ok None
  | ex -> Error ex.Message
