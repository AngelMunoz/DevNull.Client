namespace DevNull.Client.Layouts.Editor

open DevNull.Client.Core.ApiTypes

/// The editor's stored state: the selected ref, read from the one
/// string the settings file keeps. The representation stays hidden;
/// the parse leaf is the only way to build one.
type StoredState

module StoredState =

  /// The ref this state selects.
  val selectedRef: state: StoredState -> LogKind

module EditorSettings =

  /// The state a fresh install reads: Following.
  val initial: unit -> StoredState

  /// Reads the stored string. A null, blank, or unknown value answers
  /// the default, Following.
  val parse: raw: string | null -> StoredState

  /// Writes the state as the one stored string.
  val store: state: StoredState -> string
