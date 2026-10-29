module DevNull.Client.Views.AboutView

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open Microsoft.Extensions.Logging
open DevNull.Client
open DevNull.Client.DeviceAuth
open DevNull.Client.Theme

/// One line of the stack list: the name and what it does here.
let stackLine(name: string, what: string) : Control =
  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(8.0)
    .Children(
      TextBlock().Classes(Theme.name Theme.Mono).Text(name),
      TextBlock().Classes(Theme.name Theme.Muted).Text(what)
    )

/// One outbound link button; the browser owns the page.
let linkButton(log: ILogger, label: string, url: string) : Control =
  Button()
    .Classes(Theme.name Theme.GhostButton)
    .Padding(8.0, 4.0, 8.0, 4.0)
    .Content(label)
    .OnClickHandler(fun _ args ->
      args.Handled <- true

      match startBrowser url with
      | Ok() -> ()
      | Error message ->
        log.LogWarning("The page did not open: {Error}", message))

module Parts =

  /// The disclaimer card: the one legal line the page must carry. The
  /// wrapped block keeps a right margin inside the box: real text
  /// shaping can shape a line a few pixels wider than the measured
  /// wrap width, and the border would cut the last glyph.
  let disclaimer() : Control =
    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .Padding(12.0)
      .Child(
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .TextWrapping(TextWrapping.Wrap)
          .Margin(0.0, 0.0, 4.0, 0.0)
          .Text(
            "This project is not affiliated with or endorsed by /dev/null. "
            + "The name appears here only to name the service. "
            + "Every mark belongs to its owner."
          )
      )

  /// The stack list: what the client rides on, one line per piece.
  let stack() : Control =
    let lines: Control array = [|
      stackLine("F# / .NET", "the language and runtime")
      stackLine("Avalonia", "the cross-platform UI")
      stackLine("NXUI", "the fluent F# layer over it")
      stackLine("Navs.Avalonia", "the router")
      stackLine("MVUish.Avalonia", "the state loop")
      stackLine("Serilog", "the logs")
      stackLine("JDeck", "the JSON decoder")
    |]

    StackPanel().Spacing(4.0).Children(lines)

  /// The outbound links: the service, its brand rules, its terms.
  let links(log: ILogger) : Control =
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(4.0)
      .Children(
        linkButton(log, "dev-null.sh", "https://dev-null.sh"),
        linkButton(log, "brand", "https://dev-null.sh/brand"),
        linkButton(log, "terms", "https://dev-null.sh/terms")
      )

let view(log: ILogger) : Control =
  let content =
    StackPanel()
      .Spacing(12.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Title).Text("About"),
        TextBlock().Classes(Theme.name Theme.Subtitle).Text("DevNull.Client"),
        TextBlock()
          .TextWrapping(TextWrapping.Wrap)
          .Text(
            "An unofficial desktop client for dev-null.sh. "
            + "It reads and writes the service through its public API, "
            + "the same one the site and its CLI script use."
          ),
        Parts.disclaimer(),
        TextBlock().Classes(Theme.name Theme.Subtitle).Text("Built with"),
        Parts.stack(),
        TextBlock().Classes(Theme.name Theme.Subtitle).Text("Links"),
        Parts.links log
      )

  // The page scrolls one axis: the column measures against the window,
  // never an unbounded extent.
  ScrollViewer()
    .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
    .Padding(Theme.pagePadding)
    .Content(
      StackPanel()
        .MaxWidth(720.0)
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .Children(content)
    )
