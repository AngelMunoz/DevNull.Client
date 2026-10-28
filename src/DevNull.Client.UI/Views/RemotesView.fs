module DevNull.Client.Views.RemotesView

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Controls.Shapes
open Avalonia.Controls.Templates
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open Avalonia.VisualTree
open NXUI.Extensions
open FSharp.Control.Reactive
open FSharp.UMX
open MVUish.Avalonia
open MVUish.Avalonia.Collections
open IcedTasks
open Navs
open DevNull.Client
open DevNull.Client.AttachmentSources
open DevNull.Client.Components
open DevNull.Client.Core
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Core.Types
open DevNull.Client.Errors
open DevNull.Client.Formatting
open DevNull.Client.Icons
open DevNull.Client.ImageCache
open DevNull.Client.Natives
open DevNull.Client.Profiles
open DevNull.Client.Remotes
open DevNull.Client.RemotesState
open DevNull.Client.SettingsStore
open DevNull.Client.Stores
open DevNull.Client.Theme
open DevNull.Client.UiClock

module CoreRemotes = DevNull.Client.Core.Api.Remotes

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideNatives
  and 'env :> IProvideRemotes> = {
  Env: 'env
  Session: SignedInSession
  Nav: INavigable
  Context: RouteContext
}

/// The page's state logic: model, messages, update, the row shaping,
/// and the subscriptions that feed it from outside.
module State =

  /// One search's answer: the matching conversations, and the people a
  /// new remote can start with.
  type SearchFound = {
    Term: string
    Conversations: Conversation list
    Candidates: RemoteCandidate list
  }

  /// Where the thread reading goes next.
  type Landing =
    | Message of id: int
    | Present

  type Model = {
    /// The store's snapshot, live through the subscription.
    State: RemotesState
    Tab: InboxTab
    /// The search box's raw text.
    Query: string
    Found: SearchFound option
    /// Whether the tab's next page was already asked for, so the
    /// scroll-end probe cannot ask twice.
    AskedMore: bool
    /// Whether the account takes fetched requests at all.
    RequestsOn: bool
    /// The shared clock's last tick, for the relative times.
    Now: DateTimeOffset
    /// Whether the thread reading rides the bottom edge.
    Riding: bool
    /// Where the thread reading goes next: one message a reply ghost
    /// asked for, or the present edge. The pane lands, then clears.
    Landing: Landing option
    /// The newest message id a read was already sent through, so the
    /// read goes out once per arrival, not once per scroll tick.
    MarkedThrough: int
    /// The conversation key the composer's draft belongs to; a new
    /// target reloads the draft from the settings.
    DraftKey: string
    /// The composer's text.
    Draft: string
    /// The staged images and their descriptions.
    Images: ImageAttachment list
    Descriptions: string list
    /// Which link the pushed message's card takes.
    LinkChoice: LinkCard
    /// The staged voice note, which rides alone when it rides.
    Voice: (ImageAttachment * Waveform) option
    /// Whether the microphone is recording now.
    Recording: bool
    /// The org-create picker's chosen name and members.
    OrgName: string
    OrgMembers: string list
    /// The share-history choice of the org-add picker.
    OrgShare: bool
    /// The rename picker's text.
    OrgRename: string
    /// One line the menus say when the server refuses.
    Notice: string option
    /// The newest message id the reader saw while riding the bottom
    /// edge; messages past it count as unseen once the reader leaves.
    SeenThrough: int
    /// Messages that arrived while the reader was away from the bottom
    /// edge, the count the "N new" pill carries.
    Unseen: int
    /// Whether a whole viewport of messages sits below the reading.
    Behind: bool
  }

  type Msg =
    | StoreChanged of RemotesState
    | ClockTicked of moment: DateTimeOffset
    | SessionChanged of state: SessionState
    | TabChosen of InboxTab
    | Searched of text: string
    | SearchLanded of term: string * Result<SearchFound, AppError>
    | MoreWanted
    | Opened of ChatTarget
    | Closed
    | SettleWanted of partner: User * merging: bool
    | Settled of taken: bool
    | OlderWanted
    | RideMoved of riding: bool * behind: bool
    | ThreadScrolled of offset: float
    | GhostWanted of messageId: int
    | Walked of messageId: int * found: bool
    | Landed
    | PresentWanted
    | PreviewsChecked
    | ReadSettled of went: bool
    | StoreWorkDone
    | DraftChanged of text: string
    | DraftSettled of text: string
    | ImageRemoved of index: int
    | Described of index: int * text: string
    | LinkChosen of LinkCard
    | ReplyWanted of Message
    | ReplyCancelled
    | SendWanted
    | RetryWanted of clientId: string<ClientId>
    | PickWanted
    | Picked of ImageAttachment list
    | MuteWanted of MuteDuration
    | PinWanted of pinned: bool
    | ArchiveWanted of archived: bool
    | ActionSettled of Result<unit, AppError>
    | BlockWanted of partner: User
    | Blocked of Result<unit, AppError>
    | OrgCreateWanted
    | OrgNameChanged of name: string
    | OrgCreated of Result<OrgRef, AppError>
    | OrgRenameWanted
    | OrgRenameChanged of name: string
    | OrgRenamed of Result<unit, AppError>
    | OrgAddWanted
    | OrgMemberChanged of username: string
    | OrgShareChanged of shared: bool
    | OrgMembersAdded of Result<unit, AppError>
    | OrgRoleWanted of username: string * role: OrgRole
    | OrgRoleSet of Result<unit, AppError>
    | OrgRemoveWanted of username: string * leaving: bool
    | OrgRemoved of left: bool * Result<unit, AppError>
    | MenuNotice of message: string
    | ReactWanted of messageId: int * emoji: string
    | ReactSettled of took: bool
    | RecordWanted
    | RecordCancelled
    | VoiceStaged of Result<(ImageAttachment * Waveform), string>
    | VoiceDiscarded

  /// The search term the raw box text stands for: trimmed, with the
  /// leading `@` signs taken off.
  let inline termOf(query: string) : string = query.Trim().TrimStart('@')

  /// The tabs the inbox shows: the fetched requests only while the
  /// account takes them.
  let tabsOf(requestsOn: bool) : (InboxTab * string) list = [
    TabOpen, "Open"

    if requestsOn then
      TabFetched, "Fetched"

    TabStashed, "Stashed"
  ]

  /// One handle the reader knows: `you` for the viewer, `@name` for
  /// anyone else, `someone` for a user the payload did not carry.
  let handleOf(who: User option, viewerId: string) : string =
    match who with
    | None -> "someone"
    | Some user -> if user.Id = viewerId then "you" else "@" + user.Handle

  /// What one logged org change says, from the reader's point of view.
  let eventText(viewerId: string, message: Message) : string =
    match message.Event with
    | None -> message.Body
    | Some event ->
      let actor = handleOf(message.User, viewerId)
      let subject = handleOf(message.Subject, viewerId)

      match event with
      | Created ->
        if message.Body = "" then
          $"{actor} created the org"
        else
          $"{actor} created {message.Body}"
      | Added -> $"{actor} added {subject}"
      | Removed -> $"{actor} removed {subject}"
      | Left -> $"{actor} left"

      | Renamed ->
        if message.Body = "" then
          $"{actor} cleared the org's name"
        else
          $"{actor} renamed the org to {message.Body}"
      | Promoted -> $"{actor} made {subject} a maintainer"
      | Demoted -> $"{actor} made {subject} a contributor"
      | Inherited -> $"{actor} inherited maintainer"
      | UnknownEvent _ -> message.Body

  /// One org's name: its own, or its other members' first names.
  let orgTitle(viewerId: string, row: Conversation) : string =
    match row.Name with
    | Some name -> name
    | None ->
      let others =
        row.Members
        |> Option.defaultValue []
        |> List.filter(fun member' -> member'.User.Id <> viewerId)

      match others with
      | [] -> "Just you"
      | _ ->
        others
        |> List.map(fun member' -> member'.User.Name.Split(' ')[0])
        |> String.concat ", "

  /// One row's excerpt: what the last message says, and who said it.
  type Excerpt = {
    Prefix: string
    Text: string
    IsEvent: bool
  }

  /// The last message as the row previews it: the event line for an
  /// org event, the body otherwise, with a voice note or an image
  /// standing in for an empty one.
  let excerptOf(viewerId: string, row: Conversation) : Excerpt =
    match row.LastMessage with
    | None -> {
        Prefix = ""
        Text = ""
        IsEvent = false
      }
    | Some last ->
      if last.Event.IsSome then
        {
          Prefix = ""
          Text = eventText(viewerId, last)
          IsEvent = true
        }
      else
        let body =
          if last.Body <> "" then last.Body
          elif last.Voice.IsSome then "sent a voice note"
          else "sent an image"

        let prefix =
          if last.UserId = viewerId then
            "you: "
          else
            match row.Partner with
            | None ->
              let who =
                last.User
                |> Option.map(fun user -> user.Name.Split(' ')[0])
                |> Option.defaultValue "someone"

              who + ": "
            | Some _ -> ""

        {
          Prefix = prefix
          Text = body
          IsEvent = false
        }

  /// One row of the inbox column: a conversation of one tab, or a
  /// person a new remote can start with. The tab rides the row so a
  /// recycled row knows which settle actions it carries.
  type InboxRow =
    | Conversation of tab: InboxTab * row: Conversation
    | Candidate of person: RemoteCandidate

  /// The rows the column shows: the search's answer while one stands,
  /// the tab's list otherwise.
  let rowsOf(model: Model) : InboxRow list =
    match model.Found with
    | Some found -> [
        for row in found.Conversations -> InboxRow.Conversation(TabOpen, row)

        for person in found.Candidates -> InboxRow.Candidate person
      ]
    | None ->
      let items =
        (listTab(model.State, model.Tab)).Items |> Option.defaultValue []

      List.map (fun row -> InboxRow.Conversation(model.Tab, row)) items

  /// One row's stable key.
  let rowKey(row: InboxRow) : string =
    match row with
    | InboxRow.Conversation(_, row) -> "row:" + ChatTargets.summaryKey row
    | InboxRow.Candidate person -> "new:" + person.User.Handle

  /// Whether one tab's list never loaded.
  let inline isLoading(state: RemotesState, tab: InboxTab) : bool =
    (listTab(state, tab)).Items.IsNone

  /// The empty line each tab says when its list holds nothing.
  let emptyLine(tab: InboxTab) : string =
    match tab with
    | TabOpen -> "no conversations yet"
    | TabFetched -> "no requests fetched"
    | TabStashed -> "nothing stashed"

  /// One debounced search read: the conversations the term matches in
  /// the tab, and the people a new remote can start with while the
  /// open tab stands.
  [<Struct>]
  type SearchQuery = {
    Session: SignedInSession
    Tab: InboxTab
    Term: string
  }

  let search
    (
      env:
        #IProvideClock & #IProvideNonceSource & #IProvideSigner & #IProvideHttp,
      query: SearchQuery
    ) : Async<Result<SearchFound, AppError>> = asyncEx {
    do! Async.Sleep 150

    let! page =
      CoreRemotes.inbox(
        env,
        {
          KeyFile = query.Session.KeyFile
          Tab = query.Tab
          Search = Some query.Term
          Before = None
        }
      )

    match page with
    | Error error -> return Error error
    | Ok page ->
      if query.Tab <> TabOpen then
        return
          Ok {
            Term = query.Term
            Conversations = page.Conversations
            Candidates = []
          }
      else
        let! people =
          CoreRemotes.candidates(
            env,
            {
              KeyFile = query.Session.KeyFile
              Search = Some query.Term
              Org = None
              For = Some CoreRemotes.ForRemote
            }
          )

        return
          match people with
          | Error error -> Error error
          | Ok people ->
            Ok {
              Term = query.Term
              Conversations = page.Conversations
              Candidates = List.truncate 5 people
            }
  }

  /// One tab's next page, through the store.
  let loadTab(store: RemotesStore, tab: InboxTab) : Async<unit> =
    store.LoadTab tab

  /// One staged message as the thread shows it: the message on its
  /// way in, or one the server refused.
  type Bubble =
    | Sent of Message
    | Staged of PendingSend

  /// One author's run of bubbles: consecutive messages by one side
  /// that render as one stacked group.
  type Run = {
    Key: string
    IsMine: bool
    AuthorId: string
    /// The org author's user payload, for the label an org run wears.
    Author: User option
    Bubbles: Bubble list
  }

  /// One row of the thread: a day break, the unread divider, an org
  /// event, or one author's run.
  type ThreadRow =
    | Day of key: string * label: string
    | Head of count: int
    | Event of message: Message
    | Run of run: Run

  /// One bubble as the row fold sees it: what it carries, when it
  /// landed, and who wrote it.
  type FoldedBubble = {
    Kind: Bubble
    At: DateTimeOffset
    IsMine: bool
    AuthorId: string
    Key: string
    IsEvent: bool
    Id: int
  }

  /// The thread's rows: the messages and staged sends folded into day
  /// breaks, the unread divider above the first message the reader
  /// had not seen, quiet event lines, and author runs. The web's
  /// `itemsOf`, on the store's thread state.
  let threadRows
    (viewerId: string, isOrg: bool, now: DateTimeOffset, thread: ThreadState)
    : ThreadRow list =
    let bubbles: FoldedBubble list = [
      for message in thread.Messages ->
        {
          Kind = Sent message
          At = message.CreatedAt
          IsMine = message.UserId = viewerId
          AuthorId = message.UserId
          Key = UMX.untag message.ClientId
          IsEvent = message.Event.IsSome
          Id = message.Id
        }

      for pending in thread.Pending ->
        {
          Kind = Staged pending
          At = pending.CreatedAt
          IsMine = true
          AuthorId = viewerId
          Key = UMX.untag pending.ClientId
          IsEvent = false
          Id = Int32.MaxValue
        }
    ]

    let unread =
      thread.Messages
      |> List.filter(fun message ->
        message.Id > thread.Head
        && message.Id <= thread.HeadUntil
        && message.UserId <> viewerId
        && message.Event.IsNone)

    let firstUnread =
      unread
      |> List.tryHead
      |> Option.bind(fun message ->
        bubbles
        |> List.tryFindIndex(fun bubble ->
          bubble.Id = message.Id && not bubble.IsEvent))

    let orgAuthor(bubble: FoldedBubble) : User option =
      if isOrg && not bubble.IsMine then
        match bubble.Kind with
        | Sent message -> message.User
        | Staged _ -> None
      else
        None

    let startDay: string option = None
    let startRows: ThreadRow list = []

    let (_, rows) =
      ((startDay, startRows), List.indexed bubbles)
      ||> List.fold(fun (day, rows) (index, bubble) ->
        let dayKey = bubble.At.LocalDateTime.ToString("yyyy-MM-dd")

        let rows =
          if Some dayKey <> day then
            rows @ [ Day(dayKey, dayLabel(now, bubble.At)) ]
          else
            rows

        let rows =
          match firstUnread with
          | Some at when at > 0 && at = index ->
            rows @ [ Head(List.length unread) ]
          | _ -> rows

        match bubble.Kind with
        | Sent message when message.Event.IsSome ->
          Some dayKey, rows @ [ Event message ]
        | _ ->
          let run: Run = {
            Key = bubble.Key
            IsMine = bubble.IsMine
            AuthorId = bubble.AuthorId
            Author = orgAuthor bubble
            Bubbles = [ bubble.Kind ]
          }

          let joined =
            match List.tryLast rows with
            | Some(ThreadRow.Run last) when
              last.IsMine = bubble.IsMine && last.AuthorId = bubble.AuthorId
              ->
              (rows |> List.removeAt(List.length rows - 1))
              @ [
                ThreadRow.Run {
                  last with
                      Bubbles = last.Bubbles @ [ bubble.Kind ]
                }
              ]
            | _ -> rows @ [ ThreadRow.Run run ]

          Some dayKey, joined)

    rows

  /// One thread row's stable key.
  let threadRowKey(row: ThreadRow) : string =
    match row with
    | Day(key, _) -> "day:" + key
    | Head _ -> "head"
    | Event message -> $"event:{message.Id}"
    | Run run -> "run:" + run.Key

  /// What the typing line says: one name, two names, or the crowd.
  let typingText(who: string list) : string =
    match who with
    | [] -> ""
    | [ only ] -> $"{only} is typing…"
    | [ one; two ] -> $"{one} and {two} are typing…"
    | crowd -> $"{List.length crowd} people are typing…"

  /// The typing names one thread carries, as handles: the partner, or
  /// the org's members.
  let typingNames(isOrg: bool, thread: ThreadState) : string list =
    if isOrg then
      thread.Typing |> List.map(fun handle -> "@" + handle)
    else if thread.PartnerTyping then
      [
        match thread.Target with
        | Partner user -> "@" + user.Handle
        | Org _ -> ()
      ]
    else
      []

  /// The message the receipts stand under: my newest one in a
  /// conversation, the newest one of anyone in an org.
  let statusOf
    (isOrg: bool, viewerId: string, thread: ThreadState)
    : Message option =
    let carries(message: Message) =
      message.Event.IsNone && (isOrg || message.UserId = viewerId)

    thread.Messages |> List.tryFindBack carries

  /// One chip per emoji: how many left it, whether the viewer's is
  /// among them, and who they are, in the order each was first left.
  type ReactionSummary = {
    Emoji: string
    Count: int
    IsMine: bool
    Who: string list
  }

  /// The reactions one click away before the user has any history:
  /// joy, check, and heart.
  let defaultsQuick: string list = [ "😂"; "✅"; "❤️" ]

  /// The reactions one message carries, folded to one chip per emoji.
  let summarizeReactions
    (reactions: Reaction list, viewerId: string)
    : ReactionSummary list =
    (Map.empty, reactions)
    ||> List.fold(fun byEmoji reaction ->
      let summary =
        byEmoji
        |> Map.tryFind reaction.Emoji
        |> Option.defaultValue {
          Emoji = reaction.Emoji
          Count = 0
          IsMine = false
          Who = []
        }

      Map.add
        reaction.Emoji
        {
          summary with
              Count = summary.Count + 1
              IsMine = summary.IsMine || reaction.UserId = viewerId
              Who = summary.Who @ [ reaction.UserId ]
        }
        byEmoji)
    |> Map.values
    |> List.ofSeq

  /// The three reactions one click away: joy, check, and heart at
  /// first; an emoji the user clearly prefers takes the weakest slot
  /// in place.
  let quickReactions(usage: ReactionUse list) : string list =
    let countOf(emoji: string) =
      usage
      |> List.tryFind(fun reaction -> reaction.Emoji = emoji)
      |> Option.map _.Count
      |> Option.defaultValue 0

    let weakestOf(slots: string list) : int =
      slots
      |> List.indexed
      |> List.minBy(fun (index, slot) -> countOf slot, index)
      |> fst

    let take (slots: string list) (reaction: ReactionUse) : string list =
      if List.contains reaction.Emoji slots then
        slots
      else
        let weakest = weakestOf slots

        if reaction.Count >= countOf slots[weakest] + 3 then
          slots |> List.updateAt weakest reaction.Emoji
        else
          slots

    let ranked = usage |> List.sortByDescending _.Count

    List.fold take defaultsQuick ranked

  /// The emoji the user reacts with, most used first, past the quick
  /// three.
  let frequentReactions(usage: ReactionUse list) : string list =
    usage |> List.sortByDescending _.Count |> List.map _.Emoji

  /// One fetched request settled, through the store.
  let settle(store: RemotesStore, partner: User, merging: bool) : Async<bool> =
    store.SettleRequest(partner, merging)

  /// The page above the loaded messages, through the store.
  let loadOlder(store: RemotesStore, target: ChatTarget) : Async<unit> =
    store.LoadOlder target

  /// Older pages until one message sits in the thread, through the
  /// store.
  let walkTo
    (store: RemotesStore, target: ChatTarget, messageId: int)
    : Async<bool> =
    store.LoadUntil(target, messageId)

  /// One message's link card read again, through the store.
  let preview
    (store: RemotesStore, target: ChatTarget, messageId: int)
    : Async<unit> =
    store.RefreshLinkPreview(target, messageId)

  /// One thread pulled through its newest message, through the store.
  /// Nothing goes out while the reading does not ride the bottom edge
  /// or the window stands inactive.
  let markRead
    (store: RemotesStore, target: ChatTarget, focused: bool)
    : Async<bool> =
    store.MarkRead(target, focused)

  /// The 48 loudness bars of one finished recording, sampled evenly
  /// across its samples, 0 to 100.
  let barsOf(samples: int16 array) : int list = [
    for bar in 0..47 ->
      let from' = bar * samples.Length / 48
      let upto = (bar + 1) * samples.Length / 48

      let peak =
        seq { from' .. max from' (upto - 1) }
        |> Seq.map(fun at -> abs(int samples[at]))
        |> Seq.append(Seq.singleton 0)
        |> Seq.max

      min 100 (peak * 100 / 32768)
  ]

  /// One recording's bytes staged as the push API takes them: a voice
  /// attachment and its waveform, or the failure that named why not.
  let stageVoice(bytes: byte[]) : Result<(ImageAttachment * Waveform), string> =
    let parsed: Result<DevNull.Client.Core.Commit.Voice, AppError> =
      DevNull.Client.Core.Commit.Voices.parse("voice.wav", bytes)

    let waveform: Result<Waveform, AppError> =
      DevNull.Client.Core.Commit.Voices.parseWaveform(
        barsOf(VoiceMedia.samples bytes |> Option.defaultValue [||])
      )

    match parsed, waveform with
    | Ok _, Ok bars ->
      Ok(
        ({
          FileName = "voice.wav"
          Bytes = bytes
         },
         bars)
      )
    | Error failure, _ -> Error(describe failure)
    | _, Error failure -> Error(describe failure)

  /// The links one draft carries, first-wins order.
  let linksOf(text: string) : string list =
    text.Split(' ', '\n', '\r', '\t')
    |> Array.choose(fun word ->
      match Uri.TryCreate(word.Trim(), UriKind.Absolute) with
      | true, NonNull uri when uri.Scheme = "http" || uri.Scheme = "https" ->
        Some(uri.ToString())
      | _ -> None)
    |> Array.distinct
    |> List.ofArray

  /// One link as the picker names it: the address without its scheme.
  let plainLink(url: string) : string =
    url
    |> fun url ->
      if url.StartsWith "https://" then url.Substring 8
      elif url.StartsWith "http://" then url.Substring 7
      else url

  /// The draft one conversation parked, or none.
  let stashedDraft(props: Props<'env>, key: string) : string option =
    props.Env.Settings.Current.ChatDrafts
    |> Map.tryFind key
    |> Option.filter(fun text -> text <> "")

  /// One debounced beat, so the stash parks only once the typing
  /// rests.
  let rest() : Async<unit> = async { do! Async.Sleep 300 }

  /// One push, through the store.
  let push
    (store: RemotesStore, target: ChatTarget, message: OutgoingMessage)
    : Async<unit> =
    store.Push(target, message)

  /// One refused send staged again, through the store.
  let retry
    (store: RemotesStore, target: ChatTarget, clientId: string<ClientId>)
    : Async<unit> =
    store.Retry(target, clientId)

  let init (props: Props<'env>) () : Model * Cmd<Msg> =
    let requestsOn =
      props.Session.Session.Remotes
      |> Option.map _.Requests
      |> Option.defaultValue false

    let key =
      props.Env.Remotes.Current.OpenTarget
      |> Option.map ChatTargets.key
      |> Option.defaultValue ""

    let draft = stashedDraft(props, key)

    {
      State = props.Env.Remotes.Current
      Tab = TabOpen
      Query = ""
      Found = None
      AskedMore = false
      RequestsOn = requestsOn
      Now = DateTimeOffset.UtcNow
      Riding = true
      Landing = None
      MarkedThrough = 0
      DraftKey = key
      Draft = draft |> Option.defaultValue ""
      Images = []
      Descriptions = []
      LinkChoice = LinkCard.FirstLink
      Voice = None
      Recording = false
      OrgName = ""
      OrgMembers = []
      OrgShare = true
      OrgRename = ""
      Notice = None
      SeenThrough = 0
      Unseen = 0
      Behind = false
    },
    Cmd.none

  /// The open thread's state, when one stands and has loaded.
  let openThread(model: Model) : ThreadState option =
    model.State.OpenTarget
    |> Option.map ChatTargets.key
    |> Option.bind(fun key -> model.State.Threads |> Map.tryFind key)

  /// The reading's seen bookkeeping: riding the bottom edge marks
  /// everything seen; away from it, arrivals past the mark count as
  /// unseen.
  let seenOf(viewerId: string, model: Model) : Model =
    match openThread model, model.Riding with
    | Some thread, true ->
      let latest =
        thread.Messages
        |> List.tryLast
        |> Option.map _.Id
        |> Option.defaultValue 0

      {
        model with
            SeenThrough = latest
            Unseen = 0
      }
    | Some thread, false ->
      let unseen =
        thread.Messages
        |> List.filter(fun message ->
          message.Id > model.SeenThrough
          && message.UserId <> viewerId
          && message.Event.IsNone)
        |> List.length

      { model with Unseen = unseen }
    | None, _ -> { model with Unseen = 0 }

  /// The read one riding reader owes the open thread: one pull
  /// through the newest message per arrival, never per scroll tick.
  let readCmd(props: Props<'env>, model: Model) : Model * Cmd<Msg> =
    match model.State.OpenTarget with
    | Some target when model.Riding && model.State.WindowActive ->
      let key = ChatTargets.key target

      match model.State.Threads |> Map.tryFind key with
      | Some thread when thread.IsLoaded ->
        let latest =
          thread.Messages
          |> List.tryLast
          |> Option.map _.Id
          |> Option.defaultValue 0

        if latest > model.MarkedThrough then
          { model with MarkedThrough = latest },
          Cmd.OfAsync.perform
            markRead
            (props.Env.Remotes, target, true)
            ReadSettled
        else
          model, Cmd.none
      | _ -> model, Cmd.none
    | _ -> model, Cmd.none

  let update (props: Props<'env>) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | StoreChanged state ->
      let next = { model with State = state }

      let list = listTab(state, model.Tab)

      let settled =
        if model.AskedMore && not list.LoadingMore then
          { next with AskedMore = false }
        else
          next

      // A new conversation checks its own draft out of the stash; the
      // restore is silent, the way the site's chat is.
      let keyed =
        state.OpenTarget |> Option.map ChatTargets.key |> Option.defaultValue ""

      if keyed <> model.DraftKey then
        let draft = stashedDraft(props, keyed)

        readCmd(
          props,
          seenOf(
            props.Session.Session.User.Id,
            {
              settled with
                  DraftKey = keyed
                  Draft = draft |> Option.defaultValue ""
                  Images = []
                  Descriptions = []
                  LinkChoice = LinkCard.FirstLink
                  Voice = None
                  Recording = false
            }
          )
        )
      else
        readCmd(props, seenOf(props.Session.Session.User.Id, settled))
    | ClockTicked moment -> { model with Now = moment }, Cmd.none
    | SessionChanged state ->
      let requestsOn =
        match state with
        | SignedIn entry ->
          entry.Session.Remotes
          |> Option.map _.Requests
          |> Option.defaultValue false
        | SignedOut -> false

      { model with RequestsOn = requestsOn }, Cmd.none
    | TabChosen tab ->
      let next = {
        model with
            Tab = tab
            AskedMore = false
      }

      let load =
        if isLoading(model.State, tab) then
          Cmd.OfAsync.perform loadTab (props.Env.Remotes, tab) (fun () ->
            MoreWanted)
        else
          Cmd.none

      let term = termOf model.Query

      let searchAgain =
        if term = "" then
          Cmd.none
        else
          Cmd.OfAsync.perform
            search
            (props.Env,
             {
               Session = props.Session
               Tab = tab
               Term = term
             })
            (fun result -> SearchLanded(term, result))

      next, Cmd.batch [ load; searchAgain ]
    | Searched text ->
      let term = termOf text

      if Some term = (model.Found |> Option.map _.Term) then
        { model with Query = text }, Cmd.none
      else
        {
          model with
              Query = text
              Found = None
        },
        (if term = "" then
           Cmd.none
         else
           Cmd.OfAsync.perform
             search
             (props.Env,
              {
                Session = props.Session
                Tab = model.Tab
                Term = term
              })
             (fun result -> SearchLanded(term, result)))
    | SearchLanded(term, result) ->
      if term <> termOf model.Query then
        model, Cmd.none
      else
        match result with
        | Ok found -> { model with Found = Some found }, Cmd.none
        | Error error ->
          // A failed search keeps the empty answer: the search's own
          // rows stay quiet rather than noisy.
          {
            model with
                Found =
                  Some {
                    Term = term
                    Conversations = []
                    Candidates = []
                  }
          },
          Cmd.none
    | MoreWanted ->
      let list = listTab(model.State, model.Tab)

      if
        model.Found.IsSome
        || model.AskedMore
        || not list.HasMore
        || list.LoadingMore
      then
        model, Cmd.none
      else
        { model with AskedMore = true },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, tab: InboxTab) -> store.LoadMore tab)
          (props.Env.Remotes, model.Tab)
          (fun () -> MoreWanted)
    | Opened target ->
      props.Env.Remotes.SetOpenTarget(Some target)
      model, Cmd.none
    | Closed ->
      props.Env.Remotes.SetOpenTarget None
      model, Cmd.none
    | SettleWanted(partner, merging) ->
      model,
      Cmd.OfAsync.perform
        settle
        (props.Env.Remotes, partner, merging)
        (fun taken -> Settled taken)
    | Settled _ -> model, Cmd.none
    | OlderWanted ->
      let asks =
        model.State.OpenTarget
        |> Option.bind(fun target ->
          model.State.Threads
          |> Map.tryFind(ChatTargets.key target)
          |> Option.map(fun thread ->
            target,
            thread.IsLoaded && thread.HasOlder && not thread.IsLoadingOlder))

      match asks with
      | Some(target, true) ->
        model,
        Cmd.OfAsync.perform loadOlder (props.Env.Remotes, target) (fun () ->
          StoreWorkDone)
      | _ -> model, Cmd.none
    | RideMoved(riding, behind) ->
      readCmd(
        props,
        seenOf(
          props.Session.Session.User.Id,
          {
            model with
                Riding = riding
                Behind = behind
          }
        )
      )
    | ThreadScrolled offset ->
      model.State.OpenTarget
      |> Option.iter(fun target ->
        props.Env.Remotes.RememberScroll(ChatTargets.key target, offset))

      model, Cmd.none
    | GhostWanted messageId ->
      match model.State.OpenTarget with
      | Some target ->
        model,
        Cmd.OfAsync.perform
          walkTo
          (props.Env.Remotes, target, messageId)
          (fun found -> Walked(messageId, found))
      | None -> model, Cmd.none
    | Walked(messageId, found) ->
      if found then
        {
          model with
              Landing = Some(Message messageId)
        },
        Cmd.none
      else
        {
          model with
              Notice = Some "Could not load this message. Try again."
        },
        Cmd.none
    | Landed -> { model with Landing = None }, Cmd.none
    | PresentWanted ->
      {
        model with
            Landing = Some Present
            Riding = true
      },
      Cmd.none
    | PreviewsChecked ->
      let asks =
        model.State.OpenTarget
        |> Option.map(fun target ->
          let thread =
            model.State.Threads
            |> Map.tryFind(ChatTargets.key target)
            |> Option.defaultValue(emptyThread target)

          thread.Messages
          |> List.filter(fun message ->
            message.LinkPreview
            |> Option.map _.IsPending
            |> Option.defaultValue false)
          |> List.map(fun message -> target, message.Id))
        |> Option.defaultValue []

      model,
      Cmd.batch [
        for target, messageId in asks ->
          Cmd.OfAsync.perform
            preview
            (props.Env.Remotes, target, messageId)
            (fun () -> StoreWorkDone)
      ]
    | ReadSettled _ -> model, Cmd.none
    | StoreWorkDone -> model, Cmd.none
    | DraftChanged text ->
      let next = { model with Draft = text }

      // The typing ping rides the change; the store throttles it.
      let typing =
        model.State.OpenTarget
        |> Option.filter(fun _ -> text.Trim() <> "")
        |> Option.map(fun target ->
          Cmd.OfAsync.perform
            (fun (store: RemotesStore, who: ChatTarget) ->
              store.PingTyping who)
            (props.Env.Remotes, target)
            (fun () -> StoreWorkDone))
        |> Option.defaultValue Cmd.none

      // The stash parks once the typing rests, and only if the text
      // still stands then.
      next,
      Cmd.batch [
        typing
        Cmd.OfAsync.perform rest () (fun () -> DraftSettled text)
      ]
    | DraftSettled text ->
      if text = model.Draft then
        props.Env.Settings.SetChatDraft(model.DraftKey, text)

      model, Cmd.none
    | ImageRemoved index ->
      if index >= 0 && index < List.length model.Images then
        {
          model with
              Images = List.removeAt index model.Images
              Descriptions = List.removeAt index model.Descriptions
        },
        Cmd.none
      else
        model, Cmd.none
    | Described(index, text) ->
      if index >= 0 && index < List.length model.Descriptions then
        let descriptions =
          model.Descriptions
          |> List.mapi(fun at current -> if at = index then text else current)

        {
          model with
              Descriptions = descriptions
        },
        Cmd.none
      else
        model, Cmd.none
    | LinkChosen choice -> { model with LinkChoice = choice }, Cmd.none
    | ReplyWanted message ->
      let parent: MessageParent = {
        Id = message.Id
        UserId = message.UserId
        Body = message.Body
        ImagesCount = List.length message.Images
        HasVoice = message.Voice.IsSome
      }

      model.State.OpenTarget
      |> Option.iter(fun target ->
        props.Env.Remotes.SetReplyingTo(target, Some parent))

      model, Cmd.none
    | ReplyCancelled ->
      model.State.OpenTarget
      |> Option.iter(fun target ->
        props.Env.Remotes.SetReplyingTo(target, None))

      model, Cmd.none
    | SendWanted ->
      let body = model.Draft.Trim()

      let canSend =
        (body <> "" || not model.Images.IsEmpty || model.Voice.IsSome)
        && model.State.OpenTarget.IsSome

      match model.State.OpenTarget with
      | Some target when canSend ->
        let message: OutgoingMessage = {
          Body = (if body = "" then None else Some body)
          ReplyTo =
            (model.State.Threads
             |> Map.tryFind(ChatTargets.key target)
             |> Option.bind _.ReplyingTo
             |> Option.map _.Id)
          Card = model.LinkChoice
          Images = model.Images
          ImageDescriptions =
            model.Descriptions
            |> List.map(fun text ->
              if text.Trim() = "" then None else Some(text.Trim()))
          Voice = model.Voice
        }

        props.Env.Remotes.SetReplyingTo(target, None)
        props.Env.Settings.SetChatDraft(model.DraftKey, "")

        {
          model with
              Draft = ""
              Images = []
              Descriptions = []
              LinkChoice = LinkCard.FirstLink
              Voice = None
        },
        Cmd.OfAsync.perform push (props.Env.Remotes, target, message) (fun () ->
          StoreWorkDone)
      | _ -> model, Cmd.none
    | RetryWanted clientId ->
      match model.State.OpenTarget with
      | Some target ->
        model,
        Cmd.OfAsync.perform
          retry
          (props.Env.Remotes, target, clientId)
          (fun () -> StoreWorkDone)
      | None -> model, Cmd.none
    | PickWanted ->
      model,
      Cmd.OfAsync.perform
        (fun (sources: IAttachmentSources) -> sources.PickFiles())
        props.Env.Attachments
        (fun picked -> Picked picked)
    | Picked picked ->
      let images = (model.Images @ picked) |> List.truncate 4

      let count = List.length images

      let descriptions =
        model.Descriptions
        |> List.truncate count
        |> fun kept -> kept @ List.replicate (count - List.length kept) ""

      {
        model with
            Images = images
            Descriptions = descriptions
      },
      Cmd.none
    | MuteWanted duration ->
      match model.State.OpenTarget with
      | Some target ->
        { model with Notice = None },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, who: ChatTarget, how: MuteDuration) ->
            store.SetMuted(who, how))
          (props.Env.Remotes, target, duration)
          ActionSettled
      | None -> model, Cmd.none
    | PinWanted pinned ->
      match model.State.OpenTarget with
      | Some target ->
        { model with Notice = None },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, who: ChatTarget, pin: bool) ->
            store.SetPinned(who, pin))
          (props.Env.Remotes, target, pinned)
          ActionSettled
      | None -> model, Cmd.none
    | ArchiveWanted archived ->
      match model.State.OpenTarget with
      | Some target ->
        { model with Notice = None },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, who: ChatTarget, away: bool) -> async {
            let! moved = store.SetArchived(who, away)

            // The store already rolled the row back and logged the
            // server's word; the notice says only what happened.
            return
              if moved then
                Ok()
              else
                Error(
                  SignInFailed "Could not update the conversation. Try again."
                )
          })
          (props.Env.Remotes, target, archived)
          ActionSettled
      | None -> model, Cmd.none
    | ActionSettled outcome ->
      match outcome with
      | Ok() -> { model with Notice = None }, Cmd.none
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        Cmd.none
    | BlockWanted partner ->
      { model with Notice = None },
      Cmd.OfAsync.perform
        (fun (env, session: SignedInSession, who: User) -> async {
          let! blocked =
            DevNull.Client.Core.Api.Users.block(
              env,
              {
                KeyFile = session.KeyFile
                Username = who.Handle
              }
            )

          return blocked |> Result.map ignore
        })
        (props.Env, props.Session, partner)
        Blocked
    | Blocked outcome ->
      match outcome with
      | Ok _ ->
        props.Env.Remotes.SetOpenTarget None

        { model with Notice = None }, Cmd.none
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        Cmd.none
    | OrgCreateWanted ->
      let name =
        model.OrgName.Trim()
        |> fun trimmed -> if trimmed = "" then None else Some trimmed

      { model with Notice = None },
      Cmd.OfAsync.perform
        (fun (store: RemotesStore, orgName, members) ->
          store.CreateOrg(orgName, members))
        (props.Env.Remotes, name, model.OrgMembers)
        OrgCreated
    | OrgNameChanged name -> { model with OrgName = name }, Cmd.none
    | OrgCreated outcome ->
      match outcome with
      | Ok org ->
        props.Env.Remotes.SetOpenTarget(Some(Org org))

        {
          model with
              OrgName = ""
              OrgMembers = []
              Notice = None
        },
        Cmd.none
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        Cmd.none
    | OrgRenameWanted ->
      match model.State.OpenTarget with
      | Some(Org org) ->
        let name =
          model.OrgRename.Trim()
          |> fun trimmed -> if trimmed = "" then None else Some trimmed

        {
          model with
              Notice = None
              OrgRename = ""
        },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, which: OrgRef, orgName) ->
            store.RenameOrg(which, orgName))
          (props.Env.Remotes, org, name)
          OrgRenamed
      | _ -> model, Cmd.none
    | OrgRenameChanged name -> { model with OrgRename = name }, Cmd.none
    | OrgRenamed outcome ->
      match outcome with
      | Ok() -> { model with Notice = None }, Cmd.none
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        Cmd.none
    | OrgAddWanted ->
      match model.State.OpenTarget with
      | Some(Org org) ->
        let members =
          model.OrgMembers
          |> List.map _.Trim()
          |> List.filter(fun member' -> member' <> "")

        { model with Notice = None },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, which: OrgRef, added, shared) ->
            store.AddOrgMembers(which, added, shared))
          (props.Env.Remotes, org, members, model.OrgShare)
          OrgMembersAdded
      | _ -> model, Cmd.none
    | OrgMemberChanged username ->
      let members =
        if List.contains username model.OrgMembers then
          List.except [ username ] model.OrgMembers
        else
          model.OrgMembers @ [ username ]

      { model with OrgMembers = members }, Cmd.none
    | OrgShareChanged shared -> { model with OrgShare = shared }, Cmd.none
    | OrgMembersAdded outcome ->
      match outcome with
      | Ok() ->
        {
          model with
              OrgMembers = []
              Notice = None
        },
        Cmd.none
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        Cmd.none
    | OrgRoleWanted(username, role) ->
      match model.State.OpenTarget with
      | Some(Org org) ->
        { model with Notice = None },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, which: OrgRef, member', part) ->
            store.SetOrgMemberRole(which, member', part))
          (props.Env.Remotes, org, username, role)
          OrgRoleSet
      | _ -> model, Cmd.none
    | OrgRoleSet outcome ->
      match outcome with
      | Ok() -> { model with Notice = None }, Cmd.none
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        Cmd.none
    | OrgRemoveWanted(username, leaving) ->
      match model.State.OpenTarget with
      | Some(Org org) ->
        { model with Notice = None },
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, which: OrgRef, member', gone) ->
            store.RemoveOrgMember(which, member', gone))
          (props.Env.Remotes, org, username, leaving)
          (fun outcome -> OrgRemoved(leaving, outcome))
      | _ -> model, Cmd.none
    | OrgRemoved(left, outcome) ->
      match outcome with
      | Ok() ->
        // Leaving takes the org out of the state; the pane steps back
        // to the quiet invitation.
        if left then
          props.Env.Remotes.SetOpenTarget None

        { model with Notice = None }, Cmd.none
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        Cmd.none
    | MenuNotice message -> { model with Notice = Some message }, Cmd.none
    | ReactWanted(messageId, emoji) ->
      match model.State.OpenTarget with
      | Some target ->
        model,
        Cmd.OfAsync.perform
          (fun (store: RemotesStore, who: ChatTarget, id: int, mark: string) ->
            store.ToggleReaction(who, id, mark))
          (props.Env.Remotes, target, messageId, emoji)
          ReactSettled
      | None -> model, Cmd.none
    | ReactSettled _ -> model, Cmd.none
    | RecordWanted -> { model with Recording = true }, Cmd.none
    | RecordCancelled -> { model with Recording = false }, Cmd.none
    | VoiceStaged outcome ->
      match outcome with
      | Ok staged ->
        {
          model with
              Recording = false
              Voice = Some staged
              Notice = None
        },
        Cmd.none
      | Error why ->
        {
          model with
              Recording = false
              Notice = Some why
        },
        Cmd.none
    | VoiceDiscarded -> { model with Voice = None }, Cmd.none

  /// The page's inlets from outside: the shared clock, the session, the
  /// store, and the watcher's readership handles. The thread watch
  /// rides the open target: a new target restarts the sub, and the
  /// page's end releases it.
  let subscribe (props: Props<'env>) (model: Model) : Sub<Msg> =
    let watcher = props.Env.Remotes.Watcher

    let threadWatch =
      model.State.OpenTarget
      |> Option.map(fun target ->
        Sub.ofId ("watch:" + ChatTargets.key target) (fun _ ->
          watcher.WatchThread target))
      |> Option.toList

    Sub.batch [
      Sub.fromObservable
        "clock"
        (props.Env.UiClock.Minute |> Observable.map ClockTicked)

      Sub.fromObservable
        "session"
        (props.Env.Session.Observe |> Observable.map SessionChanged)

      Sub.fromObservable
        "store"
        (props.Env.Remotes.Observe |> Observable.map StoreChanged)

      yield! threadWatch

      // A card still being read asks again on a slow tick; the sub
      // lives only while a pending card stands, so a settled thread
      // never ticks.
      let previewsPending =
        model.State.OpenTarget
        |> Option.map(fun target ->
          model.State.Threads
          |> Map.tryFind(ChatTargets.key target)
          |> Option.map(fun thread ->
            thread.Messages
            |> List.exists(fun message ->
              message.LinkPreview
              |> Option.map _.IsPending
              |> Option.defaultValue false))
          |> Option.defaultValue false)
        |> Option.defaultValue false

      if previewsPending then
        Sub.interval "link-previews" (TimeSpan.FromSeconds 5.0) (fun () ->
          PreviewsChecked)

      Sub.ofId "inbox" (fun _ -> watcher.WatchInbox())
    ]

/// The page's semantic regions; the view only composes these.
module Parts =

  /// Everything one row's parts read: the dispatch, the model, and the
  /// image cache.
  type RowCtx = {
    Dispatch: Dispatch<State.Msg>
    Model: IObservable<State.Model>
    Images: ImageCacheService
  }

  /// One partner's avatar.
  let partnerAvatar(images: ImageCacheService, partner: User) : Control =
    Avatar.view {
      Name = partner.Name
      Url = partner.AvatarUrl
      Lane = partner.Lane
      Size = 40.0
      Cache = images
    }

  /// An org's avatar: two of its other members, overlapping, in the
  /// space of one avatar.
  let orgAvatar
    (images: ImageCacheService, viewerId: string, row: Conversation)
    : Control =
    let others =
      row.Members
      |> Option.defaultValue []
      |> List.filter(fun member' -> member'.User.Id <> viewerId)
      |> List.truncate 2

    let panel = Panel()

    panel.Width <- 40.0
    panel.Height <- 40.0

    let placed =
      others
      |> List.mapi(fun index member' ->
        let corner =
          Avatar.view {
            Name = member'.User.Name
            Url = member'.User.AvatarUrl
            Lane = member'.User.Lane
            Size = 26.0
            Cache = images
          }

        let horizontal, vertical =
          if index = 0 then
            HorizontalAlignment.Left, VerticalAlignment.Top
          else
            HorizontalAlignment.Right, VerticalAlignment.Bottom

        corner.HorizontalAlignment(horizontal).VerticalAlignment(vertical))

    panel.Children [| for avatar in placed -> avatar |] |> ignore

    panel

  /// One conversation's title line: the partner's name and handle, or
  /// the org's title and member count.
  let titleLine(viewerId: string, row: Conversation) : Control =
    match row.Partner with
    | Some partner ->
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Text(partner.Name)
            .TextTrimming(TextTrimming.CharacterEllipsis),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text("@" + partner.Handle)
            .TextTrimming(TextTrimming.CharacterEllipsis)
        )
    | None ->
      let others =
        row.Members
        |> Option.defaultValue []
        |> List.filter(fun member' -> member'.User.Id <> viewerId)

      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Text(State.orgTitle(viewerId, row))
            .TextTrimming(TextTrimming.CharacterEllipsis),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text($"org · {List.length others + 1}")
        )

  /// One conversation's preview line: the sender's prefix and the last
  /// message's text.
  let excerptLine(excerpt: State.Excerpt) : Control =
    if excerpt.IsEvent then
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text(excerpt.Text)
        .TextTrimming(TextTrimming.CharacterEllipsis)
    else
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Text(excerpt.Prefix + excerpt.Text)
        .TextTrimming(TextTrimming.CharacterEllipsis)

  /// One row's right edge: the pin and mute marks, then the ahead pill
  /// or the relative time.
  let marksLine(model: IObservable<State.Model>, row: Conversation) : Control =
    let now = model |> Observable.map _.Now

    let pinned =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text("pinned")
        .IsVisible(row.PinnedAt.IsSome)

    let muted =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text("muted")
        .IsVisible(
          now
          |> Observable.map(fun moment ->
            row.MutedUntilAt
            |> Option.map(fun until -> until > moment)
            |> Option.defaultValue false)
        )

    let ahead =
      Border()
        .Classes(Theme.name Theme.AheadPill)
        .Child(
          TextBlock()
            .Text(if row.Ahead > 99 then "99+ ahead" else $"{row.Ahead} ahead")
        )
        .IsVisible(row.Ahead > 0)

    let time =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text(
          match row.LastMessageAt with
          | Some at -> now |> Observable.map(fun moment -> timeAgo(moment, at))
          | None -> now |> Observable.map(fun _ -> "")
        )
        .IsVisible(row.Ahead = 0)

    StackPanel()
      .Spacing(2.0)
      .HorizontalAlignment(HorizontalAlignment.Right)
      .Children(pinned, muted, ahead, time)

  /// One fetched request's settle row: merge into the open list, or
  /// drop it for good.
  let settleRow
    (dispatch: Dispatch<State.Msg>, partner: User, merging: bool, label: string)
    : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(8.0, 2.0, 8.0, 2.0)
      .FontSize(13.0)
      .Content(label)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        dispatch(State.SettleWanted(partner, merging)))

  /// One conversation row of the inbox column.
  let conversationRow
    (ctx: RowCtx, viewerId: string, tab: InboxTab, row: Conversation)
    : Control =
    let active =
      ctx.Model
      |> Observable.map(fun current ->
        current.State.OpenTarget
        |> Option.map ChatTargets.key
        |> Option.contains(ChatTargets.summaryKey row))

    let settle: Control array =
      if tab = TabFetched then
        row.Partner
        |> Option.toList
        |> List.collect(fun partner -> [
          settleRow(ctx.Dispatch, partner, true, "merge")
          settleRow(ctx.Dispatch, partner, false, "drop")
        ])
        |> Array.ofList
      else
        [||]

    let body: Control array = [|
      titleLine(viewerId, row)
      excerptLine(State.excerptOf(viewerId, row))
      yield! settle
    |]

    let avatar: Control =
      match row.Partner with
      | Some partner -> partnerAvatar(ctx.Images, partner)
      | None -> orgAvatar(ctx.Images, viewerId, row)

    let content =
      let grid = Grid()

      grid.ColumnDefinitions <- ColumnDefinitions("Auto,*,Auto")

      grid.Children(
        avatar
          .Column(0)
          .Margin(0.0, 0.0, 12.0, 0.0)
          .VerticalAlignment(VerticalAlignment.Center),
        StackPanel()
          .Spacing(2.0)
          .Margin(0.0, 10.0, 0.0, 10.0)
          .Children(body)
          .Column(1),
        marksLine(ctx.Model, row).Column(2).Margin(12.0, 10.0, 12.0, 10.0)
      )

    let button =
      Button()
        .Classes(Theme.name Theme.CommitRow)
        .HorizontalContentAlignment(HorizontalAlignment.Stretch)
        .Content(content)
        .OnClickHandler(fun _ _ ->
          ctx.Dispatch(State.Opened(ChatTargets.targetOf row)))

    let bar =
      Border()
        .Classes(Theme.name Theme.AccentBack)
        .Width(3.0)
        .CornerRadius(2.0)
        .VerticalAlignment(VerticalAlignment.Stretch)
        .Margin(0.0, 8.0, 0.0, 8.0)
        .IsVisible(active)

    Theme.bindClass(button, Theme.PrimarySoftBack, active)
    Panel().Children(bar, button)

  /// One candidate row: a person a new remote can start with.
  let candidateRow(ctx: RowCtx, person: RemoteCandidate) : Control =
    let identity =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          TextBlock()
            .Classes(Theme.name Theme.Subtitle)
            .Text(person.User.Name)
            .TextTrimming(TextTrimming.CharacterEllipsis),
          TextBlock()
            .Classes(Theme.name Theme.Mono)
            .Text("@" + person.User.Handle)
            .TextTrimming(TextTrimming.CharacterEllipsis)
        )

    let reason =
      TextBlock()
        .Classes(Theme.name Theme.Muted)
        .Text(person.Reason |> Option.defaultValue "")
        .IsVisible(person.Reason.IsSome)

    let avatar =
      Avatar.view {
        Name = person.User.Name
        Url = person.User.AvatarUrl
        Lane = person.User.Lane
        Size = 40.0
        Cache = ctx.Images
      }

    let columns = Grid()

    columns.ColumnDefinitions <- ColumnDefinitions("Auto,*")

    Button()
      .Classes(Theme.name Theme.CommitRow)
      .HorizontalContentAlignment(HorizontalAlignment.Stretch)
      .Content(
        columns.Children(
          avatar
            .Column(0)
            .Margin(0.0, 0.0, 12.0, 0.0)
            .VerticalAlignment(VerticalAlignment.Center),
          StackPanel()
            .Spacing(2.0)
            .Margin(0.0, 10.0, 12.0, 10.0)
            .Children(identity, reason)
            .Column(1)
        )
      )
      .OnClickHandler(fun _ _ ->
        ctx.Dispatch(State.Opened(Partner person.User)))

  /// The tabs of the inbox, the fetched one only while the account
  /// takes requests; the fetched tab carries the waiting count.
  let tabRow
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let strip =
      Observable.combineLatest
        (model |> Observable.map _.RequestsOn)
        (model
         |> Observable.map(fun current ->
           current.State.Requests |> Option.defaultValue 0))
      |> Observable.distinctUntilChanged
      |> Observable.map(fun (on, waiting) ->
        let tabOf(tab: InboxTab, label: string) : Control =
          let waitingPill =
            Border()
              .Classes(Theme.name Theme.TabBadge)
              .Child(
                TextBlock()
                  .Text(if waiting > 99 then "99+" else string waiting)
              )

          ToolTip.SetTip(waitingPill, $"{waiting} waiting")

          let badge: Control array = [|
            if tab = TabFetched && waiting > 0 then
              yield waitingPill
          |]

          let button =
            Button()
              .Classes(Theme.name Theme.TabButton)
              .Padding(12.0, 8.0, 12.0, 6.0)
              .Content(
                StackPanel()
                  .Orientation(Orientation.Horizontal)
                  .Spacing(6.0)
                  .Children(
                    [|
                      yield (TextBlock().Text(label) :> Control)
                      yield! badge
                    |]
                  )
              )
              .OnClickHandler(fun _ _ -> dispatch(State.TabChosen tab))

          Theme.bindClass(
            button,
            Theme.TabButtonActive,
            model |> Observable.map(fun current -> current.Tab = tab)
          )

          button

        let strip = StackPanel().Orientation(Orientation.Horizontal)

        strip.Children [|
          for tab, label in State.tabsOf on -> tabOf(tab, label)
        |]
        |> ignore

        strip)

    ContentControl().Content(strip |> _.ToBinding())

  /// The search box of the inbox.
  let searchBox
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    TextBox()
      .Name("RemotesSearch")
      .PlaceholderText("search")
      .Text(model |> Observable.map _.Query |> _.ToBinding())
      .OnTextChangedHandler(fun box _ ->
        box.Text
        |> Option.ofObj
        |> Option.defaultValue ""
        |> State.Searched
        |> dispatch)

  /// The inbox column's rows: virtualized and keyed. The scroll-end
  /// probe asks the update for the next page; the guards there keep it
  /// from asking twice.
  let rows
    (ctx: RowCtx, viewerId: string, list: IObservable<State.InboxRow list>)
    : Control =
    let template =
      FuncDataTemplate<State.InboxRow>(
        (fun item _ ->
          if isNull(box item) then
            Panel() :> Control
          else
            match item with
            | State.InboxRow.Conversation(tab, row) ->
              conversationRow(ctx, viewerId, tab, row)
            | State.InboxRow.Candidate person -> candidateRow(ctx, person)),
        false
      )

    let items =
      ItemsControl()
        .ItemTemplate(template)
        .ItemsPanel(FuncTemplate<Panel>(fun () -> VirtualizingStackPanel()))
        .BindItemsSource(list, State.rowKey)

    let scroller = ScrollViewer().Content(items)

    scroller.Name <- "RemotesInbox"
    scroller.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled

    scroller.ScrollChanged.Add(fun _ ->
      if
        scroller.Extent.Height - scroller.Viewport.Height - scroller.Offset.Y <
          240.0
      then
        ctx.Dispatch State.MoreWanted)

    scroller

  /// The quiet centered line the column says when its list says
  /// nothing: loading, empty, or nothing found.
  let quietLine(text: IObservable<string>) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .Text(text)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Margin(12.0, 24.0, 12.0, 12.0)
      .TextWrapping(TextWrapping.Wrap)

  /// The line the column says over its rows: nothing while rows show,
  /// the loading word while the tab's first page is out, the empty
  /// word once the list holds nothing, and the nothing-found word
  /// while a search's answer holds nothing.
  let lineOf(model: IObservable<State.Model>) : IObservable<string> =
    model
    |> Observable.map(fun current ->
      let list = listTab(current.State, current.Tab)

      if current.Found.IsSome then
        if
          current.Found.Value.Conversations.IsEmpty
          && current.Found.Value.Candidates.IsEmpty
        then
          "nothing found"
        else
          ""
      elif list.Items = Some [] then
        State.emptyLine current.Tab
      elif list.Items.IsNone then
        "loading the inbox…"
      else
        "")

  /// The whole inbox column: the search box, the tabs, and the rows
  /// over the quiet line.
  /// The org-create picker: a quiet button whose flyout takes an
  /// optional 60-char name and the members picked from the search's
  /// candidate list. Control-owned flyout, the house pattern.
  let orgPicker
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    let candidates =
      model
      |> Observable.map(fun current ->
        current.Found |> Option.map _.Candidates |> Option.defaultValue [])

    let nameBox =
      TextBox()
        .Name("OrgCreateName")
        .PlaceholderText("org name (optional, 60 chars)")
        .MaxLength(60)
        .MinHeight(30.0)
        .FontSize(12.0)
        .MaxWidth(220.0)
        .Text(
          model
          |> Observable.map _.OrgName
          |> Observable.distinctUntilChanged
          |> _.ToBinding()
        )
        .OnTextChangedHandler(fun box _ ->
          box.Text
          |> Option.ofObj
          |> Option.defaultValue ""
          |> State.OrgNameChanged
          |> dispatch)

    let picks = ContentControl()

    picks.Content(
      candidates
      |> Observable.distinctUntilChanged
      |> Observable.map(fun found ->
        let rows: Control array = [|
          for person in found ->
            Button()
              .Classes(Theme.name Theme.GhostButton)
              .Padding(6.0, 2.0, 6.0, 2.0)
              .FontSize(12.0)
              .HorizontalContentAlignment(HorizontalAlignment.Left)
              .Content($"+ @{person.User.Handle}")
              .OnClickHandler(fun _ args ->
                args.Handled <- true
                dispatch(State.OrgMemberChanged person.User.Handle))
        |]

        StackPanel().Children rows)
      |> _.ToBinding()
    )
    |> ignore

    let chosen =
      ContentControl()
        .Content(
          model
          |> Observable.map(fun current -> current.OrgMembers)
          |> Observable.distinctUntilChanged
          |> Observable.map(fun members ->
            let picked =
              if members.IsEmpty then
                "nobody picked yet"
              else
                let names = String.concat ", " members

                $"members: {names} ({1 + List.length members}/100)"

            TextBlock()
              .Classes(Theme.name Theme.Mono)
              .FontSize(11.0)
              .Text(picked)
              .TextWrapping(TextWrapping.Wrap)
            :> Control)
          |> _.ToBinding()
        )

    let flyout =
      Popup()
        .Child(
          Border()
            .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
            .BorderThickness(1.0)
            .CornerRadius(6.0)
            .Padding(6.0)
            .MaxWidth(260.0)
            .Child(
              StackPanel()
                .Spacing(4.0)
                .Children(
                  nameBox,
                  TextBlock()
                    .Classes(Theme.name Theme.Mono)
                    .FontSize(11.0)
                    .Text("search people first; picks come from the results"),
                  picks,
                  chosen,
                  Button()
                    .Classes(Theme.name Theme.AccentButton)
                    .Padding(8.0, 2.0, 8.0, 2.0)
                    .FontSize(12.0)
                    .Content("create org")
                    .IsEnabled(
                      model
                      |> Observable.map(fun current ->
                        not current.OrgMembers.IsEmpty)
                    )
                    .OnClickHandler(fun _ args ->
                      args.Handled <- true
                      dispatch State.OrgCreateWanted)
                )
            )
        )

    Panel()
      .Children(
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0, 2.0, 6.0, 2.0)
          .FontSize(12.0)
          .Content("new org")
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            flyout.IsOpen <- not flyout.IsOpen),
        flyout
      )

  /// The inbox's header row: the search box with the org picker beside
  /// it, and the tabs under them.
  let inboxHead
    (model: IObservable<State.Model>, dispatch: Dispatch<State.Msg>)
    : Control =
    DockPanel()
      .LastChildFill(true)
      .Children(
        orgPicker(model, dispatch).Dock(Dock.Right).Margin(0.0, 12.0, 12.0, 8.0),
        searchBox(model, dispatch).Margin(12.0, 12.0, 8.0, 8.0)
      )

  let inbox
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>
    ) : Control =
    let ctx: RowCtx = {
      Dispatch = dispatch
      Model = model
      Images = props.Env.ImageCache
    }

    let viewerId = props.Session.Session.User.Id

    let list = model |> Observable.map State.rowsOf

    let column =
      let grid = Grid()

      grid.RowDefinitions <- RowDefinitions("Auto,Auto,*")

      grid.Children(
        inboxHead(model, dispatch).Row(0),
        tabRow(model, dispatch).Row(1),
        rows(ctx, viewerId, list).Row(2)
      )

    let line = quietLine(lineOf model).VerticalAlignment(VerticalAlignment.Top)

    // The quiet line sits over the rows: the empty and loading words
    // own the column while no row exists to cover them.
    Panel().Children(column, line)

  /// The thread pane while nothing is checked out: the site's quiet
  /// invitation.
  let noThread() : Control =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .Text("no conversation checked out")
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Center)
      .TextWrapping(TextWrapping.Wrap)

