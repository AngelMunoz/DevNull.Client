module DevNull.Client.Views.CommitRows

open System
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache
open DevNull.Client.Formatting
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.UserFlyout

/// The model values one page's cards follow in a single emission:
/// whether pastéis show, the clock, the poll choices the page holds,
/// the commit id whose star, vote, or pastel call runs, and the commit
/// id whose pin call runs.
[<Struct>]
type RowSlices = {
  Pasteis: bool
  Now: DateTimeOffset
  PollChoices: Map<string, string>
  Voting: string option
  Pinning: string option
}

/// What one page adds to the shared row builder: where its card events
/// go, the signed-in viewer, the page's image cache, hover card source,
/// and zone, the pin command where the page offers one — the card's own
/// gate decides whether a row arms it — the More actions slot the page
/// builds, whether the vote and pastel actions show, the three host-run
/// action commands, and the translation lookup the page builds from its
/// own book.
type Host = {
  OnEvent: Event -> unit
  ViewerId: string
  Images: ImageCacheService
  Flyout: Source voption
  Zone: Zone
  OnPin: (Commit -> unit) voption
  /// The More actions slot: copy link, share, report, delete, follow,
  /// mute, block. An absent slot draws no trigger at all.
  Menu: MenuSlot voption
  /// Whether the row offers the page's vote and pastel actions. A
  /// search row keeps its star and hides both.
  Actions: bool
  OnStar: Commit -> unit
  /// The stargazers list the author's own star count opens; an
  /// absent command leaves the count read-only.
  OnStars: (Commit -> unit) voption
  OnVote: Commit -> unit
  OnPastel: Commit -> unit
  Translation: IObservable<Commit -> TranslationView voption>
}

/// Builds one page's row context once. The star, vote, and pastel
/// action slots share one busy slice, and the pastéis, clock, poll
/// choices, and pin busy slices are each mapped once from the page's
/// model and multicasted, so every realized card follows one upstream
/// subscription.
val row:
  host: Host * model: IObservable<'m> * slicesOf: ('m -> RowSlices) -> RowProps
