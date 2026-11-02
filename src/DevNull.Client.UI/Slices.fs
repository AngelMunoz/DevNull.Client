module DevNull.Client.Slices

open System
open FSharp.Control.Reactive

let inline share(source: IObservable<'T>) : IObservable<'T> =
  source |> Observable.replayBuffer 1 |> Observable.refCount

let inline gate (keyOf: 'T -> 'K) (source: IObservable<'T>) : IObservable<'K> =
  source |> Observable.map keyOf |> Observable.distinctUntilChanged

let inline gateByRef
  (source: IObservable<'T>)
  : IObservable<'T> when 'T: not struct =
  source |> Observable.distinctUntilChangedCompare HashIdentity.Reference
