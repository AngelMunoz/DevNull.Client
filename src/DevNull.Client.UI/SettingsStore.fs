module DevNull.Client.SettingsStore

open System
open System.IO
open System.Reactive.Subjects
open System.Text.Json
open System.Text.Json.Nodes
open FSharp.Control.Reactive
open IcedTasks

/// The theme variants the app follows: the system, or one pinned.
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

type AppSettings = {
  CloseToTray: bool
  Theme: ThemeChoice
  SidebarExpanded: bool
  /// Unsent chat drafts by conversation key, emptied as they go out.
  ChatDrafts: Map<string, string>
  /// The voice-note player's volume, 0.0 to 1.0.
  VoiceVolume: float
}

/// The live local settings: one state stream for readers, one writer
/// stream for the file. Every write — immediate or settled — funnels
/// through the writer stream, which hands each to the `persist` hook
/// serialized, so no two writes ever cross and no member decides
/// timing inline. The store never touches the file system itself.
type AppSettingsStore(settings: AppSettings, persist: AppSettings -> unit) =

  let state = new BehaviorSubject<AppSettings>(settings)

  // The file's own stream: one writer at the end of it, and the
  // subject's serialized hand-off means the thread a change came from
  // stops mattering.
  let disk = new System.Reactive.Subjects.Subject<AppSettings>()

  do disk |> Observable.add persist

  // The drafts arrive at typing speed, so their writes settle once
  // the typing rests: each park starts a settle, a newer park
  // cancels it, and the surviving settle writes the settings as they
  // stand then.
  let parks = new System.Reactive.Subjects.Subject<unit>()

  do
    parks
    |> Observable.map(fun () -> asyncEx {
      do! Async.Sleep 500
      return state.Value
    })
    |> Observable.switchAsync
    |> Observable.add disk.OnNext

  let publish(value: AppSettings) =
    if state.Value <> value then
      state.OnNext value

  let change(value: AppSettings) =
    disk.OnNext value
    publish value

  new(settings: AppSettings option, persist: AppSettings -> unit) =
    AppSettingsStore(
      settings
      |> Option.defaultValue {
        CloseToTray = false
        Theme = System
        SidebarExpanded = true
        ChatDrafts = Map.empty
        VoiceVolume = 1.0
      },
      persist
    )

  member _.Current: AppSettings = state.Value

  member _.Observe: IObservable<AppSettings> = state :> IObservable<AppSettings>

  member _.SetCloseToTray(enabled: bool) : unit =
    change(
      {
        state.Value with
            CloseToTray = enabled
      }
    )

  member _.SetTheme(choice: ThemeChoice) : unit =
    change({ state.Value with Theme = choice })

  member _.SetSidebarExpanded(expanded: bool) : unit =
    change(
      {
        state.Value with
            SidebarExpanded = expanded
      }
    )

  /// Parks one conversation's unsent draft at once; an empty text
  /// drops it. The file's own copy settles once the typing rests.
  member _.SetChatDraft(key: string, text: string) : unit =
    let drafts =
      if String.IsNullOrWhiteSpace text then
        Map.remove key state.Value.ChatDrafts
      else
        Map.add key text state.Value.ChatDrafts

    publish({ state.Value with ChatDrafts = drafts })
    parks.OnNext()

  member _.SetVoiceVolume(volume: float) : unit =
    change(
      {
        state.Value with
            VoiceVolume = volume
      }
    )

type IProvideSettings =
  abstract Settings: AppSettingsStore

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
    json.["theme"] <- JsonValue.Create(themeString settings.Theme)
    json.["sidebar_expanded"] <- JsonValue.Create settings.SidebarExpanded

    let drafts = JsonObject()

    for key, text in Map.toList settings.ChatDrafts do
      drafts[key] <- JsonValue.Create text

    json.["chat_drafts"] <- drafts
    json.["voice_volume"] <- JsonValue.Create settings.VoiceVolume
    File.WriteAllText(settingsPath, json.ToJsonString())
    Ok()
  with ex ->
    Error ex.Message

/// The stored settings: `Ok None` when the file is absent, `Error` when
/// the file exists and cannot be read.
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

    Ok(
      Some {
        CloseToTray = closeToTray
        Theme = theme
        SidebarExpanded = sidebarExpanded
        ChatDrafts = chatDrafts
        VoiceVolume = voiceVolume
      }
    )
  with
  | :? FileNotFoundException
  | :? DirectoryNotFoundException -> Ok None
  | ex -> Error ex.Message
