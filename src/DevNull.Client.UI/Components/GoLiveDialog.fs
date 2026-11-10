module DevNull.Client.Components.GoLiveDialog

open System
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Controls.Primitives
open Avalonia.Layout
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Stores

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher> = {
  Env: 'env
  Session: SignedInSession
  WentLive: Result<Me, AppError> -> unit
  Close: unit -> unit
}

// The page's state logic: model, messages, and update.
module State =

  type Model = {
    Url: string
    Hours: int
    Busy: bool
    Failure: string voption
  }

  type Msg =
    | UrlChanged of text: string
    | HoursChanged of hours: int
    | SubmitRequested
    | Started of Result<Me, AppError>
    | CloseRequested

  let initial = {
    Url = ""
    Hours = 2
    Busy = false
    Failure = ValueNone
  }

  let invalidUrlMessage = "Use a link to your stream on YouTube or Twitch."

  let invalidHoursMessage = "A stream lasts between 1 and 12 hours."

  let update
    (props: Props<'env>)
    (message: Msg)
    (model: Model)
    : Model * Cmd<Msg> =
    match message with
    | UrlChanged text -> { model with Url = text }, Cmd.none
    | HoursChanged hours -> { model with Hours = hours }, Cmd.none
    | CloseRequested when model.Busy -> model, Cmd.none
    | CloseRequested -> model, Cmd.ofAction(props.Close)
    | SubmitRequested when model.Busy -> model, Cmd.none
    | SubmitRequested ->
      match LivePlatforms.forUrl(model.Url) with
      | None ->
        {
          model with
              Failure = ValueSome invalidUrlMessage
        },
        Cmd.none
      | Some _ ->
        match LivePlatforms.tryParseHours model.Hours with
        | None ->
          {
            model with
                Failure = ValueSome invalidHoursMessage
          },
          Cmd.none
        | Some hours ->
          {
            model with
                Busy = true
                Failure = ValueNone
          },
          Cmd.OfAsync.perform
            Me.goLive
            (props.Env,
             {
               KeyFile = props.Session.KeyFile
               Url = model.Url
               Hours = hours
             })
            Started
    | Started result ->
      let failed =
        match result with
        | Ok _ -> None
        | Error error -> Some(describe error)

      {
        model with
            Busy = false
            Failure = failed |> ValueOption.ofOption
      },
      match result with
      | Ok _ -> Cmd.ofAction(fun () -> props.WentLive result)
      | Error _ -> Cmd.none

// The dialog's semantic regions; the view only composes these.
module Parts =

  let inline hoursLabel(hours: int) : string =
    if hours = 1 then "1 hour" else $"%d{hours}"

  // The stream link box.
  let urlBox
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    TextBox()
      .Name("LiveUrl")
      .PlaceholderText("https://www.youtube.com/live/… or https://twitch.tv/…")
      .Text(model |> Observable.map _.Url)
      .IsEnabled(model |> Observable.map(fun current -> not current.Busy))
      .OnTextChangedHandler(fun box _ ->
        box.Text
        |> Option.ofObj
        |> Option.defaultValue ""
        |> State.UrlChanged
        |> dispatch)

  // The hours slider beside its word.
  let hoursRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let slider = Slider()

    slider.Minimum <- 1.0
    slider.Maximum <- float LivePlatforms.maxHours
    slider.Value <- 2.0
    slider.TickFrequency <- 1.0
    slider.IsSnapToTickEnabled <- true

    slider.ValueChanged.Add(fun args ->
      dispatch(State.HoursChanged(int args.NewValue)))

    StackPanel()
      .Spacing(8.0)
      .Children(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .Children(
            TextBlock().Classes(Theme.name Theme.Subtitle).Text("For"),
            TextBlock()
              .Classes(Theme.name Theme.Mono)
              .HorizontalAlignment(HorizontalAlignment.Right)
              .Text(
                model |> Observable.map(fun current -> hoursLabel current.Hours)
              )
          ),
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .Children(
            slider.Name("LiveHours"),
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(4.0)
              .Children(
                TextBlock().Classes(Theme.name Theme.Muted).Text("1h"),
                TextBlock()
                  .Classes(Theme.name Theme.Muted)
                  .Text($"{LivePlatforms.maxHours}h")
              )
          )
      )

  // The dialog's close and submit actions.
  let actions
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(8.0)
      .Children(
        Button()
          .Name("LiveCancel")
          .Classes(Theme.name Theme.GhostButton)
          .Content("Cancel")
          .IsEnabled(model |> Observable.map(fun current -> not current.Busy))
          .OnClickHandler(fun _ _ -> dispatch State.CloseRequested),
        Button()
          .Name("GoLive")
          .Classes(Theme.name Theme.AccentButton)
          .Content("Go live")
          .IsEnabled(model |> Observable.map(fun current -> not current.Busy))
          .OnClickHandler(fun _ _ -> dispatch State.SubmitRequested)
      )

  // The refusal that stays until the next words fix it.
  let failureLine(model: IObservable<State.Model>) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .TextWrapping(TextWrapping.Wrap)
      .Margin(0.0, 8.0, 0.0, 0.0)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Failure |> ValueOption.defaultValue "")
      )
      .IsVisible(model |> Observable.map(fun current -> current.Failure.IsSome))

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =
  let body =
    StackPanel()
      .Spacing(12.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Title).Text("Go live"),
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .TextWrapping(TextWrapping.Wrap)
          .Text(
            "Share your YouTube or Twitch stream and your avatar gets a live marker that sends people to it, for as long as you choose."
          ),
        TextBlock().Classes(Theme.name Theme.Subtitle).Text("Stream link"),
        Parts.urlBox(model, dispatch),
        Parts.hoursRow(model, dispatch),
        Parts.failureLine model,
        Parts.actions(model, dispatch)
      )

  Border()
    .Name("GoLiveDialogCard")
    .Classes(Theme.name Theme.Card)
    .MaxWidth(520.0)
    .Padding(16.0)
    .Child(body)

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher>(props: Props<'env>) as this =
  inherit UserControl()

  do
    this.Name <- "GoLiveDialog"

    let program =
      Program.mkProgram
        (fun () -> State.initial, Cmd.none)
        (State.update props)
        (view props)

    let root, _ = Programs.runComponent(this, props.Env.Dispatcher, program)

    this.Content <- root
