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
  Stretch: Stretch
  Placeholder: Control voption
  ToolTip: string voption
}

// The loader is the one custom control of the app: a plain `Image`
// needs a ready bitmap, and the feed recycles rows, so the load needs
// a control-owned lifetime. The URL is fixed at construction because
// every feed row rebuilds its content per item; no property system
// machinery is needed for that.
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

              Ok(Bitmap.DecodeToWidth(stream, props.DecodeWidth))
            with ex ->
              Error ex.Message)

        match decoded with
        | Ok bitmap ->
          Dispatcher.UIThread.Post(fun () ->
            if not source.IsCancellationRequested then
              this.Content <- Image().Source(bitmap).Stretch(props.Stretch))
        | Error _ -> ()
    }

    Async.Start(work, source.Token)

  do this.Content <- placeholder

  override this.OnAttachedToVisualTree(_: VisualTreeAttachmentEventArgs) =
    start()

  override this.OnDetachedFromVisualTree(_: VisualTreeAttachmentEventArgs) =
    stop()
    this.Content <- placeholder

let view(props: Props) : Control =
  let loader = Loader(props)

  match props.ToolTip with
  | ValueNone -> ()
  | ValueSome tip -> ToolTip.SetTip(loader, tip)

  loader
