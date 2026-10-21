module DevNull.Client.Components.Composer

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open IcedTasks
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Stores
open DevNull.Client.Theme

/// What the composer answers to: a fresh commit, a reply, or a quote.
[<Struct>]
type Target =
  | Fresh
  | ReplyTo of parentId: string
  | Quoting of quotedId: string

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  Session: SignedInSession
  Target: Target
  OnCommitted: Commit -> unit
  Register: (IDisposable -> unit) voption
}

/// The composer's state logic: model, messages, and update.
module State =

  type Model = {
    Busy: bool
    Error: AppError option
    Committed: int
  }

  type Msg =
    | Submitted of body: string
    | Committed of Result<Commit, AppError>

  let initial = {
    Busy = false
    Error = None
    Committed = 0
  }

  let apiTarget(target: Target) : Commits.Target =
    match target with
    | Fresh -> Commits.New
    | ReplyTo parentId -> Commits.ReplyTo parentId
    | Quoting quotedId -> Commits.Quote quotedId

  let init (props: Props<'env>) () = initial, Cmd.none

  let update (props: Props<'env>) msg model =
    match msg with
    | Submitted body ->
      { model with Busy = true; Error = None },
      Cmd.OfAsync.perform
        Commits.create
        (props.Env,
         {
           KeyFile = props.Session.KeyFile
           Body = body
           Target = apiTarget props.Target
         })
        Committed
    | Committed result ->
      match result with
      | Ok commit ->
        props.OnCommitted commit

        {
          initial with
              Committed = model.Committed + 1
        },
        Cmd.none
      | Error error ->
        {
          model with
              Busy = false
              Error = Some error
        },
        Cmd.none

/// The composer's semantic regions; the view only composes these.
module Parts =

  /// The input: one line to start, the site's textarea rhythm; the
  /// inner presenter takes over past the cap.
  let body(props: Props<'env>) : TextBox =
    let placeholder =
      match props.Target with
      | Fresh -> "What did you ship?"
      | ReplyTo _ -> "Write your reply…"
      | Quoting _ -> "Add your comment to the quote…"

    TextBox()
      .Name("ComposerBody")
      .PlaceholderText(placeholder)
      .AcceptsReturn(true)
      .MinHeight(44.0)
      .MaxHeight(300.0)
      .TextWrapping(TextWrapping.Wrap)

  /// Every landed commit leaves the box empty for the next one, so a
  /// sent body never lingers to be cut or reposted.
  let clearOnCommit(model: IObservable<State.Model>, body: TextBox) : unit =
    model |> Observable.map _.Committed |> Observable.add(fun _ -> body.Clear())

  /// The error line under the input.
  let errorLine(model: IObservable<State.Model>) : Control =
    TextBlock()
      .TextWrapping(TextWrapping.Wrap)
      .Foreground(Brushes.danger)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Error |> Option.map describe |> Option.defaultValue "")
      )
      .IsVisible(model |> Observable.map(_.Error.IsSome))

  /// The action row: the commit button docked right, the error line
  /// filling the rest.
  let actionRow
    (
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      body: TextBox
    ) : Control =
    DockPanel()
      .LastChildFill(true)
      .Children(
        Button()
          .Classes(Theme.name Theme.AccentButton)
          .Content("Commit")
          .IsEnabled(model |> Observable.map(fun current -> not current.Busy))
          .OnClickHandler(fun _ _ ->
            dispatch(
              State.Submitted(
                body.Text |> Option.ofObj |> Option.defaultValue ""
              )
            ))
          .DockRight(),
        errorLine model
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let body = Parts.body props

  Parts.clearOnCommit(model, body)

  // No box: the composer rides the rail plain, the way the site's does
  // on the log and the thread; a host that wants a card wraps it.
  StackPanel()
    .Margin(0.0, 0.0, 0.0, 12.0)
    .Spacing(8.0)
    .Children(body, Parts.actionRow(model, dispatch, body))

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)

    match props.Register with
    | ValueNone ->
      let root, _ = Program.runWithLifetime this () program
      this.Content <- root
    | ValueSome register ->
      let root, lifetime = Program.runWithLifecycle () program
      this.Content <- root
      register lifetime
