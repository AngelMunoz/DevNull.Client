module DevNull.Client.AttachmentSources

open System
open System.IO
open System.Threading.Tasks
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

/// The largest image dimension the composer stages. The server
/// re-encodes every post well under a megabyte, so a phone-camera
/// original only bloats the model, the multipart copy, and the upload.
let maxDimension = 1600

/// The staging bound: payloads this small already match what the
/// server keeps, so they pass through untouched.
let stagingBound = 1024 * 1024

/// One staged image, shrunk to the staging bound: a larger original
/// decodes down off the UI thread and re-encodes to PNG. A payload
/// that fails to decode passes through untouched; the sniffing parser
/// and the size cap still speak after it.
let shrink(attachment: ImageAttachment) : Async<ImageAttachment> =
  if attachment.Bytes.Length <= stagingBound then
    async { return attachment }
  else
    asyncEx {
      let! decoded =
        Task.Run(fun () ->
          try
            // The first decode caps the width; a portrait taller than
            // the bound needs the height cap instead.
            use wideStream = new MemoryStream(attachment.Bytes)
            let wide = Bitmap.DecodeToWidth(wideStream, maxDimension)

            if wide.PixelSize.Height <= maxDimension then
              ValueSome wide
            else
              wide.Dispose()
              use tallStream = new MemoryStream(attachment.Bytes)

              Bitmap.DecodeToHeight(tallStream, maxDimension) |> ValueSome
          with _ ->
            ValueNone)
        |> Async.AwaitTask

      match decoded with
      | ValueSome bitmap ->
        use saved = bitmap
        use buffer = new MemoryStream()
        saved.Save buffer

        return {
          attachment with
              Bytes = buffer.ToArray()
        }
      | ValueNone -> return attachment
    }

/// One picked file read to bytes. An unreadable file is skipped, so
/// one bad pick never blocks the rest.
let readOf(file: IStorageFile) : Async<ImageAttachment option> = asyncEx {
  try
    use! stream = file.OpenReadAsync()
    use buffer = new MemoryStream()
    do! stream.CopyToAsync buffer

    let! staged =
      shrink {
        FileName = file.Name
        Bytes = buffer.ToArray()
      }

    return Some staged
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
  // parser treats a paste like any other file; the staging bound then
  // shrinks a full-resolution screenshot paste like a picked one.
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

        let! staged =
          shrink {
            FileName = $"paste-{Guid.NewGuid():N}.png"
            Bytes = buffer.ToArray()
          }

        return Some staged
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
