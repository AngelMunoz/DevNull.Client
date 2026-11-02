module DevNull.Client.Slices

open System

/// One gated slice with one upstream subscription, replayed to a
/// binding that joins after the last emission.
val inline share: source: IObservable<'T> -> IObservable<'T>

/// Passes the projection on only when `keyOf` changes.
val inline gate: keyOf: ('T -> 'K) -> source: IObservable<'T> -> IObservable<'K>

/// Passes the source on only when its reference changes. Use it for a
/// list or record a page hands to a feed, so a tick that left the value
/// alone does not rebuild the rows.
val inline gateByRef:
  source: IObservable<'T> -> IObservable<'T> when 'T: not struct
