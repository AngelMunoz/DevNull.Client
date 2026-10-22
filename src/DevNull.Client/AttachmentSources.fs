module DevNull.Client.AttachmentSources

open System
open System.IO
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Input.Platform
open Avalonia.Media.Imaging
open Avalonia.Platform.Storage
open IcedTasks
open DevNull.Client.Core.Commit

/// Worker: the two ways images enter the composer — the file picker
/// and the clipboard.
type IAttachmentSources =
  abstract PickFiles: unit -> Async<ImageAttachment list>

  abstract ReadClipboardImage: unit -> Async<ImageAttachment option>

  abstract ReadClipboardText: unit -> Async<string option>

/// Accessor for the attachment sources capability.
type IProvideAttachmentSources =
  abstract Attachments: IAttachmentSources

/// The image types the picker offers, the three the server accepts.
let imageTypes =
  FilePickerFileType(
    "Images",
    Patterns = [ "*.jpg"; "*.jpeg"; "*.png"; "*.webp" ]
  )

/// One picked file read to bytes. An unreadable file is skipped, so
/// one bad pick never blocks the rest.
let readOf(file: IStorageFile) : Async<ImageAttachment option> = asyncEx {
  try
    use! stream = file.OpenReadAsync()
    use buffer = new MemoryStream()
    do! stream.CopyToAsync buffer

    return
      Some {
        FileName = file.Name
        Bytes = buffer.ToArray()
      }
  with :? IOException ->
    return None
}

/// The desktop sources over the window the resolver finds.
let desktop(resolve: unit -> TopLevel voption) : IAttachmentSources =
  let pick(top: TopLevel) = asyncEx {
    let options = FilePickerOpenOptions(AllowMultiple = true)

    options.FileTypeFilter <- [ imageTypes ]

    let! files = top.StorageProvider.OpenFilePickerAsync options

    let! candidates = files |> Seq.map readOf |> Async.Parallel

    return candidates |> Array.choose id |> List.ofArray
  }

  // The clipboard bitmap re-encodes to PNG bytes, so the sniffing
  // parser treats a paste like any other file.
  let clipboardImage(top: TopLevel) = asyncEx {
    match top.Clipboard with
    | null -> return None
    | clipboard ->
      let! bitmap = clipboard.TryGetValueAsync(DataFormat.Bitmap)

      match bitmap with
      | null -> return None
      | pasted ->
        use buffer = new MemoryStream()
        pasted.Save buffer

        return
          Some {
            FileName = $"paste-{Guid.NewGuid():N}.png"
            Bytes = buffer.ToArray()
          }
  }

  let clipboardText(top: TopLevel) = asyncEx {
    match top.Clipboard with
    | null -> return None
    | clipboard ->
      let! text = clipboard.TryGetValueAsync(DataFormat.Text)

      return Option.ofObj text
  }

  {
    new IAttachmentSources with
      member _.PickFiles() = asyncEx {
        match resolve() with
        | ValueNone -> return []
        | ValueSome top -> return! pick top
      }

      member _.ReadClipboardImage() = asyncEx {
        match resolve() with
        | ValueNone -> return None
        | ValueSome top -> return! clipboardImage top
      }

      member _.ReadClipboardText() = asyncEx {
        match resolve() with
        | ValueNone -> return None
        | ValueSome top -> return! clipboardText top
      }
  }
