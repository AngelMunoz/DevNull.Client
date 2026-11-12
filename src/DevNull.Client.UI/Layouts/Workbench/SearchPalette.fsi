module DevNull.Client.Workbench.SearchPalette

open System
open Avalonia.Controls
open MVUish.Avalonia
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Errors
open DevNull.Client.ImageCache

/// The palette's state: the typed term, the people read, the notice a
/// too-short term gives, and the people the server answered.
type Model = {
  Term: string
  Loading: bool
  Error: AppError option
  People: User list
  Searched: bool
  Notice: string option
}

/// The palette's messages: the term, its people read, a picked person,
/// the commit search hand-off, and the close request.
type Msg =
  | TermChanged of text: string
  | PeopleRequested
  | PeopleFetched of Result<ProfileMatches, AppError>
  | PersonPicked of User
  | CommitsRequested
  | CloseRequested

/// What the palette needs from its host: the protocol workers, the
/// signed key file, the image cache, and the three sinks that leave it.
type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp> = {
  Env: 'env
  KeyFile: string
  Images: ImageCacheService
  OnOpenRepository: string -> unit
  OnSearchCommits: string -> unit
  OnClose: unit -> unit
}

/// The people term the palette sends: trimmed, without the leading `@`.
val inline peopleTermOf: raw: string -> string voption

/// The commit term the palette hands to the center list: the server's
/// three-to-hundred-character window.
val inline commitTermOf: raw: string -> string voption

/// The palette's first state; it reads nothing until the reader asks.
val init: props: Props<'env> -> Model * Cmd<Msg>

/// The palette's one update function.
val update: props: Props<'env> * msg: Msg * model: Model -> Model * Cmd<Msg>

/// The palette's overlay.
val view:
  props: Props<'env> ->
  model: IObservable<Model> ->
  dispatch: Dispatch<Msg> ->
    Control

/// The palette's program. It has no subscription and no poll.
val program: props: Props<'env> -> Program<unit, Model, Msg, Control>
