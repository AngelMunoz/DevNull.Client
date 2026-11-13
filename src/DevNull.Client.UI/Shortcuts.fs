module DevNull.Client.Shortcuts

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Presenters
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open Avalonia.VisualTree
open NXUI.Extensions
open DevNull.Client.Theme

[<Struct>]
type Actions = {
  Compose: unit -> unit
  Search: unit -> unit
  Escape: unit -> unit
  Navigate: string -> unit
  ToggleFullScreen: unit -> unit
  WalkCards: int -> unit
}

type CheatsheetItem = { Keys: string list; Words: string }

[<Struct>]
type Props = {
  Root: Window
  Actions: Actions
  Help: unit -> unit
  OverlayOpen: unit -> bool
}

// How long a pressed `g` arms the go-to pair: the site's one second.
let goWindow = TimeSpan.FromSeconds 1.0

// The pending `g` of one go-to pair: the moment its window ends. A
// deadline, not a timer — nothing scheduled holds state between
// presses, and the window that installed the handler owns this cell.
type Pending() =
  member val Deadline: DateTimeOffset voption = ValueNone with get, set

// True when keyboard focus sits in a text field, where every plain
// key belongs to the sentence being typed.
let typing(root: Window) : bool =
  match root.FocusManager.GetFocusedElement() with
  | :? TextBox -> true
  | _ -> false

// The focused visual under the root, when focus sits on one.
let focusedVisual(root: Window) : Visual option =
  match root.FocusManager.GetFocusedElement() with
  | :? Visual as visual -> Some visual
  | _ -> None

// True while ctrl, meta, or alt is held: the pair a held key belongs
// to the platform or the control, never the table. Shift stays free —
// the question mark rides it.
let held(args: KeyEventArgs) : bool =
  args.KeyModifiers
  &&& (KeyModifiers.Alt ||| KeyModifiers.Control ||| KeyModifiers.Meta)
  <> KeyModifiers.None

let install(props: Props) : unit =
  let pending = Pending()

  let armed() : bool =
    match pending.Deadline with
    | ValueSome due -> DateTimeOffset.UtcNow < due
    | ValueNone -> false

  props.Root.AddHandler(
    InputElement.KeyDownEvent,
    (fun _ args ->
      // The chrome pair answers even mid-sentence: fullscreen owns the
      // window, not the caret. The search's pair joins it — the site's
      // own Ctrl K opens the palette while typing.
      match args.Key, args.KeyModifiers with
      | Key.F11, modifiers when modifiers = KeyModifiers.None ->
        args.Handled <- true
        props.Actions.ToggleFullScreen()
      | Key.K, modifiers when
        modifiers &&& (KeyModifiers.Control ||| KeyModifiers.Meta)
        <> KeyModifiers.None
        ->
        args.Handled <- true
        props.Actions.Search()
      | _ when held args -> ()
      | _ when props.OverlayOpen() || typing props.Root ->
        pending.Deadline <- ValueNone
      | _ ->
        let go = armed()

        // Any key consumes a pending pair, the way the site's window
        // does; the table below re-arms on a second `g`.
        pending.Deadline <- ValueNone

        match go, args.Key with
        | true, Key.L ->
          args.Handled <- true
          props.Actions.Navigate "/log/latest"
        | true, Key.E ->
          args.Handled <- true
          props.Actions.Navigate "/log/trending"
        | true, Key.P ->
          args.Handled <- true
          props.Actions.Navigate "/profile"
        | true, Key.S ->
          args.Handled <- true
          props.Actions.Navigate "/settings"
        | _, Key.G ->
          args.Handled <- true
          pending.Deadline <- ValueSome(DateTimeOffset.UtcNow + goWindow)
        | _, Key.J ->
          args.Handled <- true
          props.Actions.WalkCards 1
        | _, Key.K ->
          args.Handled <- true
          props.Actions.WalkCards -1
        | _, Key.C ->
          args.Handled <- true
          props.Actions.Compose()
        | _, Key.OemQuestion ->
          args.Handled <- true
          props.Help()
        // Spanish and several other layouts put ? on the apostrophe
        // key with Shift; the plain apostrophe stays a typable key.
        | _, Key.OemQuotes when
          args.KeyModifiers &&& KeyModifiers.Shift <> KeyModifiers.None
          ->
          args.Handled <- true
          props.Help()
        | _ -> ()),
    RoutingStrategies.Tunnel
  )

  // The escape lands after the focused control had its turn: a control
  // that handles it keeps it, and the window acts only on the rest.
  props.Root.AddHandler(
    InputElement.KeyDownEvent,
    (fun _ args ->
      if args.Key = Key.Escape && args.KeyModifiers = KeyModifiers.None then
        args.Handled <- true
        props.Actions.Escape()),
    RoutingStrategies.Bubble
  )

