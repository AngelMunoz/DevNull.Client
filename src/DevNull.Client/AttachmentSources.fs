module DevNull.Client.AttachmentSources

open System
open System.IO
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Input.Platform
open Avalonia.Platform.Storage
open IcedTasks
open Serilog
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

/// One picked file read to bytes. An unreadable file is skipped, so one
/// bad pick never blocks the rest. The bytes stage as picked: the
/// service re-encodes every post itself, and the commit parse caps what
/// leaves the client, so staging never decodes or re-encodes a photo.
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

  // The clipboard carries the image bytes the owning app put there, in
  // the same three types the picker offers. Asking for those bytes
  // stages a paste like a picked file: no full-resolution decode and no
  // re-encode, so a large photo costs its file size, not its pixels.
  let rawImage
    (clipboard: IClipboard, imageType: string, extension: string)
    : Async<ImageAttachment option> = asyncEx {
    let! bytes =
      clipboard.TryGetValueAsync(DataFormat.CreateBytesPlatformFormat imageType)

    return
      Option.ofObj bytes
      |> Option.map(fun payload -> {
        FileName = $"paste-{Guid.NewGuid():N}.{extension}"
        Bytes = payload
      })
  }

  /// The first image format the clipboard offers, in preference order.
  let rec firstRawImage
    (clipboard: IClipboard, formats: (string * string) list)
    : Async<ImageAttachment option> = asyncEx {
    match formats with
    | [] -> return None
    | (imageType, extension) :: rest ->
      match! rawImage(clipboard, imageType, extension) with
      | Some staged -> return Some staged
      | None -> return! firstRawImage(clipboard, rest)
  }

  // The same three types under the names each platform uses: X11 and
  // Wayland ask for MIME types, Windows registers PNG and JFIF, and
  // macOS publishes public.jpeg. A name the platform lacks answers
  // null, and the next one runs.
  let rawImageFormats: (string * string) list = [
    "image/png", "png"
    "PNG", "png"
    "image/jpeg", "jpg"
    "JFIF", "jpg"
    "public.jpeg", "jpg"
    "image/webp", "webp"
  ]

  // A platform that exposes no image bytes falls back to the decoded
  // bitmap; the bitmap owns its native pixels, so it disposes here.
  let clipboardImage(top: TopLevel) = asyncEx {
    match top.Clipboard with
    | null -> return None
    | clipboard ->
      match! firstRawImage(clipboard, rawImageFormats) with
      | Some staged -> return Some staged
      | None ->
        Log.Warning(
          "The clipboard offers no raw image bytes; falling back to a decoded bitmap."
        )

        let! decoded = clipboard.TryGetValueAsync(DataFormat.Bitmap)

        match decoded with
        | null -> return None
        | pasted ->
          use bitmap = pasted
          use buffer = new MemoryStream()
          bitmap.Save buffer

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
