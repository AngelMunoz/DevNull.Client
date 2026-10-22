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

// The CLI composite lives under a name the API's commit record also
// carries; the alias keeps both in reach.
module Cli = DevNull.Client.Core.Commit.Commit

/// What the composer answers to: a fresh commit, a reply, or a quote.
[<Struct>]
type Target =
  | Fresh
  | ReplyTo of parentId: string
  | Quoting of quotedId: string

/// What one landed post answers with.
[<Struct>]
type Posted =
  | Created of Commit
  | PostedUrl of url: string

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideSshKeys> = {
  Env: 'env
  Session: SignedInSession
  Target: Target
  OnCommitted: Posted -> unit
  Register: (IDisposable -> unit) voption
}

/// The composer's state logic: model, messages, and update.
module State =

  type Model = {
    Busy: bool
    Error: AppError option
    Committed: int
    Attachments: ImageAttachment list
  }

  type Msg =
    | Submitted of body: string
    | AttachRequested
    | ImagesAdded of ImageAttachment list
    | ImageRemoved of index: int
    | Committed of Result<Commit, AppError>
    | CliPosted of Result<CommitPosted, AppError>

  let initial = {
    Busy = false
    Error = None
    Committed = 0
    Attachments = []
  }

  let apiTarget(target: Target) : Commits.Target =
    match target with
    | Fresh -> Commits.New
    | ReplyTo parentId -> Commits.ReplyTo parentId
    | Quoting quotedId -> Commits.Quote quotedId

  /// What one CLI post needs from the composer: the body, the parent
  /// it answers, and the staged images. The key file is resolved at
  /// post time, never carried here.
  [<Struct>]
  type CliPost = {
    Body: string
    ReplyTo: string option
    Images: ImageAttachment list
  }

  /// The CLI route authenticates the ssh keys on the user's GitHub
  /// account, not this app's device key: offer the local keys, keep
  /// the one the lookup claims, and sign with its private half.
  let commitViaCli<'env
    when 'env :> IProvideClock
    and 'env :> IProvideNonceSource
    and 'env :> IProvideSigner
    and 'env :> IProvideHttp
    and 'env :> IProvideSshKeys>
    (env: 'env, post: CliPost)
    : Async<Result<CommitPosted, AppError>> = asyncEx {
    let! found = Cli.findKey(env, String.concat "\n" (env.SshKeys.Candidates()))

    match found with
    | Error error -> return Error error
    | Ok keyId ->
      let keyIdText = KeyIds.value keyId

      match env.SshKeys.PrivateKeyOf keyIdText with
      | Some keyFile ->
        return!
          Cli.commit(
            env,
            {
              Body = post.Body
              ReplyTo = post.ReplyTo
              KeyFile = keyFile
              Images = post.Images
            }
          )
      | None -> return Error(MissingPrivateKey keyIdText)
  }

  let init (props: Props<'env>) () = initial, Cmd.none

  let update (props: Props<'env>) msg model =
    match msg with
    | AttachRequested ->
      // The picker is modal; the answer arrives as one message.
      model, Cmd.OfAsync.perform props.Env.Attachments.PickFiles () ImagesAdded
    | ImagesAdded staged ->
      let combined = model.Attachments @ staged

      // The same parse the CLI composite runs, at stage time: the
      // count, the size, and the content sniff speak before the post.
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
    | Submitted body ->
      { model with Busy = true; Error = None },
      (match model.Attachments with
       | [] ->
         Cmd.OfAsync.perform
           Commits.create
           (props.Env,
            {
              KeyFile = props.Session.KeyFile
              Body = body
              Target = apiTarget props.Target
            })
           Committed
       | images ->
         Cmd.OfAsync.perform
           commitViaCli
           (props.Env,
            {
              Body = body
              ReplyTo =
                match props.Target with
                | ReplyTo parentId -> Some parentId
                | _ -> None
              Images = images
            })
           CliPosted)
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
        Cmd.ofEffect(fun _ -> props.OnCommitted(Created commit))
      | Error error ->
        {
          model with
              Busy = false
              Error = Some error
        },
        Cmd.none
    | CliPosted result ->
      match result with
      | Ok posted ->
        {
          initial with
              Committed = model.Committed + 1
        },
        Cmd.ofEffect(fun _ ->
          props.OnCommitted(PostedUrl(CommitPosted.url posted)))
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
      | Quoting _ -> "Add your take"

    TextBox()
      .Name("ComposerBody")
      .PlaceholderText(placeholder)
      .AcceptsReturn(true)
      .MinHeight(44.0)
      .MaxHeight(300.0)
      .TextWrapping(TextWrapping.Wrap)

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
      .IsVisible(model |> Observable.map _.Error.IsSome)

  /// One staged image chip: the bytes decoded small, the file name,
  /// and its remove button.
  let chip
    (dispatch: Dispatch<State.Msg>, index: int, staged: ImageAttachment)
    : Control =
    // The preview decodes the staged bytes on the UI thread: staging is
    // a rare, user-driven act over at most three files.
    let preview =
      use stream = new MemoryStream(staged.Bytes)

      let bitmap = new Bitmap(stream)

      Avalonia.Controls
        .Image()
        .Source(bitmap)
        .Width(48.0)
        .Height(48.0)
        .Stretch(Stretch.UniformToFill)

    Border()
      .BorderBrush(Brushes.border)
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
  /// gives us accepts a quote with images.
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
        .IsEnabled(
          model |> Observable.map(fun current -> current.Attachments.Length < 3)
        )
        .OnClickHandler(fun _ _ -> dispatch State.AttachRequested)

  /// The action row: the attach button and the commit button docked
  /// right, the error line filling the rest.
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
        errorLine model
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let body = Parts.body props

  Parts.clearOnCommit(model, body)
  Parts.pasteInto(props, dispatch, body)

  // No box: the composer rides the rail plain, the way the site's does
  // on the log and the thread; a host that wants a card wraps it.
  StackPanel()
    .Margin(0.0, 0.0, 0.0, 12.0)
    .Spacing(8.0)
    .Children(
      body,
      Parts.attachmentStrip(model, dispatch),
      Parts.actionRow(props, model, dispatch, body)
    )

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideSshKeys>(props: Props<'env>) as this =
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