let moveCardFocus(dispatcher: IDispatcher, root: Window, step: int) : unit =
  // The realized cards under the root. A virtualized feed realizes only
  // its viewport plus a band, and recycling hands containers new items,
  // so the walk orders by feed position, never by visual order.
  let cardsOf() : Button array =
    root.GetVisualDescendants()
    |> Seq.choose(fun visual ->
      match visual with
      | :? Button as button when button.Classes.Contains(name Theme.CommitRow) ->
        Some button
      | _ -> None)
    |> Array.ofSeq

  let focusedCardOf(cards: Button array) : Button option =
    match focusedVisual root with
    | None -> None
    | Some element ->
      cards
      |> Array.tryFind(fun card ->
        Object.ReferenceEquals(card, element)
        || element.GetVisualAncestors()
           |> Seq.exists(fun ancestor ->
             Object.ReferenceEquals(ancestor, card)))

  let feedOf(cards: Button array) : ItemsControl option =
    cards
    |> Array.tryPick(fun card ->
      card.FindAncestorOfType<ItemsControl>() |> Option.ofObj)

  // The feed position of a card: the item index of its row container.
  let spotOf(items: ItemsControl, card: Button) : int voption =
    match card.FindAncestorOfType<ContentPresenter>() with
    | null -> ValueNone
    | container ->
      let at = items.IndexFromContainer container

      if at < 0 then ValueNone else ValueSome at

  let placedOf
    (items: ItemsControl, cards: Button array)
    : (int * Button) array =
    cards
    |> Array.choose(fun card ->
      match spotOf(items, card) with
      | ValueSome at -> Some(at, card)
      | ValueNone -> None)
    |> Array.sortBy fst

  let land'(card: Button) : unit =
    card.Focus() |> ignore
    card.BringIntoView()

  // Lands the nearest realized card beyond a feed position, toward the
  // step. From no position, the step's end of the feed. False when no
  // realized card sits beyond, so the caller can page and retry.
  let landNearest(placed: (int * Button) array, from: int voption) : bool =
    let ahead =
      placed
      |> Array.filter(fun (spot, _) ->
        match from with
        | ValueSome at -> if step > 0 then spot > at else spot < at
        | ValueNone -> true)

    match ahead with
    | [||] -> false
    | _ ->
      let target = if step > 0 then ahead[0] else ahead[ahead.Length - 1]

      land'(snd target)
      true

  // One walk over the realized cards, by feed position.
  let attempt() : bool =
    let cards = cardsOf()

    match feedOf cards with
    | None -> true
    | Some items ->
      let placed = placedOf(items, cards)

      if placed.Length = 0 then
        true
      else
        let from =
          focusedCardOf cards
          |> Option.bind(fun card ->
            placed
            |> Array.tryPick(fun (spot, other) ->
              if Object.ReferenceEquals(other, card) then
                Some spot
              else
                None))
          |> Option.toValueOption

        landNearest(placed, from)

  if not(attempt()) then
    // The card the reader stepped toward sits outside the realized
    // band: page the feed's scroller one row toward the step — which
    // realizes it — then land the nearest card beyond where the walk
    // came from. The position is taken before the page, because
    // recycling can drop the focused card's row, and the landing waits
    // one layout pass: the panel re-realizes at render priority after
    // the page, and a focus set inside the same dispatch can land on a
    // row that pass recycles. At the feed's true end the offset already
    // holds, nothing realizes, and the walk clamps.
    let cards = cardsOf()

    match focusedCardOf cards with
    | Some card ->
      let from =
        feedOf cards
        |> Option.bind(fun items -> spotOf(items, card) |> ValueOption.toOption)

      match card.FindAncestorOfType<ScrollViewer>() |> Option.ofObj with
      | None -> ()
      | Some scroller ->
        let wanted = scroller.Offset.Y + float step * card.Bounds.Height
        let ceiling = scroller.Extent.Height - scroller.Viewport.Height

        scroller.Offset <-
          Vector(0.0, Math.Min(Math.Max(wanted, 0.0), Math.Max(ceiling, 0.0)))

        root.UpdateLayout()

        // The panel commits its recycled rows on a dispatcher turn
        // after the page; a focus set inside that window is cleared
        // with the row it landed on. Land past that commit, then
        // once more: the first landing takes the reader's key, the
        // second holds it.
        let settleLand() : unit =
          let settled = cardsOf()

          feedOf settled
          |> Option.iter(fun items ->
            landNearest(placedOf(items, settled), from |> Option.toValueOption)
            |> ignore)

        dispatcher.Post(
          (fun () ->
            settleLand()

            dispatcher.Post(
              (fun () -> settleLand()),
              DispatcherPriority.Default
            )),
          DispatcherPriority.Default
        )
    | None -> ()

// One key chip of a row.
let chip(key: string) : Control =
  Border()
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(1.0))
    .CornerRadius(4.0)
    .Padding(6.0, 1.0, 6.0, 1.0)
    .Child(TextBlock().Classes(Theme.name Theme.Mono).FontSize(12.0).Text(key))

// One row of the list: the action on the left, its keys on the right.
let row(item: CheatsheetItem) : Control =
  let grid = Grid()

  grid.ColumnDefinitions <- ColumnDefinitions("*,Auto")

  grid.Children(
    TextBlock()
      .Text(item.Words)
      .TextWrapping(TextWrapping.Wrap)
      .VerticalAlignment(VerticalAlignment.Center)
      .Column(0),
    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(4.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Column(1)
      .Children([| for key in item.Keys -> chip key |])
  )

let helpCard(dispatcher: IDispatcher, items: CheatsheetItem list) : Control =
  let card =
    Border()
      .Name("ShortcutsCard")
      .Classes(Theme.name Theme.Card)
      .MaxWidth(460.0)
      .MaxHeight(520.0)
      .Focusable(true)
      .Child(
        ScrollViewer()
          .Content(
            StackPanel()
              .Margin(20.0)
              .Spacing(8.0)
              .Children(
                TextBlock()
                  .Classes(Theme.name Theme.Title)
                  .Text("Keyboard shortcuts"),
                TextBlock()
                  .Classes(Theme.name Theme.Muted)
                  .Text("They work anywhere except while you're typing."),
                StackPanel().Children([| for item in items -> row item |])
              )
          )
      )

  // The list takes the keyboard when it appears, so the cards under
  // the scrim answer no keys while it is open.
  card.AttachedToVisualTree.Add(fun _ ->
    dispatcher.Post(
      (fun () -> card.Focus() |> ignore),
      DispatcherPriority.Default
    ))

  card
