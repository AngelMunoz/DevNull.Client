module DevNull.Client.Workbench.RowActions

open System
open System.Reactive.Subjects
open Avalonia.Threading
open IcedTasks
open Microsoft.Extensions.Logging
open FSharp.Control.Reactive
open DevNull.Client.Components.CommitCard
open DevNull.Client.Components.CommitMenu
open DevNull.Client.Core.Api
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Env
open DevNull.Client.Errors
open DevNull.Client.Profiles
open DevNull.Client.Stores
open DevNull.Client.TranslationCache
open DevNull.Client.UserSettingsStore
open DevNull.Client.Views
open DevNull.Client.Views.TranslationModel

type SurfaceProps<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideDispatcher
  and 'env :> IProvideProfiles
  and 'env :> IProvideTranslations
  and 'env :> IProvideUserSettings
  and 'env :> IProvideLogging> = {
  Env: 'env
  Session: SignedInSession
  Now: IObservable<DateTimeOffset>
  OfferBasicActions: bool
  OnUpdated: Commit -> unit
  OnDeleted: string -> unit
  OnReport: Commit -> unit
  OnShare: Commit -> unit
}

type SurfaceView = {
  Star: ActionSlot voption
  Vote: ActionSlot voption
  Pastel: ActionSlot voption
  Menu: MenuSlot voption
  Translation: IObservable<Commit -> TranslationView voption>
  Busy: IObservable<string option>
  Handle: Event -> unit
}

let private unreadRelation: AuthorRelation = {
  IsFollowing = false
  IsMuting = false
  IsBlocking = false
}

let private toRelation(loaded: ProfileDetails) : AuthorRelation = {
  IsFollowing = loaded.IsFollowing
  IsMuting = loaded.IsMuting
  IsBlocking = false
}

