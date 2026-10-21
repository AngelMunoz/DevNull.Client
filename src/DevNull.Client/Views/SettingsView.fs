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
  Context: RouteContext
}

/// The page's state logic: model, messages, update, and the settings
/// subscription that feeds it from outside.
module State =

  type Model = {
    Me: Me option
    Error: AppError option
    Note: string option
    Busy: bool
    /// The local machine preferences, live from the settings store.
    Settings: AppSettings
  }

  type Msg =
    | LaneSet of index: int
    | LaneAutomatic
    | PasteisModeToggled
    | GithubSyncRequested
    | MeDone of Result<Me, AppError>
    | MeNote of string
    | CloseToTraySet of enabled: bool
    | ThemeChosen of choice: ThemeChoice
    | SettingsChanged of settings: AppSettings

  let initial = {
    Me = None
    Error = None
    Note = None
    Busy = true
    Settings = { CloseToTray = false; Theme = System }
  }

  let fetchMe(env, session: SignedInSession) =
    Me.get(env, { KeyFile = session.KeyFile })

  let init (props: Props<'env>) () =
    {
      initial with
          Settings = (props.Env :> IProvideSettings).Settings.Current
    },
    Cmd.OfAsync.perform fetchMe (props.Env, props.Session) MeDone

  let laneIndex(lane: Lane option) : int option =
    lane
    |> Option.bind(function
      | LaneIndex index -> Some index
      | LaneColor _ -> None)

  let update (props: Props<'env>) msg model =
    match msg with
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
    | CloseToTraySet enabled ->
      let settings = {
        model.Settings with
            CloseToTray = enabled
      }

      let cmd =
        if enabled = model.Settings.CloseToTray then
          Cmd.none
        else
          let store = (props.Env :> IProvideSettings).Settings
          Cmd.ofAction(fun () -> store.SetCloseToTray enabled)

      { model with Settings = settings }, cmd
    | ThemeChosen choice ->
      let store = (props.Env :> IProvideSettings).Settings

      {
        model with
            Settings = { model.Settings with Theme = choice }
      },
      Cmd.ofAction(fun () -> store.SetTheme choice)
    | SettingsChanged settings -> { model with Settings = settings }, Cmd.none

  /// The page's inlet from outside: the local settings store.
  let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
    Sub.fromObservable
      "settings"
      ((props.Env :> IProvideSettings).Settings.Observe
       |> Observable.map SettingsChanged)

/// The page's semantic regions; the view only composes these.
module Parts =

  /// The error card: one plain line per failure, in the danger tone.
  let errorPanel(model: IObservable<State.Model>) : Control =
    let errorText =
      TextBlock()
        .TextWrapping(TextWrapping.Wrap)
        .Foreground(Brushes.danger)
        .Text(
          model
          |> Observable.map(fun current ->
            current.Error |> Option.map describe |> Option.defaultValue "")
        )

    Border()
      .Classes(Theme.name Theme.Card)
      .Margin(0.0, 8.0, 0.0, 8.0)
      .Child(errorText)
      .IsVisible(model |> Observable.map(_.Error.IsSome))

  /// The note line, when an action left a message.
  let noteText(model: IObservable<State.Model>) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Text(
        model
        |> Observable.map(fun current -> current.Note |> Option.defaultValue "")
      )
      .IsVisible(model |> Observable.map _.Note.IsSome)

  /// The busy line of the page.
  let busy(model: IObservable<State.Model>) : Control =
    Status
      .busy("Loading the settings…")
      .IsVisible(model |> Observable.map _.Busy)

  /// The lane palette row.
  let laneRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    // The palette is rebuilt when the lane changes: the ring follows.
    let laneHost =
      let content: IObservable<Control> =
        model
        |> Observable.map(fun current ->
          match current.Me with
          | Some me ->
            let current' = State.laneIndex me.PreferredLane

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
                .OnClickHandler(fun _ _ -> dispatch(State.LaneSet index))

            let automatic =
              Button()
                .Classes(Theme.name Theme.NavButton)
                .Content("Automatic")
                .OnClickHandler(fun _ _ -> dispatch State.LaneAutomatic)

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

  /// The appearance row. It follows the model: a choice dispatches, the
  /// update writes the store, and the store's emission returns through
  /// the settings subscription.
  let themeRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let themeHost = ContentControl()

    let choices =
      model
      |> Observable.map(fun current ->
        let choiceButton(label: string, choice: ThemeChoice) : Control =
          let button =
            Button()
              .Classes(Theme.name Theme.NavButton)
              .Content(label)
              .OnClickHandler(fun _ _ -> dispatch(State.ThemeChosen choice))

          if current.Settings.Theme = choice then
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

    themeHost.Content(choices.ToBinding()) |> ignore

    StackPanel()
      .Spacing(6.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Subtitle).Text("Appearance"),
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .Text("The light and dark palettes of the site, or the system's."),
        themeHost
      )

  /// The pastéis mode row: one button for each state.
  let pasteisRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let pasteisOn =
      model
      |> Observable.map(fun current ->
        current.Me |> Option.map(_.PasteisMode) |> Option.defaultValue false)

    let off =
      Button()
        .Classes(Theme.name Theme.NavButton)
        .Content("Turn pastéis mode on")
        .IsVisible(pasteisOn |> Observable.map not)
        .OnClickHandler(fun _ _ -> dispatch State.PasteisModeToggled)

    let on =
      Button()
        .Classes(Theme.name Theme.NavButton)
        .Content("Turn pastéis mode off")
        .IsVisible(pasteisOn)
        .OnClickHandler(fun _ _ -> dispatch State.PasteisModeToggled)

    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(8.0)
      .Children(off, on)

  /// The GitHub sync button.
  let syncButton(dispatch: Dispatch<State.Msg>) : Control =
    Button()
      .Classes(Theme.name Theme.NavButton)
      .Content("Sync the GitHub profile")
      .OnClickHandler(fun _ _ -> dispatch State.GithubSyncRequested)

  /// The application preferences row. It rides the MVU loop: the
  /// toggle dispatches, the update writes the store, and the store's
  /// emission comes back through the settings subscription.
  let applicationRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let settings = model |> Observable.map _.Settings

    let toggle =
      ToggleSwitch()
        .IsChecked(
          settings
          |> Observable.map(fun current -> Nullable(current.CloseToTray))
        )
        .OnIsCheckedChangedHandler(fun toggle _ ->
          toggle.IsChecked
          |> Option.ofNullable
          |> Option.iter(State.CloseToTraySet >> dispatch))

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

  /// The sign-out button; the caller owns the actual sign-out.
  let signOutButton(props: Props<'env>) : Control =
    Button()
      .Classes(Theme.name Theme.DangerButton)
      .Content("Sign out")
      .OnClickHandler(fun _ _ -> props.OnSignOut())

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =
  let user = props.Session.Session.User

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
          Parts.laneRow(model, dispatch),
          Parts.themeRow(model, dispatch),
          Parts.pasteisRow(model, dispatch),
          Parts.syncButton dispatch,
          Parts.applicationRow(model, dispatch),
          Parts.signOutButton props,
          Parts.errorPanel model,
          Parts.noteText model,
          Parts.busy model
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
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root
    props.Context.AddDisposable lifetime
