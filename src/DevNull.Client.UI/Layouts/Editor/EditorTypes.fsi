namespace DevNull.Client.Layouts.Editor

open DevNull.Client.Core.ApiTypes

/// The identity of one editor tab: the pinned history tab, or one
/// ordinary tab. The representation stays hidden; the factories are
/// the only way to build one.
type TabId

module TabId =

  /// The pinned history tab's identity.
  val history: TabId

  /// The identity of one ordinary tab, from its stable key.
  val ordinary: key: string -> TabId

  /// The key this identity carries.
  val key: id: TabId -> string

  /// Whether this is the pinned history tab's identity.
  val isHistory: id: TabId -> bool

/// The title one ref carries in the strip and the explorer.
module LogLabel =

  /// The readable name of one ref.
  val ofKind: log: LogKind -> string

/// One tab in the strip: its identity, title, pin state, and dirty
/// count. The representation stays hidden; the factories are the only
/// way to build one.
type EditorTab

module EditorTab =

  /// The one pinned tab: the selected ref's history. Cannot close.
  val pinned: log: LogKind -> EditorTab

  /// One ordinary tab over its identity and title.
  val ordinary: id: TabId * title: string -> EditorTab

  /// The tab's identity.
  val id: tab: EditorTab -> TabId

  /// The tab's title.
  val title: tab: EditorTab -> string

  /// The same tab under another title.
  val withTitle: title: string * tab: EditorTab -> EditorTab

  /// Whether the tab stays open through its close control.
  val isPinned: tab: EditorTab -> bool

  /// The dirty count the dot shows; zero draws no dot.
  val dirty: tab: EditorTab -> int

  /// The same tab under another dirty count; a negative count reads
  /// as zero.
  val withDirty: count: int * tab: EditorTab -> EditorTab

/// The width band the frame renders in.
type WidthBand =
  | Full
  | Folded
  | Trimmed

module WidthBand =

  /// The band at the given width: full at 1000 and up, folded from
  /// 700 to 1000, trimmed below 700.
  val ofWidth: width: float -> WidthBand

/// The editor host's state: the selected ref, the tabs in order, and
/// the active tab. The pinned history tab always stays in the list.
type EditorState

module EditorState =

  /// The state of one ref with only its pinned history tab.
  val initial: log: LogKind -> EditorState

  /// The ref whose history the pinned tab shows.
  val selectedRef: state: EditorState -> LogKind

  /// The same state on another ref; the pinned tab's title follows.
  val selectRef: log: LogKind * state: EditorState -> EditorState

  /// Every tab, pinned first.
  val tabs: state: EditorState -> EditorTab list

  /// The active tab's identity.
  val activeTab: state: EditorState -> TabId

  /// The same state on another tab, when that tab exists.
  val selectTab: id: TabId * state: EditorState -> EditorState

  /// The same state under another tab list. A list without the pinned
  /// history tab gains it back at the head.
  val withTabs: tabs: EditorTab list * state: EditorState -> EditorState

  /// The same state without one ordinary tab. A pinned tab never
  /// closes; closing the active ordinary tab selects the history tab.
  val closeTab: id: TabId * state: EditorState -> EditorState

  /// The same state with one tab's dirty count set.
  val setDirty: id: TabId * count: int * state: EditorState -> EditorState
