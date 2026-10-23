module DevNull.Client.RouteStore

open System

/// The app's route state: the live path the router stands on. The
/// shell feeds it once from the router; readers follow it through
/// their programs' subscriptions, so no view ever binds it directly.
type RouteStore =

  /// One shared instance for the app.
  new: unit -> RouteStore

  /// Records the route the router landed on.
  member Land: url: string -> unit

  /// The live route path, starting with the current one.
  member Path: IObservable<string>

  /// The route the router stands on.
  member Current: string

/// Accessor for the route store capability.
type IProvideRoutes =
  abstract Routes: RouteStore
