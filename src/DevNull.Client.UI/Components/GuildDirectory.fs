module DevNull.Client.Components.GuildDirectory

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.Components.GuildRow
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.ImageCache
open DevNull.Client.Icons
open DevNull.Client.Programs
open DevNull.Client.Stores
open DevNull.Client.Theme

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache> = {
  Env: 'env
  SignedIn: SignedInSession
  OnOpen: Guild -> unit
  Title: string
}

// The directory's state logic: the term as typed, and the page's own
// reads beside it — one numbered page at a time, the way the site's
// own directory pages.
module State =

  // One read the page asks for: the term it reads for, and the page.
  [<Struct>]
  type Ask = { Query: string option; Page: int }

  type Model = {
    // The box, as typed.
    Term: string
    // The cards of the page the rows answer.
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
    | Opened of Guild

  let read(env: 'env, signedIn: SignedInSession, ask: Ask) = async {
    let! outcome =
      Guilds.list(
        env,
        {
          KeyFile = signedIn.KeyFile
          Query = ask.Query
          Page = ask.Page
        }
      )

    return ask, outcome
  }

  // One ask: the first fires its read, the rest wait behind it as the
  // newest wanted.
  let fire(props: Props<'env>, model: Model, ask: Ask) : Model * Cmd<Msg> =
    match model.Out with
    | ValueSome _ -> { model with Wanted = ValueSome ask }, Cmd.none
    | ValueNone ->
      {
        model with
            Out = ValueSome ask
            Wanted = ValueNone
      },
      Cmd.OfAsync.perform read (props.Env, props.SignedIn, ask) Arrived

  let init (props: Props<'env>) () : Model * Cmd<Msg> =
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

  let update (props: Props<'env>) msg (model: Model) : Model * Cmd<Msg> =
    match msg with
    | TermTyped term -> { model with Term = term }, Cmd.none
    | SearchAsked ->
      let ask = {
        Query = SearchTerms.guildTerm model.Term |> ValueOption.toOption
        Page = 1
      }

      // The page the cards already answer asks nothing.
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
      // Only the read the page waits for applies; an answer the box
      // has moved past leaves the cards as they stand.
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
          // refused; any other rejection keeps the cards, so a rate
          // limit never reads as "off".
          | Error(Refused(status, _, _)) when status = 404 -> {
              model with
                  Refused = true
            }
          | Error _ -> model

        let resting = { landed with Out = ValueNone }

        // The parked ask goes out, or the page rests.
        match resting.Wanted with
        | ValueSome wanted -> fire(props, resting, wanted)
        | ValueNone -> resting, Cmd.none
      else
        model, Cmd.none
    | Opened guild -> model, Cmd.ofAction(fun () -> props.OnOpen guild)

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  let refused =
    model |> Observable.map _.Refused |> Observable.distinctUntilChanged

  let offered =
    model
    |> Observable.map(fun current -> not current.Refused)
    |> Observable.distinctUntilChanged

  let title =
    TextBlock()
      .Classes(Theme.name Theme.Title)
      .Margin(16.0, 16.0, 16.0, 0.0)
      .Text(props.Title)

  let subtitle =
    TextBlock()
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0, 4.0, 16.0, 0.0)
      .TextWrapping(TextWrapping.Wrap)
      .Text(
        "Public groups, biggest first. Join to see their commits in Following."
      )

  // The search row: the box, its Search button, and the helper line
  // under both, the site's own words.
  let search =
    let box =
      TextBox()
        .Name("GuildSearch")
        .MaxLength(100)
        .PlaceholderText("Search guilds")
        .FontSize(15.0)
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
        .Name("GuildSearchGo")
        .Classes(Theme.name Theme.AccentButton)
        .Content("Search")
        .OnClickHandler(fun _ _ -> dispatch State.SearchAsked)

    StackPanel()
      .Margin(16.0, 12.0, 16.0, 0.0)
      .Children(
        DockPanel()
          .LastChildFill(true)
          .Children(go.DockRight().Margin(8.0, 0.0, 0.0, 0.0), box),
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .FontSize(12.0)
          .Margin(0.0, 6.0, 0.0, 0.0)
          .Text("Search names, #handles, and descriptions.")
      )

  // The count bar: how many guilds the directory holds, and which of
  // how many pages the cards answer, with the pager's chevrons.
  let pager =
    let hasPrev =
      model
      |> Observable.map(fun current ->
        match current.Settled with
        | ValueSome settled -> settled.Page > 1
        | ValueNone -> false)
      |> Observable.distinctUntilChanged

    let hasNext =
      model
      |> Observable.map(fun current ->
        match current.Settled with
        | ValueSome settled -> settled.Page < current.LastPage
        | ValueNone -> false)
      |> Observable.distinctUntilChanged

    DockPanel()
      .Name("GuildPager")
      .Margin(16.0, 12.0, 16.0, 4.0)
      .LastChildFill(false)
      .Children(
        TextBlock()
          .Name("GuildCount")
          .Classes(Theme.name Theme.Muted)
          .FontSize(13.0)
          .VerticalAlignment(VerticalAlignment.Center)
          .Text(
            model
            |> Observable.map(fun current ->
              let word = if current.Total = 1 then "guild" else "guilds"

              $"{current.Total} {word}")
            |> Observable.distinctUntilChanged
          ),
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .DockRight()
          .VerticalAlignment(VerticalAlignment.Center)
          .Children(
            Button()
              .Name("GuildPrevPage")
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

                  $"Page {page} of {current.LastPage}")
                |> Observable.distinctUntilChanged
              ),
            Button()
              .Name("GuildNextPage")
              .Classes(Theme.name Theme.GhostButton)
              .Padding(8.0)
              .Content(Icons.iconAt(Glyph.ChevronRight, 14.0))
              .IsEnabled(hasNext)
              .OnClickHandler(fun _ _ -> dispatch State.NextPage)
          )
      )

  let refusal =
    TextBlock()
      .Name("GuildsRefused")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0)
      .Text("Guilds are not available.")
      .IsVisible(refused)

  let empty =
    TextBlock()
      .Name("GuildEmpty")
      .Classes(Theme.name Theme.Muted)
      .Margin(16.0, 24.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Text("No guilds found.")
      .IsVisible(
        model
        |> Observable.map(fun current ->
          current.Settled.IsSome && not current.Refused && current.Rows.IsEmpty)
        |> Observable.distinctUntilChanged
      )

  // The card grid: two columns, one numbered page of cards — the wire
  // caps a page at twenty-four, so the grid never holds more.
  let grid = UniformGrid().Columns(2)

  model
  |> Observable.map _.Rows
  |> Observable.distinctUntilChanged
  |> Observable.subscribe(fun rows ->
    grid.Children.Clear()

    rows
    |> List.iter(fun guild ->
      grid.Children.Add(
        GuildRow.view {
          Guild = guild
          Cache = props.Env.ImageCache
          OnOpen = fun picked -> dispatch(State.Opened picked)
        }
      )))
  |> ignore

  ScrollViewer()
    .Content(
      StackPanel()
        .Children(
          title,
          subtitle,
          search.IsVisible(offered),
          pager.IsVisible(offered),
          refusal,
          empty,
          grid.Margin(16.0, 0.0, 6.0, 16.0)
        )
    )

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideImageCache>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)

    // The directory is replace-only: the first detach ends the
    // program.
    Programs.runComponentOnAttach(
      this,
      props.Env.Dispatcher,
      program,
      fun root -> this.Content <- root
    )
    |> ignore
