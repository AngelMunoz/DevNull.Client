module DevNull.Client.Editor.StatusBar

open System
open Avalonia.Controls
open DevNull.Client.Core.ApiTypes

/// What the status strip reads: the selected ref, the viewer's lane
/// and key, the last fetch, the dirty count, and the width band.
type Props = {
  Ref: LogKind
  Lane: Lane option
  Key: string
  FetchedAt: DateTimeOffset option
  Now: DateTimeOffset
  NewCount: int
  Band: WidthBand
}

/// The layout's own status strip: the full form from 700 px up, the
/// ref and dirty count alone below that.
val view: props: Props -> Control
