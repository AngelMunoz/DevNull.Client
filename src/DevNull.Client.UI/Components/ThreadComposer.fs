module DevNull.Client.Components.ThreadComposer

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Shapes
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components.Palette
open DevNull.Client.Components.ThreadRecorder
open DevNull.Client.Components.ThreadVoice
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Commit
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Icons
open DevNull.Client.Natives
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Theme
open Microsoft.Extensions.Logging

/// The services one thread composer draws on: the viewer the mention
/// list leaves out, the attachment sources the paste gesture asks, and
/// the voice player's dependencies.
[<Struct>]
type Services = {
  ViewerId: string
  Attachments: IAttachmentSources
  Audio: IAudioEngine voption
  Http: IHttp
  Settings: AppSettingsStore
  Solo: Solo
  Log: ILogger
}

/// What the composer reports up: the draft and caret, the staged
/// images, the link choice, the reply and send asks, the picker and
/// clipboard, the recording lane, and the ping.
[<Struct>]
type Actions = {
  DraftChanged: string * int -> unit
  ImageRemoved: int -> unit
  LinkChosen: LinkCard -> unit
  ReplyCancelled: unit -> unit
  Send: unit -> unit
  PickImages: unit -> unit
  Picked: ImageAttachment list -> unit
  Record: unit -> unit
  VoiceStaged: Result<(ImageAttachment * Waveform), AppError> -> unit
  RecordCancelled: unit -> unit
  VoiceDiscarded: unit -> unit
  Ping: unit -> unit
}

/// The composer's inputs: its services and target, the live thread
/// and the model slices its controls follow, and the actions it
/// reports.
[<Struct>]
type Props = {
  Services: Services
  Target: ChatTarget
  Thread: IObservable<ThreadState>
  Typing: IObservable<string list>
  Draft: IObservable<string>
  Images: IObservable<ImageAttachment list>
  LinkChoice: IObservable<LinkCard>
  Mention: IObservable<string option>
  Voice: IObservable<(ImageAttachment * Waveform) option>
  Recording: IObservable<bool>
  Actions: Actions
}

/// Whether the target is an org thread.
let inline isOrg(target: ChatTarget) =
  match target with
  | Org _ -> true
  | Partner _ -> false

/// The word the input box carries while the draft is empty.
let placeholderOf(target: ChatTarget) =
  match target with
  | Partner partner -> $"Message @{partner.Handle}"
  | Org org ->
    let orgName = org.Name |> Option.defaultValue "the org"
    $"Message {orgName}"

/// The ping tooltip's word for the target.
let pingLabelOf(target: ChatTarget) =
  match target with
  | Partner partner -> $"Ping @{partner.Handle}"
  | Org org ->
    let orgName = org.Name |> Option.defaultValue "the org"
    $"Ping {orgName}"

/// The link-card picker: a quiet button whose flyout lists the
/// draft's links, the first one, or none. The button shows only while
/// the draft carries a link.
let linkPicker(props: Props) : Control =
  let links = props.Draft |> Observable.map RemotesText.linksOf

  let label =
    Observable.combineLatest links props.LinkChoice
    |> Observable.map(fun (found, chosen) ->
      match chosen with
      | LinkCard.CardOf url when found |> List.contains url ->
        RemotesText.plainLink url
      | LinkCard.NoCard -> "no card"
      | _ -> if found.IsEmpty then "no links" else "first link")

  let rows =
    Observable.combineLatest links props.LinkChoice
    |> Observable.map(fun (found, _) -> [
      "first link", LinkCard.FirstLink

      for url in found do
        RemotesText.plainLink url, LinkCard.CardOf url

      "no card", LinkCard.NoCard
    ])
    |> Observable.distinctUntilChanged

  let host =
    ContentControl()
      .Content(
        rows
        |> Observable.map(fun picks ->
          StackPanel().Children [|
            for label', pick in picks ->
              (Button()
                .Classes(Theme.name Theme.GhostButton)
                .Padding(6.0, 2.0, 6.0, 2.0)
                .FontSize(12.0)
                .Content(label')
                .OnClickHandler(fun _ args ->
                  args.Handled <- true
                  props.Actions.LinkChosen pick)
              :> Control)
          |])
        |> _.ToBinding()
      )

  let flyout =
    Popup()
      .Child(
        Border()
          .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
          .BorderThickness(1.0)
          .CornerRadius(6.0)
          .Padding(4.0)
          .Child(host)
      )

  let quiet =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(6.0, 0.0, 6.0, 0.0)
      .FontSize(11.0)
      .Content(label |> Observable.distinctUntilChanged |> _.ToBinding())
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        flyout.IsOpen <- not flyout.IsOpen)

  ToolTip.SetTip(quiet, "Choose which link gets a card")

  Panel()
    .Children(quiet, flyout)
    .HorizontalAlignment(HorizontalAlignment.Right)
    .IsVisible(links |> Observable.map(fun found -> not found.IsEmpty))

