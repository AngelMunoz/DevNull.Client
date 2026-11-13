module DevNull.Client.Slices

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open FSharp.Control.Reactive

let inline share(source: IObservable<'T>) : IObservable<'T> =
  source |> Observable.replayBuffer 1 |> Observable.refCount

let inline gate (keyOf: 'T -> 'K) (source: IObservable<'T>) : IObservable<'K> =
  source |> Observable.map keyOf |> Observable.distinctUntilChanged

let inline gateByRef
  (source: IObservable<'T>)
  : IObservable<'T> when 'T: not struct =
  source |> Observable.distinctUntilChangedCompare HashIdentity.Reference

/// Passes the source on only when the payload reference is new or the
/// cheap key changed. Use it for a list feed whose rows also read a
/// small config (a ranked flag, a last-visit marker, a clock minute).
/// A missing payload counts as one stable reference, so an empty
/// option does not rebuild on every tick.
let gateByRefKey
  (refOf: 'T -> 'U | null)
  (keyOf: 'T -> 'K)
  (source: IObservable<'T>)
  : IObservable<'T> =
  let equalityComparer: IEqualityComparer<'T> = {
    new IEqualityComparer<'T> with
      member _.Equals(left, right) =
        match left, right with
        | null, null -> true
        | null, _
        | _, null -> false
        | left, right ->
          match refOf left, refOf right with
          | null, null -> keyOf left = keyOf right
          | null, _
          | _, null -> false
          | a, b -> obj.ReferenceEquals(a, b) && keyOf left = keyOf right

      member _.GetHashCode value = RuntimeHelpers.GetHashCode(refOf value)
  }

  source |> Observable.distinctUntilChangedCompare equalityComparer