let create(props: SurfaceProps<'env>) : SurfaceView =
  let busy = new BehaviorSubject<string option>(None)
  let relations = new BehaviorSubject<Map<string, AuthorRelation>>(Map.empty)
  let choices = new BehaviorSubject<Map<string, string>>(Map.empty)
  let book = new BehaviorSubject<Book>(initial)

  let logger =
    props.Env.LoggerFactory.CreateLogger "DevNull.Client.Workbench.RowActions"

  let post(action: unit -> unit) =
    props.Env.Dispatcher.Post(Action action, DispatcherPriority.Default)

  let held(handle: string) : AuthorRelation =
    relations.Value |> Map.tryFind handle |> Option.defaultValue unreadRelation

  let setBusy(commitId: string option) = post(fun () -> busy.OnNext commitId)

  let setRelation(handle: string, relation: AuthorRelation) =
    post(fun () -> relations.OnNext(relations.Value |> Map.add handle relation))

  let setBook(next: Book) = post(fun () -> book.OnNext next)

  let updateCommit(commit: Commit) = post(fun () -> props.OnUpdated commit)

  let failure(what: string, error: AppError) =
    post(fun () ->
      logger.LogWarning("The {Action} failed: {Error}", what, describe error))

  let runCommit
    (
      what: string,
      commit: Commit,
      start: unit -> Async<Result<Commit, AppError>>
    ) =
    setBusy(Some commit.Id)

    Async.Start(
      asyncEx {
        let! result = start()

        setBusy None

        match result with
        | Ok updated -> updateCommit updated
        | Error error -> failure(what, error)
      }
    )

  let star(commit: Commit) =
    runCommit(
      "star",
      commit,
      fun () -> CommitActions.toggleStar(props.Env, props.Session, commit)
    )

  let pastel(commit: Commit) =
    setBusy(Some commit.Id)

    Async.Start(
      asyncEx {
        let! result =
          CommitActions.pastel(
            props.Env,
            {
              Session = props.Session
              Commit = commit
            }
          )

        setBusy None

        match result with
        | Ok given -> updateCommit given.Commit
        | Error error -> failure("pastel", error)
      }
    )

  let vote(commit: Commit) =
    match selectedOption(choices.Value, commit) with
    | None -> ()
    | Some option ->
      setBusy(Some commit.Id)

      Async.Start(
        asyncEx {
          let! result =
            CommitActions.vote(
              props.Env,
              {
                Session = props.Session
                Commit = commit
                Option = option
              }
            )

          setBusy None

          match result with
          | Ok updated -> updateCommit updated
          | Error error -> failure("vote", error)
        }
      )

  let loadRelation(commit: Commit) =
    let handle = Author.handle commit.Author

    if
      Author.id commit.Author = props.Session.Session.User.Id
      || relations.Value |> Map.containsKey handle
    then
      ()
    else
      Async.Start(
        asyncEx {
          let! details = props.Env.Profiles.Fetch(handle)

          match details with
          | Some loaded -> setRelation(handle, toRelation loaded)
          | None -> ()
        }
      )

  let toggleFollow(commit: Commit) =
    let handle = Author.handle commit.Author
    let before = held handle

    setBusy(Some commit.Id)

    setRelation(
      handle,
      {
        before with
            IsFollowing = not before.IsFollowing
      }
    )

    Async.Start(
      asyncEx {
        let! result =
          CommitActions.toggleFollow(
            props.Env,
            {
              Session = props.Session
              Author = commit.Author
              Flag = before.IsFollowing
            }
          )

        setBusy None

        match result with
        | Ok() -> ()
        | Error error ->
          setRelation(handle, before)
          failure("follow", error)
      }
    )

  let toggleMute(commit: Commit) =
    let handle = Author.handle commit.Author
    let before = held handle

    setBusy(Some commit.Id)

    setRelation(
      handle,
      {
        before with
            IsMuting = not before.IsMuting
      }
    )

    Async.Start(
      asyncEx {
        let! result =
          CommitActions.toggleMute(
            props.Env,
            {
              Session = props.Session
              Author = commit.Author
              Flag = before.IsMuting
            }
          )

        setBusy None

        match result with
        | Ok mute ->
          setRelation(handle, { before with IsMuting = mute.IsMuting })
        | Error error ->
          setRelation(handle, before)
          failure("mute", error)
      }
    )

  let blockAuthor(commit: Commit) =
    let handle = Author.handle commit.Author
    let before = held handle

    setBusy(Some commit.Id)

    Async.Start(
      asyncEx {
        let! result =
          CommitActions.blockAuthor(
            props.Env,
            {
              Session = props.Session
              Author = commit.Author
              Flag = before.IsBlocking
            }
          )

        setBusy None

        match result with
        | Ok blocked ->
          setRelation(
            handle,
            {
              before with
                  IsBlocking = blocked.IsBlocking
            }
          )
        | Error error ->
          setRelation(handle, before)
          failure("block", error)
      }
    )

  let delete(commit: Commit) =
    setBusy(Some commit.Id)

    Async.Start(
      asyncEx {
        let! result =
          Commits.delete(
            props.Env,
            {
              KeyFile = props.Session.KeyFile
              CommitId = commit.Id
            }
          )

        setBusy None

        match result with
        | Ok() -> post(fun () -> props.OnDeleted commit.Id)
        | Error error -> failure("delete", error)
      }
    )

  let translate(commit: Commit) =
    if canReuse(book.Value, commit) then
      setBook(showTranslated(book.Value, commit.Id))
    else
      Async.Start(
        asyncEx {
          let cache = (props.Env :> IProvideTranslations).Translations
          let settings = props.Env.UserSettings.Current
          let! recalled = recall(cache, book.Value, commit.Id)

          match recalled with
          | Some found -> setBook found
          | None ->
            let marked = request(book.Value, commit.Id)
            setBook marked

            let ask() =
              Commits.translate(
                props.Env,
                {
                  KeyFile = props.Session.KeyFile
                  CommitId = commit.Id
                  Language = languageFor(settings, commit)
                }
              )

            let! result = execute(retryDelays, ask)
            let now = DateTimeOffset.UtcNow

            let args: Finish = {
              Book = marked
              Now = now
              CommitId = commit.Id
              Result = result
            }

            setBook(finish args)
            Async.Start(store(cache, args))
        }
      )

  let menu: MenuSlot = {
    Share =
      if props.Session.Session.Remotes.IsSome then
        ValueSome props.OnShare
      else
        ValueNone
    Report = ValueSome props.OnReport
    Delete = ValueSome delete
    ToggleFollow = toggleFollow
    ToggleMute = toggleMute
    Block = blockAuthor
    Relations =
      relations
      |> Observable.map(fun current ->
        fun commit ->
          current
          |> Map.tryFind(Author.handle commit.Author)
          |> ValueOption.ofOption)
    Busy = busy :> IObservable<string option>
  }

  let handle(event: Event) =
    match event with
    | MenuOpened commit -> loadRelation commit
    | TranslateRequested commit -> translate commit
    | OriginalRequested commit -> setBook(showOriginal(book.Value, commit.Id))
    | PollChoiceChanged(commit, option, chosen) ->
      let next =
        choose {
          Choices = choices.Value
          CommitId = commit.Id
          OptionId = option.Id
          Chosen = chosen
        }

      choices.OnNext next
    | _ -> ()

  let starSlot =
    if props.OfferBasicActions then
      ValueSome {
        Command = star
        Busy = busy :> IObservable<string option>
      }
    else
      ValueNone

  let voteSlot =
    if props.OfferBasicActions then
      ValueSome {
        Command = vote
        Busy = busy :> IObservable<string option>
      }
    else
      ValueNone

  let pastelSlot =
    if props.OfferBasicActions then
      ValueSome {
        Command = pastel
        Busy = busy :> IObservable<string option>
      }
    else
      ValueNone

  let translation =
    Observable.combineLatest (book :> IObservable<Book>) props.Now
    |> Observable.map(fun (current, moment) ->
      fun commit ->
        view {
          Automatic = props.Session.Session.AutomaticTranslation
          Book = current
          Now = moment
          Commit = commit
        })

  {
    Star = starSlot
    Vote = voteSlot
    Pastel = pastelSlot
    Menu = ValueSome menu
    Translation = translation
    Busy = busy :> IObservable<string option>
    Handle = handle
  }
