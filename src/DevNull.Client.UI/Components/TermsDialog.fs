module DevNull.Client.Components.TermsDialog

open System
open Avalonia.Controls
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open Microsoft.Extensions.Logging
open DevNull.Client
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.DeviceAuth
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Stores

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  OnAccepted: Me -> unit
}

module State =

  type Model = {
    Agreed: bool
    Busy: bool
    Failure: string voption
  }

  type Msg =
    | Ticked of bool
    | AcceptRequested
    | Accepted of Result<Me, AppError>

  let initial = {
    Agreed = false
    Busy = false
    Failure = ValueNone
  }

  let accept(props: Props<'env>) : Async<Result<Me, AppError>> =
    Me.acceptTerms(
      props.Env,
      {
        KeyFile = props.Session.KeyFile
        Version = props.Session.Session.TermsVersion
      }
    )

  let update (props: Props<'env>) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Ticked agreed -> { model with Agreed = agreed }, Cmd.none
    | AcceptRequested ->
      {
        model with
            Busy = true
            Failure = ValueNone
      },
      Cmd.OfAsync.perform accept props Accepted
    | Accepted(Ok me) ->
      { model with Busy = false }, Cmd.ofAction(fun () -> props.OnAccepted me)
    | Accepted(Error error) ->
      {
        model with
            Busy = false
            Failure = ValueSome(describe error)
      },
      Cmd.none

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =
  let log = props.Env.LoggerFactory.CreateLogger "DevNull.Client.TermsDialog"

  let openPage(path: string, args: RoutedEventArgs) =
    args.Handled <- true

    match props.Env.Browser.Open(baseAddress + path) with
    | Ok() -> ()
    | Error message -> log.LogWarning("The page did not open: {Error}", message)

  let canAccept =
    model |> Observable.map(fun current -> current.Agreed && not current.Busy)

  let failure = model |> Observable.map _.Failure

  Border()
    .Classes(Theme.name Theme.Card)
    .MaxWidth(520.0)
    .Child(
      StackPanel()
        .Spacing(12.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Title)
            .Text("Before you continue"),
          TextBlock()
            .Classes(Theme.name Theme.Muted)
            .TextWrapping(TextWrapping.Wrap)
            .Text(
              "This is an unofficial, third-party app. It talks to /dev/null's public API, which may change and break this app without notice. The terms and the privacy policy are /dev/null's — accept them to keep using the service through this app."
            ),
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(
              Button()
                .Classes(Theme.name Theme.GhostButton)
                .Padding(8.0, 4.0, 8.0, 4.0)
                .Content("Read the terms")
                .OnClickHandler(fun _ args -> openPage("/terms", args)),
              Button()
                .Classes(Theme.name Theme.GhostButton)
                .Padding(8.0, 4.0, 8.0, 4.0)
                .Content("Read the privacy policy")
                .OnClickHandler(fun _ args -> openPage("/privacy", args))
            ),
          CheckBox()
            .Content("I accept the terms and the privacy policy.")
            .OnIsCheckedChangedHandler(fun box _ ->
              let ticked =
                match box.IsChecked with
                | value when value.HasValue && value.Value -> true
                | _ -> false

              dispatch(State.Ticked ticked)),
          Button()
            .Classes(Theme.name Theme.AccentButton)
            .HorizontalAlignment(HorizontalAlignment.Right)
            .Content("Accept")
            .IsEnabled(canAccept)
            .OnClickHandler(fun _ _ -> dispatch State.AcceptRequested),
          TextBlock()
            .Classes(Theme.name Theme.FgDanger)
            .TextWrapping(TextWrapping.Wrap)
            .IsVisible(failure |> Observable.map ValueOption.isSome)
            .Text(
              failure
              |> Observable.map(function
                | ValueSome word -> word
                | ValueNone -> "")
            )
        )
    )

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideBrowser
  and 'env :> IProvideLogging>(props: Props<'env>) as this =
  inherit UserControl()

  do
    this.Name <- "TermsDialog"

    let program =
      Program.mkProgram
        (fun () -> State.initial, Cmd.none)
        (State.update props)
        (view props)

    let root, _ = Programs.runComponent(this, props.Env.Dispatcher, program)

    this.Content <- root
