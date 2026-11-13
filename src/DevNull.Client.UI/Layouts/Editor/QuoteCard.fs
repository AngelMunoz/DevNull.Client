module DevNull.Client.Editor.QuoteCard

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Threading
open NXUI.Extensions
open FSharp.Control.Reactive
open MVUish.Avalonia
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components.CommitCard
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Formatting
open DevNull.Client.ImageCache
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UiClock
open DevNull.Client.UserSettingsStore

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideDispatcher
  and 'env :> IProvideRemotes
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideBrowser> = {
  Env: 'env
  Session: SignedInSession
  Zone: Zone
  Commit: Commit
  Now: unit -> DateTimeOffset
  OnOpenThread: Commit -> unit
  OnReply: Commit -> unit
  OnQuote: Commit -> unit
  OnOpenRepo: string -> unit
}

type Model = {
  Commit: Commit
  Cards: EditorCards.State
  Deleted: bool
}

type Msg = CardMsg of EditorCards.Msg

let cardsProps(props: Props<'env>) : EditorCards.Props<'env> = {
  Env = props.Env
  Session = props.Session
  Zone = props.Zone
  Surface = EditorCards.Detail
  Now = props.Now
  OnOpenRepo = props.OnOpenRepo
}

let init(props: Props<'env>) = {
  Commit = props.Commit
  Cards = EditorCards.initial(cardsProps props)
  Deleted = false
}

let update(props: Props<'env>, msg: Msg, model: Model) =
  match msg with
  | CardMsg cardMsg ->
    let cards, cmd, effects =
      EditorCards.update(cardsProps props, cardMsg, model.Cards)

    let next =
      effects
      |> List.fold
        (fun (current: Model) effect ->
          match effect with
          | EditorCards.CommitChanged updated -> {
              current with
                  Commit = updated
            }
          | EditorCards.CommitDeleted _ -> { current with Deleted = true }
          | EditorCards.PinChanged(_, updated) -> {
              current with
                  Commit = updated
            }
          | EditorCards.Failed _ -> current)
        { model with Cards = cards }

    next, EditorCards.mapCmd(CardMsg, cmd)

let view
  (props: Props<'env>, model: IObservable<Model>, dispatch: Dispatch<Msg>)
  : Control =
  let cardDispatch msg = dispatch(CardMsg msg)

  let fallback =
    function
    | Opened commit -> props.OnOpenThread commit
    | ReplyToggled commit -> props.OnReply commit
    | QuoteRequested commit -> props.OnQuote commit
    | MoreRepliesRequested commit -> props.OnOpenThread commit
    | _ -> ()

  let cardStateEqual
    (left: bool * Commit * EditorCards.State)
    (right: bool * Commit * EditorCards.State)
    : bool =
    let deletedLeft, commitLeft, cardsLeft = left
    let deletedRight, commitRight, cardsRight = right

    deletedLeft = deletedRight
    && commitLeft.Id = commitRight.Id
    && (obj.ReferenceEquals(cardsLeft, cardsRight) || cardsLeft = cardsRight)

  let card =
    model
    |> Observable.map(fun current ->
      current.Deleted, current.Commit, current.Cards)
    |> Observable.distinctUntilChangedCompare
      {
        new Collections.Generic.IEqualityComparer<
          bool * Commit * EditorCards.State
             > with
          member _.Equals(left, right) = cardStateEqual left right

          member _.GetHashCode((deleted, commit, _)) =
            HashCode.Combine(deleted, commit.Id)
      }
    |> Observable.map(fun (deleted, commit, cards) ->
      let rowProps =
        EditorCards.row {
          Props = cardsProps props
          State = Observable.single cards
          Dispatch = cardDispatch
          OnEvent = EditorCards.event(cardDispatch, fallback)
        }

      let rowProps = { rowProps with Dense = true }

      if deleted then
        tombstone()
      else
        feedRow rowProps (LiveCommit commit))
    |> _.ToBinding()

  Grid()
    .Children(
      ContentControl().Content(card),
      EditorCards.layer(
        cardsProps props,
        model |> Observable.map _.Cards |> Slices.share,
        cardDispatch
      )
    )

let program(props: Props<'env>) : Program<unit, Model, Msg, Control> =
  Program.mkProgram
    (fun () -> init props, Cmd.none)
    (fun msg model -> update(props, msg, model))
    (fun model dispatch -> view(props, model, dispatch))

let mount(dispatcher: IDispatcher, props: Props<'env>) =
  DevNull.Client.Programs.runPage(dispatcher, program props)
