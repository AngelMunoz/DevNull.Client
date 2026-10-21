module DevNull.Client.UiClock

open System
open System.Reactive.Subjects
open Avalonia.Threading

let ticks = new Subject<DateTimeOffset>()

/// One shared minute tick, started on the UI thread the first time a
/// view subscribes.
let start() =
  let timer =
    DispatcherTimer(
      DispatcherPriority.Background,
      Interval = TimeSpan.FromMinutes 1.0
    )

  timer.Tick.Add(fun _ -> ticks.OnNext(DateTimeOffset.UtcNow))
  timer.Start()

let started = lazy (start())

/// One shared per-minute tick on the UI thread, started lazily.
let minute: IObservable<DateTimeOffset> = {
  new IObservable<DateTimeOffset> with
    member _.Subscribe(observer) =
      started.Force()
      ticks.Subscribe observer
}
