module DevNull.Client.Views.GuildView

open Avalonia.Controls
open Navs
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore

/// One guild's board, the site's own guild page: the identity header
/// (icon, name, the `#handle · Public guild · members` line, the
/// description, the posting hint), the composer aimed at the guild
/// while it is not paused, and the bulletin board beneath — the pinned
/// commit leading with its marker, the root commits newest first in a
/// virtualized feed, and the load-more footer while the board holds
/// another numbered page. The 404 of a flag-off server or an unknown
/// handle stands as one line; any other failure offers its retry. The
/// cards' guild chips open boards in the app.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideUserSettings
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Handle: string
  Nav: INavigable
}

/// The guild board page. Replace-only: a host builds one and never
/// re-shows it, and the first detach ends the program.
type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideUserSettings
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging> =
  new: props: Props<'env> -> Page<'env>
  inherit UserControl
