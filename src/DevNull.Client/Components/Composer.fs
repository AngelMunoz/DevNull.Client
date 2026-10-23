module DevNull.Client.Components.Composer

open System
open System.IO
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Interactivity
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open IcedTasks
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Commit
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types
open DevNull.Client.Errors
open DevNull.Client.Icons
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.Theme

/// What the composer answers to: a fresh commit, a reply, or a quote.
[<Struct>]
type Target =
  | Fresh
  | ReplyTo of parentId: string
  | Quoting of quotedId: string

/// One poll being built: two to four options and the time it stays
/// open. A poll rides a fresh commit only, never beside images.
type PollDraft = {
  Options: string list
  Duration: PollDuration
}

/// Where a host whose row recycles keeps the draft alive: the text the
/// composer starts from, and the sink every change reaches.
type DraftSource = {
  Seed: string
  Changed: string -> unit
}

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideAttachmentSources> = {
  Env: 'env
  Session: SignedInSession
  Target: Target
  OnCommitted: Commit -> unit
  Register: (IDisposable -> unit) voption
  Draft: DraftSource voption
}

/// The composer's state logic: model, messages, and update.
module State =

  type Model = {
    Busy: bool
    Error: AppError option
    Committed: int
    Body: string
    Attachments: ImageAttachment list
    Poll: PollDraft option
  }

  type Msg =
    | Submitted of body: string
    | BodyChanged of text: string
    | AttachRequested
    | ImagesAdded of ImageAttachment list
    | ImageRemoved of index: int
    | PollToggled
    | PollOptionChanged of index: int * text: string
    | PollOptionAdded
    | PollOptionRemoved of index: int
    | PollDurationPicked of PollDuration
    | Committed of Result<Commit, AppError>

  let initial = {
    Busy = false
    Error = None
    Committed = 0
    Body = ""
    Attachments = []
    Poll = None
  }

  let apiTarget(target: Target) : Commits.Target =
    match target with
    | Fresh -> Commits.New
    | ReplyTo parentId -> Commits.ReplyTo parentId
    | Quoting quotedId -> Commits.Quote quotedId

  let init (props: Props<'env>) () =
    let seeded =
      match props.Draft with
      | ValueSome draft -> draft.Seed
      | ValueNone -> ""

    { initial with Body = seeded }, Cmd.none

  let update (props: Props<'env>) msg (model: Model) =
    match msg with
    | BodyChanged text ->
      let changed =
        match props.Draft with
        | ValueSome draft -> Cmd.ofEffect(fun _ -> draft.Changed text)
        | ValueNone -> Cmd.none

      { model with Body = text }, changed
    | AttachRequested ->
      // The picker is modal; the answer arrives as one message.
      model, Cmd.OfAsync.perform props.Env.Attachments.PickFiles () ImagesAdded
    | ImagesAdded staged ->
      let combined = model.Attachments @ staged

      // The same parse the post runs, at stage time: the count, the
      // size, and the content sniff speak before the post.
      match CommitImages.parse combined with
      | Ok _ -> { model with Attachments = combined }, Cmd.none
      | Error error -> { model with Error = Some error }, Cmd.none
    | ImageRemoved index ->
      let attachments =
        model.Attachments
        |> List.indexed
        |> List.filter(fun (at, _) -> at <> index)
        |> List.map snd

      { model with Attachments = attachments }, Cmd.none
    | PollToggled ->
      // A poll rides a fresh commit with no images; opening one puts
      // the staged images back.
      let poll =
        match model.Poll with
        | Some _ -> None
        | None ->
          Some {
            Options = [ ""; "" ]
            Duration = PollDuration.OneDay
          }

      {
        model with
            Poll = poll
            Attachments = []
      },
      Cmd.none
    | PollOptionChanged(index, text) ->
      let changed =
        model.Poll
        |> Option.map(fun poll ->
          let options =
            poll.Options
            |> List.indexed
            |> List.map(fun (at, optionText) ->
              if at = index then text else optionText)

          { poll with Options = options })

      { model with Poll = changed }, Cmd.none
    | PollOptionAdded ->
      let changed =
        model.Poll
        |> Option.bind(fun poll ->
          if List.length poll.Options >= 4 then
            None
          else
            Some {
              poll with
                  Options = poll.Options @ [ "" ]
            })

      { model with Poll = changed }, Cmd.none
    | PollOptionRemoved index ->
      let changed =
        model.Poll
        |> Option.bind(fun poll ->
          if List.length poll.Options <= 2 then
            None
          else
            Some {
              poll with
                  Options =
                    poll.Options
                    |> List.indexed
                    |> List.filter(fun (at, _) -> at <> index)
                    |> List.map snd
            })

      { model with Poll = changed }, Cmd.none
    | PollDurationPicked duration ->
      let changed =
        model.Poll |> Option.map(fun poll -> { poll with Duration = duration })

      { model with Poll = changed }, Cmd.none
    | Submitted body ->
      // The poll speaks at the post: the same parse the server runs,
      // before the request leaves.
      let pollSpec: Result<PollSpec option, AppError> =
        match model.Poll with
        | None -> Ok None
        | Some draft ->
          let trimmed =
            draft.Options
            |> List.map _.Trim()
            |> List.filter(fun option -> option <> "")

          PollSpecs.parse(trimmed, draft.Duration) |> Result.map Some

      match pollSpec with
      | Error error ->
        // The refusal keeps the box open with what was typed.
        { model with Error = Some error }, Cmd.none
      | Ok spec ->
        { model with Busy = true; Error = None },
        Cmd.OfAsync.perform
          Commits.create
          (props.Env,
           {
             KeyFile = props.Session.KeyFile
             Body = body
             Target = apiTarget props.Target
             Images = model.Attachments
             Poll = spec
           })
          Committed
    | Committed result ->
      match result with
      | Ok commit ->
        // The host's callback (often a navigation) is a command, never
        // an inline effect: running it inside update can dispose this
        // very program before the loop publishes the model.
        {
          initial with
              Committed = model.Committed + 1
        },
        Cmd.ofEffect(fun _ -> props.OnCommitted commit)
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
  /// inner presenter takes over past the cap. The text rides the
  /// model: the one-way seed answers a recycled row's rebuild, and
  /// every change reports back, so the draft outlives the box.
  let body
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : TextBox =
    let placeholder =
      match props.Target with
      | Fresh -> "What did you ship?"
      | ReplyTo _ -> "Write your reply…"
      | Quoting _ -> "Add your take"

    let box =
      TextBox()
        .Name("ComposerBody")
        .PlaceholderText(placeholder)
        .AcceptsReturn(true)
        .MinHeight(44.0)
        .MaxHeight(300.0)
        .TextWrapping(TextWrapping.Wrap)

    box.Text(
      model
      |> Observable.map _.Body
      |> Observable.distinctUntilChanged
      |> _.ToBinding()
    )
    |> ignore

    box.TextChanged.Add(fun _ ->
      let text =
        match box.Text with
        | null -> ""
        | typed -> typed

      dispatch(State.BodyChanged text))

    box

  /// The paste lane: the platform's paste gesture asks the attachment
  /// sources first — a clipboard image stages, clipboard text lands in
  /// the box. The gesture is always handled, so the box's own text
  /// paste never races the image read.
  let pasteInto
    (props: Props<'env>, dispatch: Dispatch<State.Msg>, body: TextBox)
    : unit =
    let gestureMatches(args: KeyEventArgs) =
      match Application.Current with
      | null -> false
      | app ->
        match app.PlatformSettings with
        | null -> false
        | settings ->
          settings.HotkeyConfiguration.Paste
          |> Seq.exists(fun gesture -> gesture.Matches args)

    body.AddHandler(
      InputElement.KeyDownEvent,
      (fun _ args ->
        if gestureMatches args then
          args.Handled <- true

          asyncEx {
            let! image = props.Env.Attachments.ReadClipboardImage()

            match image with
            | Some staged ->
              Dispatcher.UIThread.Post(fun () ->
                dispatch(State.ImagesAdded [ staged ]))
            | None ->
              let! text = props.Env.Attachments.ReadClipboardText()

              match text with
              | Some pasted ->
                Dispatcher.UIThread.Post(fun () -> body.SelectedText <- pasted)
              | None -> ()
          }
          |> Async.StartImmediate),
      RoutingStrategies.Tunnel
    )

  /// Every landed commit leaves the box empty for the next one: the
  /// model resets its body, and the box's one-way text binding follows.
  /// The error line under the input.
  let errorLine(model: IObservable<State.Model>) : Control =
    TextBlock()
      .TextWrapping(TextWrapping.Wrap)
      .Classes(Theme.name Theme.FgDanger)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Error |> Option.map describe |> Option.defaultValue "")
      )
      .IsVisible(model |> Observable.map _.Error.IsSome)

  /// One staged image chip: the bytes decoded small, the file name,
  /// and its remove button.
  let chip
    (dispatch: Dispatch<State.Msg>, index: int, staged: ImageAttachment)
    : Control =
    // The preview decodes the staged bytes on the UI thread: staging is
    // a rare, user-driven act over at most three files. The decoded
    // bitmap is native memory, so the chip image disposes it when the
    // strip rebuilds and drops it.
    let bitmap =
      use stream = new MemoryStream(staged.Bytes)
      new Bitmap(stream)

    let preview =
      Avalonia.Controls
        .Image()
        .Source(bitmap)
        .Width(48.0)
        .Height(48.0)
        .Stretch(Stretch.UniformToFill)

    preview.DetachedFromVisualTree.Add(fun _ -> bitmap.Dispose())

    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .Padding(6.0)
      .Child(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            preview,
            StackPanel()
              .VerticalAlignment(VerticalAlignment.Center)
              .Children(
                TextBlock()
                  .Classes(Theme.name Theme.Mono)
                  .Text(staged.FileName)
                  .MaxWidth(140.0)
                  .TextTrimming(TextTrimming.CharacterEllipsis),
                Button()
                  .Name($"RemoveImage{index}")
                  .Classes(Theme.name Theme.GhostButton)
                  .Padding(4.0, 0.0, 4.0, 0.0)
                  .Content("remove")
                  .OnClickHandler(fun _ _ -> dispatch(State.ImageRemoved index))
              )
          )
      )

  /// The staged strip above the action row.
  let attachmentStrip
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    ContentControl()
      .Content(
        model
        |> Observable.map(fun current ->
          match current.Attachments with
          | [] -> Panel() :> Control
          | staged ->
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(6.0)
              .Children(
                [|
                  for index, image in List.indexed staged ->
                    chip(dispatch, index, image)
                |]
              )
            :> Control)
        |> _.ToBinding()
      )

  /// The attach button: the image glyph, a live count, disabled past
  /// three staged files. A quote target hides it — no route the server
  /// gives us accepts a quote with images — and an open poll hides it
  /// too: the server takes a poll or images, never both.
  let attachButton
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let count =
      model |> Observable.map(fun current -> $"{current.Attachments.Length}/3")

    match props.Target with
    | Quoting _ -> Panel() :> Control
    | _ ->
      Button()
        .Name("AttachButton")
        .Classes(Theme.name Theme.GhostButton)
        .Padding(6.0, 2.0, 6.0, 2.0)
        .Content(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(4.0)
            .Children(
              Icons.iconAt(Glyph.Image, 16.0),
              TextBlock().Classes(Theme.name Theme.Mono).Text(count)
            )
        )
        .IsVisible(model |> Observable.map(fun current -> current.Poll.IsNone))
        .IsEnabled(
          model |> Observable.map(fun current -> current.Attachments.Length < 3)
        )
        .OnClickHandler(fun _ _ -> dispatch State.AttachRequested)

  /// The poll button: opens the builder. A poll rides a fresh commit
  /// with no images, so replies, quotes, and staged images hide it.
  let pollButton
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let offered =
      model
      |> Observable.map(fun current ->
        match props.Target with
        | Fresh -> current.Attachments.IsEmpty
        | _ -> false)

    Button()
      .Name("PollButton")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(6.0, 2.0, 6.0, 2.0)
      .Content(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(4.0)
          .Children(
            Icons.iconAt(Glyph.Poll, 16.0),
            TextBlock().Classes(Theme.name Theme.Mono).Text("poll")
          )
      )
      .IsVisible(offered)
      .OnClickHandler(fun _ _ -> dispatch State.PollToggled)

  /// One option box of the poll builder: the text rides the model the
  /// way the body does, so the structure rebuilds only when the option
  /// count moves.
  let pollOptionBox
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>, index: int)
    : Control =
    let optionText(current: State.Model) : string =
      current.Poll
      |> Option.bind(fun poll -> List.tryItem index poll.Options)
      |> Option.defaultValue ""

    let box =
      TextBox()
        .Name($"PollOption{index}")
        .PlaceholderText($"Option {index + 1}")
        .MinHeight(32.0)

    box.Text(
      model
      |> Observable.map optionText
      |> Observable.distinctUntilChanged
      |> _.ToBinding()
    )
    |> ignore

    box.TextChanged.Add(fun _ ->
      let text =
        match box.Text with
        | null -> ""
        | typed -> typed

      dispatch(State.PollOptionChanged(index, text)))

    box

  /// The row of one poll option: its box and, past the first two, its
  /// remove button.
  let pollOptionRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>, index: int)
    : Control =
    DockPanel()
      .LastChildFill(true)
      .Children(
        Button()
          .Name($"PollOptionRemove{index}")
          .Classes(Theme.name Theme.GhostButton)
          .Padding(4.0, 0.0, 4.0, 0.0)
          .VerticalAlignment(VerticalAlignment.Center)
          .Content("remove")
          .IsVisible(
            model
            |> Observable.map(fun current ->
              current.Poll
              |> Option.map(fun poll -> List.length poll.Options > 2)
              |> Option.defaultValue false)
          )
          .DockRight()
          .OnClickHandler(fun _ _ -> dispatch(State.PollOptionRemoved index)),
        pollOptionBox(model, dispatch, index)
      )

  /// One duration pill of the picker; the picked one carries the
  /// active look.
  let durationPill
    (
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      duration: PollDuration
    ) : Control =
    let label =
      match duration with
      | OneHour -> "1 hour"
      | OneDay -> "1 day"
      | ThreeDays -> "3 days"
      | SevenDays -> "7 days"

    let picked =
      model
      |> Observable.map(fun current ->
        current.Poll
        |> Option.map(fun poll -> poll.Duration = duration)
        |> Option.defaultValue false)
      |> Observable.distinctUntilChanged

    let pill =
      Button()
        .Classes(Theme.name Theme.FilterPill)
        .Padding(8.0, 2.0, 8.0, 2.0)
        .Content(label)
        .OnClickHandler(fun _ _ -> dispatch(State.PollDurationPicked duration))

    Theme.bindClass(pill, Theme.FilterPillActive, picked)

    pill

  /// The poll builder under the body: the option rows, the add button
  /// under four, the duration picker, and the close that puts it away.
  /// The structure rebuilds only when the option count moves, so the
  /// boxes keep their carets while typing.
  let pollPanel
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let durations: PollDuration list = [ OneHour; OneDay; ThreeDays; SevenDays ]

    let built(count: int) : Control =
      match count with
      | 0 -> Panel() :> Control
      | _ ->
        let children: Control array = [|
          for index in 0 .. count - 1 do
            pollOptionRow(model, dispatch, index)

          Button()
            .Name("PollOptionAdd")
            .Classes(Theme.name Theme.GhostButton)
            .Padding(6.0, 2.0, 6.0, 2.0)
            .HorizontalAlignment(HorizontalAlignment.Left)
            .Content("Add option")
            .IsVisible(
              model
              |> Observable.map(fun current ->
                current.Poll
                |> Option.map(fun poll -> List.length poll.Options < 4)
                |> Option.defaultValue false)
            )
            .OnClickHandler(fun _ _ -> dispatch State.PollOptionAdded)

          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(6.0)
            .Children(
              [|
                for duration in durations ->
                  durationPill(model, dispatch, duration)
              |]
            )

          Button()
            .Name("PollClose")
            .Classes(Theme.name Theme.GhostButton)
            .Padding(6.0, 2.0, 6.0, 2.0)
            .HorizontalAlignment(HorizontalAlignment.Left)
            .Content("Remove poll")
            .OnClickHandler(fun _ _ -> dispatch State.PollToggled)
        |]

        StackPanel().Spacing(6.0).Children(children) :> Control

    ContentControl()
      .Content(
        model
        |> Observable.map(fun current ->
          current.Poll
          |> Option.map(fun poll -> List.length poll.Options)
          |> Option.defaultValue 0)
        |> Observable.distinctUntilChanged
        |> Observable.map built
        |> _.ToBinding()
      )

  /// The action row: the attach and poll buttons and the commit button
  /// docked right, the error line filling the rest.
  let actionRow
    (
      props: Props<'env>,
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
        attachButton(props, model, dispatch),
        pollButton(props, model, dispatch),
        errorLine model
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let body = Parts.body(props, model, dispatch)

  Parts.pasteInto(props, dispatch, body)

  // No box: the composer rides the rail plain, the way the site's does
  // on the log and the thread; a host that wants a card wraps it.
  StackPanel()
    .Margin(0.0, 0.0, 0.0, 12.0)
    .Spacing(8.0)
    .Children(
      body,
      Parts.attachmentStrip(model, dispatch),
      Parts.pollPanel(model, dispatch),
      Parts.actionRow(props, model, dispatch, body)
    )

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideAttachmentSources>(props: Props<'env>) as this =
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
