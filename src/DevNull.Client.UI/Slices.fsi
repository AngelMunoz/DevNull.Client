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

/// Passes the source on only when the payload reference is new or the
/// cheap key changed. Use it for a list feed whose rows also read a
/// small config (a ranked flag, a last-visit marker, a clock minute).
/// A missing payload counts as one stable reference, so an empty
/// option does not rebuild on every tick.
val gateByRefKey:
  refOf: ('T -> 'U | null) ->
  keyOf: ('T -> 'K) ->
  source: IObservable<'T> ->
    IObservable<'T>
    when 'K: equality
