module DevNull.Client.Components.ThreadMessage

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Components.ThreadVoice
open Microsoft.Extensions.Logging

/// The services one message row draws on: the image cache for
/// pictures and avatars, the voice player's dependencies, the logger
/// of its failures, and the reading pane's live width.
[<Struct>]
type Media = {
  Cache: ImageCacheService
  Audio: IAudioEngine voption
  Http: IHttp
  Settings: AppSettingsStore
  Solo: Solo
  Log: ILogger
  PaneWidth: IObservable<float>
}

/// One reaction chip's display data: the emoji, its count, whether the
/// viewer left one, and the joined names of who did.
[<Struct>]
type ReactionChip = {
  Emoji: string
  Count: int
  Mine: bool
  Who: string
}

/// The reactions one tray offers: the quick picks on top, the ranked
/// rest under the divider.
[<Struct>]
type ReactionChoices = {
  Quick: string list
  Rest: string list
}

/// One message's pin affordance: whether it is pinned, and whether
/// the viewer may move the pin.
[<Struct>]
type PinState = { Pinned: bool; Maintainer: bool }

/// What one bubble reports up: answer it, react to it, pin or unpin
/// it, open its gallery, or walk to the message it answers.
[<Struct>]
type BubbleActions = {
  Reply: Message -> unit
  React: string -> unit
  Pin: bool -> unit
  OpenImage: Message * Control -> unit
  Walk: int -> unit
}

/// One message bubble's inputs: the message, its side, the author
/// payload the reply ghost names, the media services, and the live
/// slices the row follows.
[<Struct>]
type BubbleProps = {
  Message: Message
  Mine: bool
  ViewerId: string
  Author: User option
  Media: Media
  ShowsStatus: IObservable<bool>
  Receipt: IObservable<(bool * string) option>
  Preview: IObservable<LinkPreview option>
  Reactions: IObservable<ReactionChip list>
  ReactionChoices: IObservable<ReactionChoices>
  Pin: IObservable<PinState> voption
  Actions: BubbleActions
}

/// One carried bubble: a sent message, or one still on its way in.
type RunBubble =
  | Sent of Message
  | Staged of PendingSend

/// One author's run of bubbles: consecutive messages by one side that
/// render as one stacked group.
[<Struct>]
type Run = {
  IsMine: bool
  AuthorId: string
  Author: User option
  Bubbles: RunBubble list
}

/// One run's inputs: the run, whether the thread is an org's, the
/// viewer, the services its label draws on, the live status message,
/// the bubble factory for the messages that arrived, and the staged
/// retry.
[<Struct>]
type RunProps = {
  Run: Run
  IsOrg: bool
  ViewerId: string
  Media: Media
  Status: IObservable<Message option>
  Bubble: Message -> BubbleProps
  OnRetry: PendingSend -> unit
}

/// One message bubble: the body, its voice, images, link card, reply
/// ghost, reactions, pin, and receipts.
val bubble: props: BubbleProps -> Control

/// One author's run: the org label, the bubbles stacked, and the
/// clock line under the last sent bubble.
val run: props: RunProps -> Control

/// One quiet org event line.
val eventRow: text: string -> Control

/// One calendar-day break: the centered quiet pill with its dot.
val dayRow: label: string -> Control

/// The unread divider: the "HEAD · N new" line the head wears.
val headRow: count: int -> Control
