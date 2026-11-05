module DevNull.Client.Views.CommitRows

open System
open DevNull.Client
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Formatting
open DevNull.Client.ImageCache

[<Struct>]
type RowSlices = {
  Pasteis: bool
  Now: DateTimeOffset
  PollChoices: Map<string, string>
  Voting: string option
  Pinning: string option
}

type Host = {
  OnEvent: Event -> unit
  ViewerId: string
  Images: ImageCacheService
  Flyout: Source voption
  Zone: Zone
  OnPin: (Commit -> unit) voption
  OnShare: (Commit -> unit) voption
  Actions: bool
  OnStar: Commit -> unit
  OnVote: Commit -> unit
  OnPastel: Commit -> unit
  Translation: IObservable<Commit -> TranslationView voption>
}

let row
  (host: Host, model: IObservable<'m>, slicesOf: 'm -> RowSlices)
  : RowProps =
  let slices = model |> Slices.gate slicesOf |> Slices.share

  let busy = slices |> Observable.map _.Voting |> Slices.share

  let inline slot(command: Commit -> unit) : ActionSlot voption =
    ValueSome { Command = command; Busy = busy }

  let inline offered(command: Commit -> unit) : ActionSlot voption =
    if host.Actions then slot command else ValueNone

  let pin: PinToggle voption =
    match host.OnPin with
    | ValueSome command ->
      ValueSome {
        Command = command
        Busy = slices |> Observable.map _.Pinning |> Slices.share
      }
    | ValueNone -> ValueNone

  {
    OnEvent = host.OnEvent
    ViewerId = host.ViewerId
    Pasteis = slices |> Observable.map _.Pasteis |> Slices.share
    Now = slices |> Observable.map _.Now |> Slices.share
    PollChoices = slices |> Observable.map _.PollChoices |> Slices.share
    Dense = false
    Images = host.Images
    Flyout = host.Flyout
    Pin = pin
    Star = slot host.OnStar
    Vote = offered host.OnVote
    Pastel = offered host.OnPastel
    Share = host.OnShare
    Translation = host.Translation
    Zone = host.Zone
  }