/// The open thread: the detail column's reading pane. The pane itself
/// rebuilds only when the checked-out target moves; everything inside
/// follows the model stream, so a poll refresh never rebuilds a row.
module Thread =

  /// Only one note sounds at a time, the site's own rule: each claim
  /// stops the playback that held the stage before it.
  type Solo() =
    let gate = obj()
    let held: IAudioPlayback option ref = ref None

    member _.Claim(playback: IAudioPlayback) =
      lock gate (fun () ->
        match !held with
        | Some playing when not(obj.ReferenceEquals(playing, playback)) ->
          playing.Stop()
        | _ -> ()

        held := Some playback)

  /// The tail-follow state of one thread list: the reader rides the
  /// bottom edge until their own scrolling leaves it, and returns when
  /// they reach it again. Rows arriving while riding snap the list
  /// back to the edge.
  type Tail() =
    member val Riding = true with get, set

    member val Extent = 0.0 with get, set

  /// A remembered reading place coming back: lands once, keeps asking
  /// for older pages while the loaded extent cannot reach it, and
  /// gives up on a page that grows nothing.
  type Restore(target: float) =
    member _.Target = target

    member val Landed = target <= 0.0 with get, set

    member val Asked = false with get, set

  /// Everything one row of the thread reads: the dispatch, the model,
  /// the image cache, the viewer, and whether the thread is an org's.
  type RowCtx = {
    Dispatch: Dispatch<State.Msg>
    Model: IObservable<State.Model>
    Images: ImageCacheService
    ViewerId: string
    IsOrg: bool
    Nav: INavigable
    Audio: IAudioEngine voption
    Http: IHttp
    Settings: AppSettingsStore
    /// The one note that may sound at a time.
    Solo: Solo
  }

  /// One author's name as the receipt and ghost lines know it.
  let authorName(ctx: RowCtx, who: User option, authorId: string) : string =
    match who with
    | Some user -> user.Name
    | None -> if authorId = ctx.ViewerId then "You" else "Someone"

  /// The reply ghost: the message a bubble answers, peeking out above
  /// it. A click walks the thread to the original.
  let ghost
    (ctx: RowCtx, reply: MessageParent, mine: bool, author: string)
    : Control =
    let snippet =
      let body = reply.Body.Replace("\r", " ").Replace("\n", " ")

      // The site's snippet: links fall away, so the words carry it.
      let words =
        body.Split(' ')
        |> Array.filter(fun word ->
          not(word.StartsWith "http://" || word.StartsWith "https://"))

      let trimmed = String.concat " " words |> _.Trim()

      if trimmed <> "" then
        trimmed
      elif reply.HasVoice then
        "voice note"
      elif reply.ImagesCount > 1 then
        $"{reply.ImagesCount} images"
      else
        "image"

    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(8.0, 3.0, 8.0, 3.0)
      .FontSize(12.0)
      .Opacity(0.9)
      .HorizontalAlignment(
        if mine then
          HorizontalAlignment.Right
        else
          HorizontalAlignment.Left
      )
      .Content(
        StackPanel()
          .Spacing(1.0)
          .Children(
            TextBlock()
              .Classes(Theme.name Theme.Mono)
              .FontSize(11.0)
              .Text(author),
            TextBlock()
              .Classes(Theme.name Theme.Muted)
              .FontSize(12.0)
              .Text(snippet)
              .TextTrimming(TextTrimming.CharacterEllipsis)
              .MaxWidth(260.0)
          )
      )
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        ctx.Dispatch(State.GhostWanted reply.Id))

  /// The images of one message: fixed boxes on the signed URLs, the
  /// click opening the gallery.
  let images(ctx: RowCtx, who: Message) : Control voption =
    if who.Images.IsEmpty then
      ValueNone
    else
      let boxes: Control array = [|
        for image in who.Images ->
          AsyncImage
            .view(
              {
                Cache = ctx.Images
                Url = image.Url
                DecodeWidth = 512
                DecodeHeight = ValueSome 320
                Stretch = Stretch.UniformToFill
                Placeholder = ValueNone
                ToolTip = image.Description |> Option.toValueOption
              }
            )
            .Width(232.0)
            .Height(150.0)
      |]

      let gallery = WrapPanel().Children(boxes)

      let open' =
        Button()
          .Classes(Theme.name Theme.CommitRow)
          .Padding(0.0)
          .CornerRadius(10.0)
          .Content(gallery)
          .OnClickHandler(fun sender _ ->
            match TopLevel.GetTopLevel sender with
            | :? Window as owner ->
              ImageViewer.show {
                Owner = owner
                Cache = ctx.Images
                Photos = who.Images
                Start = 0
              }
            | _ -> ())

      ValueSome open'

  /// One message's link card, live from the thread: the card once the
  /// server read it, the quiet word while it reads.
  let linkCard
    (ctx: RowCtx, key: string, messageId: int, mine: bool)
    : Control voption =
    let cardOf(preview: LinkPreview) : Control =
      if preview.IsPending then
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(12.0)
          .Margin(0.0, 4.0, 0.0, 0.0)
          .Text("reading the link…")
      else
        Border()
          .CornerRadius(10.0)
          .BorderThickness(1.0)
          .Classes(Theme.name Theme.Hairline)
          .Padding(10.0, 8.0, 10.0, 8.0)
          .Margin(0.0, 6.0, 0.0, 0.0)
          .MaxWidth(280.0)
          .Child(
            StackPanel()
              .Spacing(2.0)
              .Children(
                TextBlock()
                  .Classes(
                    if mine then
                      Theme.name Theme.FgOnAccent
                    else
                      Theme.name Theme.Subtitle
                  )
                  .Text(preview.Title |> Option.defaultValue preview.Url)
                  .TextWrapping(TextWrapping.Wrap),
                TextBlock()
                  .Classes(
                    if mine then
                      Theme.name Theme.FgOnAccent
                    else
                      Theme.name Theme.Mono
                  )
                  .FontSize(11.0)
                  .Text(preview.Url)
                  .TextTrimming(TextTrimming.CharacterEllipsis)
              )
          )

    let card =
      ctx.Model
      |> Observable.map(fun current ->
        current.State.Threads
        |> Map.tryFind key
        |> Option.bind(fun thread ->
          thread.Messages
          |> List.tryFind(fun message -> message.Id = messageId)
          |> Option.bind _.LinkPreview))
      |> Observable.distinctUntilChanged

    let host = ContentControl()

    host.Content(
      card
      |> Observable.map(function
        | Some preview -> cardOf preview
        | None -> null)
      |> _.ToBinding()
    )
    |> ignore

    host.IsVisible(card |> Observable.map Option.isSome) |> ignore

    ValueSome host

  /// Everything the voice player reads: the engine the platform head
  /// passed, the transport the bytes download through, the settings
  /// the volume lives in, the one-note stage, and the clip itself.
  [<Struct>]
  type VoiceCtx = {
    Audio: IAudioEngine voption
    Http: IHttp
    Settings: AppSettingsStore
    Solo: Solo
    Clip: VoiceClip
  }

  /// One played voice note: the 48-bar waveform drawn from the
  /// payload, play/pause, the speed cycle, the volume, and the clock.
  /// The bars' fill and the clock are control-owned and repainted by
  /// one timer that stops on every path: end, stop, detach, page end.
  type VoicePlayer(voice: VoiceCtx) as this =
    inherit UserControl()

    let clip = voice.Clip

    let speeds = [| 1.0; 1.5; 2.0 |]
    let speedLabels = [| "1x"; "1.5x"; "2x" |]

    // The download happens once; the bytes are control-owned after,
    // and the engine's own pipeline decodes them.
    let mutable media: byte array option = None
    let mutable playing: IAudioPlayback option = None
    let mutable speedAt = 0

    let playedColumns = [|
      for value in clip.Waveform ->
        Border()
          .Width(3.0)
          .CornerRadius(1.5)
          .Height(2.0 + float value)
          .VerticalAlignment(VerticalAlignment.Bottom)
          .Opacity(0.35)
          .Classes(Theme.name Theme.FgAccent)
    |]

    let clock =
      TextBlock().Classes(Theme.name Theme.Mono).FontSize(11.0).MinWidth(76.0)

    let playButton = Button()

    let speedButton = Button()

    // Whether the note is mid-play; the button's own label follows it.
    let mutable sounding = false

    // A note the engine could not start — or bytes that never came —
    // leaves the player at its failed word rather than noise.
    let fail() =
      playing |> Option.iter(fun held -> held.Stop())
      playing <- None
      sounding <- false
      playButton.Content <- "failed"

    let paint(position: TimeSpan) =
      let playedMs = min clip.DurationMs (int position.TotalMilliseconds)
      let played = min 1.0 (float playedMs / float clip.DurationMs)
      let seconds = playedMs / 1000

      clock.Text <- $"0:{seconds:D2} / 0:{clip.DurationMs / 1000:D2}"

      let lit = int(played * float playedColumns.Length)

      playedColumns
      |> Array.iteri(fun at column ->
        column.Opacity <- (if at < lit then 1.0 else 0.35))

    let goIdle() =
      playing |> Option.iter(fun held -> held.Stop())
      playing <- None
      sounding <- false
      playButton.Content <- "play"
      paint(TimeSpan.FromMilliseconds(float clip.DurationMs))

    let timer = DispatcherTimer()

    let volumeFlyout = Popup()

    let start() =
      let beginPlay(bytes: byte array) = asyncEx {
        match voice.Audio with
        | ValueNone -> return ()
        | ValueSome head ->
          let! sounded =
            head.Play(
              bytes,
              speeds[speedAt],
              voice.Settings.Current.VoiceVolume
            )

          match sounded with
          | Error _ -> return Dispatcher.UIThread.Post fail
          | Ok playback ->
            playing |> Option.iter(fun held -> held.Stop())
            voice.Solo.Claim(playback)
            playing <- Some playback
            playback.Ended.Add(fun _ -> Dispatcher.UIThread.Post goIdle)
            paint playback.Position
            timer.Start()
      }

      match voice.Audio, media with
      | ValueSome _, Some bytes -> beginPlay bytes |> Async.StartImmediate
      | ValueSome _, None ->
        // The bytes download once, then they stay for replays.
        asyncEx {
          let! heard = voice.Http.GetBytes(clip.Url)

          match heard with
          | Ok bytes ->
            media <- Some bytes

            Dispatcher.UIThread.Post(fun _ ->
              if sounding then
                beginPlay bytes |> Async.StartImmediate)
          | Error _ -> Dispatcher.UIThread.Post fail
        }
        |> Async.Start
      | ValueNone, _ -> ()

      sounding <- true
      playButton.Content <- "pause"

    let pause() =
      timer.Stop()
      goIdle()

    do
      let wave =
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(2.0)
          .Height(28.0)
          .VerticalAlignment(VerticalAlignment.Center)
          .Children([| for column in playedColumns -> column :> Control |])

      playButton.Content <- "play"
      speedButton.Content <- speedLabels[0]

      timer.Interval <- TimeSpan.FromMilliseconds 100.0

      timer.Tick.Add(fun _ ->
        match playing with
        | Some playback -> paint playback.Position
        | None -> goIdle())

      playButton.OnClickHandler(fun _ args ->
        args.Handled <- true

        if sounding then pause() else start())
      |> ignore

      speedButton.OnClickHandler(fun sender args ->
        args.Handled <- true

        speedAt <- (speedAt + 1) % speeds.Length
        sender.Content <- speedLabels[speedAt]

        // A speed change while playing restarts at the new pace.
        if sounding then
          pause()
          start())
      |> ignore

      // The device-wide volume the site keeps: a fader in a quiet
      // flyout, quantized to whole percents, saved as it moves.
      let volumeAt() =
        int(round(voice.Settings.Current.VoiceVolume * 100.0))

      let volumeWord = TextBlock().Classes(Theme.name Theme.Mono).FontSize(11.0)

      let fader = Slider(Minimum = 0.0, Maximum = 100.0)

      let speak(p: int) = string p + "%"

      fader.Width <- 120.0
      fader.Value <- float(volumeAt())
      volumeWord.Text <- speak(volumeAt())

      fader.ValueChanged.Add(fun args ->
        let percent = int(round args.NewValue)

        volumeWord.Text <- speak percent
        voice.Settings.SetVoiceVolume(float percent / 100.0))

      volumeFlyout.Child <-
        Border()
          .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
          .BorderThickness(1.0)
          .CornerRadius(6.0)
          .Padding(8.0)
          .Child(StackPanel().Spacing(4.0).Children(fader, volumeWord))

      let volumeButton =
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(4.0, 2.0, 4.0, 2.0)
          .Content(Icons.iconAt(Glyph.Speaker, 14.0))
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            volumeFlyout.IsOpen <- not volumeFlyout.IsOpen)

      ToolTip.SetTip(volumeButton, "Voice volume")

      this.Content <-
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .Children(
            Panel().Children(volumeButton, volumeFlyout),
            playButton
              .Classes(Theme.name Theme.GhostButton)
              .Padding(6.0, 2.0, 6.0, 2.0),
            wave,
            clock,
            speedButton
              .Classes(Theme.name Theme.GhostButton)
              .Padding(4.0, 2.0, 4.0, 2.0)
              .FontSize(11.0)
          )

      paint TimeSpan.Zero

    override this.OnDetachedFromVisualTree(e) =
      base.OnDetachedFromVisualTree e
      timer.Stop()
      volumeFlyout.IsOpen <- false
      goIdle()

  /// One message's voice note: the waveform player.
  let voiceChip(voice: VoiceCtx) : Control = VoicePlayer(voice) :> Control

  /// The receipts under one message: pushed until the other side
  /// pulled it, the pull's own time in a direct thread, and the
  /// org's everyone word when all of them pulled. Live from the
  /// thread, because the read position moves under a built row.
  let receipts
    (ctx: RowCtx, key: string, message: Message, mine: bool)
    : Control voption =
    if not mine then
      ValueNone
    else
      let line =
        ctx.Model
        |> Observable.map(fun current ->
          current.State.Threads
          |> Map.tryFind key
          |> Option.map(fun thread ->
            if ctx.IsOrg then
              let others =
                thread.Members
                |> List.filter(fun member' ->
                  member'.User.Id <> message.UserId)

              let pulled =
                others
                |> List.filter(fun member' ->
                  member'.ReadThroughId >= message.Id)

              if List.isEmpty others then
                ""
              elif
                List.length pulled = List.length others
                && List.length others > 3
              then
                "pulled by everyone"
              else
                $"pulled by {List.length pulled} of {List.length others}"
            elif
              (thread.PartnerReadThroughId >= message.Id
               && thread.PartnerReadThroughId > 0)
            then
              let pulledAt = message.CreatedAt.LocalDateTime.ToString("HH:mm")

              $"pulled {pulledAt}"
            else
              "pushed"))

      let block =
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(11.0)
          .Margin(4.0, 2.0, 4.0, 0.0)
          .HorizontalAlignment(
            if mine then
              HorizontalAlignment.Right
            else
              HorizontalAlignment.Left
          )
          .Text(
            line |> Observable.map(fun text -> text |> Option.defaultValue "")
          )
          .IsVisible(
            line
            |> Observable.map(fun text ->
              (text |> Option.defaultValue "") <> "")
          )

      ValueSome block

  /// The who-reacted line: the viewer, the partner, a member the
  /// thread carries — each as their handle — or someone the payload
  /// did not name.
  let whoNames
    (thread: ThreadState option, viewerId: string, who: string list)
    : string =
    who
    |> List.map(fun userId ->
      if userId = viewerId then
        "you"
      else
        let named =
          match thread with
          | Some thread ->
            match thread.Target with
            | Partner user when user.Id = userId -> Some("@" + user.Handle)
            | _ ->
              thread.Members
              |> List.tryFind(fun member' -> member'.User.Id = userId)
              |> Option.map(fun member' -> "@" + member'.User.Handle)
          | None -> None

        named |> Option.defaultValue "someone")
    |> String.concat ", "

  /// One reaction chip: the emoji and its count, worn on the mine
  /// wash when the viewer's reaction is among them. The tip names who
  /// left it. Clicking toggles the viewer's.
  let reactionChip
    (ctx: RowCtx, messageId: int, summary: State.ReactionSummary, who: string)
    : Control =
    let chip =
      Border()
        .Classes(
          Theme.name(
            if summary.IsMine then
              Theme.ReactionChipMine
            else
              Theme.ReactionChip
          )
        )
        .Child(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(4.0)
            .Children(
              TextBlock().Text(summary.Emoji),
              TextBlock()
                .Classes(Theme.name Theme.Mono)
                .FontSize(11.0)
                .Text(string summary.Count)
            )
        )

    ToolTip.SetTip(chip, who)

    Button()
      .Classes(Theme.name Theme.CommitRow)
      .Padding(0.0)
      .Content(chip)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        ctx.Dispatch(State.ReactWanted(messageId, summary.Emoji)))

  /// The chips row under one message: one chip per emoji the message
  /// carries, live from the thread. The message carries none until it
  /// does, and the row hides then.
  let reactionChips(ctx: RowCtx, key: string, messageId: int) : Control =
    let summaries =
      ctx.Model
      |> Observable.map(fun current ->
        let thread = current.State.Threads |> Map.tryFind key

        let chips =
          thread
          |> Option.bind(fun thread ->
            thread.Messages
            |> List.tryFind(fun message -> message.Id = messageId)
            |> Option.map(fun message ->
              State.summarizeReactions(message.Reactions, ctx.ViewerId)))

        (chips |> Option.defaultValue [], thread))
      |> Observable.distinctUntilChanged

    let host = ContentControl()

    host.Content(
      summaries
      |> Observable.map(fun (rows, thread) ->
        let chips: Control array = [|
          for summary in rows ->
            reactionChip(
              ctx,
              messageId,
              summary,
              whoNames(thread, ctx.ViewerId, summary.Who)
            )
        |]

        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(4.0)
          .Margin(2.0, 2.0, 0.0, 0.0)
          .Children(chips))
      |> _.ToBinding()
    )
    |> ignore

    host.IsVisible(
      summaries |> Observable.map(fun (rows, _) -> not rows.IsEmpty)
    )
    |> ignore

    host

  /// The quick picks beside the message: the three reactions one
  /// click away, following how the user reacts. Clicking toggles the
  /// viewer's.
  let quickPicks(ctx: RowCtx, messageId: int) : Control =
    let picks =
      ctx.Model
      |> Observable.map(fun current ->
        current.State.ReactionUsage
        |> Option.map State.quickReactions
        |> Option.defaultValue(State.quickReactions []))
      |> Observable.distinctUntilChanged

    let host = ContentControl()

    host.Content(
      picks
      |> Observable.map(fun emoji ->
        let picks: Control array = [|
          for mark in emoji ->
            Button()
              .Classes(Theme.name Theme.GhostButton)
              .Padding(4.0, 0.0, 4.0, 0.0)
              .FontSize(13.0)
              .Content(mark)
              .OnClickHandler(fun _ args ->
                args.Handled <- true
                ctx.Dispatch(State.ReactWanted(messageId, mark)))
        |]

        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(2.0)
          .Children(picks))
      |> _.ToBinding()
    )
    |> ignore

    host

  /// The picker flyout: the quick three, then the emoji the user
  /// reacts with, most used first. Control-owned, the house pattern;
  /// a pick closes it.
  let reactionPicker(ctx: RowCtx, messageId: int) : Control =
    let ranked =
      ctx.Model
      |> Observable.map(fun current ->
        let usage = current.State.ReactionUsage |> Option.defaultValue []

        let quick = State.quickReactions usage

        quick @ (usage |> State.frequentReactions |> List.except quick)
        |> List.distinct)
      |> Observable.distinctUntilChanged

    let flyout = Popup()
    let host = ContentControl()

    host.Content(
      ranked
      |> Observable.map(fun emoji ->
        let picks: Control array = [|
          for mark in emoji ->
            Button()
              .Classes(Theme.name Theme.GhostButton)
              .Padding(6.0, 2.0, 6.0, 2.0)
              .FontSize(15.0)
              .Content(mark)
              .OnClickHandler(fun _ args ->
                args.Handled <- true
                flyout.IsOpen <- false
                ctx.Dispatch(State.ReactWanted(messageId, mark)))
        |]

        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(2.0)
          .Children(picks))
      |> _.ToBinding()
    )
    |> ignore

    flyout.Child <-
      Border()
        .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
        .BorderThickness(1.0)
        .CornerRadius(6.0)
        .Padding(6.0)
        .MaxWidth(220.0)
        .Child(host)

    let more =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(4.0, 0.0, 4.0, 0.0)
        .FontSize(13.0)
        .Content("＋")
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          flyout.IsOpen <- not flyout.IsOpen)

    ToolTip.SetTip(more, "More reactions")

    Panel().Children(more, flyout)

  /// One bubble: the message body in a rounded fill, mine on the
  /// accent, theirs on the card surface. The bubble carries its own
  /// name, so a ghost's landing can find and flash it.
  let bubble
    (
      ctx: RowCtx,
      key: string,
      who: Message,
      mine: bool,
      showsStatus: IObservable<bool>
    ) : Control =
    let body =
      CommitCard.body(
        ValueNone,
        who.Body,
        who.BodySegments,
        who.Mentions,
        (if mine then CommitCard.OnFill else CommitCard.Accent)
      )

    let bubbleBorder =
      Border()
        .Name($"ChatMessage{who.Id}")
        .Classes(
          Theme.name(if mine then Theme.AccentBack else Theme.SurfaceBack)
        )
        .CornerRadius(12.0)
        .Padding(10.0, 6.0, 10.0, 6.0)
        .MaxWidth(420.0)
        .Child(
          StackPanel()
            .Spacing(4.0)
            .Children(
              [|
                yield body

                match images(ctx, who) with
                | ValueSome shown -> yield shown
                | ValueNone -> ()

                match who.Voice with
                | Some clip ->
                  yield
                    voiceChip(
                      {
                        Audio = ctx.Audio
                        Http = ctx.Http
                        Settings = ctx.Settings
                        Solo = ctx.Solo
                        Clip = clip
                      }
                    )
                | None -> ()

                match linkCard(ctx, key, who.Id, mine) with
                | ValueSome card -> yield card
                | ValueNone -> ()
              |]
            )
        )

    // The double-click heart: the site's double-tap, on the desktop's
    // own pair of clicks.
    bubbleBorder.DoubleTapped.Add(fun _ ->
      ctx.Dispatch(State.ReactWanted(who.Id, "❤️")))

    let children: Control array = [|
      match who.ReplyTo with
      | Some reply ->
        let author =
          authorName(
            ctx,
            (match who.User with
             | Some user when reply.UserId <> ctx.ViewerId -> Some user
             | _ -> None),
            reply.UserId
          )

        yield ghost(ctx, reply, mine, author)
      | None -> ()

      yield bubbleBorder

      yield
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Margin(2.0, 0.0, 0.0, 0.0)
          .Children(
            reactionChips(ctx, key, who.Id),
            quickPicks(ctx, who.Id),
            reactionPicker(ctx, who.Id)
          )

      match receipts(ctx, key, who, mine) with
      | ValueSome line ->
        line.IsVisible(showsStatus) |> ignore
        yield line
      | ValueNone -> ()
    |]

    StackPanel()
      .Spacing(2.0)
      .HorizontalAlignment(
        if mine then
          HorizontalAlignment.Right
        else
          HorizontalAlignment.Left
      )
      .Children(children)

  /// One staged bubble: the message on its way in, or one the server
  /// refused — refused once more with its retry. The staged shape is
  /// hollow, the site's own: no fill, an accent edge; the refused one
  /// a danger edge.
  let stagedBubble(ctx: RowCtx, pending: PendingSend) : Control =
    let children: Control array = [|
      match pending.Message.Body with
      | Some text ->
        yield CommitCard.body(ValueNone, text, [], [], CommitCard.Accent)
      | None -> ()

      yield
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            TextBlock()
              .Classes(
                Theme.name(
                  if pending.Rejected then Theme.FgDanger else Theme.Muted
                )
              )
              .FontSize(11.0)
              .Text(if pending.Rejected then "rejected" else "staged"),
            (if pending.Rejected then
               Button()
                 .Classes(Theme.name Theme.GhostButton)
                 .Padding(4.0, 0.0, 4.0, 2.0)
                 .FontSize(11.0)
                 .Content("retry")
                 .OnClickHandler(fun _ args ->
                   args.Handled <- true
                   ctx.Dispatch(State.RetryWanted pending.ClientId))
               :> Control
             else
               TextBlock() :> Control)
          )
    |]

    Border()
      .Classes(
        Theme.name(
          if pending.Rejected then
            Theme.RejectedBubble
          else
            Theme.StagedBubble
        )
      )
      .CornerRadius(12.0)
      .Padding(10.0, 6.0, 10.0, 6.0)
      .MaxWidth(420.0)
      .Child(StackPanel().Spacing(4.0).Children(children))
      .HorizontalAlignment(HorizontalAlignment.Right)

  /// One author's run: the org label, the bubbles stacked, the
  /// receipts under the status message's bubble.
  let run(ctx: RowCtx, key: string, ofRun: State.Run) : Control =
    let statusMessage =
      ctx.Model
      |> Observable.map(fun current ->
        current.State.Threads
        |> Map.tryFind key
        |> Option.bind(fun thread ->
          State.statusOf(ctx.IsOrg, ctx.ViewerId, thread)))

    let children: Control array = [|
      if ctx.IsOrg && not ofRun.IsMine then
        let label: Control =
          match ofRun.Author with
          | Some user ->
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(4.0)
              .Children(
                Avatar.view {
                  Name = user.Name
                  Url = user.AvatarUrl
                  Lane = user.Lane
                  Size = 16.0
                  Cache = ctx.Images
                },
                TextBlock()
                  .Classes(Theme.name Theme.Mono)
                  .FontSize(11.0)
                  .Text(authorName(ctx, ofRun.Author, ofRun.AuthorId))
                  .VerticalAlignment(VerticalAlignment.Center)
              )
            :> Control
          | None ->
            TextBlock()
              .Classes(Theme.name Theme.Mono)
              .FontSize(11.0)
              .Text(authorName(ctx, ofRun.Author, ofRun.AuthorId))
            :> Control

        yield
          label
            .Margin(4.0, 6.0, 4.0, 0.0)
            .HorizontalAlignment(HorizontalAlignment.Left)

      for carried in ofRun.Bubbles ->
        match carried with
        | State.Sent message ->
          bubble(
            ctx,
            key,
            message,
            ofRun.IsMine,
            statusMessage
            |> Observable.map(fun status ->
              status |> Option.map _.Id |> Option.contains message.Id)
          )
        | State.Staged pending -> stagedBubble(ctx, pending)

      // The run's reply affordance answers its newest message; the
      // composer opens the reply bar.
      match
        ofRun.Bubbles
        |> List.tryFindBack(function
          | State.Sent message -> message.Event.IsNone
          | State.Staged _ -> false)
      with
      | Some(State.Sent newest) ->
        yield
          Button()
            .Classes(Theme.name Theme.GhostButton)
            .Padding(6.0, 0.0, 6.0, 2.0)
            .FontSize(11.0)
            .Content("reply")
            .HorizontalAlignment(
              if ofRun.IsMine then
                HorizontalAlignment.Right
              else
                HorizontalAlignment.Left
            )
            .OnClickHandler(fun _ args ->
              args.Handled <- true
              ctx.Dispatch(State.ReplyWanted newest))
      | _ -> ()
    |]

    StackPanel()
      .Spacing(2.0)
      .Margin(0.0, 6.0, 0.0, 0.0)
      .HorizontalAlignment(
        if ofRun.IsMine then
          HorizontalAlignment.Right
        else
          HorizontalAlignment.Left
      )
      .Children(children)

  /// One quiet org event line.
  let eventRow(ctx: RowCtx, message: Message) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .FontSize(12.0)
      .Margin(0.0, 8.0, 0.0, 0.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Text(State.eventText(ctx.ViewerId, message))

  /// One calendar-day break: the marker pill with its trailing
  /// hairline.
  let dayRow(label: string) : Control =
    let rule =
      Border()
        .Classes(Theme.name Theme.Hairline)
        .Height(1.0)
        .VerticalAlignment(VerticalAlignment.Center)

    DockPanel()
      .Margin(0.0, 12.0, 0.0, 0.0)
      .Children(
        Border()
          .Classes(Theme.name Theme.DayMarker)
          .Dock(Dock.Left)
          .Child(TextBlock().Text(label)),
        rule
      )

  /// The unread divider: the "HEAD · N new" line the head wears.
  let headRow(count: int) : Control =
    Border()
      .Classes(Theme.name Theme.HeadRule)
      .Margin(0.0, 10.0, 0.0, 0.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .Child(TextBlock().Text($"HEAD · {count} new"))

  /// The message list: virtualized, keyed, following its tail.
  let list
    (ctx: RowCtx, key: string, restore: float, thread: IObservable<ThreadState>)
    : Control =
    let tail = Tail()
    let chase = Restore(restore)

    let rowItems =
      thread
      |> Observable.map(fun current ->
        State.threadRows(
          ctx.ViewerId,
          ctx.IsOrg,
          DateTimeOffset.UtcNow,
          current
        ))

    let template =
      FuncDataTemplate<State.ThreadRow>(
        (fun item _ ->
          if isNull(box item) then
            Panel() :> Control
          else
            match item with
            | State.ThreadRow.Day(_, label) -> dayRow label
            | State.ThreadRow.Head count -> headRow count
            | State.ThreadRow.Event message -> eventRow(ctx, message)
            | State.ThreadRow.Run ofRun -> run(ctx, key, ofRun)),
        false
      )

    let items =
      ItemsControl()
        .ItemTemplate(template)
        .ItemsPanel(FuncTemplate<Panel>(fun () -> VirtualizingStackPanel()))
        .BindItemsSource(rowItems, State.threadRowKey)

    let scroller = ScrollViewer().Name("RemotesThread").Content(items)

    scroller.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled

    // The scroll model in one handler: the top asks for older pages,
    // the bottom edge is the tail a riding reader keeps, a remembered
    // place lands once the extent can hold it, and every settled place
    // parks itself in the store.
    scroller.ScrollChanged.Add(fun _ ->
      let extent = scroller.Extent.Height
      let room = extent - scroller.Viewport.Height

      if scroller.Offset.Y < 240.0 && room > 0.0 then
        ctx.Dispatch State.OlderWanted

      let fromEnd = room - scroller.Offset.Y

      if fromEnd < 48.0 then
        tail.Riding <- true
      elif extent > tail.Extent + 0.5 && tail.Riding then
        scroller.ScrollToEnd()
      else
        tail.Riding <- false

      // A whole viewport below the reading is the site's own bar for
      // "far from the present edge".
      let behind = fromEnd > scroller.Viewport.Height

      ctx.Dispatch(State.RideMoved(tail.Riding, behind))

      if not chase.Landed then
        if room >= chase.Target then
          scroller.Offset <- Vector(0.0, chase.Target)
          chase.Landed <- true
        elif not chase.Asked then
          chase.Asked <- true
          ctx.Dispatch State.OlderWanted
        elif extent > tail.Extent then
          // The ask grew nothing the first time; land where the list
          // ends rather than chase forever.
          chase.Landed <- true

      tail.Extent <- extent
      ctx.Dispatch(State.ThreadScrolled scroller.Offset.Y))

    // The landing asks: one message a ghost named, or the present
    // edge. The listener ends with the list's detach.
    let landings =
      ctx.Model |> Observable.map _.Landing |> Observable.distinctUntilChanged

    let land =
      Sub.ofId "landing" (fun _ ->
        landings.Subscribe(function
          | Some(State.Message id) ->
            scroller.Offset <- Vector(0.0, 0.0)

            scroller.GetVisualDescendants()
            |> Seq.tryPick(fun visual ->
              match visual with
              | :? Border as border when visual.Name = $"ChatMessage{id}" ->
                Some border
              | _ -> None)
            |> Option.iter(fun border ->
              border.Classes.Add(Theme.name Theme.FlashRing) |> ignore

              let timer = DispatcherTimer()
              timer.Interval <- TimeSpan.FromMilliseconds 1200.0

              let stop() =
                timer.Stop()
                border.Classes.Remove(Theme.name Theme.FlashRing) |> ignore

              timer.Tick.Add(fun _ -> stop())
              border.DetachedFromVisualTree.Add(fun _ -> stop())
              timer.Start())

            ctx.Dispatch State.Landed
          | Some State.Present ->
            scroller.ScrollToEnd()
            ctx.Dispatch State.Landed
          | None -> ()))
      |> Sub.untilDetached scroller
      |> List.exactlyOne
      |> snd

    land ignore |> ignore
    scroller

  /// The composer: the meta row with the stash chip and the counter,
  /// the reply bar, the staged images, and the input row. Ctrl+Enter
  /// The floating return to the thread's present edge; while messages
  /// arrived unseen, it doubles as the "N new" pill.
  let present
    (
      dispatch: Dispatch<State.Msg>,
      visible: IObservable<bool>,
      label: IObservable<string>
    ) : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(10.0, 4.0, 10.0, 4.0)
      .HorizontalAlignment(HorizontalAlignment.Center)
      .VerticalAlignment(VerticalAlignment.Bottom)
      .Margin(0.0, 0.0, 0.0, 8.0)
      .Content(label)
      .IsVisible(visible)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        dispatch State.PresentWanted)

  /// One staged image chip: its name, its remove, and its description
  /// box. The box's text follows its own description alone, so typing
  /// keeps the caret while the model moves around it.
  let imageChip
    (
      dispatch: Dispatch<State.Msg>,
      described: IObservable<string>,
      image: ImageAttachment,
      index: int
    ) : Control =
    let described =
      TextBox()
        .PlaceholderText("describe the image")
        .FontSize(13.0)
        .MinHeight(32.0)
        .Text(described |> Observable.distinctUntilChanged |> _.ToBinding())
        .OnTextChangedHandler(fun box _ ->
          box.Text
          |> Option.ofObj
          |> Option.defaultValue ""
          |> fun text -> State.Described(index, text)
          |> dispatch)

    StackPanel()
      .Spacing(2.0)
      .Children(
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(6.0)
          .Children(
            TextBlock()
              .Classes(Theme.name Theme.Mono)
              .FontSize(12.0)
              .Text(image.FileName)
              .TextTrimming(TextTrimming.CharacterEllipsis),
            Button()
              .Classes(Theme.name Theme.GhostButton)
              .Padding(4.0, 0.0, 4.0, 2.0)
              .FontSize(12.0)
              .Content("x")
              .OnClickHandler(fun _ args ->
                args.Handled <- true
                dispatch(State.ImageRemoved index))
          ),
        described
      )

  /// The link-card picker: a quiet button whose flyout lists the
  /// draft's links, the first one, or none. The button itself shows
  /// only while the draft carries a link. The flyout is
  /// control-owned: it dies with the pane.
  let linkPicker
    (
      dispatch: Dispatch<State.Msg>,
      draft: IObservable<string>,
      choice: IObservable<LinkCard>
    ) : Control =
    let links = draft |> Observable.map State.linksOf

    let label =
      Observable.combineLatest links choice
      |> Observable.map(fun (found, chosen) ->
        match chosen with
        | LinkCard.CardOf url when found |> List.contains url ->
          State.plainLink url
        | LinkCard.NoCard -> "no card"
        | _ -> if found.IsEmpty then "no links" else "first link")

    let rows =
      Observable.combineLatest links choice
      |> Observable.map(fun (found, chosen) -> [
        yield ("first link", LinkCard.FirstLink)

        for url in found -> State.plainLink url, LinkCard.CardOf url
        yield ("no card", LinkCard.NoCard)
      ])
      |> Observable.distinctUntilChanged

    let host = ContentControl()

    host.Content(
      rows
      |> Observable.map(fun picks ->
        StackPanel().Children [|
          for label', pick in picks ->
            let choice: Control =
              Button()
                .Classes(Theme.name Theme.GhostButton)
                .Padding(6.0, 2.0, 6.0, 2.0)
                .FontSize(12.0)
                .Content(label')
                .OnClickHandler(fun _ args ->
                  args.Handled <- true
                  dispatch(State.LinkChosen pick))

            choice
        |])
      |> _.ToBinding()
    )
    |> ignore

    let flyout =
      Popup()
        .Child(
          Border()
            .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
            .BorderThickness(1.0)
            .CornerRadius(6.0)
            .Padding(4.0)
            .Child(host)
        )

    let quiet =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(6.0, 0.0, 6.0, 0.0)
        .FontSize(11.0)
        .Content(label |> Observable.distinctUntilChanged |> _.ToBinding())
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          flyout.IsOpen <- not flyout.IsOpen)

    ToolTip.SetTip(quiet, "Choose which link gets a card")

    Panel()
      .Children(quiet, flyout)
      .HorizontalAlignment(HorizontalAlignment.Right)
      .IsVisible(links |> Observable.map(fun found -> not found.IsEmpty))

  /// Why a capture or a playback could not start, in one line.
  let audioWhy(failure: AudioFailure) : string =
    match failure with
    | NoDevice -> "no microphone was found"
    | Denied -> "the microphone is not allowed to this app"
    | Busy -> "the microphone is busy with another app"
    | AudioFailed message -> message

  /// The recording lane's own bounds, the web's own: a half second
  /// before a recording counts, a minute before it stops itself, and
  /// the warning color through its last ten seconds.
  let minRecording = TimeSpan.FromMilliseconds 500.0
  let maxRecording = TimeSpan.FromSeconds 60.0
  let warnRecording = TimeSpan.FromSeconds 50.0

  /// The live recording lane: 48 bars that follow the microphone's
  /// level on a 50 ms timer, the elapsed clock, the stop and cancel
  /// buttons. The capture and the timer are control-owned and stop on
  /// every path; a stop under the half second reads as a cancel.
  type RecordLane(audio: IAudioEngine voption, dispatch: Dispatch<State.Msg>) as this
    =
    inherit UserControl()

    let bars = [|
      for _ in 0..47 ->
        Border()
          .Width(3.0)
          .Height(2.0)
          .CornerRadius(1.5)
          .VerticalAlignment(VerticalAlignment.Bottom)
          .Classes(Theme.name Theme.FgAccent)
    |]

    let clock =
      TextBlock().Classes(Theme.name Theme.Mono).FontSize(11.0).MinWidth(64.0)

    let meter = DispatcherTimer()

    let watch = Diagnostics.Stopwatch()

    let mutable capture: IMicrophoneCapture option = None

    // A lane that detached mid-open stops whatever the engine hands
    // back; the meter never starts on a detached control.
    let mutable gone = false

    let release() =
      meter.Stop()
      watch.Stop()

      capture |> Option.iter(fun held -> held.Cancel())

      capture <- None

    let finish() =
      match capture with
      | None -> ()
      | Some held ->
        let taken = watch.Elapsed

        release()

        if taken < minRecording then
          dispatch State.RecordCancelled
        else
          asyncEx {
            let! stopped = held.Stop()

            let staged =
              match stopped with
              | Ok wav -> State.stageVoice wav
              | Error failure -> Error(audioWhy failure)

            dispatch(State.VoiceStaged staged)
          }
          |> Async.StartImmediate

    do
      let wave =
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(2.0)
          .Height(28.0)
          .VerticalAlignment(VerticalAlignment.Center)
          .Children([| for bar in bars -> bar :> Control |])

      let stop =
        Button()
          .Classes(Theme.name Theme.AccentButton)
          .Padding(8.0, 2.0, 8.0, 2.0)
          .Content("Stop")
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            finish())

      let cancel =
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(8.0, 2.0, 8.0, 2.0)
          .Content("Cancel")
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            release()
            dispatch State.RecordCancelled)

      this.Content <-
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .Children(wave, clock, stop, cancel)

      meter.Interval <- TimeSpan.FromMilliseconds 50.0

      meter.Tick.Add(fun _ ->
        let level = capture |> Option.map _.Level() |> Option.defaultValue 0.0

        // The bars shift left: each tick pushes the newest level onto
        // the end and the oldest falls off, the way the web's meter
        // scrolls.
        Array.blit bars 1 bars 0 (bars.Length - 1)

        bars[bars.Length - 1].Height <- 2.0 + level * 26.0

        let seconds = int watch.Elapsed.TotalSeconds

        clock.Text <- $"0:{seconds:D2}/1:00"

        // The last ten seconds read in the site's warning color.
        clock.Classes.Remove(Theme.name Theme.FgWarning) |> ignore

        if watch.Elapsed >= warnRecording then
          clock.Classes.Add(Theme.name Theme.FgWarning) |> ignore

        if watch.Elapsed >= maxRecording then
          finish())

      // The capture opens on attach; a refusal ends the lane at once
      // with the failure named in the notice.
      this.AttachedToVisualTree.Add(fun _ ->
        asyncEx {
          match audio with
          | ValueNone -> dispatch State.RecordCancelled
          | ValueSome engine ->
            let! opened = engine.StartCapture()

            if gone then
              match opened with
              | Ok live -> live.Cancel()
              | Error _ -> ()
            else
              match opened with
              | Ok live ->
                capture <- Some live
                watch.Restart()
                meter.Start()
              | Error failure ->
                dispatch(State.VoiceStaged(Error(audioWhy failure)))
        }
        |> Async.StartImmediate)

    override this.OnDetachedFromVisualTree(e) =
      base.OnDetachedFromVisualTree e
      gone <- true
      release()

  /// The composer: the meta row with the stash chip and the counter,
  /// the reply bar, the staged images, and the input row. Ctrl+Enter
  /// pushes, Esc cancels the reply.
  let composer
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      target: ChatTarget,
      thread: IObservable<ThreadState>
    ) : Control =
    let placeholder =
      match target with
      | Partner partner -> $"Message @{partner.Handle}"
      | Org org ->
        let orgName = org.Name |> Option.defaultValue "the org"
        $"Message {orgName}"

    let draft = model |> Observable.map _.Draft
    let images = model |> Observable.map _.Images
    let descriptions = model |> Observable.map _.Descriptions
    let choice = model |> Observable.map _.LinkChoice

    let reply = thread |> Observable.map(fun current -> current.ReplyingTo)

    let counter = draft |> Observable.map(fun text -> text.TrimEnd().Length)

    // The site's own word: quiet until 80%, amber from 95%, danger at
    // the limit, thousands grouped the way the counter reads them.
    let counterBlock =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .FontSize(11.0)
        .Text(
          counter
          |> Observable.map(fun length ->
            FormattableString.Invariant $"""{length:N0}/2,000""")
        )
        .IsVisible(counter |> Observable.map(fun length -> length >= 1600))

    Theme.bindClass(
      counterBlock,
      Theme.FgWarning,
      counter |> Observable.map(fun length -> length >= 1900 && length < 2000)
    )

    // The counter goes quiet-danger at the limit; the class carries
    // the color, never a brush observable.
    Theme.bindClass(
      counterBlock,
      Theme.FgDanger,
      counter |> Observable.map(fun length -> length >= 2000)
    )

    let metaRow =
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8.0)
        .Children(
          TextBlock().Classes(Theme.name Theme.Mono).FontSize(11.0),
          linkPicker(dispatch, draft, choice),
          counterBlock
        )

    let replyBar =
      ContentControl()
        .Content(
          reply
          |> Observable.map(function
            | Some parent ->
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(8.0)
                .Children(
                  TextBlock()
                    .Classes(Theme.name Theme.Muted)
                    .FontSize(12.0)
                    .Text($"in reply to: {parent.Body}")
                    .TextTrimming(TextTrimming.CharacterEllipsis),
                  Button()
                    .Classes(Theme.name Theme.GhostButton)
                    .Padding(4.0, 0.0, 4.0, 2.0)
                    .FontSize(11.0)
                    .Content("cancel")
                    .OnClickHandler(fun _ args ->
                      args.Handled <- true
                      dispatch State.ReplyCancelled)
                )
              :> Control
            | None -> null)
          |> _.ToBinding()
        )

    // The staged images: the chips rebuild only when the image list
    // itself moves; each description box follows its own description,
    // so typing never rebuilds the panel under the caret.
    let staged =
      ContentControl()
        .Content(
          images
          |> Observable.distinctUntilChanged
          |> Observable.map(fun staged' ->
            let chips: Control array = [|
              for index, image in List.indexed staged' ->
                imageChip(
                  dispatch,
                  descriptions
                  |> Observable.map(fun described ->
                    described |> List.tryItem index |> Option.defaultValue ""),
                  image,
                  index
                )
            |]

            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(8.0)
              .Children(chips)
            :> Control)
          |> _.ToBinding()
        )
        .IsVisible(images |> Observable.map(fun staged' -> not staged'.IsEmpty))

    let input =
      TextBox()
        .Name("RemotesComposer")
        .PlaceholderText(placeholder)
        .MinHeight(40.0)
        .MaxHeight(160.0)
        .AcceptsReturn(true)
        .Text(draft |> Observable.distinctUntilChanged |> _.ToBinding())
        .OnTextChangedHandler(fun box _ ->
          box.Text
          |> Option.ofObj
          |> Option.defaultValue ""
          |> State.DraftChanged
          |> dispatch)

    // The site's fine-pointer rule, through the tunnel so the box's
    // own Enter handling never eats it: Enter pushes, Shift+Enter
    // makes the newline, and Esc cancels the reply or discards a live
    // recording.
    input.AddHandler(
      InputElement.KeyDownEvent,
      (fun _ args ->
        let plainEnter =
          args.Key = Key.Enter && args.KeyModifiers = KeyModifiers.None

        let controlEnter =
          args.Key = Key.Enter
          && args.KeyModifiers.HasFlag(KeyModifiers.Control)

        if plainEnter || controlEnter then
          args.Handled <- true
          dispatch State.SendWanted
        elif args.Key = Key.Escape then
          args.Handled <- true
          dispatch State.ReplyCancelled
          dispatch State.RecordCancelled
        else
          ()),
      RoutingStrategies.Tunnel
    )

    let pick =
      let button =
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0)
          .Content(Icons.iconAt(Glyph.Image, 16.0))
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            dispatch State.PickWanted)

      ToolTip.SetTip(button, "Attach images")
      button

    let send =
      Button()
        .Classes(Theme.name Theme.AccentButton)
        .MinWidth(72.0)
        .Content("Push")
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          dispatch State.SendWanted)

    // The staged voice note: a quiet chip with its discard; the voice
    // rides alone, so the chip replaces the mic button while staged.
    let stagedVoice =
      let host = ContentControl()

      host.Content(
        model
        |> Observable.map(fun current -> current.Voice)
        |> Observable.distinctUntilChanged
        |> Observable.map(function
          | Some _ ->
            StackPanel()
              .Orientation(Orientation.Horizontal)
              .Spacing(6.0)
              .Children(
                Icons.iconAt(Glyph.Mic, 14.0),
                TextBlock()
                  .Classes(Theme.name Theme.Mono)
                  .FontSize(11.0)
                  .Text("voice note staged")
                  .VerticalAlignment(VerticalAlignment.Center),
                Button()
                  .Classes(Theme.name Theme.GhostButton)
                  .Padding(4.0, 0.0, 4.0, 2.0)
                  .FontSize(11.0)
                  .Content("discard")
                  .OnClickHandler(fun _ args ->
                    args.Handled <- true
                    dispatch State.VoiceDiscarded)
              )
            :> Control
          | None -> TextBlock() :> Control)
        |> _.ToBinding()
      )
      |> ignore

      host.IsVisible(
        model |> Observable.map(fun current -> current.Voice.IsSome)
      )
      |> ignore

      host

    // The mic button: one quiet round of recording, offered only while
    // nothing else rides the push and the platform passed an engine.
    let mic =
      let button =
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0)
          .Content(Icons.iconAt(Glyph.Mic, 16.0))
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            dispatch State.RecordWanted)

      ToolTip.SetTip(button, "Record a voice note")

      button.IsVisible(
        Observable.combineLatest
          (model
           |> Observable.map(fun current ->
             current.Voice.IsNone && current.Images.IsEmpty))
          (model
           |> Observable.map(fun current ->
             current.Draft.Trim() = "" && props.Env.Natives.Audio.IsSome))
        |> Observable.map(fun (clear, ready) -> clear && ready)
      )
      |> ignore

      button

    // The recording lane: the live bars, the elapsed clock, the stop
    // and cancel buttons. The capture and the 50 ms meter timer are
    // control-owned; every path — stop, cancel, detach, the 60 s
    // ceiling — stops the timer and releases the capture.
    let recordLane =
      let lane = RecordLane(props.Env.Natives.Audio, dispatch)

      ContentControl()
        .Name("RemotesRecord")
        .Content(lane)
        .IsVisible(model |> Observable.map(fun current -> current.Recording))

    StackPanel()
      .Spacing(6.0)
      .Margin(0.0, 8.0, 0.0, 0.0)
      .Children(
        metaRow,
        replyBar,
        staged,
        stagedVoice,
        recordLane,
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .Children(pick, mic, StackPanel().Spacing(0.0).Children(input), send)
      )

  /// The bar that takes the composer's place when the viewer cannot
  /// push: a fetched request to settle, or the word on why. The
  /// server's copy verbatim.
  let accessBar
    (dispatch: Dispatch<State.Msg>, partner: User, access: PushAccess)
    : Control =
    let bar =
      match access with
      | PushRequest ->
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(8.0)
          .Children(
            TextBlock()
              .Text(
                $"@{partner.Handle} wants to push to you. Merge to reply, or drop it. They won't be told."
              )
              .TextWrapping(TextWrapping.Wrap)
              .Classes(Theme.name Theme.Muted),
            Button()
              .Classes(Theme.name Theme.GhostButton)
              .Content("Drop")
              .OnClickHandler(fun _ args ->
                args.Handled <- true
                dispatch(State.SettleWanted(partner, false))),
            Button()
              .Classes(Theme.name Theme.AccentButton)
              .Content("Merge")
              .OnClickHandler(fun _ args ->
                args.Handled <- true
                dispatch(State.SettleWanted(partner, true)))
          )
        :> Control
      | PushWaiting ->
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(13.0)
          .Text($"pushed. waiting for @{partner.Handle} to merge your request")
          .TextWrapping(TextWrapping.Wrap)
        :> Control
      | PushRestricted ->
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(13.0)
          .Text(
            $"@{partner.Handle} doesn't follow you, so you can't push to them yet"
          )
          .TextWrapping(TextWrapping.Wrap)
        :> Control
      | PushClosed
      | UnknownAccess _ ->
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(13.0)
          .Text($"@{partner.Handle} isn't taking remotes")
          .TextWrapping(TextWrapping.Wrap)
        :> Control
      | PushOpen -> TextBlock() :> Control

    bar

  /// One quiet line the menus say when the server refuses, shown under
  /// the pane's header.
  let notice(model: IObservable<State.Model>) : Control =
    TextBlock()
      .Classes(Theme.name Theme.Mono)
      .FontSize(11.0)
      .Text(
        model
        |> Observable.map(fun current ->
          current.Notice |> Option.defaultValue "")
      )
      .TextWrapping(TextWrapping.Wrap)
      .IsVisible(model |> Observable.map(fun current -> current.Notice.IsSome))

  /// One menu pick as a button row.
  let menuPick
    (dispatch: Dispatch<State.Msg>, label: string, msg: State.Msg)
    : Control =
    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(6.0, 2.0, 6.0, 2.0)
      .FontSize(12.0)
      .HorizontalAlignment(HorizontalAlignment.Stretch)
      .HorizontalContentAlignment(HorizontalAlignment.Left)
      .Content(label)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        dispatch msg)

  /// One destructive pick: the site's own word first, so one click
  /// never lands it. The flyout carries the question, the
  /// consequences, and the Cancel and destructive buttons.
  let confirmPick
    (
      dispatch: Dispatch<State.Msg>,
      label: string,
      title: string,
      body: string,
      verb: string,
      msg: State.Msg
    ) : Control =
    let flyout = Popup()

    flyout.Child <-
      Border()
        .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
        .BorderThickness(1.0)
        .CornerRadius(6.0)
        .Padding(10.0)
        .MaxWidth(300.0)
        .Child(
          StackPanel()
            .Spacing(6.0)
            .Children(
              TextBlock()
                .Classes(Theme.name Theme.Subtitle)
                .FontSize(13.0)
                .Text(title),
              TextBlock()
                .Classes(Theme.name Theme.Muted)
                .FontSize(12.0)
                .TextWrapping(TextWrapping.Wrap)
                .Text(body),
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(8.0)
                .HorizontalAlignment(HorizontalAlignment.Right)
                .Children(
                  Button()
                    .Classes(Theme.name Theme.GhostButton)
                    .Padding(8.0, 2.0, 8.0, 2.0)
                    .FontSize(12.0)
                    .Content("Cancel")
                    .OnClickHandler(fun _ args ->
                      args.Handled <- true
                      flyout.IsOpen <- false),
                  Button()
                    .Classes(Theme.name Theme.DangerButton)
                    .Padding(8.0, 2.0, 8.0, 2.0)
                    .FontSize(12.0)
                    .Content(verb)
                    .OnClickHandler(fun _ args ->
                      args.Handled <- true
                      flyout.IsOpen <- false
                      dispatch msg)
                )
            )
        )

    Panel()
      .Children(
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0, 2.0, 6.0, 2.0)
          .FontSize(12.0)
          .HorizontalAlignment(HorizontalAlignment.Stretch)
          .HorizontalContentAlignment(HorizontalAlignment.Left)
          .Content(label)
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            flyout.IsOpen <- not flyout.IsOpen),
        flyout
      )

  /// The conversation's own menu: view profile, pin, stash, mutes,
  /// block or leave. The flyout is control-owned, the way the link
  /// picker's is.
  let actionsMenu
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      target: ChatTarget,
      thread: IObservable<ThreadState>
    ) : Control =
    let title =
      match target with
      | Partner partner -> partner.Name
      | Org org -> org.Name |> Option.defaultValue "the org"

    let pinned =
      thread |> Observable.map(fun current -> current.PinnedAt.IsSome)

    let archived = thread |> Observable.map _.IsArchived

    let pick(label: string, msg: State.Msg) : Control =
      menuPick(dispatch, label, msg)

    let stateful
      (label: string, msg: State.Msg, shown: IObservable<bool>)
      : Control =
      let control = pick(label, msg)
      control.IsVisible(shown) |> ignore
      control

    let rows: Control array = [|
      match target with
      | Partner partner ->
        yield
          Button()
            .Classes(Theme.name Theme.GhostButton)
            .Padding(6.0, 2.0, 6.0, 2.0)
            .FontSize(12.0)
            .HorizontalContentAlignment(HorizontalAlignment.Left)
            .Content("view profile")
            .OnClickHandler(fun _ args ->
              args.Handled <- true
              Navigation.go(props.Nav, "/profile/" + partner.Handle, ignore))
      | Org _ -> ()

      yield stateful("pin", State.PinWanted true, pinned |> Observable.map not)

      yield stateful("unpin", State.PinWanted false, pinned)

      yield
        stateful(
          "stash",
          State.ArchiveWanted true,
          archived |> Observable.map not
        )

      yield stateful("pop", State.ArchiveWanted false, archived)

      yield!
        [
          "mute 1 hour", MuteHour
          "mute until tomorrow", MuteTomorrow
          "mute 1 week", MuteWeek
          "mute forever", MuteForever
          "unmute", MuteOff
        ]
        |> List.map(fun (label, how) -> pick(label, State.MuteWanted how))

      match target with
      | Partner partner ->
        yield
          confirmPick(
            dispatch,
            $"block @{partner.Handle}",
            $"Block {partner.Name}?",
            "This conversation goes away for both of you, and neither of you can message, follow, reply to, or star the other. They won't be told. You can unblock them any time from your settings.",
            "Block",
            State.BlockWanted partner
          )
      | Org _ ->
        yield
          confirmPick(
            dispatch,
            $"leave {title}",
            $"Leave {title}?",
            "You stop seeing the org and its history. A maintainer would have to add you back. If you are its only maintainer, the longest-standing member takes over.",
            "Leave",
            State.OrgRemoveWanted(props.Session.Session.User.Handle, true)
          )
    |]

    let flyout =
      Popup()
        .Child(
          Border()
            .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
            .BorderThickness(1.0)
            .CornerRadius(6.0)
            .Padding(4.0)
            .Child(StackPanel().Children(rows))
        )

    let more =
      Button()
        .Classes(Theme.name Theme.GhostButton)
        .Padding(6.0)
        .Content("···")
        .OnClickHandler(fun _ args ->
          args.Handled <- true
          flyout.IsOpen <- not flyout.IsOpen)

    ToolTip.SetTip(more, $"More options for {title}")

    Panel()
      .Children(more, flyout)
      .HorizontalAlignment(HorizontalAlignment.Right)

  /// The org header's member stack: the org's members, ringed and
  /// overlapped, two rows deep when the org is wide.
  let memberStack(ctx: RowCtx, members: ThreadMember list) : Control =
    StackPanel().Orientation(Orientation.Horizontal).Spacing(0.0).Children [|
      for member' in members ->
        (Avatar.view {
          Name = member'.User.Name
          Url = member'.User.AvatarUrl
          Lane = member'.User.Lane
          Size = 26.0
          Cache = ctx.Images
        })
          .Margin(-6.0, 0.0, 0.0, 0.0)
    |]

  /// The maintainer's menu on an org header: rename, add members with
  /// the share-history choice, a role change per member, remove, and
  /// leave. Control-owned flyout, the house pattern.
  let orgMenu
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      org: OrgRef,
      thread: IObservable<ThreadState>
    ) : Control =
    let viewer = props.Session.Session.User

    let members = thread |> Observable.map(fun current -> current.Members)

    let role = thread |> Observable.map(fun current -> current.Role)

    let renameBox =
      TextBox()
        .Name("OrgRename")
        .PlaceholderText("name")
        .MaxLength(60)
        .MinHeight(30.0)
        .FontSize(12.0)
        .MaxWidth(180.0)
        .Text(
          model
          |> Observable.map _.OrgRename
          |> Observable.distinctUntilChanged
          |> _.ToBinding()
        )
        .OnTextChangedHandler(fun box _ ->
          box.Text
          |> Option.ofObj
          |> Option.defaultValue ""
          |> State.OrgRenameChanged
          |> dispatch)

    let memberBox =
      TextBox()
        .Name("OrgMember")
        .PlaceholderText("@handle")
        .MinHeight(30.0)
        .FontSize(12.0)
        .MaxWidth(180.0)
        .OnKeyDownHandler(fun box args ->
          if args.Key = Key.Enter then
            args.Handled <- true

            let typed =
              box.Text |> Option.ofObj |> Option.defaultValue "" |> _.Trim()

            dispatch(State.OrgMemberChanged typed))

    // The add-section's own count: everyone the org would hold once
    // the picked names land.
    let addCount =
      Observable.combineLatest members (model |> Observable.map _.OrgMembers)
      |> Observable.map(fun (current, picked) ->
        let others =
          current |> List.filter(fun member' -> member'.User.Id <> viewer.Id)

        List.length others + 1 + List.length picked)
      |> Observable.distinctUntilChanged

    let memberHost = ContentControl()

    memberHost.Content(
      members
      |> Observable.map(fun current ->
        current |> List.filter(fun member' -> member'.User.Id <> viewer.Id))
      |> Observable.distinctUntilChanged
      |> Observable.map(fun others ->
        let rows: Control array = [|
          for member' in others ->
            let flipping =
              match member'.Role with
              | Some Maintainer
              | Some(UnknownRole _) -> "make contributor", Contributor
              | Some Contributor
              | None -> "make maintainer", Maintainer

            let label, role = flipping

            StackPanel()
              .Spacing(2.0)
              .Children(
                StackPanel()
                  .Orientation(Orientation.Horizontal)
                  .Spacing(4.0)
                  .Children(
                    TextBlock()
                      .Classes(Theme.name Theme.Mono)
                      .FontSize(11.0)
                      .Text("@" + member'.User.Handle)
                      .VerticalAlignment(VerticalAlignment.Center),
                    menuPick(
                      dispatch,
                      label,
                      State.OrgRoleWanted(member'.User.Handle, role)
                    )
                  ),
                menuPick(
                  dispatch,
                  "Remove from org",
                  State.OrgRemoveWanted(member'.User.Handle, false)
                )
              )
              .Margin(0.0, 2.0, 0.0, 0.0)
        |]

        StackPanel().Children rows)
      |> _.ToBinding()
    )
    |> ignore

    let rows: Control array = [|
      yield
        TextBlock().Classes(Theme.name Theme.Mono).FontSize(11.0).Text("rename")

      yield
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(4.0)
          .Children(
            renameBox,
            Button()
              .Classes(Theme.name Theme.GhostButton)
              .Padding(6.0, 2.0, 6.0, 2.0)
              .FontSize(11.0)
              .Content("save")
              .OnClickHandler(fun _ args ->
                args.Handled <- true
                dispatch State.OrgRenameWanted)
          )

      yield
        TextBlock()
          .Classes(Theme.name Theme.Mono)
          .FontSize(11.0)
          .Text("add members")
          .Margin(0.0, 6.0, 0.0, 0.0)

      yield
        ContentControl()
          .Content(
            addCount
            |> Observable.map(fun count ->
              TextBlock()
                .Classes(Theme.name Theme.Mono)
                .FontSize(11.0)
                .Text($"{count}/100")
              :> Control)
            |> _.ToBinding()
          )

      yield memberBox

      yield
        StackPanel()
          .Orientation(Orientation.Horizontal)
          .Spacing(4.0)
          .Children(
            TextBlock()
              .Classes(Theme.name Theme.Mono)
              .FontSize(11.0)
              .Text(
                "Share history — they can read everything pushed before they joined"
              )
              .TextWrapping(TextWrapping.Wrap)
              .VerticalAlignment(VerticalAlignment.Center),
            CheckBox()
              .IsChecked(
                model
                |> Observable.map(fun current -> current.OrgShare)
                |> _.ToBinding()
              )
              .OnIsCheckedChangedHandler(fun box _ ->
                let shared =
                  match box.IsChecked with
                  | v when v.HasValue && v.Value -> true
                  | _ -> false

                dispatch(State.OrgShareChanged shared))
          )

      yield memberHost

      yield
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0, 2.0, 6.0, 2.0)
          .FontSize(12.0)
          .HorizontalContentAlignment(HorizontalAlignment.Left)
          .Content("add")
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            dispatch State.OrgAddWanted)

      yield
        (let orgName = org.Name |> Option.defaultValue "the org"

         confirmPick(
           dispatch,
           $"leave {orgName}",
           $"Leave {orgName}?",
           "You stop seeing the org and its history. A maintainer would have to add you back. If you are its only maintainer, the longest-standing member takes over.",
           "Leave",
           State.OrgRemoveWanted(viewer.Handle, true)
         ))
    |]

    let flyout =
      Popup()
        .Child(
          Border()
            .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
            .BorderThickness(1.0)
            .CornerRadius(6.0)
            .Padding(6.0)
            .MaxWidth(280.0)
            .Child(ScrollViewer().Content(StackPanel().Children(rows)))
        )

    Panel()
      .Children(
        Button()
          .Classes(Theme.name Theme.GhostButton)
          .Padding(6.0)
          .Content("members")
          .IsVisible(
            role |> Observable.map(fun current -> current = Some Maintainer)
          )
          .OnClickHandler(fun _ args ->
            args.Handled <- true
            flyout.IsOpen <- not flyout.IsOpen),
        flyout
      )
      .HorizontalAlignment(HorizontalAlignment.Right)

  /// The pane's footer: the composer while the thread takes pushes,
  /// the access bar when a direct thread does not, nothing for a
  /// closed org. The content rebuilds only when the access itself
  /// moves: the composer's own bindings must survive every poll.
  let footer
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      target: ChatTarget,
      thread: IObservable<ThreadState>
    ) : Control =
    let access =
      thread |> Observable.map _.Access |> Observable.distinctUntilChanged

    ContentControl()
      .Content(
        access
        |> Observable.map(function
          | PushOpen -> composer(props, model, dispatch, target, thread)
          | PushRequest
          | PushWaiting
          | PushRestricted
          | PushClosed
          | UnknownAccess _ as refused ->
            match target with
            | Partner partner -> accessBar(dispatch, partner, refused)
            | Org _ -> null)
        |> _.ToBinding()
      )

  /// The thread pane for one target: the identity header, the message
  /// list over its overlays, and the typing line.
  let pane
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      solo: Solo,
      target: ChatTarget,
      key: string
    ) : Control =
    let isOrg =
      match target with
      | Org _ -> true
      | Partner _ -> false

    let ctx: RowCtx = {
      Dispatch = dispatch
      Model = model
      Images = props.Env.ImageCache
      ViewerId = props.Session.Session.User.Id
      IsOrg = isOrg
      Nav = props.Nav
      Audio = props.Env.Natives.Audio
      Http = props.Env.Http
      Settings = props.Env.Settings
      Solo = solo
    }

    let thread =
      model
      |> Observable.map(fun current ->
        current.State.Threads
        |> Map.tryFind key
        |> Option.defaultValue(RemotesState.emptyThread target))

    let restore =
      props.Env.Remotes.ScrollPlaceOf key |> ValueOption.defaultValue 0.0

    let header =
      let identity: Control =
        match target with
        | Partner partner ->
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(10.0)
            .Children(
              Avatar.view {
                Name = partner.Name
                Url = partner.AvatarUrl
                Lane = partner.Lane
                Size = 36.0
                Cache = ctx.Images
              },
              StackPanel()
                .VerticalAlignment(VerticalAlignment.Center)
                .Children(
                  TextBlock()
                    .Classes(Theme.name Theme.Subtitle)
                    .Text(partner.Name),
                  TextBlock()
                    .Classes(Theme.name Theme.Mono)
                    .Text("@" + partner.Handle)
                )
            )
          :> Control
        | Org org ->
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(10.0)
            .Children(
              TextBlock()
                .Classes(Theme.name Theme.Subtitle)
                .Text(org.Name |> Option.defaultValue "org")
                .VerticalAlignment(VerticalAlignment.Center),
              ContentControl()
                .Content(
                  thread
                  |> Observable.map(fun current ->
                    memberStack(ctx, current.Members))
                  |> _.ToBinding()
                )
            )
          :> Control

      let menu: Control =
        match target with
        | Org org ->
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Children(
              orgMenu(props, model, dispatch, org, thread),
              actionsMenu(props, model, dispatch, target, thread)
            )
          :> Control
        | Partner _ -> actionsMenu(props, model, dispatch, target, thread)

      DockPanel().LastChildFill(true).Children(menu.Dock(Dock.Right), identity)

    let loadingOlder = thread |> Observable.map _.IsLoadingOlder

    let loadingLine =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text("loading older…")
        .HorizontalAlignment(HorizontalAlignment.Center)
        .Margin(0.0, 8.0, 0.0, 0.0)
        .VerticalAlignment(VerticalAlignment.Top)
        .IsVisible(loadingOlder)

    // The site's own bar: the pill shows when the reading sits a
    // viewport or more behind the present edge, or when messages
    // arrived the reader has not seen; unseen messages name their
    // count.
    let awayFromPresent =
      model
      |> Observable.map(fun current ->
        (not current.Riding && current.Behind) || current.Unseen > 0)
      |> Observable.distinctUntilChanged

    let presentLabel =
      model
      |> Observable.map(fun current ->
        if current.Unseen > 0 then
          $"{current.Unseen} new"
        else
          "back to present")
      |> Observable.distinctUntilChanged

    let presentButton = present(dispatch, awayFromPresent, presentLabel)

    let listHost =
      Panel()
        .Children(list(ctx, key, restore, thread), loadingLine, presentButton)

    let typing =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .FontSize(12.0)
        .Margin(0.0, 4.0, 0.0, 4.0)
        .Text(
          thread
          |> Observable.map(fun current ->
            State.typingText(State.typingNames(isOrg, current)))
        )
        .IsVisible(
          thread
          |> Observable.map(fun current ->
            State.typingNames(isOrg, current) |> List.isEmpty |> not)
        )

    let rows =
      let grid = Grid()

      grid.RowDefinitions <- RowDefinitions("Auto,Auto,*,Auto,Auto")

      grid.Children(
        header.Row(0).Margin(0.0, 0.0, 0.0, 8.0),
        notice(model).Row(1),
        listHost.Row(2),
        typing.Row(3),
        footer(props, model, dispatch, target, thread).Row(4)
      )

    let quietWord =
      match target with
      | Partner partner -> $"empty history. say hi to @{partner.Handle}"
      | Org _ -> "empty history. say hi to the org"

    let quiet =
      TextBlock()
        .Classes(Theme.name Theme.Mono)
        .Text(quietWord)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .VerticalAlignment(VerticalAlignment.Center)
        .IsVisible(
          thread
          |> Observable.map(fun current ->
            current.IsLoaded
            && current.Messages.IsEmpty
            && current.Pending.IsEmpty)
        )

    Panel().Children(rows, quiet)

  /// The detail column: the pane of the checked-out target — rebuilt
  /// only when the target moves — or the quiet invitation.
  let column
    (
      props: Props<'env>,
      model: IObservable<State.Model>,
      dispatch: Dispatch<State.Msg>,
      solo: Solo
    ) : Control =
    let checkedOut =
      model
      |> Observable.map _.State.OpenTarget
      |> Observable.distinctUntilChanged

    ContentControl()
      .Content(
        checkedOut
        |> Observable.map(function
          | Some target ->
            pane(props, model, dispatch, solo, target, ChatTargets.key target)
          | None -> Parts.noThread())
        |> _.ToBinding()
      )

