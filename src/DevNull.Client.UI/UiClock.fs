module DevNull.Client.UiClock

open System
open System.Reactive.Subjects
open System.Timers

type UiClockService =
  abstract Minute: IObservable<DateTimeOffset>

type IProvideUiClock =
  abstract UiClock: UiClockService

module UiClockService =

  let minute(ticks: Subject<DateTimeOffset>) : IObservable<DateTimeOffset> =
    ticks

  // The timer is a thread-pool one - the same kind MVUish's own
  // Sub.interval starts: ticks reach pages through dispatch, and
  // constructing the service never touches the UI dispatcher. A
  // DispatcherTimer built off the session thread would instead claim
  // the UI dispatcher before the headless session owns it.
  let create(interval: TimeSpan) =
    let ticks = new Subject<DateTimeOffset>()
    let timer = new Timer(interval.TotalMilliseconds)

    timer.Elapsed.Add(fun _ -> ticks.OnNext(DateTimeOffset.UtcNow))

    timer.Start()

    {
      new UiClockService with
        member _.Minute = minute ticks
    }
