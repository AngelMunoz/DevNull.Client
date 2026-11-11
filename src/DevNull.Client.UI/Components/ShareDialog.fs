module DevNull.Client.Components.ShareDialog

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open Avalonia.VisualTree
open NXUI.Extensions
open FSharp.Control.Reactive
open IcedTasks
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components.Avatar
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.Stores

module CoreRemotes = DevNull.Client.Core.Api.Remotes

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideRemotes> = {
  Env: 'env
  Session: SignedInSession
  Commit: Commit
  Sent: User -> unit
  Close: unit -> unit
}

module State =

  type Model = {
    Term: string
    Asked: string
    Answer: (string * RemoteCandidate list) option
    Failure: string voption
    Recipient: User option
  }

  type Msg =
    | Typed of string
    | SearchDone of term: string * Result<RemoteCandidate list, AppError>
    | Picked of User
    | CloseRequested
    | SendRequested
    | SendDone of User

  let inline termOf(value: string) : string = value.Trim().TrimStart('@')

  let initial = {
    Term = ""
    Asked = ""
    Answer = None
    Failure = ValueNone
    Recipient = None
  }

  let search
    (props: Props<'env>, term: string)
    : Async<Result<RemoteCandidate list, AppError>> = asyncEx {
    return!
      CoreRemotes.candidates(
        props.Env,
        {
          KeyFile = props.Session.KeyFile
          Search = Some term
          Org = None
          For = Some CoreRemotes.ForShare
        }
      )
  }

  let handOff(props: Props<'env>, recipient: User) =
    // The engine stages the link first — the thread opens already
    // carrying it — and owns its delivery; a refusal stays as the
    // thread's refused bubble, so the dialog never waits and never
    // carries the outcome.
    Async.StartImmediate(
      props.Env.Remotes.Push(
        Partner recipient,
        {
          Body = Some props.Commit.HtmlUrl
          ReplyTo = None
          Card = LinkCard.FirstLink
          Images = []
          ImageDescriptions = []
          Voice = None
        }
      )
    )

    props.Env.Remotes.SetOpenTarget(Some(Partner recipient))

  let answered(model: Model) : bool =
    model.Answer |> Option.exists(fun (term, _) -> term = termOf model.Term)

  let rowsOf(model: Model) : RemoteCandidate list =
    match model.Answer with
    | Some(term, people) when term = termOf model.Term -> people
    | _ -> []

  let searching(model: Model) : bool =
    let term = termOf model.Term

    term <> "" && model.Failure.IsNone && not(answered model)

  let update (props: Props<'env>) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Typed value when value = model.Term -> model, Cmd.none
    | Typed value ->
      let term = termOf value

      if term = "" then
        {
          model with
              Term = value
              Asked = ""
              Answer = None
              Failure = ValueNone
        },
        Cmd.none
      else
        {
          model with
              Term = value
              Asked = term
              Failure = ValueNone
        },
        Cmd.OfAsync.perform search (props, term) (fun result ->
          SearchDone(term, result))
    | SearchDone(term, result) ->
      if term <> model.Asked then
        model, Cmd.none
      else
        match result with
        | Ok people ->
          {
            model with
                Answer = Some(term, people)
                Failure = ValueNone
          },
          Cmd.none
        | Error error ->
          {
            model with
                Failure = ValueSome(describe error)
          },
          Cmd.none
    | Picked person -> { model with Recipient = Some person }, Cmd.none
    | CloseRequested -> model, Cmd.ofAction props.Close
    | SendRequested ->
      match model.Recipient with
      | Some recipient ->
        model,
        Cmd.ofEffect(fun dispatch ->
          handOff(props, recipient)
          dispatch(SendDone recipient))
      | _ -> model, Cmd.none
    | SendDone recipient -> model, Cmd.ofAction(fun () -> props.Sent recipient)

module Parts =

  [<Struct>]
  type RowCtx<'env
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp
    and 'env :> IProvideDispatcher
    and 'env :> IProvideImageCache
    and 'env :> IProvideRemotes> = {
    Props: Props<'env>
    Dispatch: Dispatch<State.Msg>
  }

  let candidateRow
    (ctx: RowCtx<'env>, index: int, person: RemoteCandidate)
    : Control =
    let reason = person.Reason |> Option.map(fun why -> "can't push: " + why)

    let identity = Grid()

    identity.ColumnDefinitions <- ColumnDefinitions("*,Auto")

    let _ =
      identity.Children(
        TextBlock()
          .Classes(Theme.name Theme.Subtitle)
          .Text(person.User.Name)
          .TextTrimming(TextTrimming.CharacterEllipsis)
          .Column(0),
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .Text("@" + person.User.Handle)
          .TextTrimming(TextTrimming.CharacterEllipsis)
          .MaxWidth(180.0)
          .Margin(6.0, 0.0, 0.0, 0.0)
          .Column(1)
      )

    let reasonLine =
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Text(reason |> Option.defaultValue "")
        .TextTrimming(TextTrimming.CharacterEllipsis)
        .IsVisible(reason.IsSome)

    let avatar =
      Avatar.view {
        Name = person.User.Name
        Url = person.User.AvatarUrl
        Lane = person.User.Lane
        Size = 40.0
        Cache = ctx.Props.Env.ImageCache
        Square = false
        Viewable = false
      }

    let columns = Grid()

    columns.ColumnDefinitions <- ColumnDefinitions("Auto,*")

    Button()
      .Name($"ShareCandidate{index}")
      .Classes(Theme.name Theme.CommitRow)
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .IsEnabled(person.Reason.IsNone)
      .Content(
        columns.Children(
          avatar
            .Column(0)
            .Margin(0.0, 0.0, 12.0, 0.0)
            .VerticalAlignment(VerticalAlignment.Center),
          StackPanel()
            .Spacing(2.0)
            .Margin(0.0, 10.0, 12.0, 10.0)
            .Children(identity, reasonLine)
            .Column(1)
        )
      )
      .OnClickHandler(fun _ _ -> ctx.Dispatch(State.Picked person.User))

  let rows
    (props: Props<'env>, model: State.Model, dispatch: Dispatch<State.Msg>)
    : Control =
    let ctx: RowCtx<'env> = { Props = props; Dispatch = dispatch }

    let people =
      State.rowsOf model
      |> List.sortBy(fun person -> person.Reason |> Option.isSome)

    match people with
    | [] when State.answered model ->
      TextBlock()
        .Name("ShareNoMatches")
        .Classes(Theme.name Theme.Muted)
        .Text("No one matches.")
        .TextWrapping(TextWrapping.Wrap)
    | [] -> Panel()
    | people ->
      StackPanel().Spacing(2.0).Children [|
        for index, person in List.indexed people ->
          candidateRow(ctx, index, person)
      |]

  let picker
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let box =
      TextBox()
        .Name("ShareSearch")
        .MaxLength(100)
        .PlaceholderText("Find a developer")
        .Text(model |> Observable.map _.Term)
        .OnTextChangedHandler(fun box _ ->
          box.Text |> Option.ofObj |> Option.iter(State.Typed >> dispatch))

    let loading =
      ProgressBar()
        .Name("ShareLoading")
        .IsIndeterminate(true)
        .Height(2.0)
        .MaxHeight(2.0)
        .IsVisible(model |> Observable.map State.searching)

    let failure =
      TextBlock()
        .Classes(Theme.name Theme.FgDanger)
        .TextWrapping(TextWrapping.Wrap)
        .IsVisible(model |> Observable.map _.Failure.IsSome)
        .Text(
          model
          |> Observable.map(fun current ->
            current.Failure |> ValueOption.defaultValue "")
        )

    let rowsHost =
      ContentControl()
        .Content(
          model
          |> Observable.map(fun current -> rows(props, current, dispatch))
          |> _.ToBinding()
        )

    StackPanel()
      .Spacing(12.0)
      .Children(
        TextBlock()
          .Classes(Theme.name Theme.Title)
          .Text("Send commit in Remotes"),
        box,
        loading,
        failure,
        ScrollViewer()
          .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
          .MaxHeight(320.0)
          .Content(rowsHost)
      )

  let confirm
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let title =
      model
      |> Observable.map(fun current ->
        current.Recipient
        |> Option.map(fun recipient -> $"Send commit to @{recipient.Handle}?")
        |> Option.defaultValue "")

    let preview =
      Border()
        .Classes(Theme.name Theme.Card, Theme.name Theme.Hairline)
        .BorderThickness(Thickness(1.0))
        .CornerRadius(8.0)
        .Padding(10.0)
        .Margin(0.0, 6.0, 0.0, 0.0)
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .Child(
          StackPanel()
            .Spacing(2.0)
            .Children(
              TextBlock()
                .Classes(Theme.name Theme.Mono)
                .Text("@" + (Author.handle props.Commit.Author)),
              (let body =
                CommitCard.body(
                  {
                    Flyout = ValueNone
                    Tone = CommitCard.Accent
                    Mentions = props.Commit.Mentions
                    OnOpenExternal = ValueNone
                  },
                  props.Commit.Body,
                  props.Commit.BodySegments
                )

               match body with
               | :? TextBlock as block ->
                 block.Margin <- Thickness(0.0, 0.0, 4.0, 0.0)
                 block.MaxLines <- 4
                 block.TextTrimming <- TextTrimming.CharacterEllipsis
                 block :> Control
               | other -> other)
            )
        )

    let canSend =
      model |> Observable.map(fun current -> current.Recipient.IsSome)

    StackPanel()
      .Spacing(12.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Title).Text(title),
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .TextWrapping(TextWrapping.Wrap)
          .Text("Send a link to this commit in Remotes."),
        ScrollViewer()
          .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
          .MaxHeight(320.0)
          .Content(preview),
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .HorizontalAlignment(HorizontalAlignment.Right)
          .Children(
            Button()
              .Classes(Theme.name Theme.GhostButton)
              .Content("Cancel")
              .OnClickHandler(fun _ _ -> dispatch State.CloseRequested),
            Button()
              .Classes(Theme.name Theme.AccentButton)
              .Content("Send")
              .IsEnabled(canSend)
              .OnClickHandler(fun _ _ -> dispatch State.SendRequested)
          )
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =
  let picker = Parts.picker(props, model, dispatch)
  let confirm = Parts.confirm(props, model, dispatch)

  let _ =
    picker.IsVisible(
      model |> Observable.map(fun current -> current.Recipient.IsNone)
    )

  let _ =
    confirm.IsVisible(
      model |> Observable.map(fun current -> current.Recipient.IsSome)
    )

  let card =
    Border()
      .Name("ShareDialogCard")
      .Classes(Theme.name Theme.Card)
      .MaxWidth(520.0)
      .Child(StackPanel().Children(picker, confirm))

  card.AttachedToVisualTree.Add(fun _ ->
    props.Env.Dispatcher.Post(
      (fun () ->
        card.GetVisualDescendants()
        |> Seq.tryFind(fun visual -> visual.Name = "ShareSearch")
        |> Option.iter(fun visual -> (visual :?> TextBox).Focus() |> ignore)),
      DispatcherPriority.Default
    ))

  card

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache
  and 'env :> IProvideRemotes>(props: Props<'env>) as this =
  inherit UserControl()

  do
    this.Name <- "ShareDialog"

    let program =
      Program.mkProgram
        (fun () -> State.initial, Cmd.none)
        (State.update props)
        (view props)

    let root, _ = Programs.runComponent(this, props.Env.Dispatcher, program)

    this.Content <- root
