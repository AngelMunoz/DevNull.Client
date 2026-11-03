module DevNull.Client.UiClock

open System

/// The app's clock: one shared tick on the UI thread, owned by the
/// env, so pages read time through their models instead of running
/// timers of their own.
type UiClockService =
  /// Every tick, each carrying the moment it fired.
  abstract Minute: IObservable<DateTimeOffset>

/// Accessor for the clock capability.
type IProvideUiClock =
  abstract UiClock: UiClockService

/// The clock over one interval.
module UiClockService =

  /// One shared tick; the interval is a parameter so the test env
  /// runs a fast clock.
  val create: interval: TimeSpan -> UiClockService
