module DevNull.Client.Views.CommitRows

open System
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Components
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Formatting
open DevNull.Client.ImageCache

[<Struct>]
type Slices = {
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
  OnStar: Commit -> unit
  OnVote: Commit -> unit
  OnPastel: Commit -> unit
  Translation: IObservable<Commit -> Translation.View voption>
}

let row(host: Host, model: IObservable<'m>, slicesOf: 'm -> Slices) : RowProps =
  let slices = model |> Observable.map slicesOf |> PageChrome.share

  let busy = slices |> Observable.map _.Voting |> PageChrome.share

  let slot(command: Commit -> unit) : ActionSlot voption =
    ValueSome { Command = command; Busy = busy }

  let pin: PinToggle voption =
    match host.OnPin with
    | ValueSome command ->
      ValueSome {
        Command = command
        Busy = slices |> Observable.map _.Pinning |> PageChrome.share
      }
    | ValueNone -> ValueNone

  {
    OnEvent = host.OnEvent
    ViewerId = host.ViewerId
    Pasteis = slices |> Observable.map _.Pasteis |> PageChrome.share
    Now = slices |> Observable.map _.Now |> PageChrome.share
    PollChoices = slices |> Observable.map _.PollChoices |> PageChrome.share
    Dense = false
    Images = host.Images
    Flyout = host.Flyout
    Pin = pin
    Star = slot host.OnStar
    Vote = slot host.OnVote
    Pastel = slot host.OnPastel
    Translation = host.Translation
    Zone = host.Zone
  }
