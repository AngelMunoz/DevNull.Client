module DevNull.Client.LastVisitStore

open System

/// The per-user, per-log marker of the newest commit the last visit
/// saw. One file under the app's data folder holds every marker; the
/// store keeps the map in memory and persists through the injected
/// hook, so tests never touch the file system.
type LastVisitStore =
  /// The file-backed store over the app's data folder.
  new: unit -> LastVisitStore

  /// The in-memory store over the given map and persist hook.
  new:
    stored: Map<string, DateTimeOffset> *
    persist: (Map<string, DateTimeOffset> -> unit) ->
      LastVisitStore

  /// The last visit's newest commit time, when this user and log have
  /// one.
  member LastVisit: user: string * log: string -> DateTimeOffset option

  /// Records the newest commit time as this visit's marker.
  member Mark: user: string * log: string * newest: DateTimeOffset -> unit

/// Accessor for the last-visit capability.
type IProvideLastVisit =
  abstract LastVisit: LastVisitStore
