module DevNull.Client.Views.AboutView

open Avalonia.Controls
open Microsoft.Extensions.Logging
open DevNull.Client.Env

/// The about page: what this project is, the stack it rides on, the
/// links out, and the disclaimer that it is not affiliated with or
/// endorsed by the service. The logger reports a browser that did not
/// open.
val view: browser: IBrowser * log: ILogger -> Control