/// The typing line: the meta row's leading word while the partner or
/// the org types, its three dots chasing each other on the theme's own
/// beat.
let typingLine(props: Props) : Control =
  let names = props.Typing |> Observable.distinctUntilChanged

  let dot(klass: string) =
    Ellipse()
      .Width(3.0)
      .Height(3.0)
      .Classes(klass)
      .VerticalAlignment(VerticalAlignment.Center)

  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(4.0)
    .VerticalAlignment(VerticalAlignment.Center)
    .Children(
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(2.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Children(
          dot(Theme.name Theme.TypingDot1),
          dot(Theme.name Theme.TypingDot2),
          dot(Theme.name Theme.TypingDot3)
        ),
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .FontSize(11.0)
        .Text(names |> Observable.map RemotesText.typingText)
    )
    .IsVisible(names |> Observable.map(List.isEmpty >> not))

/// The character counter: quiet until 80%, amber from 95%, danger at
/// the limit, thousands grouped the way the site reads them.
let counterBlock(draft: IObservable<string>) : Control =
  let counter = draft |> Observable.map(fun text -> text.TrimEnd().Length)

  let block =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .FontSize(11.0)
      .Text(
        counter
        |> Observable.map(fun length ->
          FormattableString.Invariant $"""{length:N0}/2,000""")
      )
      .IsVisible(counter |> Observable.map(fun length -> length >= 1600))

  let warning =
    Theme.bindClass(
      block,
      Theme.FgWarning,
      counter |> Observable.map(fun length -> length >= 1900 && length < 2000)
    )

  let danger =
    Theme.bindClass(
      block,
      Theme.FgDanger,
      counter |> Observable.map(fun length -> length >= 2000)
    )

  block.DetachedFromVisualTree.Add(fun _ ->
    warning.Dispose()
    danger.Dispose())

  block

/// The reply bar: the parent's body and the cancel that drops it.
let replyBar(props: Props) : Control =
  ContentControl()
    .Content(
      PageChrome.rebuildOn(
        props.Thread,
        (fun current -> current.ReplyingTo),
        fun current ->
          match current.ReplyingTo with
          | Some parent ->
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(8.0)
              .Children(
                TextBlock()
                  .Classes(Theme.name Theme.Muted)
                  .FontSize(12.0)
                  .Text($"in reply to: {parent.Body}")
                  .TextTrimming(TextTrimming.CharacterEllipsis),
                Button()
                  .Classes(Theme.name Theme.GhostButton)
                  .Padding(4.0, 0.0, 4.0, 2.0)
                  .FontSize(11.0)
                  .Content("cancel")
                  .OnClickHandler(fun _ args ->
                    args.Handled <- true
                    props.Actions.ReplyCancelled())
              )
            :> Control
          | None -> Panel()
      )
      |> _.ToBinding()
    )

/// The staged images: the shared strip rebuilds only when the image
/// list itself moves, so typing never re-decodes a preview under the
/// caret.
let stagedImages(props: Props) : Control =
  AttachmentStrip.view {
    Images = props.Images
    OnRemove = props.Actions.ImageRemoved
    MarkHandled = true
    Descriptions = ValueNone
    Spacing = 8.0
    HideWhenEmpty = true
  }

/// The staged voice note: the full player over the take just made,
/// with its discard beside it. The take is keyed by its length and
/// name, never by comparing the bytes themselves.
let stagedVoice(props: Props) : Control =
  ContentControl()
    .Content(
      props.Voice
      |> Observable.map(fun current ->
        current
        |> Option.map(fun (attachment, bars) ->
          (attachment.FileName, attachment.Bytes.Length), (attachment, bars)))
      |> Observable.distinctUntilChanged
      |> Observable.map(function
        | Some(_, (attachment, bars)) ->
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(
              voiceChip {
                Audio = props.Services.Audio
                Http = props.Services.Http
                Settings = props.Services.Settings
                Solo = props.Services.Solo
                Log = props.Services.Log
                Source = Staged(attachment.Bytes, bars)
              },
              Button()
                .Classes(Theme.name Theme.GhostButton)
                .Padding(4.0)
                .Content(Icons.iconAt(Glyph.Trash, 14.0))
                .VerticalAlignment(VerticalAlignment.Center)
                .OnClickHandler(fun _ args ->
                  args.Handled <- true
                  props.Actions.VoiceDiscarded())
            )
          :> Control
        | None -> TextBlock() :> Control)
      |> _.ToBinding()
    )
    .IsVisible(props.Voice |> Observable.map(fun current -> current.IsSome))

/// The recording lane: the bordered box that stands in for the input
/// row while the take is live. The lane reports through the
/// composer's actions.
let recordLane(props: Props) : Control =
  let report(outcome: Recorder.Report) =
    match outcome with
    | Recorder.Staged staged -> props.Actions.VoiceStaged staged
    | Recorder.Cancel -> props.Actions.RecordCancelled()

  ContentControl()
    .Name("RemotesRecord")
    .Content(
      props.Recording
      |> Observable.distinctUntilChanged
      |> Observable.map(fun recording ->
        if recording then
          RecordLane(props.Services.Audio, report) :> Control
        else
          null)
      |> _.ToBinding()
    )
    .IsVisible(props.Recording)

/// Who a mention can name here: the partner alone, or the org's
/// members beside the viewer.
let mentionables(props: Props, current: ThreadState) : User list =
  match props.Target with
  | Partner partner -> [ partner ]
  | Org _ ->
    current.Members
    |> List.map _.User
    |> List.filter(fun user -> user.Id <> props.Services.ViewerId)

/// The mention offers: the partner or org members whose handle starts
/// with the typed word, through the shared palette.
let mentionPicker(props: Props, input: TextBox) : Control =
  let picks =
    Observable.combineLatest props.Mention props.Thread
    |> Observable.map(fun (typed, current) ->
      typed
      |> Option.map(fun prefix ->
        mentionables(props, current)
        |> List.filter(fun user ->
          user.Handle.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        |> List.truncate 6)
      |> Option.defaultValue [])
    |> Observable.distinctUntilChanged

  let applyMention(handle: string) =
    let typed = input.Text |> Option.ofObj |> Option.defaultValue ""

    let next, caret =
      RemotesText.mentionCompleted(typed, input.CaretIndex, handle)

    input.Text <- next
    input.CaretIndex <- caret
    props.Actions.DraftChanged(next, caret)

  let pick(user: User) : RenderedRow =
    let button =
      Button()
        .Name("MentionPick")
        .Classes(Theme.name Theme.GhostButton)
        .Padding(8.0, 4.0, 8.0, 4.0)
        .FontSize(12.0)
        .HorizontalContentAlignment(HorizontalAlignment.Left)
        .Content(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(6.0)
            .Children(
              TextBlock()
                .Classes(Theme.name Theme.Mono)
                .FontSize(12.0)
                .Text("@" + user.Handle),
              TextBlock()
                .Classes(Theme.name Theme.Muted)
                .FontSize(12.0)
                .Text(user.Name)
            )
        )
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          applyMention user.Handle)

    {
      Leading = ValueNone
      Control = button
      Followed = ValueNone
    }

  let list =
    Palette.view(
      {
        Name = "MentionScroller"
        MaxHeight = 320.0
      },
      {
        Rows = picks
        KeyOf = fun user -> user.Handle
        Render = pick
        Active = Observable.single(-1)
        OnHover = ValueNone
      }
    )

  Border()
    .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
    .BorderThickness(1.0)
    .CornerRadius(6.0)
    .Padding(4.0)
    .MaxWidth(280.0)
    .HorizontalAlignment(HorizontalAlignment.Left)
    .Child(list)
    .IsVisible(picks |> Observable.map(List.isEmpty >> not))

/// The input box: the draft's text and caret, the send and cancel
/// keys, and the platform's paste gesture.
let inputBox(props: Props) : TextBox =
  let voiceRides = props.Voice |> Observable.map(fun current -> current.IsSome)

  let input =
    TextBox()
      .Name("RemotesComposer")
      .PlaceholderText(placeholderOf props.Target)
      .MinHeight(40.0)
      .MaxHeight(160.0)
      .AcceptsReturn(true)
      .TextWrapping(TextWrapping.Wrap)
      // The site's own rule: a voice note rides alone, so its draft
      // is closed to typing while one is staged.
      .IsEnabled(voiceRides |> Observable.map not)
      .Text(props.Draft |> Observable.distinctUntilChanged |> _.ToBinding())
      .OnTextChangedHandler(fun box _ ->
        let text = box.Text |> Option.ofObj |> Option.defaultValue ""

        props.Actions.DraftChanged(text, box.CaretIndex))
      // The caret is the mention's own place: a move inside the text
      // re-reads what stands at the caret, the way typing does.
      .OnCaretIndexChanged(fun box _ ->
        let text = box.Text |> Option.ofObj |> Option.defaultValue ""

        props.Actions.DraftChanged(text, box.CaretIndex))

  // The site's fine-pointer rule, through the tunnel so the box's own
  // key handling never eats it: Enter pushes, Shift+Enter makes the
  // newline, Esc cancels the reply or discards a live recording, and
  // the platform's paste gesture asks the attachment sources first — a
  // clipboard image stages, clipboard text lands in the box.
  ComposerKeys.attach {
    Body = input
    Attachments = props.Services.Attachments
    Commit = ComposerKeys.PlainOrControl
    OnCommit = props.Actions.Send
    OnStaged = fun staged -> props.Actions.Picked [ staged ]
    OnEscape =
      ValueSome(fun () ->
        props.Actions.ReplyCancelled()
        props.Actions.RecordCancelled())
  }

  input

/// The attach button: images never ride beside a staged voice note.
let pickButton(props: Props) : Control =
  let voiceRides = props.Voice |> Observable.map(fun current -> current.IsSome)

  let button =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(6.0)
      .Content(Icons.iconAt(Glyph.Image, 16.0))
      .IsEnabled(voiceRides |> Observable.map not)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        props.Actions.PickImages())

  ToolTip.SetTip(button, "Attach images")
  button

