module DevNull.Client.UiClock

open System
open System.Reactive.Subjects
open System.Timers

/// The app's clock: one shared tick, owned by the env, so pages read
/// time through their models instead of running timers of their own.
/// The timer is a thread-pool one - the same kind MVUish's own
/// Sub.interval starts: ticks reach pages through dispatch, and
/// constructing the service never touches the UI dispatcher. A
/// DispatcherTimer built off the session thread would instead claim
/// the UI dispatcher before the headless session owns it.
type UiClockService(interval: TimeSpan) =

  let ticks = new Subject<DateTimeOffset>()

  let timer = new Timer(interval.TotalMilliseconds)

  do timer.Elapsed.Add(fun _ -> ticks.OnNext(DateTimeOffset.UtcNow))

  do timer.Start()

  /// Every tick, each carrying the moment it fired.
  member _.Minute: IObservable<DateTimeOffset> =
    ticks :> IObservable<DateTimeOffset>

/// Accessor for the clock capability.
type IProvideUiClock =
  abstract UiClock: UiClockService
