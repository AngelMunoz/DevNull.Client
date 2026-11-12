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

/// The arrangement the window's content shows: the site's own frame
/// today, one case per layout the app adopts.
type LayoutChoice =
  | CurrentLayout
  | WorkbenchLayout

/// The layout choice a stored name stands for; anything unknown is
/// none.
val parseLayout: name: string -> LayoutChoice option

/// The stored name of one layout choice, the same word the layout
/// registry knows.
val layoutString: choice: LayoutChoice -> string

/// The local, machine-scoped preferences. Nothing here goes to the
/// server.
type AppSettings = {
  CloseToTray: bool
  Theme: ThemeChoice
  /// The arrangement the window's content shows.
  Layout: LayoutChoice
  /// The workbench's stored state, one raw string its own parse leaf reads.
  WorkbenchState: string
  /// Whether the desktop sidebar shows its labels.
  SidebarExpanded: bool
  /// Unsent chat drafts by conversation key, emptied as they go out.
  ChatDrafts: Map<string, string>
  /// The voice-note player's volume, 0.0 to 1.0.
  VoiceVolume: float
  /// Whether the org-pin dialog keeps its "Don't show this again"
  /// choice and never asks again on this device.
  PinPromptHidden: bool
}

/// The live local settings: one state stream for readers, one writer
/// stream for the file. Every write — immediate or settled — funnels
/// through the writer stream, which hands each to the `persist` hook
/// serialized, so no two writes ever cross and no member decides
/// timing inline: rare changes write at once, drafts settle once the
/// typing rests. The store never touches the file system itself.
type AppSettingsStore =
  /// The current settings, without a subscription.
  abstract Current: AppSettings

  /// Every settings change, starting with the current one.
  abstract Observe: IObservable<AppSettings>

  /// Writes the flag, then publishes it.
  abstract SetCloseToTray: enabled: bool -> unit

  /// Writes the choice, then publishes it.
  abstract SetTheme: choice: ThemeChoice -> unit

  /// Writes the layout choice, then publishes it. The window mounts
  /// what it names; the file keeps it for the next start.
  abstract SetLayout: choice: LayoutChoice -> unit
  abstract SetWorkbenchState: state: string -> unit

  /// Writes the sidebar width choice, then publishes it.
  abstract SetSidebarExpanded: expanded: bool -> unit

  /// Parks one conversation's unsent draft at once; an empty text
  /// drops it. The file's own copy settles once the typing rests.
  abstract SetChatDraft: key: string * text: string -> unit

  /// Writes the voice-note volume, then publishes it.
  abstract SetVoiceVolume: volume: float -> unit

  /// Writes whether the org-pin dialog ever asks again, then
  /// publishes it.
  abstract SetPinPromptHidden: hidden: bool -> unit

/// Accessor for the settings store capability.
type IProvideSettings =
  abstract Settings: AppSettingsStore

/// The live local settings over one state subject and one writer
/// stream.
module AppSettingsStore =

  /// The stored settings, or the defaults when the file is absent or
  /// unreadable.
  val create:
    settings: AppSettings option * persist: (AppSettings -> unit) ->
      AppSettingsStore

/// The stored settings: `Ok None` when the file is absent, `Error` when
/// the file exists and cannot be read.
val load: unit -> Result<AppSettings option, string>

/// Writes the settings file; the message carries the failure.
val save: settings: AppSettings -> Result<unit, string>
