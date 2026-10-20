module DevNull.Client.CommitView

open Avalonia.Controls
open DevNull.Client.Env

/// The one window of the app: sign-in, a message box, an optional reply hash, a commit button, a status line.
val buildWindow: env: AppEnv * callback: string option -> Window
