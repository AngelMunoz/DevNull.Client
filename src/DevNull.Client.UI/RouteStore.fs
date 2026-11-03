module DevNull.Client.RouteStore

open System
open System.Reactive.Subjects

type RouteStore =
  abstract Land: url: string -> unit
  abstract Path: IObservable<string>
  abstract Current: string

type IProvideRoutes =
  abstract Routes: RouteStore

module RouteStore =

  let land(subject: BehaviorSubject<string>, url: string) =
    if subject.Value <> url then
      subject.OnNext url

  let path(subject: BehaviorSubject<string>) : IObservable<string> = subject

  let create() =
    let subject = new BehaviorSubject<string>("")

    {
      new RouteStore with
        member _.Land(url) = land(subject, url)
        member _.Path = path subject
        member _.Current = subject.Value
    }
