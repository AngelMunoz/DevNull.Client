module DevNull.Client.Editor.RemoteList

open System
open Avalonia.Controls
open Avalonia.Threading
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideRemotes
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideDispatcher
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideBrowser
  and 'env :> IProvideRecentSearches
  and 'env :> IProvideLogging
  and 'env :> IProvideSettings
  and 'env :> IProvideNatives> = {
  Env: 'env
  Session: SignedInSession
  Zone: Zone
  OnOpen: ChatTarget -> unit
  OnOpenProfile: string -> unit
}

module State =

  let inline pageOf(props: Props<'env>) : DevNull.Client.Views.Props<'env> = {
    Env = props.Env
    Session = props.Session
    OnOpenProfile = props.OnOpenProfile
  }

  let init
    (props: Props<'env>)
    : DevNull.Client.Views.State.Model * Cmd<DevNull.Client.Views.State.Msg> =
    DevNull.Client.Views.RemotesModel.init (pageOf props) ()

  let update
    (
      props: Props<'env>,
      msg: DevNull.Client.Views.State.Msg,
      model: DevNull.Client.Views.State.Model
    ) : DevNull.Client.Views.State.Model * Cmd<DevNull.Client.Views.State.Msg> =
    match msg with
    | DevNull.Client.Views.State.Opened target ->
      model, Cmd.ofAction(fun () -> props.OnOpen target)
    | _ -> DevNull.Client.Views.RemotesModel.update (pageOf props) msg model

  let subscribe
    (props: Props<'env>, model: DevNull.Client.Views.State.Model)
    : Sub<DevNull.Client.Views.State.Msg> =
    DevNull.Client.Views.RemotesModel.subscribe (pageOf props) model

let view
  (
    props: Props<'env>,
    model: IObservable<DevNull.Client.Views.State.Model>,
    dispatch: Dispatch<DevNull.Client.Views.State.Msg>
  ) : Control =
  DevNull.Client.Views.Parts.inbox(State.pageOf props, model, dispatch)

let program
  (props: Props<'env>)
  : Program<
      unit,
      DevNull.Client.Views.State.Model,
      DevNull.Client.Views.State.Msg,
      Control
     >
  =
  Program.mkProgram
    (fun () -> State.init props)
    (fun msg model -> State.update(props, msg, model))
    (fun model dispatch -> view(props, model, dispatch))
  |> Program.withSubscription(fun model -> State.subscribe(props, model))

let mount(dispatcher: IDispatcher, props: Props<'env>) =
  DevNull.Client.Programs.runPage(dispatcher, program props)
