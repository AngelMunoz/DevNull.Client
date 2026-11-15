namespace DevNull.Client.Views

open System
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.UiClock

module RemotesMount =

  type Props<'env
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
    and 'env :> IProvideLogging> = {
    Env: 'env
    Session: SignedInSession
    OnOpenProfile: string -> unit
    OnTargetChanged: ChatTarget option -> unit
  }

  type Mount = {
    Inbox: Control
    Thread: Control
    Lifetime: IDisposable
    Send: State.Msg -> unit
  }

  // One mount's own inbox sibling: a plain record while the mount owns
  // its program handle and send path.
  type Cell = {
    mutable Inbox: Control option
    mutable Thread: Control option
    mutable Send: (State.Msg -> unit) option
  }

  // The mount's page props from the mount props: the router-free shape
  // the shared page props now carry.
  let inline pageOf(props: Props<'env>) : DevNull.Client.Views.Props<'env> = {
    Env = props.Env
    Session = props.Session
    OnOpenProfile = props.OnOpenProfile
  }

  // The mount's own subscription: the store's open target, keyed so a
  // refresh that rebuilds the target's payload never fires the host.
  let subscribeTarget(props: Props<'env>) : IDisposable =
    props.Env.Remotes.Observe
    |> Observable.map _.OpenTarget
    |> Observable.distinctUntilChangedKey(
      Option.map ChatTargets.key
      >> function
        | None -> null
        | Some value -> value
    )
    |> Observable.subscribe props.OnTargetChanged

  let mount(dispatcher: IDispatcher, props: Props<'env>) : Mount =
    let page = pageOf props

    let cell: Cell = {
      Inbox = None
      Thread = None
      Send = None
    }

    let capture
      (model: IObservable<State.Model>)
      (dispatch: Dispatch<State.Msg>)
      : Control =
      cell.Inbox <- Some(Parts.inbox(page, model, dispatch))
      cell.Thread <- Some(ThreadPane.column(page, model, dispatch))
      cell.Send <- Some dispatch
      Panel()

    let program =
      Program.mkProgram
        (RemotesModel.init page)
        (RemotesModel.update page)
        capture
      |> Program.withSubscription(fun model ->
        Sub.batch [
          RemotesModel.subscribe page model
          Sub.ofId "mount:target" (fun _ -> subscribeTarget props)
        ])

    let _, lifetime = Programs.runPage(dispatcher, program)

    match cell.Inbox, cell.Thread, cell.Send with
    | Some inbox, Some thread, Some send -> {
        Inbox = inbox
        Thread = thread
        Lifetime = lifetime
        Send = send
      }
    | _ -> failwith "the remotes mount never built its inbox and thread"

  // The panel a host shows while the account has remotes off.
  let disabledPanel
    (onEnable: unit -> unit, onUseWebsite: unit -> unit)
    : Control =
    StackPanel()
      .Spacing(8.0)
      .Margin(16.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .Text("direct messages are off")
          .HorizontalAlignment(HorizontalAlignment.Center)
          .TextWrapping(TextWrapping.Wrap),
        Button()
          .Classes(Theme.name Theme.AccentButton)
          .Name("RemotesEnable")
          .Content("Turn on remotes")
          .HorizontalAlignment(HorizontalAlignment.Center)
          .OnClickHandler(fun _ _ -> onEnable()),
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Name("RemotesUseWebsite")
          .Content("Open the Website layout")
          .HorizontalAlignment(HorizontalAlignment.Center)
          .OnClickHandler(fun _ _ -> onUseWebsite())
      )
