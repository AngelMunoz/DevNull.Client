module DevNull.Client.AttachmentSources

open Avalonia.Controls
open DevNull.Client.Core.Commit

/// Worker: the two ways images enter the composer — the file picker
/// and the clipboard. The composer never touches the storage provider
/// or the clipboard itself, so tests inject a fake.
type IAttachmentSources =
  /// Opens the picker; the picked files read to bytes. A cancelled
  /// picker or a missing window answers an empty list.
  abstract PickFiles: unit -> Async<ImageAttachment list>

  /// One image from the clipboard, when the clipboard carries one.
  abstract ReadClipboardImage: unit -> Async<ImageAttachment option>

  /// The clipboard's text, when it carries text.
  abstract ReadClipboardText: unit -> Async<string option>

/// Accessor for the attachment sources capability.
type IProvideAttachmentSources =
  abstract Attachments: IAttachmentSources

/// The desktop sources over the window the resolver finds: the
/// storage provider's picker and the clipboard's universal formats.
val desktop: resolve: (unit -> TopLevel voption) -> IAttachmentSources
