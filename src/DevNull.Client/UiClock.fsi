module DevNull.Client.UiClock

open System

/// One shared per-minute tick on the UI thread, started lazily.
val minute: IObservable<DateTimeOffset>
