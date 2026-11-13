module DevNull.Client.Editor.Palette

open System
open System.Reactive.Disposables
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Threading
open NXUI.Extensions
open Navs
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.Stores
open DevNull.Client.UiClock

type Command =
  | SwitchRef of LogKind
  | OpenNotifications
  | OpenPanel of EditorPanel.Tab
  | UseDefaultLayout

let commands = [
  SwitchRef LogKind.Latest
  SwitchRef LogKind.Following
  SwitchRef LogKind.Trending
  OpenNotifications
  OpenPanel EditorPanel.Activity
  OpenPanel EditorPanel.Terminal
  UseDefaultLayout
]

let label(command: Command) =
  match command with
  | SwitchRef log -> $"switch ref: {LogLabel.ofKind log}"
  | OpenNotifications -> "open panel: NOTIFICATIONS"
  | OpenPanel tab -> $"open panel: {EditorPanel.tabLabel tab}"
  | UseDefaultLayout -> "use default layout"

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideLogging
  and 'env :> IProvideRecentSearches> = {
  Env: 'env
  Session: SignedInSession
  OnOpenRepo: string -> unit
  OnOpenCommit: string -> unit
  OnOpenOrg: string -> unit
  OnOpenSearch: string -> unit
  OnSelectRef: LogKind -> unit
  OnOpenNotifications: unit -> unit
  OnOpenPanel: EditorPanel.Tab -> unit
  OnUseDefaultLayout: unit -> unit
  OnOpenCompose: unit -> unit
  OnClose: unit -> unit
}

// The dialog ignores the navigation result; the adapter only runs the
// action the URL names.
let success: Result<NavigationResult, NavigationError> =
  Ok Unchecked.defaultof<NavigationResult>

let searchNav(props: Props<'env>) : INavigable = {
  new INavigable with
    member _.Navigate(url: string) =
      let path = url.Split('?').[0]

      let parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries)

      match parts with
      | [| "commit"; id |] ->
        props.OnOpenCommit(Uri.UnescapeDataString id)
        fun _ -> Task.FromResult success
      | [| "profile"; handle |] ->
        props.OnOpenRepo(Uri.UnescapeDataString handle)
        fun _ -> Task.FromResult success
      | [| "org"; handle |] ->
        props.OnOpenOrg(Uri.UnescapeDataString handle)
        fun _ -> Task.FromResult success
      | [| "search"; term |] ->
        props.OnOpenSearch(Uri.UnescapeDataString term)
        fun _ -> Task.FromResult success
      | _ -> fun _ -> Task.FromResult(Error(RouteNotFound url))

    member _.NavigateByName(_: string, _: RouteParams) =
      fun _ -> Task.FromResult(Error(RouteNotFound "named route"))

    member _.Back() =
      fun _ -> Task.FromResult(Error(RouteNotFound "back"))

    member _.Forward() =
      fun _ -> Task.FromResult(Error(RouteNotFound "forward"))
}

let runCommand(props: Props<'env>, command: Command) =
  match command with
  | SwitchRef log -> props.OnSelectRef log
  | OpenNotifications -> props.OnOpenNotifications()
  | OpenPanel tab -> props.OnOpenPanel tab
  | UseDefaultLayout -> props.OnUseDefaultLayout()

  props.OnClose()

let commandButton(index: int, props: Props<'env>, command: Command) =
  Button()
    .Name($"PaletteCommand-{index}")
    .Classes(Theme.name Theme.GhostButton)
    .Padding(Thickness(8.0, 4.0))
    .Content(TextBlock().Text(label command).Classes(Theme.name Theme.Mono))
    .OnClickHandler(fun _ _ -> runCommand(props, command))

let commandBar(props: Props<'env>) =
  let rows: Control list =
    commands
    |> List.mapi(fun index command -> commandButton(index, props, command))

  Border()
    .Name("PaletteCommands")
    .Margin(Thickness(24.0, 0.0, 24.0, 12.0))
    .Child(
      WrapPanel()
        .Orientation(Orientation.Horizontal)
        .Children(Array.ofList rows)
    )

let mount(dispatcher: IDispatcher, props: Props<'env>) : Control * IDisposable =
  let log =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Layouts.Editor.Palette"

  let searches =
    new DevNull.Client.Search.Session(
      (fun term ->
        Search.profiles(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            Term = term
          }
        )),
      (fun term ->
        Search.commits(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            Term = term
            Limit = Some 5
          }
        )),
      log
    )

  let dialog =
    SearchDialog.Page {
      Env = props.Env
      SignedIn = props.Session
      Searches = searches
      Recents = (props.Env :> IProvideRecentSearches).RecentSearches
      Nav = searchNav props
      OpenCompose = props.OnOpenCompose
      Close = props.OnClose
    }

  let shell = Grid()
  shell.RowDefinitions <- RowDefinitions("*,Auto")

  shell.Children(dialog.Row(0), commandBar(props).Row(1)) |> ignore

  shell, Disposable.Empty
