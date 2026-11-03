namespace DevNull.Client.Views

open MVUish.Avalonia

/// The page's state logic: the model, the update, the subscriptions
/// that feed it from outside, and the store calls it runs.
module RemotesModel =

  /// The page's first model: the store's snapshot, the open target's
  /// parked draft, and the quiet defaults.
  val init: props: Props<'env> -> unit -> State.Model * Cmd<State.Msg>

  /// One message against the model: the new model and the effects it
  /// asks for.
  val update:
    props: Props<'env> ->
      (State.Msg -> State.Model -> State.Model * Cmd<State.Msg>)

  /// The page's inlets from outside: the shared clock, the session, the
  /// store, and the watcher's readership handles. The thread watch
  /// rides the open target: a new target restarts the sub, and the
  /// page's end releases it.
  val subscribe: props: Props<'env> -> model: State.Model -> Sub<State.Msg>
