module DevNull.Client.Components.AsyncImage

open System.IO
open System.Threading
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Threading
open NXUI.Extensions
open IcedTasks
open DevNull.Client.ImageCache

[<Struct>]
type Props = {
  Cache: ImageCacheService
  Url: string
  DecodeWidth: int
  /// Decodes by height instead of width: the fixed-height cells of the
  /// feed and the viewer's viewport fit their images this way, and a
  /// tall photo costs its displayed pixels, not its full frame.
  DecodeHeight: int voption
  Stretch: Stretch
  Placeholder: Control voption
  ToolTip: string voption
}

// The loader is the one custom control of the app: a plain `Image`
// needs a ready bitmap, and the feed recycles rows, so the load needs
// a control-owned lifetime. The URL is fixed at construction because
// every feed row rebuilds its content per item; no property system
// machinery is needed for that. The decoded bitmap is native memory
// the GC cannot weigh, so the control owns it: the previous bitmap
// disposes on replace, the shown one on detach.
type Loader(props: Props) as this =
  inherit ContentControl()

  let placeholder: Control =
    match props.Placeholder with
    | ValueSome control -> control
    | ValueNone -> Panel() :> Control

  // The load's token source is the control's lifecycle state, the one
  // mutable field; attach and detach own it, both on the UI thread.
  let mutable current = None: CancellationTokenSource option

  let stop() =
    match current with
    | Some source ->
      source.Cancel()
      source.Dispose()
      current <- None
    | None -> ()

  // The shown bitmap rides the same lifecycle: one at a time, disposed
  // by whoever drops it.
  let mutable shown = None: Bitmap option

  let drop() =
    shown |> Option.iter(fun bitmap -> bitmap.Dispose())
    shown <- None

  let show(bitmap: Bitmap) =
    drop()
    shown <- Some bitmap
    this.Content <- Image().Source(bitmap).Stretch(props.Stretch)

  let start() =
    stop()

    let source = new CancellationTokenSource()
    current <- Some source

    let work = asyncEx {
      match! props.Cache.Get props.Url with
      | None -> ()
      | Some bytes ->
        // Decode off the UI thread: Skia results are immutable and
        // carry no thread affinity, so the bitmap crosses threads and
        // lands on the UI thread below.
        let! decoded =
          Task.Run(fun () ->
            try
              use stream = new MemoryStream(bytes)

              let bitmap =
                match props.DecodeHeight with
                | ValueSome height -> Bitmap.DecodeToHeight(stream, height)
                | ValueNone -> Bitmap.DecodeToWidth(stream, props.DecodeWidth)

              Ok bitmap
            with ex ->
              Error ex.Message)

        match decoded with
        | Ok bitmap ->
          Dispatcher.UIThread.Post(fun () ->
            // A detach between the decode and this post already dropped
            // and disposed; the token keeps the late bitmap out, and
            // the control owns the one it takes.
            if not source.IsCancellationRequested then
              show bitmap)
        | Error _ -> ()
    }

    Async.Start(work, source.Token)

  do this.Content <- placeholder

  override this.OnAttachedToVisualTree(_: VisualTreeAttachmentEventArgs) =
    start()

  override this.OnDetachedFromVisualTree(_: VisualTreeAttachmentEventArgs) =
    stop()
    drop()
    this.Content <- placeholder

let view(props: Props) : Control =
  let loader = Loader(props)

  match props.ToolTip with
  | ValueNone -> ()
  | ValueSome tip -> ToolTip.SetTip(loader, tip)

  loader
