namespace DevNull.Client.Views

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
open DevNull.Client.Env
open Microsoft.Extensions.Logging
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
  and 'env :> IProvideRemotes
  and 'env :> IProvideLogging> = {
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
    /// The `@token` the caret stands in while one is being typed, so
    /// the mention offers its handles.
    Mention: string option
    /// The staged images.
    Images: ImageAttachment list
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
    /// The newest ping already answered with the system's own note;
    /// a ping past it sounds once, never on the open.
    PingHeard: int
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
    | DraftChanged of text: string * caret: int
    | DraftSettled of text: string
    | ImageRemoved of index: int
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
    | PingWanted
    | Pinged of Result<unit, AppError>
    | VoiceStaged of Result<(ImageAttachment * Waveform), AppError>
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
      | Ping ->
        // Direct threads leave the author out: there, it can only be
        // the partner — the site's own words.
        if message.UserId = viewerId then
          "you sent a ping"
        else
          match message.User with
          | Some _ -> $"{actor} sent a ping"
          | None -> "you got a ping"
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

  /// One thread's shaping ask: who the viewer is, whether the thread
  /// is an org's, the clock the day labels read, and the thread state
  /// itself.
  [<Struct>]
  type ThreadShape = {
    ViewerId: string
    IsOrg: bool
    Now: DateTimeOffset
    Thread: ThreadState
  }

  /// The thread's rows: the messages and staged sends folded into day
  /// breaks, the unread divider above the first message the reader
  /// had not seen, quiet event lines, and author runs. The web's
  /// `itemsOf`, on the store's thread state.
  let threadRows(shape: ThreadShape) : ThreadRow list =
    let {
          ViewerId = viewerId
          IsOrg = isOrg
          Now = now
          Thread = thread
        } =
      shape

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

  /// What the typing line says: one name, two names, or the crowd. The
  /// dots are the view's own beat, never part of the word.
  let typingText(who: string list) : string =
    match who with
    | [] -> ""
    | [ only ] -> $"{only} is typing"
    | [ one; two ] -> $"{one} and {two} are typing"
    | crowd -> $"{List.length crowd} people are typing"

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
  /// The staging reads the WAV's own samples for the bars, so bytes
  /// the sampler cannot read refuse the stage instead of staging a
  /// silent waveform.
  let stageVoice
    (bytes: byte[])
    : Result<(ImageAttachment * Waveform), AppError> =
    let parsed: Result<DevNull.Client.Core.Commit.Voice, AppError> =
      DevNull.Client.Core.Commit.Voices.parse("voice.wav", bytes)

    match parsed, VoiceMedia.samples bytes with
    | Ok _, Some samples ->
      barsOf samples
      |> DevNull.Client.Core.Commit.Voices.parseWaveform
      |> Result.map(fun bars ->
        ({
          FileName = "voice.wav"
          Bytes = bytes
         },
         bars))
    | Error failure, _ -> Error failure
    | Ok _, None -> Error(UnsupportedVoice "voice.wav")

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

  let inline isWordChar(c: char) = Char.IsLetterOrDigit c || c = '_'

  /// Where the word characters around the caret begin: the first index
  /// past the run of letters, digits, and underscores that ends just
  /// before the caret.
  let tokenStartOf(text: string, caret: int) : int =
    let rec back(at: int) =
      if at >= 0 && isWordChar text[at] then
        back(at - 1)
      else
        at + 1

    back(caret - 1)

  /// The `@token` the caret stands in, when one does: the letters after
  /// an `@` that opens a word, up to the caret. None once the token
  /// closed or the caret sits elsewhere.
  let mentionOf(text: string, caret: int) : string option =
    let tokenStart = tokenStartOf(text, caret)

    let opens =
      tokenStart > 0
      && text[tokenStart - 1] = '@'
      && (tokenStart - 1 = 0 || Char.IsWhiteSpace text[tokenStart - 2])

    if opens then
      let upto = min caret text.Length

      Some(text.Substring(tokenStart, upto - tokenStart))
    else
      None

  /// One handle's completion of the mention at the caret: the rewritten
  /// text and where the caret lands after it.
  let mentionCompleted
    (text: string, caret: int, handle: string)
    : string * int =
    let tokenStart = tokenStartOf(text, caret)

    let from =
      if tokenStart > 0 && text[tokenStart - 1] = '@' then
        tokenStart - 1
      else
        tokenStart

    let replacement = "@" + handle + " "
    let cut = min caret text.Length

    (text.Substring(0, from) + replacement + text.Substring(cut),
     from + replacement.Length)

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
  let rest() : Async<unit> = asyncEx { do! Async.Sleep 300 }

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
      Mention = None
      Images = []
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
      PingHeard = 0
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

  /// The newest ping one thread carries, by its message id.
  let inline lastPingOf(thread: ThreadState) =
    thread.Messages
    |> List.tryFindBack(fun message -> message.Event = Some Ping)
    |> Option.map _.Id
    |> Option.defaultValue 0

  /// A ping that just landed answers with the system's own note: the
  /// viewer's own ping sounds even muted, a partner's only while the
  /// thread is not — the site's own rule. A ping sounds only in a
  /// thread the model already held loaded: pings already there when a
  /// thread opens stay still.
  let pingCmd
    (props: Props<'env>, model: Model, next: Model)
    : Model * Cmd<Msg> =
    match next.State.OpenTarget with
    | Some target ->
      let key = ChatTargets.key target

      match next.State.Threads |> Map.tryFind key with
      | Some thread when thread.IsLoaded ->
        let heard = lastPingOf thread

        let heldBefore =
          match model.State.Threads |> Map.tryFind key with
          | Some before -> before.IsLoaded
          | None -> false

        let own =
          thread.Messages
          |> List.tryFind(fun message -> message.Id = heard)
          |> Option.map(fun message ->
            message.UserId = props.Session.Session.User.Id)
          |> Option.defaultValue false

        let muted =
          thread.MutedUntilAt
          |> Option.map(fun until -> until > next.Now)
          |> Option.defaultValue false

        let sound =
          if heldBefore && heard > model.PingHeard then
            match props.Env.Natives.Sounds with
            | ValueSome head when own || not muted ->
              Cmd.OfFunc.attempt
                (fun (sounds: ISystemSounds) -> sounds.Notify())
                head
                (fun failure -> MenuNotice(failure.Message))
            | _ -> Cmd.none
          else
            Cmd.none

        { next with PingHeard = heard }, sound
      | _ -> next, Cmd.none
    | None -> { next with PingHeard = 0 }, Cmd.none

  let update (props: Props<'env>) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    let log =
      props.Env.LoggerFactory.CreateLogger "DevNull.Client.Views.Remotes"

    // Every log line is an effect: update returns it as a command,
    // and the loop writes it after the computation.
    let failed what error =
      Cmd.ofAction(fun () ->
        log.LogWarning(
          "The {What} failed: {Error}",
          what,
          bounded 300 (describe error)
        ))

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

        let carried, reading =
          readCmd(
            props,
            seenOf(
              props.Session.Session.User.Id,
              {
                settled with
                    DraftKey = keyed
                    Draft = draft |> Option.defaultValue ""
                    Mention = None
                    Images = []
                    LinkChoice = LinkCard.FirstLink
                    Voice = None
                    Recording = false
              }
            )
          )

        let answered, sound = pingCmd(props, model, carried)
        answered, Cmd.batch [ reading; sound ]
      else
        let carried, reading =
          readCmd(props, seenOf(props.Session.Session.User.Id, settled))

        let answered, sound = pingCmd(props, model, carried)
        answered, Cmd.batch [ reading; sound ]
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
        // rows stay quiet rather than noisy. The log keeps the reason.
        {
          model with
              Found =
                Some {
                  Term = term
                  Conversations = []
                  Candidates = []
                }
        },
        failed $"the search for {term}" error
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
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The walk to message {Message} found nothing.",
            messageId
          ))
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
    | DraftChanged(text, caret) ->
      let next = {
        model with
            Draft = text
            Mention = mentionOf(text, caret)
      }

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

      // The site's own rule: a voice note rides alone, so its push
      // carries nothing beside it.
      let ridingAlone =
        model.Voice.IsSome && (body <> "" || not model.Images.IsEmpty)

      let canSend =
        (body <> "" || not model.Images.IsEmpty || model.Voice.IsSome)
        && model.State.OpenTarget.IsSome
        && not ridingAlone

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
          ImageDescriptions = [ for _ in model.Images -> None ]
          Voice = model.Voice
        }

        props.Env.Remotes.SetReplyingTo(target, None)
        props.Env.Settings.SetChatDraft(model.DraftKey, "")

        {
          model with
              Draft = ""
              Mention = None
              Images = []
              LinkChoice = LinkCard.FirstLink
              Voice = None
        },
        Cmd.batch [
          Cmd.OfAsync.perform
            push
            (props.Env.Remotes, target, message)
            (fun () -> StoreWorkDone)

          Cmd.ofAction(fun () ->
            log.LogInformation(
              "The message for {Key} goes out ({Images} images, {Voice}).",
              ChatTargets.key target,
              List.length model.Images,
              match model.Voice with
              | Some _ -> "voice attached"
              | None -> "no voice"
            ))
        ]
      | Some _ when ridingAlone ->
        {
          model with
              Notice =
                Some "A voice note rides alone. Discard it to type a message."
        },
        Cmd.none
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
      // A voice note rides alone: while one is staged, no image lands.
      if model.Voice.IsSome then
        model, Cmd.none
      else
        {
          model with
              Images = (model.Images @ picked) |> List.truncate 4
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
          (fun (store: RemotesStore, who: ChatTarget, away: bool) ->
            store.SetArchived(who, away))
          (props.Env.Remotes, target, archived)
          (fun moved ->
            // The store already rolled the row back and logged the
            // server's word; the notice says only what happened.
            if moved then
              ActionSettled(Ok())
            else
              MenuNotice "Could not update the conversation. Try again.")
      | None -> model, Cmd.none
    | ActionSettled outcome ->
      match outcome with
      | Ok() -> { model with Notice = None }, Cmd.none
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        failed "the conversation action" error
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

        { model with Notice = None },
        Cmd.ofAction(fun () -> log.LogInformation("The block changed."))
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        failed "the block" error
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
        Cmd.ofAction(fun () ->
          log.LogInformation("The org {Org} was created.", org.Id))
      | Error error ->
        {
          model with
              Notice = Some(describe error)
        },
        failed "the org creation" error
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
    | PingWanted ->
      match model.State.OpenTarget with
      | Some target ->
        model,
        Cmd.OfAsync.perform
          (fun (env, session: SignedInSession, who: ChatTarget) -> async {
            let threadTarget =
              match who with
              | Partner user -> CoreRemotes.Partner user.Handle
              | Org org -> CoreRemotes.Org org.Id

            return!
              CoreRemotes.ping(
                env,
                {
                  KeyFile = session.KeyFile
                  Target = threadTarget
                }
              )
          })
          (props.Env, props.Session, target)
          Pinged
      | None -> model, Cmd.none
    | Pinged outcome ->
      let said, logged =
        match outcome with
        | Ok() ->
          None, Cmd.ofAction(fun () -> log.LogDebug("The ping went out."))
        | Error error -> Some(describe error), failed "the ping" error

      { model with Notice = said }, logged
    | VoiceStaged outcome ->
      match outcome with
      | Ok(voice, waveform) ->
        {
          model with
              Recording = false
              Voice = Some(voice, waveform)
              Notice = None
        },
        Cmd.ofAction(fun () ->
          log.LogDebug(
            "The voice note staged ({Bytes} bytes).",
            voice.Bytes.Length
          ))
      | Error failure ->
        {
          model with
              Recording = false
              Notice = Some(describe failure)
        },
        Cmd.ofAction(fun () ->
          log.LogWarning(
            "The voice note did not stage: {Error}",
            bounded 300 (describe failure)
          ))
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
