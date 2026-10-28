module DevNull.Client.SettingsStore

open System
open System.IO
open System.Reactive.Subjects
open System.Text.Json
open System.Text.Json.Nodes

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

type AppSettingsStore(settings: AppSettings, persist: AppSettings -> unit) =

  let subject = new BehaviorSubject<AppSettings>(settings)

  let emit value =
    if subject.Value <> value then
      subject.OnNext value

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

  member _.Current: AppSettings = subject.Value

  member _.Observe: IObservable<AppSettings> =
    subject :> IObservable<AppSettings>

  member _.SetCloseToTray(enabled: bool) : unit =
    let next = {
      subject.Value with
          CloseToTray = enabled
    }

    persist next
    emit next

  member _.SetTheme(choice: ThemeChoice) : unit =
    let next = { subject.Value with Theme = choice }

    persist next
    emit next

  member _.SetSidebarExpanded(expanded: bool) : unit =
    let next = {
      subject.Value with
          SidebarExpanded = expanded
    }

    persist next
    emit next

  /// Parks one conversation's unsent draft; an empty text drops it.
  member _.SetChatDraft(key: string, text: string) : unit =
    let drafts =
      if String.IsNullOrWhiteSpace text then
        Map.remove key subject.Value.ChatDrafts
      else
        Map.add key text subject.Value.ChatDrafts

    let next = {
      subject.Value with
          ChatDrafts = drafts
    }

    persist next
    emit next

  /// Persists the voice-note volume, then publishes it.
  member _.SetVoiceVolume(volume: float) : unit =
    let next = {
      subject.Value with
          VoiceVolume = volume
    }

    persist next
    emit next

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
