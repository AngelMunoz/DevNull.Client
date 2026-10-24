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
}

/// The live local settings: one subject, persists through the `persist`
/// hook, so the store never touches the file system itself.
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

  /// Persists the flag, then publishes it.
  member SetCloseToTray: enabled: bool -> unit

  /// Persists the choice, then publishes it.
  member SetTheme: choice: ThemeChoice -> unit

  /// Persists the sidebar width choice, then publishes it.
  member SetSidebarExpanded: expanded: bool -> unit

/// Accessor for the settings store capability.
type IProvideSettings =
  abstract Settings: AppSettingsStore

/// The stored settings: `Ok None` when the file is absent, `Error` when
/// the file exists and cannot be read.
val load: unit -> Result<AppSettings option, string>

/// Writes the settings file; the message carries the failure.
val save: settings: AppSettings -> Result<unit, string>