/// The push button.
let sendButton(props: Props) : Control =
  Button()
    .Classes(Theme.name Theme.AccentButton)
    .MinWidth(72.0)
    .Content("Push")
    .OnClickHandler(fun _ args ->
      args.Handled <- true
      props.Actions.Send())

/// The mic button: it stays in place and dims while a draft or images
/// ride the push, saying why.
let micButton(props: Props) : Control =
  let free =
    Observable.combineLatest
      (props.Voice |> Observable.map(fun current -> current.IsNone))
      (props.Images |> Observable.map(fun images -> images.IsEmpty))
    |> Observable.map(fun (voiceFree, imagesFree) -> voiceFree && imagesFree)

  let button =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(6.0)
      .Content(Icons.iconAt(Glyph.Mic, 16.0))
      .IsEnabled(
        Observable.combineLatest
          free
          (props.Draft |> Observable.map(fun text -> text.Trim() = ""))
        |> Observable.map(fun (free', quiet) -> free' && quiet)
      )
      .IsVisible(props.Services.Audio |> ValueOption.isSome)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        props.Actions.Record())

  ToolTip.SetTip(button, "Record a voice note (max. 1 min)")
  button

/// The ping button: a quiet nudge for the partner or the org.
let pingButton(props: Props) : Control =
  let button =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(6.0)
      .Content(Icons.iconAt(Glyph.Vibrate, 16.0))
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        props.Actions.Ping())

  ToolTip.SetTip(button, pingLabelOf props.Target)
  button

