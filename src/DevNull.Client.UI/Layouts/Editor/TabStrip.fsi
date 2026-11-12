module DevNull.Client.Editor.TabStrip

open Avalonia.Controls

/// What the strip renders: the tab list, the active tab, the width
/// band, and the clicks the host answers.
type Props = {
  Tabs: EditorTab list
  Active: TabId
  Band: WidthBand
  OnSelect: TabId -> unit
  OnClose: TabId -> unit
}

/// One row of tabs: the pinned history tab, the ordinary tabs, each
/// ordinary tab's close control, and the narrow bands' overflow form.
val view: props: Props -> Control
