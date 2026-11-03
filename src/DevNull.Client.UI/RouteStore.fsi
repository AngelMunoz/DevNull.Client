module DevNull.Client.RouteStore

open System

/// The app's route state: the live path the router stands on. The
/// shell feeds it once from the router; readers follow it through
/// their programs' subscriptions, so no view ever binds it directly.
type RouteStore =
  /// Records the route the router landed on.
  abstract Land: url: string -> unit

  /// The live route path, starting with the current one.
  abstract Path: IObservable<string>

  /// The route the router stands on.
  abstract Current: string

/// Accessor for the route store capability.
type IProvideRoutes =
  abstract Routes: RouteStore

/// The app's route state over one subject.
module RouteStore =

  /// Builds a route store with its own subject; the env builds the
  /// app's one instance.
  val create: unit -> RouteStore
