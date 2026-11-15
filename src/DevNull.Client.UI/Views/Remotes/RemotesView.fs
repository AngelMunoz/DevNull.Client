module DevNull.Client.Views.RemotesView

open System.Reactive.Disposables
open System.Reactive.Subjects
open Avalonia.Controls
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Components
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.UiClock
open Navs

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideNatives
  and 'env :> IProvideRemotes
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging>(props: Props<'env>, context: RouteContext) as this
  =
  inherit UserControl()

  do
    let opened =
      new BehaviorSubject<bool>(props.Env.Remotes.Current.OpenTarget.IsSome)

    let mount =
      RemotesMount.mount(
        props.Env.Dispatcher,
        {
          Env = props.Env
          Session = props.Session
          OnOpenProfile = props.OnOpenProfile
          OnTargetChanged = fun target -> opened.OnNext target.IsSome
        }
      )

    this.Content <-
      SplitChrome.view {
        Side = mount.Inbox
        Main = mount.Thread
        MainOpen = opened
        OnBack = fun () -> mount.Send State.Closed
      }

    context.AddDisposable(
      Disposable.Create(fun () ->
        mount.Lifetime.Dispose()
        opened.OnCompleted()
        opened.Dispose())
    )
