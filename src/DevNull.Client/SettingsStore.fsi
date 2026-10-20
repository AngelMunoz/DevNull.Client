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

/// Accessor for the settings store capability.
type IProvideSettings =
  abstract Settings: AppSettingsStore

/// The stored settings file, or None when it is absent or unreadable.
val load: unit -> AppSettings option

/// Writes the settings file; the message carries the failure.
val save: settings: AppSettings -> Result<unit, string>
