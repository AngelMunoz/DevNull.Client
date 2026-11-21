module DevNull.Client.Workbench.GuildRegistry

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Components.AsyncImage
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Stores
open DevNull.Client.Theme

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher> = {
  Env: 'env
  Session: SignedInSession option
  Images: ImageCacheService
  OnOpen: string -> unit
}

// The directory's state logic, the same parking rules the site's own
// directory page rides: one read out at a time, the newest wanted ask
// parked behind it, and only the answer the surface waits for applied.
// The two machines must not drift on these rules.
module State =

  // One read the registry asks for: the term it reads for, and the page.
  [<Struct>]
  type Ask = { Query: string option; Page: int }

  type Model = {
    // The box, as typed.
    Term: string
    // The rows of the page the last answer settled.
    Rows: Guild list
    // The ask the rows answer; none before the first lands.
    Settled: Ask voption
    // The ask now out; one read at a time.
    Out: Ask voption
    // The ask a burst parked behind the read out.
    Wanted: Ask voption
    // How many guilds the directory holds, and its last page.
    Total: int
    LastPage: int
    // The 404 the flag-off server answers.
    Refused: bool
  }

  type Msg =
    | TermTyped of term: string
    | SearchAsked
    | PrevPage
    | NextPage
    | Arrived of ask: Ask * Result<GuildsPage, AppError>
    | Opened of handle: string

  let read
    (env: 'env, session: SignedInSession, ask: Ask)
    : Async<Ask * Result<GuildsPage, AppError>> = async {
    let! outcome =
      Guilds.list(
        env,
        {
          KeyFile = session.KeyFile
          Query = ask.Query
          Page = ask.Page
        }
      )

    return ask, outcome
  }

  // One ask: the first fires its read, the rest wait behind it as the
  // newest wanted. No session leaves the state as it stands.
  let fire(props: Props<'env>, model: Model, ask: Ask) : Model * Cmd<Msg> =
    match model.Out, props.Session with
    | ValueSome _, _ -> { model with Wanted = ValueSome ask }, Cmd.none
    | ValueNone, Some session ->
      {
        model with
            Out = ValueSome ask
            Wanted = ValueNone
      },
      Cmd.OfAsync.perform read (props.Env, session, ask) Arrived
    | ValueNone, None -> model, Cmd.none

  let init(props: Props<'env>) : Model * Cmd<Msg> =
    match props.Session with
    | Some _ ->
      fire(
        props,
        {
          Term = ""
          Rows = []
          Settled = ValueNone
          Out = ValueNone
          Wanted = ValueNone
          Total = 0
          LastPage = 1
          Refused = false
        },
        { Query = None; Page = 1 }
      )
    | None ->
      {
        Term = ""
        Rows = []
        Settled = ValueNone
        Out = ValueNone
        Wanted = ValueNone
        Total = 0
        LastPage = 1
        Refused = false
      },
      Cmd.none

  let update(props: Props<'env>, msg: Msg, model: Model) : Model * Cmd<Msg> =
    match msg with
    | TermTyped term -> { model with Term = term }, Cmd.none
    | SearchAsked ->
      let ask = {
        Query = SearchTerms.guildTerm model.Term |> ValueOption.toOption
        Page = 1
      }

      // The page the rows already answer asks nothing.
      if model.Settled = ValueSome ask then
        model, Cmd.none
      else
        fire(props, model, ask)
    | PrevPage ->
      match model.Settled with
      | ValueSome settled when settled.Page > 1 ->
        fire(props, model, { settled with Page = settled.Page - 1 })
      | _ -> model, Cmd.none
    | NextPage ->
      match model.Settled with
      | ValueSome settled when settled.Page < model.LastPage ->
        fire(props, model, { settled with Page = settled.Page + 1 })
      | _ -> model, Cmd.none
    | Arrived(ask, outcome) ->
      // Only the read the registry waits for applies; an answer the box
      // has moved past leaves the rows as they stand.
      if model.Out = ValueSome ask then
        let landed =
          match outcome with
          | Ok answered -> {
              model with
                  Rows = answered.Items
                  Settled = ValueSome ask
                  Total = answered.Total
                  LastPage = answered.LastPage
                  Refused = false
            }
          // Only the 404 the flag-off server answers reads as
          // refused; any other rejection keeps the rows, so a rate
          // limit never reads as "off".
          | Error(Refused(status, _, _)) when status = 404 -> {
              model with
                  Refused = true
            }
          | Error _ -> model

        let resting = { landed with Out = ValueNone }

        // The parked ask goes out, or the registry rests.
        match resting.Wanted with
        | ValueSome wanted -> fire(props, resting, wanted)
        | ValueNone -> resting, Cmd.none
      else
        model, Cmd.none
    | Opened handle -> model, Cmd.ofAction(fun () -> props.OnOpen handle)

let inline factsOf(guild: Guild) : string =
  [
    if guild.IsJoined then
      "joined"
    if guild.IsPaused then
      "paused"
    $"{shortCount guild.MemberCount} members"
  ]
  |> String.concat " · "

let markOf(props: Props<'env>, guild: Guild) : Control =
  match guild.IconUrl with
  | Some url ->
    Border()
      .Width(24.0)
      .Height(24.0)
      .CornerRadius(CornerRadius 6.0)
      .ClipToBounds(true)
      .Child(
        AsyncImage.view {
          Cache = props.Images
          Url = url
          DecodeWidth = 48
          DecodeHeight = ValueNone
          Stretch = Stretch.UniformToFill
          ToolTip = ValueSome guild.Name
        }
      )
  | None ->
    Border()
      .Classes(Theme.name Theme.SurfaceBack)
      .Width(24.0)
      .Height(24.0)
      .CornerRadius(CornerRadius 6.0)
      .Child(
        TextBlock()
          .FontSize(16.0)
          .VerticalAlignment(VerticalAlignment.Center)
          .HorizontalAlignment(HorizontalAlignment.Center)
          .Text(guild.Emoji)
      )

let row(props: Props<'env>, guild: Guild) : Control =
  // Fixed mark, handle, name, and facts columns: every row starts its
  // four cells at the same x, so the list reads as one table top to
  // bottom; ends stay ragged where the text is shorter.
  let grid =
    Grid(ColumnDefinitions = ColumnDefinitions "28,120,*,150")
      .Children(
        markOf(props, guild).Column(0),
        TextBlock()
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .FontSize(12.0)
          .Margin(Thickness(8.0, 0.0, 0.0, 0.0))
          .VerticalAlignment(VerticalAlignment.Center)
          .TextTrimming(TextTrimming.CharacterEllipsis)
          .Text("#" + guild.Handle)
          .Column(1),
        TextBlock()
          .Classes(Theme.name Theme.Subtitle)
          .FontSize(13.0)
          .Margin(Thickness(0.0, 0.0, 12.0, 0.0))
          .TextTrimming(TextTrimming.CharacterEllipsis)
          .VerticalAlignment(VerticalAlignment.Center)
          .Text(guild.Name)
          .Column(2),
        TextBlock()
          .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
          .FontSize(12.0)
          .TextTrimming(TextTrimming.CharacterEllipsis)
          .VerticalAlignment(VerticalAlignment.Center)
          .Text(factsOf guild)
          .Column(3)
      )

  Border()
    .Classes(Theme.name Theme.Hairline)
    .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
    .Child(
      Button()
        .Name("RegistryRow")
        .Classes(Theme.name Theme.GhostButton)
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .HorizontalContentAlignment(HorizontalAlignment.Stretch)
        .MinHeight(48.0)
        .Padding(Thickness(16.0, 12.0, 16.0, 12.0))
        .Content(grid)
        .OnClickHandler(fun _ _ -> props.OnOpen guild.Handle)
    )

let headText(current: State.Model) : string =
  let page =
    current.Settled |> ValueOption.map _.Page |> ValueOption.defaultValue 1

  let total =
    if current.Total = 1 then
      "1 guild"
    else
      $"{current.Total} guilds"

  let reading = if current.Out.IsSome then " · reading…" else ""

  $"GUILD REGISTRY · {total} · page {page}/{current.LastPage}{reading}"

let head(model: IObservable<State.Model>) : Control =
  TextBlock()
    .Name("RegistryHead")
    .Classes(Theme.name Theme.Mono)
    .VerticalAlignment(VerticalAlignment.Center)
    .Text(model |> Observable.map headText |> Observable.distinctUntilChanged)

let pager
  (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
  : Control =
  let settled = model |> Observable.map _.Settled |> Slices.share

  let hasPrev =
    settled
    |> Observable.map(fun current ->
      current
      |> ValueOption.map _.Page
      |> ValueOption.exists(fun page -> page > 1))
    |> Observable.distinctUntilChanged

  let hasNext =
    model
    |> Observable.map(fun current ->
      current.Settled
      |> ValueOption.map _.Page
      |> ValueOption.exists(fun page -> page < current.LastPage))
    |> Observable.distinctUntilChanged

  StackPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(8.0)
    .VerticalAlignment(VerticalAlignment.Center)
    .Children(
      Button()
        .Name("RegistryPrevPage")
        .Classes(Theme.name Theme.GhostButton)
        .Padding(8.0)
        .Content(Icons.iconAt(Glyph.ChevronLeft, 14.0))
        .IsEnabled(hasPrev)
        .OnClickHandler(fun _ _ -> dispatch State.PrevPage),
      TextBlock()
        .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
        .FontSize(12.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Text(
          model
          |> Observable.map(fun current ->
            let page =
              current.Settled
              |> ValueOption.map _.Page
              |> ValueOption.defaultValue 1

            $"{page}/{current.LastPage}")
          |> Observable.distinctUntilChanged
        ),
      Button()
        .Name("RegistryNextPage")
        .Classes(Theme.name Theme.GhostButton)
        .Padding(8.0)
        .Content(Icons.iconAt(Glyph.ChevronRight, 14.0))
        .IsEnabled(hasNext)
        .OnClickHandler(fun _ _ -> dispatch State.NextPage)
    )

let search
  (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
  : Control =
  let box =
    TextBox()
      .Name("RegistrySearch")
      .Classes(Theme.name Theme.Mono)
      .MaxLength(100)
      .PlaceholderText("Search guilds")
      .AcceptsReturn(false)
      .Text(model |> Observable.map _.Term)
      .OnTextChangedHandler(fun current _ ->
        current.Text
        |> Option.ofObj
        |> Option.iter(State.TermTyped >> dispatch))
      .OnKeyDownHandler(fun _ args ->
        if args.Key = Key.Enter then
          dispatch State.SearchAsked)

  let go =
    Button()
      .Name("RegistrySearchGo")
      .Classes(Theme.name Theme.GhostButton)
      .Content("SEARCH")
      .OnClickHandler(fun _ _ -> dispatch State.SearchAsked)

  DockPanel()
    .LastChildFill(true)
    .Children(
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .FontSize(12.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Text("names, #handles, descriptions")
        .DockRight()
        .Margin(Thickness(0.0, 0.0, 12.0, 0.0)),
      go.DockRight(),
      box
    )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let offered =
    model
    |> Observable.map(fun current ->
      props.Session.IsSome && not current.Refused)
    |> Observable.distinctUntilChanged

  let headBar =
    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 0.0, 1.0))
      .Padding(Thickness(16.0, 10.0, 16.0, 8.0))
      .Child(
        DockPanel()
          .LastChildFill(true)
          .Children(pager(model, dispatch).DockRight(), head model)
      )

  let searchBar =
    Border()
      .Name("RegistrySearchBar")
      .Padding(Thickness(16.0, 8.0, 16.0, 8.0))
      .Child(search(model, dispatch))
      .IsVisible(offered)

  let signedOut =
    TextBlock()
      .Name("RegistrySignedOut")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0)
      .Text("Sign in to browse the guilds.")
      .IsVisible(Observable.single(props.Session.IsNone))

  let refused =
    TextBlock()
      .Name("RegistryRefused")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0)
      .Text("Guilds are not available on this server.")
      .IsVisible(model |> Observable.map _.Refused)

  let empty =
    TextBlock()
      .Name("RegistryEmpty")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0)
      .Text(
        model
        |> Observable.map(fun current ->
          if String.IsNullOrWhiteSpace current.Term then
            "No guilds found."
          else
            $"No guild matches \"{current.Term}\".")
        |> Observable.distinctUntilChanged
      )
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Settled.IsSome && not current.Refused && current.Rows.IsEmpty)
        |> Observable.distinctUntilChanged
      )

  let note =
    TextBlock()
      .Name("RegistryNote")
      .Classes(Theme.name Theme.Mono, Theme.name Theme.FgMuted)
      .FontSize(12.0)
      .Margin(16.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Text("Join or create a guild on the site.")

  // One numbered page of at most twenty-four rows — the wire's cap —
  // and the feed virtualizes beneath it anyway.
  let rows =
    Feed.view {
      Header = ValueNone
      Items = model |> Observable.map _.Rows |> Observable.distinctUntilChanged
      KeyOf = fun guild -> guild.Handle
      Row = fun guild -> row(props, guild)
      Footer = ValueSome note
      HasMore = Observable.single false
      ScrollTo = ValueNone
      Ride = false
      Restore = ValueNone
      OnScroll = ValueNone
      OnRide = ValueNone
      OnNeedMore = ValueNone
    }

  DockPanel()
    .LastChildFill(true)
    .Children(
      StackPanel()
        .Children(headBar, searchBar, signedOut, refused, empty)
        .DockTop(),
      rows.IsVisible(offered)
    )

let program(props: Props<'env>) =
  Program.mkProgram
    (fun () -> State.init props)
    (fun msg model -> State.update(props, msg, model))
    (view props)
