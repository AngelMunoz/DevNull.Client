module DevNull.Client.Views.SettingsView

open System
open System.Globalization
open Avalonia
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open Navs
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.TranslationModel
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Env
open Microsoft.Extensions.Logging
open DevNull.Client.UserSettingsStore
open DevNull.Client.SettingsStore
open DevNull.Client.Stores

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideSettings
  and 'env :> IProvideUserSettings
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  OnSignOut: unit -> unit
  Context: RouteContext
}

// The page's state logic: model, messages, update, and the settings
// subscription that feeds it from outside.
module State =

  type Model = {
    Me: Me option
    Devices: Device list option
    Error: AppError option
    Busy: bool
    // The local machine preferences, live from the settings store.
    Settings: AppSettings
    // The server settings: the timezone, the language commits are
    // translated into, automatic translation, and the languages.
    Prefs: UserSettings option
    // The zone the picker holds, or none to follow the device.
    ZonePick: string option
    // Whether the profile shows the local time.
    ZoneShowLocal: bool
    // The time zone picker's search text.
    ZoneQuery: string
    // The language the picker holds, or none to follow the device.
    LanguagePick: TranslationLanguage option
    // The language picker's search text.
    LanguageQuery: string
    // One settings write in flight.
    Saving: bool
  }

  type Msg =
    | LaneSet of index: int
    | LaneAutomatic
    | PasteisModeToggled
    | GithubSyncRequested
    | MeDone of Result<Me, AppError>
    | DevicesDone of Result<Device list, AppError>
    | RevokeRequested of Device
    | RevokeDone of Result<unit, AppError> * Device
    | CloseToTraySet of enabled: bool
    | ThemeChosen of choice: ThemeChoice
    | SettingsChanged of settings: AppSettings
    | PrefsArrived of settings: UserSettings option
    | ZonePicked of id: string option
    | ZoneQueryChanged of text: string
    | ShowLocalToggled of show: bool
    | LanguagePicked of code: string option
    | LanguageQueryChanged of text: string
    | SaveTimezone
    | TimezoneSaved of Result<Me, AppError> * TimezoneSetting
    | SaveLanguage
    | LanguageSaved of Result<Me, AppError> * TranslationLanguage option
    | AutomaticToggled of enabled: bool
    | AutomaticSaved of Result<Me, AppError> * enabled: bool

  let initial = {
    Me = None
    Devices = None
    Error = None
    Busy = true
    Settings = {
      CloseToTray = false
      Theme = System
      SidebarExpanded = true
      ChatDrafts = Map.empty
      VoiceVolume = 1.0
      PinPromptHidden = false
    }
    Prefs = None
    ZonePick = None
    ZoneShowLocal = false
    ZoneQuery = ""
    LanguagePick = None
    LanguageQuery = ""
    Saving = false
  }

  let fetchMe(env, session: SignedInSession) =
    Me.get(env, { KeyFile = session.KeyFile })

  let fetchDevices(env, session: SignedInSession) =
    Devices.list(env, { KeyFile = session.KeyFile })

  // The zone a saved setting holds, or none to follow the device.
  let zoneOf(prefs: UserSettings) : string option =
    match prefs.Timezone with
    | ZoneOf(id, _) -> Some id
    | FollowDevice -> None

  // Whether a saved setting shows the local time.
  let showLocalOf(prefs: UserSettings) : bool =
    match prefs.Timezone with
    | ZoneOf(_, show) -> show
    | FollowDevice -> false

  // Keeps the settings the server just saved, so every page follows.
  let applyPrefs(props: Props<'env>, prefs: UserSettings option) : Cmd<Msg> =
    prefs
    |> Option.map(fun prefs ->
      Cmd.ofAction(fun () ->
        (props.Env :> IProvideUserSettings).UserSettings.Apply prefs))
    |> Option.defaultValue Cmd.none

  let init (props: Props<'env>) () =
    let store = (props.Env :> IProvideUserSettings).UserSettings
    let prefs = store.Current

    {
      initial with
          Settings = (props.Env :> IProvideSettings).Settings.Current
          Prefs = prefs
          ZonePick = prefs |> Option.bind zoneOf
          ZoneShowLocal =
            prefs |> Option.map showLocalOf |> Option.defaultValue false
          LanguagePick =
            prefs |> Option.bind(fun prefs -> prefs.TranslationLanguage)
    },
    Cmd.batch [
      Cmd.OfAsync.perform fetchMe (props.Env, props.Session) MeDone
      Cmd.OfAsync.perform fetchDevices (props.Env, props.Session) DevicesDone
      Cmd.ofAction(fun () -> store.Refresh())
    ]

  let laneIndex(lane: Lane option) : int option =
    lane
    |> Option.bind(function
      | LaneIndex index -> Some index
      | LaneColor _ -> None)

  let loggerOf(props: Props<'env>) =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Settings"

  let updateCore (log: ILogger, props: Props<'env>) msg model =
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
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The account of {Handle} arrived.",
            me.Profile.User.Handle
          ))
      | Error error ->
        {
          model with
              Busy = false
              Error = Some error
        },
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The account request failed: {Error}",
            bounded 300 (describe error)
          ))
    | DevicesDone result ->
      match result with
      | Ok devices -> { model with Devices = Some devices }, Cmd.none
      | Error error ->
        { model with Error = Some error },
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The devices request failed: {Error}",
            bounded 300 (describe error)
          ))
    | RevokeRequested device ->
      let revoke() =
        Devices.revoke(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            DeviceId = device.Id
          }
        )

      { model with Error = None },
      Cmd.OfAsync.perform revoke () (fun result -> RevokeDone(result, device))
    | RevokeDone(result, device) ->
      // Revoking the device that signed the request signs the app out
      // on it — the server's own rule — so the page hands the
      // sign-out to its caller; any other revoke just refreshes.
      let own = props.Session.Session.DeviceFingerprint

      let isSelf = own <> "" && own = device.Fingerprint

      match result with
      | Ok() when isSelf ->
        model,
        Cmd.ofAction(fun () ->
          log.LogInformation(
            "The signing device was revoked; the app signs out."
          )

          props.OnSignOut())
      | Ok() ->
        model,
        Cmd.OfAsync.perform fetchDevices (props.Env, props.Session) DevicesDone
      | Error error ->
        { model with Error = Some error },
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The revoke failed: {Error}",
            bounded 300 (describe error)
          ))
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
    | PrefsArrived prefs ->
      match prefs with
      | None -> { model with Prefs = None }, Cmd.none
      | Some prefs ->
        {
          model with
              Prefs = Some prefs
              ZonePick = zoneOf prefs
              ZoneShowLocal = showLocalOf prefs
              LanguagePick = prefs.TranslationLanguage
        },
        Cmd.none
    | ZonePicked id ->
      {
        model with
            ZonePick = id
            ZoneShowLocal = if id.IsNone then false else model.ZoneShowLocal
      },
      Cmd.none
    | ZoneQueryChanged text -> { model with ZoneQuery = text }, Cmd.none
    | ShowLocalToggled show -> { model with ZoneShowLocal = show }, Cmd.none
    | LanguagePicked code ->
      let language =
        code
        |> Option.bind(fun code ->
          model.Prefs
          |> Option.bind(fun prefs ->
            prefs.Languages
            |> List.tryFind(fun language -> language.Code = code)))

      { model with LanguagePick = language }, Cmd.none
    | LanguageQueryChanged text -> { model with LanguageQuery = text }, Cmd.none
    | SaveTimezone ->
      let timezone =
        match model.ZonePick with
        | Some id -> ZoneOf(id, model.ZoneShowLocal)
        | None -> FollowDevice

      let save() =
        Me.setTimezone(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            Timezone = timezone
          }
        )

      {
        model with
            Saving = true
            Error = None
      },
      Cmd.OfAsync.perform save () (fun result ->
        TimezoneSaved(result, timezone))
    | TimezoneSaved(result, timezone) ->
      match result with
      | Ok _ ->
        let prefs =
          model.Prefs
          |> Option.map(fun prefs -> { prefs with Timezone = timezone })

        {
          model with
              Prefs = prefs
              Saving = false
        },
        Cmd.batch [ applyPrefs(props, prefs); Cmd.ofMsg(MeDone result) ]
      | Error error ->
        {
          model with
              Saving = false
              Error = Some error
        },
        Cmd.none
    | SaveLanguage ->
      let save() =
        Me.setTranslationLanguage(
          props.Env,
          {
            KeyFile = props.Session.KeyFile
            Language = model.LanguagePick
          }
        )

      {
        model with
            Saving = true
            Error = None
      },
      Cmd.OfAsync.perform save () (fun result ->
        LanguageSaved(result, model.LanguagePick))
    | LanguageSaved(result, language) ->
      match result with
      | Ok _ ->
        let prefs =
          model.Prefs
          |> Option.map(fun prefs -> {
            prefs with
                TranslationLanguage = language
          })

        {
          model with
              Prefs = prefs
              Saving = false
        },
        Cmd.batch [ applyPrefs(props, prefs); Cmd.ofMsg(MeDone result) ]
      | Error error ->
        {
          model with
              Saving = false
              Error = Some error
        },
        Cmd.none
    | AutomaticToggled enabled ->
      match model.Prefs with
      | Some prefs when prefs.AutomaticTranslation <> enabled ->
        let save() =
          Me.setAutomaticTranslation(
            props.Env,
            {
              KeyFile = props.Session.KeyFile
              Enabled = enabled
            }
          )

        {
          model with
              Saving = true
              Error = None
        },
        Cmd.OfAsync.perform save () (fun result ->
          AutomaticSaved(result, enabled))
      | _ -> model, Cmd.none
    | AutomaticSaved(result, enabled) ->
      match result with
      | Ok _ ->
        let prefs =
          model.Prefs
          |> Option.map(fun prefs -> {
            prefs with
                AutomaticTranslation = enabled
          })

        {
          model with
              Prefs = prefs
              Saving = false
        },
        Cmd.batch [ applyPrefs(props, prefs); Cmd.ofMsg(MeDone result) ]
      | Error error ->
        {
          model with
              Saving = false
              Error = Some error
        },
        Cmd.none
    | GithubSyncRequested ->
      let sync() =
        Me.syncGithub(props.Env, { KeyFile = props.Session.KeyFile })

      { model with Error = None },
      Cmd.batch [
        Cmd.OfAsync.perform sync () MeDone

        Cmd.ofAction(fun () ->
          log.LogInformation("The GitHub sync was requested."))
      ]
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

  let update(props: Props<'env>) = updateCore(loggerOf props, props)

  // The page's inlet from outside: the local settings store.
  let subscribe (props: Props<'env>) (_: Model) : Sub<Msg> =
    [
      Sub.fromObservable
        "settings"
        ((props.Env :> IProvideSettings).Settings.Observe
         |> Observable.map SettingsChanged)

      Sub.fromObservable
        "user-settings"
        ((props.Env :> IProvideUserSettings).UserSettings.Observe
         |> Observable.map PrefsArrived)
    ]
    |> Sub.batch

// The page's semantic regions; the view only composes these.
module Parts =

  // The lane palette row.
  let laneRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    // The palette is rebuilt when its inputs change: the ring follows.
    let palette(current: State.Model) : Control =
      match current.Me with
      | Some me ->
        let current' = State.laneIndex me.PreferredLane

        let laneButton(index: int) : Control =
          Button()
            .Width(34.0)
            .Height(34.0)
            .CornerRadius(17.0)
            .BorderThickness(2.0)
            .OnClickHandler(fun _ _ -> dispatch(State.LaneSet index))
            .Classes(
              Theme.laneClass(Theme.LaneBack, Some(LaneIndex index)),
              Theme.name(
                if current' |> Option.contains index then
                  Theme.TextEdge
                else
                  Theme.Hairline
              )
            )

        let automatic =
          Button()
            .Classes(Theme.name Theme.NavButton)
            .Content("Automatic")
            .OnClickHandler(fun _ _ -> dispatch State.LaneAutomatic)

        if me.PreferredLane |> Option.isNone then
          automatic.Classes(Theme.name Theme.CardBack) |> ignore

        let row: Control array = [|
          automatic
          for index in 0..5 do
            laneButton index
        |]

        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(row)
      | None -> Status.empty ""

    let keyOf(current: State.Model) =
      match current.Me with
      | Some me ->
        struct (true,
                me.PreferredLane.IsNone,
                State.laneIndex me.PreferredLane |> Option.defaultValue -1)
      | None -> struct (false, false, -1)

    let laneHost =
      ContentControl()
        .Content(PageChrome.rebuildOn(model, keyOf, palette).ToBinding())

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

  // The appearance row. It follows the model: a choice dispatches, the
  // update writes the store, and the store's emission returns through
  // the settings subscription.
  let themeRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let themeHost = ContentControl()

    let choices =
      PageChrome.rebuildOn(
        model,
        (fun current -> current.Settings.Theme),
        fun current ->
          let choiceButton(label: string, choice: ThemeChoice) : Control =
            let button =
              Button()
                .Classes(Theme.name Theme.NavButton)
                .Content(label)
                .OnClickHandler(fun _ _ -> dispatch(State.ThemeChosen choice))

            if current.Settings.Theme = choice then
              button.Classes(Theme.name Theme.AccentEdge) |> ignore

            button

          let row: Control array = [|
            choiceButton("System", System)
            choiceButton("Light", Light)
            choiceButton("Dark", Dark)
          |]

          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(6.0)
            .Children(row)
      )

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

  // One zone the picker offers: its IANA id and its current offset.
  type Zone = { Id: string; Offset: string }

  // One zone's current offset, as GMT+2 or GMT-5:30.
  let offsetOf(zone: TimeZoneInfo) : string =
    let offset = zone.GetUtcOffset DateTimeOffset.UtcNow
    let sign = if offset < TimeSpan.Zero then "-" else "+"
    $"GMT{sign}{abs offset.Hours}:{abs offset.Minutes:D2}"

  // The IANA id of one zone: Unix machines name it already; Windows ids
  // convert through ICU.
  let ianaOf(zone: TimeZoneInfo) : string option =
    if zone.Id.Contains '/' then
      Some zone.Id
    else
      match TimeZoneInfo.TryConvertWindowsIdToIanaId zone.Id with
      | true, iana -> Option.ofObj iana
      | false, _ -> None

  // Every zone this machine knows, by IANA id. The list is built once:
  // the offsets are the current ones.
  let zones: Zone list =
    TimeZoneInfo.GetSystemTimeZones()
    |> Seq.choose(fun zone ->
      ianaOf zone
      |> Option.map(fun id ->
        let resolved =
          try
            TimeZoneInfo.FindSystemTimeZoneById id
          with _ ->
            zone

        { Id = id; Offset = offsetOf resolved }))
    |> Seq.distinctBy(fun zone -> zone.Id)
    |> Seq.sortBy(fun zone -> zone.Id)
    |> Seq.toList

  // The device's own zone, when the machine names one.
  let deviceZone: string option = ianaOf TimeZoneInfo.Local

  // What one searchable list reads and reports.
  [<Struct>]
  type Picker = {
    Model: IObservable<State.Model>
    Query: State.Model -> string
    Rows: State.Model -> (string * string) list
    // The identity of the rows the filter reads.
    Key: State.Model -> obj
    Typed: string -> unit
    Picked: string -> unit
  }

  // One searchable list: the box, the rows it leaves, and the pick each
  // row reports.
  let picker(args: Picker) : Control =
    let box =
      TextBox()
        .PlaceholderText("Search")
        .Text(args.Model |> Observable.map args.Query |> _.ToBinding())
        .OnTextChangedHandler(fun box _ ->
          args.Typed(box.Text |> Option.ofObj |> Option.defaultValue ""))

    let list =
      ContentControl()
        .Content(
          PageChrome.rebuildOn(
            args.Model,
            (fun current -> args.Query current, args.Key current),
            fun current ->
              let text = (args.Query current).Trim().ToLowerInvariant()

              let matching =
                args.Rows current
                |> List.filter(fun (key, label) ->
                  text = ""
                  || key.ToLowerInvariant().Contains text
                  || label.ToLowerInvariant().Contains text)
                |> List.truncate 80

              if matching.IsEmpty then
                Status.empty "No match."
              else
                StackPanel()
                  .Spacing(2.0)
                  .Children(
                    [|
                      for (key, label) in matching ->
                        Button()
                          .Classes(Theme.name Theme.NavButton)
                          .HorizontalAlignment(HorizontalAlignment.Stretch)
                          .HorizontalContentAlignment(HorizontalAlignment.Left)
                          .Content(label)
                          .OnClickHandler(fun _ _ -> args.Picked key)
                        :> Control
                    |]
                  )
          )
          |> _.ToBinding()
        )

    StackPanel()
      .Spacing(4.0)
      .Children(box, ScrollViewer().MaxHeight(150.0).Content(list))

  // The timezone row: the picker, the device's own zone, the local-time
  // choice, and the save.
  let timezoneRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let picked =
      model
      |> Observable.map(fun current ->
        match current.ZonePick with
        | Some id ->
          // A zone the web saved but this machine does not list shows its
          // own id, the way the site's picker names it.
          match zones |> List.tryFind(fun zone -> zone.Id = id) with
          | Some zone -> $"{zone.Offset}  {zone.Id}"
          | None -> id
        | None ->
          deviceZone
          |> Option.map(fun id -> $"Not set, using {id}")
          |> Option.defaultValue "Not set, using this device's timezone")

    let dirty =
      model
      |> Observable.map(fun current ->
        match current.Prefs with
        | None -> false
        | Some prefs ->
          let timezone =
            match current.ZonePick with
            | Some id -> ZoneOf(id, current.ZoneShowLocal)
            | None -> FollowDevice

          timezone <> prefs.Timezone)

    let device =
      deviceZone
      |> Option.map(fun id ->
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .HorizontalAlignment(HorizontalAlignment.Left)
          .Content($"Use {id} from this device")
          .IsVisible(
            model
            |> Observable.map(fun current ->
              current.ZonePick |> Option.contains id |> not)
          )
          .OnClickHandler(fun _ _ -> dispatch(State.ZonePicked(Some id)))
        :> Control)
      |> Option.defaultValue(Panel())

    let unset =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .HorizontalAlignment(HorizontalAlignment.Left)
        .Content("Unset")
        .IsVisible(
          model |> Observable.map(fun current -> current.ZonePick.IsSome)
        )
        .OnClickHandler(fun _ _ -> dispatch(State.ZonePicked None))

    let showLocal =
      CheckBox()
        .IsChecked(
          model
          |> Observable.map(fun current -> current.ZoneShowLocal)
          |> _.ToBinding()
        )
        .IsEnabled(
          model |> Observable.map(fun current -> current.ZonePick.IsSome)
        )
        .OnIsCheckedChangedHandler(fun box _ ->
          dispatch(
            State.ShowLocalToggled(
              box.IsChecked |> Option.ofNullable |> Option.contains true
            )
          ))

    let save =
      Button()
        .Classes(Theme.name Theme.NavButton)
        .Content("Save timezone")
        .IsEnabled(
          Observable.combineLatest
            dirty
            (model
             |> Observable.map(fun current ->
               current.Saving || current.Prefs.IsNone))
          |> Observable.map(fun (dirty, blocked) -> dirty && not blocked)
        )
        .OnClickHandler(fun _ _ -> dispatch State.SaveTimezone)

    StackPanel()
      .Spacing(6.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Subtitle).Text("Timezone"),
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .TextWrapping(TextWrapping.Wrap)
          .Text(
            "Times on your profile use this zone. Leave it unset to use this device's zone without sharing it. Your local time only shows on your profile if you turn that on below."
          ),
        TextBlock().Classes(Theme.name Theme.Muted).Text(picked.ToBinding()),
        picker {
          Model = model
          Query = fun current -> current.ZoneQuery
          Rows =
            fun _ -> [
              for zone in zones -> zone.Id, $"{zone.Offset}  {zone.Id}"
            ]
          Key = fun _ -> zones
          Typed = fun text -> dispatch(State.ZoneQueryChanged text)
          Picked = fun id -> dispatch(State.ZonePicked(Some id))
        },
        device,
        unset,
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .Children(
            showLocal,
            TextBlock()
              .VerticalAlignment(VerticalAlignment.Center)
              .Text("Show my local time on my profile")
          ),
        save
      )

  // The device language's English name, as the settings list it; the
  // UI culture's own name when the list is not here yet.
  let deviceLanguageName(prefs: UserSettings option) : string =
    prefs
    |> Option.bind(fun prefs ->
      prefs.Languages
      |> List.tryFind(fun language ->
        language.Code = TranslationModel.deviceLanguage))
    |> Option.map(fun language -> language.Name)
    |> Option.defaultWith(fun () -> CultureInfo.CurrentUICulture.DisplayName)

  // The translation language row: the picker, the device choice, and the
  // automatic translation switch.
  let languageRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let picked =
      model
      |> Observable.map(fun current ->
        match current.LanguagePick with
        | Some language -> language.Name
        | None ->
          $"Device language (automatic) — {deviceLanguageName current.Prefs}")

    let dirty =
      model
      |> Observable.map(fun current ->
        match current.Prefs with
        | None -> false
        | Some prefs -> current.LanguagePick <> prefs.TranslationLanguage)

    let device =
      Button()
        .Classes(Theme.name Theme.NavButton)
        .HorizontalAlignment(HorizontalAlignment.Left)
        .Content(
          TextBlock()
            .Text(
              model
              |> Observable.map(fun current ->
                $"Use {deviceLanguageName current.Prefs} from this device")
            )
        )
        .OnClickHandler(fun _ _ -> dispatch(State.LanguagePicked None))

    let save =
      Button()
        .Classes(Theme.name Theme.NavButton)
        .Content("Save translation language")
        .IsEnabled(
          Observable.combineLatest
            dirty
            (model
             |> Observable.map(fun current ->
               current.Saving || current.Prefs.IsNone))
          |> Observable.map(fun (dirty, blocked) -> dirty && not blocked)
        )
        .OnClickHandler(fun _ _ -> dispatch State.SaveLanguage)

    let automatic =
      ToggleSwitch()
        .IsChecked(
          model
          |> Observable.map(fun current ->
            current.Prefs
            |> Option.map(fun prefs -> Nullable prefs.AutomaticTranslation)
            |> Option.defaultValue(Nullable true))
        )
        .IsEnabled(
          model
          |> Observable.map(fun current ->
            current.Prefs.IsSome && not current.Saving)
        )
        .OnIsCheckedChangedHandler(fun toggle _ ->
          toggle.IsChecked
          |> Option.ofNullable
          |> Option.iter(State.AutomaticToggled >> dispatch))

    automatic.Name <- "AutomaticTranslationToggle"

    StackPanel()
      .Spacing(6.0)
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.Subtitle)
          .Text("Translation language"),
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .TextWrapping(TextWrapping.Wrap)
          .Text(
            "Choose the language for translated commits and replies, or follow this device's language. Search by English or native name."
          ),
        TextBlock().Classes(Theme.name Theme.Muted).Text(picked.ToBinding()),
        device,
        picker {
          Model = model
          Query = fun current -> current.LanguageQuery
          Rows =
            fun current ->
              current.Prefs
              |> Option.map(fun prefs -> [
                for language in prefs.Languages ->
                  let label =
                    if language.NativeName = language.Name then
                      language.Name
                    else
                      $"{language.Name} · {language.NativeName}"

                  language.Code, label
              ])
              |> Option.defaultValue []
          Key = fun current -> current.Prefs
          Typed = fun text -> dispatch(State.LanguageQueryChanged text)
          Picked = fun code -> dispatch(State.LanguagePicked(Some code))
        },
        save,
        Border()
          .Classes(Theme.name Theme.Hairline)
          .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
          .Margin(0.0, 6.0, 0.0, 6.0),
        StackPanel()
          .Spacing(4.0)
          .Children(
            TextBlock()
              .Classes(Theme.name Theme.Subtitle)
              .Text("Automatically show translations"),
            TextBlock()
              .Classes(Theme.name Theme.Muted)
              .TextWrapping(TextWrapping.Wrap)
              .Text(
                "Show available translations in your chosen language. Turn off to read originals first and choose Translate when you need it."
              ),
            automatic
          )
      )

  // The pastéis mode row: one button for each state.
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

  // The GitHub sync button.
  let syncButton(dispatch: Dispatch<State.Msg>) : Control =
    Button()
      .Classes(Theme.name Theme.NavButton)
      .Content("Sync the GitHub profile")
      .OnClickHandler(fun _ _ -> dispatch State.GithubSyncRequested)

  // The application preferences row. It rides the MVU loop: the
  // toggle dispatches, the update writes the store, and the store's
  // emission comes back through the settings subscription.
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

  // The devices row: one card per key approved to sign as the user,
  // with this device's own row named and its revoke worded as the
  // sign-out it is. Rows rebuild when the list changes.
  let devicesRow
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let own = props.Session.Session.DeviceFingerprint

    let dayOf(at: DateTimeOffset) =
      at.ToString(
        "MMM d, yyyy",
        System.Globalization.CultureInfo.InvariantCulture
      )

    let rowOf(device: Device) : Control =
      let isSelf = own <> "" && own = device.Fingerprint

      let labelOf(style, text) : Control =
        TextBlock()
          .Classes(Theme.name style)
          .TextWrapping(TextWrapping.Wrap)
          .Text(text)

      let badge: Control =
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .VerticalAlignment(VerticalAlignment.Center)
          .Text("This device")
          .IsVisible(isSelf)

      let names =
        match device.AppName with
        | Some appName -> [|
            labelOf(Theme.Subtitle, appName)
            badge
            labelOf(Theme.Muted, $"on {device.Name}")
          |]
        | None -> [| labelOf(Theme.Subtitle, device.Name); badge |]

      Border()
        .Classes(Theme.name Theme.Card)
        .Padding(12.0)
        .Child(
          StackPanel()
            .Spacing(6.0)
            .Children(
              WrapPanel().ItemSpacing(8.0).Children(names),
              TextBlock()
                .Classes(Theme.name Theme.Mono, Theme.name Theme.Muted)
                .Text(device.Fingerprint),
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(8.0)
                .Children(
                  TextBlock()
                    .Classes(Theme.name Theme.Muted)
                    .Text($"Approved {dayOf device.ApprovedAt}"),
                  TextBlock()
                    .Classes(Theme.name Theme.Muted)
                    .Text(
                      match device.LastUsedAt with
                      | Some at -> $"Last used {dayOf at}"
                      | None -> "Never used"
                    )
                ),
              Button()
                .Classes(Theme.name Theme.DangerButton)
                .Content(if isSelf then "Sign out on this device" else "Revoke")
                .OnClickHandler(fun _ _ ->
                  dispatch(State.RevokeRequested device))
            )
        )

    let rows =
      PageChrome.rebuildOn(
        model,
        (fun current -> current.Devices),
        fun current ->
          match current.Devices with
          | Some devices ->
            if devices.IsEmpty then
              Status.empty "No devices."
            else
              StackPanel()
                .Spacing(8.0)
                .Children([| for device in devices -> rowOf device |])
          | None -> Status.busy "Loading the devices…"
      )

    StackPanel()
      .Spacing(8.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Subtitle).Text("Devices"),
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .TextWrapping(TextWrapping.Wrap)
          .Text(
            "The keys approved to sign as you. Revoking the one this app runs on signs it out."
          ),
        ContentControl().Content(rows.ToBinding())
      )

  // The sign-out button; the caller owns the actual sign-out.
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

  let body =
    ScrollViewer()
      .Padding(Theme.pagePadding)
      .Content(
        StackPanel()
          .MaxWidth(560.0)
          .HorizontalAlignment(HorizontalAlignment.Stretch)
          .Spacing(14.0)
          .Children(
            TextBlock().Text(user.Name),
            Parts.laneRow(model, dispatch),
            Parts.themeRow(model, dispatch),
            Parts.timezoneRow(model, dispatch),
            Parts.languageRow(model, dispatch),
            Parts.pasteisRow(model, dispatch),
            Parts.syncButton dispatch,
            Parts.applicationRow(model, dispatch),
            Parts.devicesRow(props, model, dispatch),
            Parts.signOutButton props
          )
      )

  PageChrome.page {
    Model = model
    TitleOf = fun _ -> $"@{user.Handle}"
    Padding = Theme.pagePadding
    ErrorOf = _.Error
    Busy =
      fun current ->
        if current.Busy then
          PageChrome.Words "Loading the settings…"
        else
          PageChrome.Off
    Empty = fun _ -> PageChrome.Off
    Header = []
    Body = body
  }

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideSession
  and 'env :> IProvideSettings
  and 'env :> IProvideUserSettings
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Programs.runPage(props.Env.Dispatcher, program)

    this.Content <- root
    props.Context.AddDisposable lifetime
