module DevNull.Client.Stores

open System
open System.Reactive.Subjects
open Avalonia.Threading
open DevNull.Client.Core.ApiTypes

let emitOnUi<'value>
  (dispatcher: IDispatcher, subject: BehaviorSubject<'value>, value: 'value)
  : unit =
  dispatcher.Post((fun () -> subject.OnNext value), DispatcherPriority.Default)

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

[<Struct>]
type SessionStoreSetup = {
  Initial: DeviceSession option
  KeyFile: string
  Forget: unit -> unit
  Dispatcher: IDispatcher
}

module SessionStore =

  let emit
    (
      dispatcher: IDispatcher,
      subject: BehaviorSubject<SessionState>,
      value: SessionState
    ) =
    if subject.Value <> value then
      if dispatcher.CheckAccess() then
        subject.OnNext value
      else
        emitOnUi(dispatcher, subject, value)

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

  let enter
    (
      dispatcher: IDispatcher,
      subject: BehaviorSubject<SessionState>,
      session: SignedInSession
    ) =
    emit(dispatcher, subject, SignedIn session)

  let leave
    (
      dispatcher: IDispatcher,
      subject: BehaviorSubject<SessionState>,
      forget: unit -> unit
    ) =
    forget()
    emit(dispatcher, subject, SignedOut)

  let refresh
    (
      dispatcher: IDispatcher,
      subject: BehaviorSubject<SessionState>,
      session: DeviceSession
    ) =
    match subject.Value with
    | SignedIn current when current.Session.User.Id = session.User.Id ->
      emit(dispatcher, subject, SignedIn { current with Session = session })
    | _ -> ()

  let create(setup: SessionStoreSetup) =
    let state =
      setup.Initial
      |> Option.map(fun session ->
        SignedIn {
          Session = session
          KeyFile = setup.KeyFile
        })
      |> Option.defaultValue SignedOut

    let subject = new BehaviorSubject<SessionState>(state)

    {
      new SessionStore with
        member _.Current = current subject
        member _.Observe = observe subject
        member _.SignedIn = signedIn subject

        member _.Enter(session) =
          enter(setup.Dispatcher, subject, session)

        member _.Leave() =
          leave(setup.Dispatcher, subject, setup.Forget)

        member _.Refresh(session) =
          refresh(setup.Dispatcher, subject, session)
    }
