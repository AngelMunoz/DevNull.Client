module DevNull.Client.RouteStore

open System
open System.Reactive.Subjects

/// The app's route state: the live path the router stands on. The
/// shell feeds it once from the router; readers follow it through
/// their programs' subscriptions, so no view ever binds it directly.
type RouteStore() =

  let path = new BehaviorSubject<string>("")

  /// Records the route the router landed on.
  member _.Land(url: string) =
    if path.Value <> url then
      path.OnNext url

  /// The live route path, starting with the current one.
  member _.Path: IObservable<string> = path

  /// The route the router stands on.
  member _.Current: string = path.Value

/// Accessor for the route store capability.
type IProvideRoutes =
  abstract Routes: RouteStore