let view
  (props: Props<'env>)
  (model: IObservable<State.Model>)
  (dispatch: Dispatch<State.Msg>)
  : Control =

  // One note sounds at a time across every pane the program builds.
  let solo = Thread.Solo()

  let columns = Grid()

  columns.ColumnDefinitions <- ColumnDefinitions("320,*")

  // The inbox column's right hairline rides a border around it: the
  // column itself is a plain panel.
  let inboxColumn =
    Border()
      .Classes(Theme.name Theme.Hairline)
      .BorderThickness(Thickness(0.0, 0.0, 1.0, 0.0))
      .Child(Parts.inbox(props, model, dispatch))

  columns.Children(
    inboxColumn.Column(0),
    Thread
      .column(props, model, dispatch, solo)
      .Column(1)
      .Margin(16.0, 0.0, 16.0, 0.0)
  )
  |> ignore

  // The chat fills the strip the shell gives it; its top clears the
  // drawn caption band the way every page's pinned header does.
  Grid().Margin(Thickness(0.0, Theme.bandHeight, 0.0, 0.0)).Children(columns)

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession
  and 'env :> IProvideUiClock
  and 'env :> IProvideImageCache
  and 'env :> IProvideProfiles
  and 'env :> IProvideSettings
  and 'env :> IProvideAttachmentSources
  and 'env :> IProvideNatives
  and 'env :> IProvideRemotes>(props: Props<'env>) as this =
  inherit UserControl()

  do
    let program =
      Program.mkProgram (State.init props) (State.update props) (view props)
      |> Program.withSubscription(State.subscribe props)

    let root, lifetime = Program.runWithLifecycle () program

    this.Content <- root
    props.Context.AddDisposable lifetime
