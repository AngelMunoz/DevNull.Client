module DevNull.Client.Stores

open System
open System.Reactive.Subjects
open Avalonia.Threading
open DevNull.Client.Core.ApiTypes

let emitOnUi<'value>(subject: BehaviorSubject<'value>, value: 'value) : unit =
  Dispatcher.UIThread.Post(fun () -> subject.OnNext value)

type SignedInSession = {
  Session: DeviceSession
  KeyFile: string
}

type SessionState =
  | SignedOut
  | SignedIn of SignedInSession

type SessionStore =
  abstract Current: SessionState
  abstract Observe: IObservable<SessionState>
  abstract SignedIn: SignedInSession voption
  abstract Enter: session: SignedInSession -> unit
  abstract Leave: unit -> unit
  abstract Refresh: session: DeviceSession -> unit

type IProvideSession =
  abstract Session: SessionStore

module SessionStore =

  let emit(subject: BehaviorSubject<SessionState>, value: SessionState) =
    if subject.Value <> value then
      if Dispatcher.UIThread.CheckAccess() then
        subject.OnNext value
      else
        emitOnUi(subject, value)

  let current(subject: BehaviorSubject<SessionState>) : SessionState =
    subject.Value

  let observe
    (subject: BehaviorSubject<SessionState>)
    : IObservable<SessionState> =
    subject

  let signedIn
    (subject: BehaviorSubject<SessionState>)
    : SignedInSession voption =
    match subject.Value with
    | SignedIn session -> ValueSome session
    | SignedOut -> ValueNone

  let enter(subject: BehaviorSubject<SessionState>, session: SignedInSession) =
    emit(subject, SignedIn session)

  let leave(subject: BehaviorSubject<SessionState>, forget: unit -> unit) =
    forget()
    emit(subject, SignedOut)

  let refresh(subject: BehaviorSubject<SessionState>, session: DeviceSession) =
    match subject.Value with
    | SignedIn current when current.Session.User.Id = session.User.Id ->
      emit(subject, SignedIn { current with Session = session })
    | _ -> ()

  let create
    (initial: DeviceSession option, keyFile: string, forget: unit -> unit)
    =
    let state =
      initial
      |> Option.map(fun session ->
        SignedIn { Session = session; KeyFile = keyFile })
      |> Option.defaultValue SignedOut

    let subject = new BehaviorSubject<SessionState>(state)

    {
      new SessionStore with
        member _.Current = current subject
        member _.Observe = observe subject
        member _.SignedIn = signedIn subject
        member _.Enter(session) = enter(subject, session)
        member _.Leave() = leave(subject, forget)
        member _.Refresh(session) = refresh(subject, session)
    }
