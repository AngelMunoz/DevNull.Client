module DevNull.Client.Workbench.RowActions

open System
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.CommitMenu
open DevNull.Client.Components.UserFlyout
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore

/// The dialog one shared menu row asks the host to open.
type Dialog =
  | Share of Commit
  | Report of Commit

/// What one workbench surface gives the shared commit actions: the
/// protocol workers, the signed session, the clock ticks, whether the
/// surface offers star and pastel, the update and delete answers, and
/// the host's dialog request.
type SurfaceProps<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Now: IObservable<DateTimeOffset>
  OfferBasicActions: bool
  OnUpdated: Commit -> unit
  OnDeleted: string -> unit
  OnDialog: Dialog -> unit
  Flyout: Source voption
}

/// The slots and event handling one shared action set offers a row:
/// the star, vote, pastel, menu, and translation slots, the menu busy
/// value, and the one event handler the surface chains.
type SurfaceView = {
  Star: ActionSlot voption
  Vote: ActionSlot voption
  Pastel: ActionSlot voption
  Menu: MenuSlot voption
  Translation: IObservable<Commit -> TranslationView voption>
  Busy: IObservable<string option>
  Flyout: Source voption
  Handle: Event -> unit
}

/// Builds the shared action set and its relation, poll, and
/// translation state.
val create: props: SurfaceProps<'env> -> SurfaceView
