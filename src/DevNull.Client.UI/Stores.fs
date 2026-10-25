module DevNull.Client.Stores

open System
open System.Reactive.Subjects
open Avalonia.Threading
open DevNull.Client.Core.ApiTypes

/// Posts one subject value on the UI thread, so a producer on any
/// thread never re-enters a control off the dispatcher.
let emitOnUi<'value>(subject: BehaviorSubject<'value>, value: 'value) : unit =
  Dispatcher.UIThread.Post(fun () -> subject.OnNext value)

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
type SessionStore(state: SessionState, forget: unit -> unit) =

  let subject = new BehaviorSubject<SessionState>(state)

  let emit value =
    if subject.Value <> value then
      if Dispatcher.UIThread.CheckAccess() then
        subject.OnNext value
      else
        emitOnUi(subject, value)

  /// The state at boot, from the stored session file.
  new(initial: DeviceSession option, keyFile: string, forget: unit -> unit) =
    SessionStore(
      initial
      |> Option.map(fun session ->
        SignedIn { Session = session; KeyFile = keyFile })
      |> Option.defaultValue SignedOut,
      forget
    )

  /// The current state, without a subscription.
  member _.Current: SessionState = subject.Value

  /// Every state change, starting with the current one.
  member _.Observe: IObservable<SessionState> =
    subject :> IObservable<SessionState>

  /// The signed-in session, when one exists.
  member _.SignedIn: SignedInSession voption =
    match subject.Value with
    | SignedIn session -> ValueSome session
    | SignedOut -> ValueNone

  /// Records a completed sign-in.
  member _.Enter(session: SignedInSession) : unit = emit(SignedIn session)

  /// Records a sign-out and drops the stored session through `forget`.
  member _.Leave() : unit =
    forget()
    emit SignedOut

  /// Refreshes the server view of the signed-in session.
  member _.Refresh(session: DeviceSession) : unit =
    match subject.Value with
    | SignedIn current when current.Session.User.Id = session.User.Id ->
      emit(SignedIn { current with Session = session })
    | _ -> ()

/// Accessor for the session store capability.
type IProvideSession =
  abstract Session: SessionStore
