module DevNull.Client.Navigation

open Navs

/// Fires one navigation now, on the UI thread, and logs nothing on
/// success. The router answers with a result, so a failed navigation
/// is a silent no-op only by accident: this helper reports the error
/// through `onError`.
val inline go: nav: INavigable * url: string * onError: (string -> unit) -> unit

/// The sidebar section a URL belongs to, so the shell can highlight one
/// item at a time.
val sectionOf: url: string -> string voption