let view(props: Props) : Control =
  let input = inputBox props

  let metaRow =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(8.0)
      .Children(typingLine props, linkPicker props, counterBlock props.Draft)

  let inputRow =
    DockPanel()
      .LastChildFill(true)
      .Children(
        (pickButton props)
          .Dock(Dock.Left)
          .Margin(0.0, 0.0, 8.0, 0.0)
          .VerticalAlignment(VerticalAlignment.Center),
        (micButton props)
          .Dock(Dock.Left)
          .Margin(0.0, 0.0, 8.0, 0.0)
          .VerticalAlignment(VerticalAlignment.Center),
        (pingButton props)
          .Dock(Dock.Left)
          .Margin(0.0, 0.0, 8.0, 0.0)
          .VerticalAlignment(VerticalAlignment.Center),
        (sendButton props)
          .Dock(Dock.Right)
          .Margin(8.0, 0.0, 0.0, 0.0)
          .VerticalAlignment(VerticalAlignment.Center),
        input
      )
      .IsVisible(props.Recording |> Observable.map not)

  StackPanel()
    .Spacing(6.0)
    .Margin(0.0, 8.0, 0.0, 8.0)
    .Children(
      mentionPicker(props, input),
      metaRow,
      replyBar props,
      stagedImages props,
      stagedVoice props,
      recordLane props,
      inputRow
    )
