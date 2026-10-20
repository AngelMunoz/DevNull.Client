module DevNull.Client.Components.Feed

open System
open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Layout
open NXUI.Extensions
open MVUish.Avalonia.Collections

type Props<'T when 'T: equality> = {
  Items: IObservable<'T list>
  KeyOf: 'T -> string
  Row: 'T -> Control
  Footer: Control voption
}

let view(props: Props<'T>) : Control =
  // The recycle path clears the container's content and rebuilds its
  // child with `null` before the next row lands: the template stays
  // total against null.
  let template =
    FuncDataTemplate<'T>(
      (fun item _ ->
        if isNull(box item) then
          Panel() :> Control
        else
          props.Row item),
      false
    )

  let items =
    ItemsControl()
      .ItemTemplate(template)
      .ItemsPanel(FuncTemplate<Panel>(fun () -> VirtualizingStackPanel()))
      .BindItemsSource(props.Items, props.KeyOf)

  let children: Control array = [|
    items
    match props.Footer with
    | ValueNone -> ()
    | ValueSome footer -> footer
  |]

  let rows =
    StackPanel()
      .MaxWidth(720.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Children(children)

  ScrollViewer().Content(rows)
