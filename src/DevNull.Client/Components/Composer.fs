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
}

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

let init props () = initial, Cmd.none

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

let view
  (props: Props<'env>)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =

  let placeholder =
    match props.Target with
    | Fresh -> "What did you ship?"
    | ReplyTo _ -> "Write your reply…"
    | Quoting _ -> "Add your comment to the quote…"

  // One line to start, the site's textarea rhythm; the inner presenter
  // takes over past the cap.
  let body =
    TextBox()
      .Name("ComposerBody")
      .PlaceholderText(placeholder)
      .AcceptsReturn(true)
      .MinHeight(44.0)
      .MaxHeight(300.0)
      .TextWrapping(TextWrapping.Wrap)

  let busy = model |> Observable.map _.Busy

  // Every landed commit leaves the box empty for the next one, so a
  // sent body never lingers to be cut or reposted.
  model |> Observable.map _.Committed |> Observable.add(fun _ -> body.Clear())

  let errorLine =
    let text =
      model
      |> Observable.map(fun current ->
        current.Error |> Option.map describe |> Option.defaultValue "")

    TextBlock()
      .TextWrapping(TextWrapping.Wrap)
      .Foreground(Brushes.danger)
      .Text(text)
      .IsVisible(model |> Observable.map(_.Error.IsSome))

  // No box: the composer rides the rail plain, the way the site's
  // does on the log and the thread; a host that wants a card wraps it.
  StackPanel()
    .Margin(0.0, 0.0, 0.0, 12.0)
    .Spacing(8.0)
    .Children(
      body,
      DockPanel()
        .LastChildFill(true)
        .Children(
          Button()
            .Classes(Theme.name Theme.AccentButton)
            .Content("Commit")
            .IsEnabled(busy |> Observable.map not)
            .OnClickHandler(fun _ _ ->
              dispatch(
                Submitted(body.Text |> Option.ofObj |> Option.defaultValue "")
              ))
            .DockRight(),
          errorLine
        )
    )

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram (init props) (update props) (view props)

    this.Content <- Program.runWith () program
