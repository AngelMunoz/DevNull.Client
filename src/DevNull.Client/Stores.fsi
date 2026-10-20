module DevNull.Client.Stores

open System
open System.Reactive.Subjects
open DevNull.Client.Core.ApiTypes

/// Posts one subject value on the UI thread, so a producer on any
/// thread never re-enters a control off the dispatcher.
val emitOnUi:
  subject: BehaviorSubject<'value> * value: 'value -> unit when 'value: equality

/// One sign-in worth of state: the server session plus the device key
/// file every signed request needs.
type SignedInSession = {
  Session: DeviceSession
  KeyFile: string
}

/// The sign-in state the whole app reads.
type SessionState =
  | SignedOut
  | SignedIn of SignedInSession

/// The live sign-in state: one subject, emits on the UI thread. The
/// `forget` hook owns the stored-session file, so the store never
/// touches the file system itself.
type SessionStore =

  /// The state at boot, from the stored session file. `forget` clears
  /// the stored session on sign-out.
  new:
    initial: DeviceSession option * keyFile: string * forget: (unit -> unit) ->
      SessionStore

  /// The current state, without a subscription.
  member Current: SessionState

  /// Every state change, starting with the current one.
  member Observe: IObservable<SessionState>

  /// The signed-in session, when one exists.
  member SignedIn: SignedInSession voption

  /// Records a completed sign-in.
  member Enter: session: SignedInSession -> unit

  /// Records a sign-out and drops the stored session.
  member Leave: unit -> unit

  /// Refreshes the server view of the signed-in session.
  member Refresh: session: DeviceSession -> unit

/// Accessor for the session store capability.
type IProvideSession =
  abstract Session: SessionStore
