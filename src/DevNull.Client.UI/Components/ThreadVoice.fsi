module DevNull.Client.Components.ThreadVoice

open Avalonia.Controls
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Commit
open DevNull.Client.Core.Http
open DevNull.Client.Natives
open DevNull.Client.SettingsStore
open Microsoft.Extensions.Logging

/// Only one note sounds at a time, the site's own rule: each claim
/// stops the playback that held the stage before it. One stage,
/// swapped whole under a lock — no fields to mutate, one record.
type Solo =
  new: unit -> Solo
  member Claim: playback: IAudioPlayback -> unit

/// Where one player's note comes from: a received clip the signed
/// URL downloads, or the staged take the recorder already holds.
type VoiceSource =
  | Remote of clip: VoiceClip
  | Staged of media: byte array * waveform: Waveform

/// Everything the voice player reads: the engine the platform head
/// passed, the transport the bytes download through, the settings
/// the volume lives in, the one-note stage, the note itself, and the
/// logger of its failures.
[<Struct>]
type VoiceCtx = {
  Audio: IAudioEngine voption
  Http: IHttp
  Settings: AppSettingsStore
  Solo: Solo
  Log: ILogger
  Source: VoiceSource
}

/// One message's voice note: the waveform player.
val voiceChip: voice: VoiceCtx -> Control
