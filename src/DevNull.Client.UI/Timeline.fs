module DevNull.Client.Timeline

open System
open System.Collections.Generic
open System.Reactive.Subjects
open DevNull.Client.Core.ApiTypes

type Request =
  | Reload
  | ScrollToTop
  | CommitLanded

type TimelineService =
  abstract ForceReload: unit -> unit
  abstract ScrollToTop: unit -> unit
  abstract CommitLanded: unit -> unit
  abstract AnnounceDeletion: commitId: string -> unit
  abstract Requests: IObservable<Request>
  abstract Deletions: IObservable<string>
  abstract Remember: log: LogKind * offset: float -> unit
  abstract OffsetOf: log: LogKind -> float
  abstract RememberLog: log: LogKind -> unit
  abstract LastLog: LogKind option
  abstract RememberDraft: commitId: string * text: string -> unit
  abstract ForgetDraft: commitId: string -> unit
  abstract DraftOf: commitId: string -> string

type IProvideTimeline =
  abstract Timeline: TimelineService

module TimelineService =

  let gate = TimeSpan.FromSeconds 5.0

  type State = {
    Requests: Subject<Request>
    Deletions: Subject<string>
    LastAccepted: BehaviorSubject<DateTimeOffset>
    LastLog: BehaviorSubject<LogKind option>
    Scrolls: Dictionary<LogKind, float>
    Drafts: Dictionary<string, string>
  }

  let forceReload(state: State) =
    state.Requests.OnNext ScrollToTop

    if DateTimeOffset.UtcNow - state.LastAccepted.Value >= gate then
      state.LastAccepted.OnNext(DateTimeOffset.UtcNow)
      state.Requests.OnNext Reload

  let scrollToTop(state: State) = state.Requests.OnNext ScrollToTop

  let commitLanded(state: State) = state.Requests.OnNext CommitLanded

  let announceDeletion(state: State, commitId: string) =
    state.Deletions.OnNext commitId

  let requests(state: State) : IObservable<Request> = state.Requests

  let deletions(state: State) : IObservable<string> = state.Deletions

  let remember(state: State, log: LogKind, offset: float) =
    if offset > 0.0 then
      state.Scrolls[log] <- offset
    else
      state.Scrolls.Remove log |> ignore

  let offsetOf(state: State, log: LogKind) : float =
    match state.Scrolls.TryGetValue log with
    | true, offset -> offset
    | false, _ -> 0.0

  let rememberLog(state: State, log: LogKind) = state.LastLog.OnNext(Some log)

  let lastLog(state: State) : LogKind option = state.LastLog.Value

  let rememberDraft(state: State, commitId: string, text: string) =
    if text = "" then
      state.Drafts.Remove commitId |> ignore
    else
      state.Drafts[commitId] <- text

  let forgetDraft(state: State, commitId: string) =
    state.Drafts.Remove commitId |> ignore

  let draftOf(state: State, commitId: string) : string =
    match state.Drafts.TryGetValue commitId with
    | true, text -> text
    | false, _ -> ""

  let create() =
    let state: State = {
      Requests = new Subject<Request>()
      Deletions = new Subject<string>()
      LastAccepted =
        new BehaviorSubject<DateTimeOffset>(DateTimeOffset.MinValue)
      LastLog = new BehaviorSubject<LogKind option>(None)
      Scrolls = Dictionary<LogKind, float>()
      Drafts = Dictionary<string, string>()
    }

    {
      new TimelineService with
        member _.ForceReload() = forceReload state
        member _.ScrollToTop() = scrollToTop state
        member _.CommitLanded() = commitLanded state
        member _.AnnounceDeletion(commitId) = announceDeletion(state, commitId)
        member _.Requests = requests state
        member _.Deletions = deletions state
        member _.Remember(log, offset) = remember(state, log, offset)
        member _.OffsetOf(log) = offsetOf(state, log)

        member _.RememberLog(log) = rememberLog(state, log)
        member _.LastLog = lastLog state

        member _.RememberDraft(commitId, text) =
          rememberDraft(state, commitId, text)

        member _.ForgetDraft(commitId) = forgetDraft(state, commitId)
        member _.DraftOf(commitId) = draftOf(state, commitId)
    }
