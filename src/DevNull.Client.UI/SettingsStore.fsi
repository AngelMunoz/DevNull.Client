module DevNull.Client.SettingsStore

open System

/// The theme variants the app follows: the system, or one pinned.
type ThemeChoice =
  | System
  | Light
  | Dark

/// The theme choice a stored name stands for; anything unknown is the
/// system.
val parseTheme: name: string -> ThemeChoice option

/// The stored name of one theme choice.
val themeString: choice: ThemeChoice -> string

/// The local, machine-scoped preferences. Nothing here goes to the
/// server.
type AppSettings = {
  CloseToTray: bool
  Theme: ThemeChoice
  /// Whether the desktop sidebar shows its labels.
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
/// timing inline: rare changes write at once, drafts settle once the
/// typing rests. The store never touches the file system itself.
type AppSettingsStore =
  new:
    settings: AppSettings * persist: (AppSettings -> unit) -> AppSettingsStore

  /// The stored settings, or the defaults when the file is absent or
  /// unreadable.
  new:
    settings: AppSettings option * persist: (AppSettings -> unit) ->
      AppSettingsStore

  /// The current settings, without a subscription.
  member Current: AppSettings

  /// Every settings change, starting with the current one.
  member Observe: IObservable<AppSettings>

  /// Writes the flag, then publishes it.
  member SetCloseToTray: enabled: bool -> unit

  /// Writes the choice, then publishes it.
  member SetTheme: choice: ThemeChoice -> unit

  /// Writes the sidebar width choice, then publishes it.
  member SetSidebarExpanded: expanded: bool -> unit

  /// Parks one conversation's unsent draft at once; an empty text
  /// drops it. The file's own copy settles once the typing rests.
  member SetChatDraft: key: string * text: string -> unit

  /// Writes the voice-note volume, then publishes it.
  member SetVoiceVolume: volume: float -> unit

/// Accessor for the settings store capability.
type IProvideSettings =
  abstract Settings: AppSettingsStore

/// The stored settings: `Ok None` when the file is absent, `Error` when
/// the file exists and cannot be read.
val load: unit -> Result<AppSettings option, string>

/// Writes the settings file; the message carries the failure.
val save: settings: AppSettings -> Result<unit, string>
