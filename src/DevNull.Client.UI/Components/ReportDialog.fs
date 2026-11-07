module DevNull.Client.Components.ReportDialog

open System
open System.Runtime
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components.ImageChip
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Commit
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Icons
open DevNull.Client.Stores

module CoreCommits = DevNull.Client.Core.Api.Commits

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideAttachmentSources> = {
  Env: 'env
  Session: SignedInSession
  Commit: Commit
  Sent: unit -> unit
  Close: unit -> unit
}

module State =

  type Model = {
    Reason: ReportReason option
    Details: string
    Attachments: ImageAttachment list
    Descriptions: string list
    Busy: bool
    Failure: string voption
  }

  type Msg =
    | ReasonPicked of ReportReason
    | DetailsChanged of string
    | AttachRequested
    | ImagesAdded of ImageAttachment list
    | ImageRemoved of int
    | DescriptionChanged of index: int * text: string
    | SendRequested
    | SentDone of Result<unit, AppError>
    | CloseRequested

  let initial = {
    Reason = None
    Details = ""
    Attachments = []
    Descriptions = []
    Busy = false
    Failure = ValueNone
  }

  let rateLimitedMessage =
    "Too many reports. Please wait a minute and try again."

  let unavailableMessage = "This commit is no longer available to report."

  let offlineMessage =
    "Could not send your report. Check your connection and try again."

  let failureLine(error: AppError) =
    match error with
    | Refused(429, _, _) -> rateLimitedMessage
    | Refused(404, _, _) -> unavailableMessage
    | Refused _ -> unavailableMessage
    | TransportFailed _ -> offlineMessage
    | other -> describe other

  // The staging path allocates large-object arrays an idle dialog never
  // generates enough churn to collect: the picker's transients and every
  // staged copy wait for a generation the resting app does not run. Each
  // transition below is where that memory dies; the close paths post
  // through the page's DetachedFromVisualTree. The collection rides the
  // dispatcher at background priority, after the new model published and
  // the bindings dropped the old lists, and aggressive compaction is the
  // mode that returns the emptied regions to the OS.
  let collectStaged(props: Props<'env>) : unit =
    props.Env.Dispatcher.Post(
      (fun () ->
        GCSettings.LargeObjectHeapCompactionMode <-
          GCLargeObjectHeapCompactionMode.CompactOnce

        GC.Collect(
          2,
          GCCollectionMode.Aggressive,
          blocking = true,
          compacting = true
        )),
      DispatcherPriority.Background
    )

  let stage(props: Props<'env>, model: Model, staged: ImageAttachment list) =
    match staged with
    | [] -> model, Cmd.none
    | staged ->
      let combined = model.Attachments @ staged

      match CommitImages.parse combined with
      | Ok _ ->
        {
          model with
              Attachments = combined
              Descriptions =
                model.Descriptions @ List.replicate (List.length staged) ""
              Failure = ValueNone
        },
        Cmd.ofEffect(fun _ -> collectStaged props)
      | Error error ->
        {
          model with
              Failure = ValueSome(describe error)
        },
        Cmd.none

  let update (props: Props<'env>) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | ReasonPicked reason -> { model with Reason = Some reason }, Cmd.none
    | DetailsChanged text -> { model with Details = text }, Cmd.none
    | AttachRequested ->
      model, Cmd.OfAsync.perform props.Env.Attachments.PickFiles () ImagesAdded
    | ImagesAdded staged -> stage(props, model, staged)
    | ImageRemoved index ->
      {
        model with
            Attachments =
              model.Attachments
              |> List.indexed
              |> List.filter(fun (at, _) -> at <> index)
              |> List.map snd
            Descriptions =
              model.Descriptions
              |> List.indexed
              |> List.filter(fun (at, _) -> at <> index)
              |> List.map snd
      },
      Cmd.ofEffect(fun _ -> collectStaged props)
    | DescriptionChanged(index, text) ->
      {
        model with
            Descriptions =
              model.Descriptions
              |> List.indexed
              |> List.map(fun (at, current) ->
                if at = index then text else current)
      },
      Cmd.none
    | CloseRequested when model.Busy -> model, Cmd.none
    | CloseRequested -> model, Cmd.ofAction props.Close
    | SendRequested when model.Busy -> model, Cmd.none
    | SendRequested ->
      match model.Reason with
      | None -> model, Cmd.none
      | Some reason ->
        let details =
          if String.IsNullOrWhiteSpace model.Details then
            None
          else
            Some model.Details

        let descriptions =
          model.Descriptions
          |> List.map(fun text ->
            if String.IsNullOrWhiteSpace text then None else Some text)

        {
          model with
              Busy = true
              Failure = ValueNone
        },
        Cmd.OfAsync.perform
          CoreCommits.report
          (props.Env,
           {
             KeyFile = props.Session.KeyFile
             CommitId = props.Commit.Id
             Reason = reason
             Details = details
             Images = model.Attachments
             ImageDescriptions = descriptions
           })
          SentDone
    | SentDone(Ok()) -> { model with Busy = false }, Cmd.ofAction props.Sent
    | SentDone(Error(Refused(404, _, _))) ->
      { model with Busy = false }, Cmd.ofAction props.Close
    | SentDone(Error error) ->
      {
        model with
            Busy = false
            Failure = ValueSome(failureLine error)
      },
      Cmd.ofEffect(fun _ -> collectStaged props)

module Parts =

  let reasonLabel(reason: ReportReason) =
    match reason with
    | Spam -> "Spam or misleading links"
    | Harassment -> "Harassment or hate"
    | Unsafe -> "Harmful or private information"
    | Other -> "Something else"

  let reasonName(reason: ReportReason) =
    match reason with
    | Spam -> "Spam"
    | Harassment -> "Harassment"
    | Unsafe -> "Unsafe"
    | Other -> "Other"

  let reasons = [ Spam; Harassment; Unsafe; Other ]

  let reasonRow
    (
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      reason: ReportReason
    ) : Control =
    let option =
      RadioButton()
        .Name("ReportReason" + reasonName reason)
        .GroupName("ReportReason")
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .HorizontalContentAlignment(HorizontalAlignment.Left)
        .Content(reasonLabel reason)
        .IsEnabled(model |> Observable.map(fun current -> not current.Busy))
        .IsChecked(
          model
          |> Observable.map(fun current ->
            Nullable(current.Reason = Some reason))
        )
        .OnIsCheckedChangedHandler(fun toggle _ ->
          if toggle.IsChecked = Nullable true then
            dispatch(State.ReasonPicked reason))

    let row =
      Border().Padding(6.0, 3.0, 6.0, 3.0).CornerRadius(6.0).Child(option)

    Theme.bindClass(
      row,
      Theme.PrimarySoftBack,
      model |> Observable.map(fun current -> current.Reason = Some reason)
    )
    |> ignore

    row

  let preview(commit: Commit) : Control =
    Border()
      .Classes(Theme.name Theme.Card, Theme.name Theme.Hairline)
      .BorderThickness(Thickness(1.0))
      .CornerRadius(8.0)
      .Padding(10.0)
      .Margin(0.0, 4.0, 0.0, 4.0)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .Child(
        StackPanel()
          .Spacing(2.0)
          .Children(
            TextBlock()
              .Classes(Theme.name Theme.Mono)
              .Text("@" + commit.Author.Handle),
            TextBlock()
              .Text(commit.Body)
              .TextWrapping(TextWrapping.Wrap)
              .MaxLines(3)
              .TextTrimming(TextTrimming.CharacterEllipsis)
          )
      )

  let details
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    TextBox()
      .Name("ReportDetails")
      .MaxLength(500)
      .AcceptsReturn(true)
      .TextWrapping(TextWrapping.Wrap)
      .VerticalContentAlignment(VerticalAlignment.Top)
      .MinHeight(56.0)
      .Text(model |> Observable.map _.Details)
      .IsEnabled(model |> Observable.map(fun current -> not current.Busy))
      .OnTextChangedHandler(fun box _ ->
        box.Text
        |> Option.ofObj
        |> Option.defaultValue ""
        |> State.DetailsChanged
        |> dispatch)

  let attachButton
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let count =
      model
      |> Observable.map(fun current -> $"{List.length current.Attachments}/3")

    let button =
      Button()
        .Name("ReportAttachButton")
        .Classes(Theme.name Theme.GhostButton)
        .Padding(6.0, 2.0, 6.0, 2.0)
        .Content(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(4.0)
            .Children(
              Icons.iconAt(Glyph.Image, 16.0),
              TextBlock().Classes(Theme.name Theme.Mono).Text(count)
            )
        )
        .IsEnabled(
          model
          |> Observable.map(fun current ->
            List.length current.Attachments < 3 && not current.Busy)
        )
        .OnClickHandler(fun _ _ -> dispatch State.AttachRequested)

    ToolTip.SetTip(button, "Attach up to 3 images")
    button

  let strip
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let descriptions =
      ValueSome(fun index -> {
        Text =
          model
          |> Observable.map(fun current ->
            current.Descriptions
            |> List.tryItem index
            |> Option.defaultValue "")
          |> Observable.distinctUntilChanged
        Changed = fun text -> dispatch(State.DescriptionChanged(index, text))
      })

    let descriptionOf index =
      match descriptions with
      | ValueSome at -> ValueSome(at index)
      | ValueNone -> ValueNone

    let chips =
      model
      |> Observable.map _.Attachments
      |> Observable.distinctUntilChanged
      |> Observable.map(function
        | [] -> Panel() :> Control
        | staged ->
          StackPanel().Spacing(6.0).Children [|
            for index, image in List.indexed staged ->
              ImageChip.view {
                Image = image
                Index = index
                OnRemove = fun () -> dispatch(State.ImageRemoved index)
                MarkHandled = false
                Description = descriptionOf index
              }
          |])

    let host =
      ContentControl()
        .Content(chips |> _.ToBinding())
        .IsVisible(
          model
          |> Observable.map(fun current -> not current.Attachments.IsEmpty)
        )

    let _ =
      host.IsEnabled(model |> Observable.map(fun current -> not current.Busy))

    host

  let failureLine(model: IObservable<State.Model>) : Control =
    TextBlock()
      .Name("ReportFailure")
      .Classes(Theme.name Theme.FgDanger)
      .TextWrapping(TextWrapping.Wrap)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Failure |> ValueOption.defaultValue "")
      )
      .IsVisible(model |> Observable.map _.Failure.IsSome)

  let footer
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let label =
      model
      |> Observable.map(fun current ->
        if current.Busy then "Sending…" else "Send report")
      |> Observable.distinctUntilChanged

    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(8.0)
      .HorizontalAlignment(HorizontalAlignment.Right)
      .Children(
        Button()
          .Name("ReportCancel")
          .Classes(Theme.name Theme.GhostButton)
          .Content("Cancel")
          .IsEnabled(model |> Observable.map(fun current -> not current.Busy))
          .OnClickHandler(fun _ _ -> dispatch State.CloseRequested),
        Button()
          .Name("ReportSend")
          .Classes(Theme.name Theme.AccentButton)
          .Content(TextBlock().Text(label))
          .IsEnabled(
            model
            |> Observable.map(fun current ->
              current.Reason.IsSome && not current.Busy)
          )
          .OnClickHandler(fun _ _ -> dispatch State.SendRequested)
      )

module View =

  let body(props: Props<'env>, model, dispatch) : Control =
    let reasons =
      StackPanel().Spacing(2.0).Children [|
        for reason in Parts.reasons -> Parts.reasonRow(model, dispatch, reason)
      |]

    let hint =
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .TextWrapping(TextWrapping.Wrap)
        .Text("Add context for moderators. Up to 500 characters.")

    StackPanel()
      .Spacing(4.0)
      .Children(
        TextBlock().Classes(Theme.name Theme.Title).Text("Report commit"),
        TextBlock()
          .Classes(Theme.name Theme.Muted)
          .TextWrapping(TextWrapping.Wrap)
          .Text(
            "Your report is private. Attachments are visible only to you and moderators. Reporting does not hide this commit."
          ),
        Parts.preview props.Commit,
        TextBlock()
          .Classes(Theme.name Theme.Subtitle)
          .TextWrapping(TextWrapping.Wrap)
          .Text("Why are you reporting this commit?"),
        reasons,
        TextBlock()
          .Classes(Theme.name Theme.Subtitle)
          .Text("Details (optional)"),
        Parts.details(model, dispatch),
        hint,
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .Children(Parts.attachButton(model, dispatch)),
        Parts.strip(model, dispatch)
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =
  let body =
    ScrollViewer()
      .Name("ReportScroller")
      .ClipToBounds(true)
      .HorizontalScrollBarVisibility(ScrollBarVisibility.Disabled)
      .Content(View.body(props, model, dispatch))

  let footer =
    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 1.0, 0.0, 0.0))
      .Margin(0.0, 10.0, 0.0, 0.0)
      .Padding(0.0, 10.0, 0.0, 0.0)
      .Child(Parts.footer(model, dispatch))

  // The failure line sits under the scrolling body, above the footer:
  // an error is visible at rest, and no capture has to scroll to show it.
  let failure = Parts.failureLine(model).Margin(0.0, 8.0, 0.0, 0.0)

  let card = Grid()

  card.RowDefinitions <- RowDefinitions("*,Auto,Auto")

  Border()
    .Name("ReportDialogCard")
    .Classes(Theme.name Theme.Card)
    .MaxWidth(520.0)
    .MaxHeight(560.0)
    .Child(card.Children(body.Row(0), failure.Row(1), footer.Row(2)))

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideAttachmentSources>(props: Props<'env>) as this =
  inherit UserControl()

  do
    this.Name <- "ReportDialog"

    let program =
      Program.mkProgram
        (fun () -> State.initial, Cmd.none)
        (State.update props)
        (view props)

    let root, _ = Programs.runComponent(this, props.Env.Dispatcher, program)

    this.Content <- root

    this.DetachedFromVisualTree.Add(fun _ -> State.collectStaged props)
