module DevNull.Client.UnreadPoller

open System
open System.Reactive.Subjects
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Stores

/// Polls the signed account on an interval and pushes
/// `UnreadNotificationsCount` into the session store. It never reads
/// the inbox list: listing marks everything read as a side effect. A
/// failed poll keeps the last count, so transient transport or decode
/// failures are silent by design. `paused` is the pause gate: while it
/// holds true no request goes out, and the next false lets the poller
/// resume. The returned handle cancels the loop.
val run:
  env:
    #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp & #IProvideSession *
  paused: BehaviorSubject<bool> *
  intervalMs: int ->
    IDisposable

/// The production loop: one poll every 45 seconds.
val inline start:
  env:
    #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp & #IProvideSession *
  paused: BehaviorSubject<bool> ->
    unit
