module DevNull.Client.Components.ThreadComposer

open System
open Avalonia.Controls
open DevNull.Client.AttachmentSources
open DevNull.Client.Components.ThreadVoice
open DevNull.Client.Core.Commit
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.Natives
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
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
  Draft: IObservable<string>
  Images: IObservable<ImageAttachment list>
  LinkChoice: IObservable<LinkCard>
  Mention: IObservable<string option>
  Voice: IObservable<(ImageAttachment * Waveform) option>
  Recording: IObservable<bool>
  Actions: Actions
}

/// The composer under one thread: the typing line, the link picker,
/// the counter, the reply bar, the staged images and voice note, the
/// recording lane, and the input row with its toolbar.
val view: props: Props -> Control
