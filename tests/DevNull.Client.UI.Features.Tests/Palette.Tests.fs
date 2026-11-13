module DevNull.Client.UI.Tests.Palette

open Expecto
open DevNull.Client.Layouts.Editor

let tests =
  testList "editor palette" [
    testCase "the command list names only real commands" (fun () ->
      Expect.equal
        (Palette.commands |> List.map Palette.label)
        [
          "switch ref: Latest"
          "switch ref: Following"
          "switch ref: Trending"
          "open panel: NOTIFICATIONS"
          "open panel: ACTIVITY"
          "open panel: TERMINAL"
          "use default layout"
        ]
        "the command list")
  ]
