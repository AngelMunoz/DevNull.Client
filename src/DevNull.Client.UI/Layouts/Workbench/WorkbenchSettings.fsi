namespace DevNull.Client.Layouts.Workbench

/// The workbench's stored state as one plain string. The stored form
/// is the ref's token, then `|` and the checked-out commit id when one
/// is held. A bad string loads the default state whole: Following with
/// no checkout.
module WorkbenchSettings =

  /// The state a fresh workbench holds.
  val initial: WorkbenchState

  /// Reads one stored string. A missing, empty, or malformed string
  /// answers the default state; no half state is returned.
  val parse: raw: string -> WorkbenchState

  /// Writes the state as its stored string.
  val store: state: WorkbenchState -> string
