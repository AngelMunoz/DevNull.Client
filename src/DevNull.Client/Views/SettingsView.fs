module DevNull.Client.Views.SettingsView

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Layout
open NXUI.Extensions
open IcedTasks
open MVUish.Avalonia
open Navs
open DevNull.Client
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Navigation
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.Theme

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideSettings> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
  /// Runs when the user asks to sign out; the caller drops the session.
  OnSignOut: unit -> unit
}

type Model = {
  Me: Me option
  Error: AppError option
  Note: string option
  Busy: bool
}

type Msg =
  | MeFetched of Result<Me, AppError>
  | LaneSet of index: int
  | LaneAutomatic
  | PasteisModeToggled
  | GithubSyncRequested
  | MeDone of Result<Me, AppError>
  | MeNote of string

let initial = {
  Me = None
  Error = None
  Note = None
  Busy = true
}

let fetchMe(env, session: SignedInSession) =
  Me.get(env, { KeyFile = session.KeyFile })

let init (props: Props<'env>) () =
  initial, Cmd.OfAsync.perform fetchMe (props.Env, props.Session) MeFetched

let laneIndex(lane: Lane option) : int option =
  lane
  |> Option.bind(function
    | LaneIndex index -> Some index
    | LaneColor _ -> None)

let rec update (props: Props<'env>) msg model =
  match msg with
  | MeFetched result -> update props (MeDone result) model
  | MeDone result ->
    match result with
    | Ok me ->
      {
        model with
            Busy = false
            Error = None
            Me = Some me
      },
      Cmd.none
    | Error error ->
      {
        model with
            Busy = false
            Error = Some error
      },
      Cmd.none
  | MeNote note -> { model with Note = Some note }, Cmd.none
  | LaneSet index ->
    let set() =
      Me.setLane(
        props.Env,
        {
          KeyFile = props.Session.KeyFile
          Lane = Palette index
        }
      )

    { model with Error = None }, Cmd.OfAsync.perform set () MeDone
  | LaneAutomatic ->
    let set() =
      Me.setLane(
        props.Env,
        {
          KeyFile = props.Session.KeyFile
          Lane = Automatic
        }
      )

    { model with Error = None }, Cmd.OfAsync.perform set () MeDone
  | PasteisModeToggled ->
    let toggle() =
      let on =
        model.Me |> Option.map(_.PasteisMode) |> Option.defaultValue false

      Me.setPasteisMode(
        props.Env,
        {
          KeyFile = props.Session.KeyFile
          Enabled = not on
        }
      )

    { model with Error = None }, Cmd.OfAsync.perform toggle () MeDone
  | GithubSyncRequested ->
    let sync() =
      Me.syncGithub(props.Env, { KeyFile = props.Session.KeyFile })

    { model with Error = None }, Cmd.OfAsync.perform sync () MeDone

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let user = props.Session.Session.User

  let errorText =
    TextBlock()
      .TextWrapping(TextWrapping.Wrap)
      .Foreground(Brushes.danger)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Error |> Option.map describe |> Option.defaultValue "")
      )

  let errorPanel =
    Border()
      .Classes(Theme.name Theme.Card)
      .Margin(0.0, 8.0, 0.0, 8.0)
      .Child(errorText)
      .IsVisible(model |> Observable.map(_.Error.IsSome))

  let noteText =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Text(
        model
        |> Observable.map(fun current -> current.Note |> Option.defaultValue "")
      )
      .IsVisible(model |> Observable.map _.Note.IsSome)

  let busy =
    Status
      .busy("Loading the settings…")
      .IsVisible(model |> Observable.map _.Busy)

  let laneRow =
    // The palette is rebuilt when the lane changes: the ring follows.
    let laneHost =
      let content: IObservable<Control> =
        model
        |> Observable.map(fun current ->
          match current.Me with
          | Some me ->
            let current' = laneIndex me.PreferredLane

            let laneButton(index: int) : Control =
              Button()
                .Width(34.0)
                .Height(34.0)
                .CornerRadius(17.0)
                .Background(Brushes.lane index)
                .BorderBrush(
                  if current' = Some index then
                    Brushes.text
                  else
                    Brushes.border
                )
                .BorderThickness(2.0)
                .OnClickHandler(fun _ _ -> dispatch(LaneSet index))

            let automatic =
              Button()
                .Classes(Theme.name Theme.NavButton)
                .Content("Automatic")
                .OnClickHandler(fun _ _ -> dispatch LaneAutomatic)

            if me.PreferredLane = None then
              automatic.Background(Brushes.card) |> ignore

            let row: Control array = [|
              automatic
              for index in 0..5 do
                laneButton index
            |]

            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(6.0)
              .Children(row)
            :> Control
          | None -> Status.empty "")

      ContentControl().Content(content.ToBinding())

    StackPanel()
      .Spacing(6.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Subtitle).Text("Lane"),
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .Text(
            "The color on your commits. The server picks one when automatic."
          ),
        laneHost
      )

  // The appearance row rebuilds from the store, the way the lane row
  // follows the server settings.
  let themeRow =
    let store = (props.Env :> IProvideSettings).Settings
    let themeHost = ContentControl()

    let model =
      store.Observe
      |> Observable.map(fun current ->
        let choiceButton(label: string, choice: ThemeChoice) : Control =
          let button =
            Button()
              .Classes(Theme.name Theme.NavButton)
              .Content(label)
              .OnClickHandler(fun _ _ -> store.SetTheme choice)

          if current.Theme = choice then
            button.BorderBrush(Brushes.accent) |> ignore

          button

        let row: Control array = [|
          choiceButton("System", System)
          choiceButton("Light", Light)
          choiceButton("Dark", Dark)
        |]

        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(row))

    themeHost.Content(model.ToBinding()) |> ignore

    StackPanel()
      .Spacing(6.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Subtitle).Text("Appearance"),
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .Text("The light and dark palettes of the site, or the system's."),
        themeHost
      )

  let pasteisOn =
    model
    |> Observable.map(fun current ->
      current.Me |> Option.map(_.PasteisMode) |> Option.defaultValue false)

  let pasteisOffButton =
    Button()
      .Classes(Theme.name Theme.NavButton)
      .Content("Turn pastéis mode on")
      .IsVisible(pasteisOn |> Observable.map not)
      .OnClickHandler(fun _ _ -> dispatch PasteisModeToggled)

  let pasteisOnButton =
    Button()
      .Classes(Theme.name Theme.NavButton)
      .Content("Turn pastéis mode off")
      .IsVisible(pasteisOn)
      .OnClickHandler(fun _ _ -> dispatch PasteisModeToggled)

  let syncButton =
    Button()
      .Classes(Theme.name Theme.NavButton)
      .Content("Sync the GitHub profile")
      .OnClickHandler(fun _ _ -> dispatch GithubSyncRequested)

  // The application preferences live in the local store, off the MVUish
  // loop: like the sign-out, they are machine state, not server state.
  let applicationRow =
    let store = (props.Env :> IProvideSettings).Settings

    let toggle =
      ToggleSwitch()
        .IsChecked(store.Current.CloseToTray)
        .OnIsCheckedChangedHandler(fun toggle _ ->
          toggle.IsChecked
          |> Option.ofNullable
          |> Option.iter store.SetCloseToTray)

    toggle.Name <- "CloseToTrayToggle"

    StackPanel()
      .Spacing(6.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Subtitle).Text("Application"),
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(10.0)
          .Children(
            toggle,
            TextBlock()
              .VerticalAlignment(VerticalAlignment.Center)
              .Text("Keep running in the tray when the window closes")
          )
      )

  let signOutButton =
    Button()
      .Classes(Theme.name Theme.DangerButton)
      .Content("Sign out")
      .OnClickHandler(fun _ _ -> props.OnSignOut())

  let pasteisRow =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(8.0)
      .Children(pasteisOffButton, pasteisOnButton)

  ScrollViewer()
    .Padding(Theme.pagePadding)
    .Content(
      StackPanel()
        .MaxWidth(560.0)
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .Spacing(14.0)
        .Children(
          TextBlock().Classes(Theme.name Theme.Title).Text($"@{user.Username}"),
          TextBlock().Text(user.Name),
          laneRow,
          themeRow,
          pasteisRow,
          syncButton,
          applicationRow,
          signOutButton,
          errorPanel,
          noteText,
          busy
        )
    )

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideSettings>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram (init props) (update props) (view props)

    this.Content <- Program.runWith () program
