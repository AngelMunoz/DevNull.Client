namespace DevNull.Client.Views

open System
open FSharp.UMX
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Formatting
open DevNull.Client.RemotesState
open DevNull.Client.Views.State

/// The remotes page's pure shaping: the inbox rows, the thread rows,
/// the reaction and typing words, and the pane's own row facts.
module RemotesRows =

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

  /// The last message as the row previews it.
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

  /// The empty line each tab says when its list holds nothing.
  let emptyLine(tab: InboxTab) : string =
    match tab with
    | TabOpen -> "no conversations yet"
    | TabFetched -> "no requests fetched"
    | TabStashed -> "nothing stashed"

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

    let _, rows =
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

  /// The typing names one thread carries, as handles.
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

  /// The reactions one click away before the user has any history.
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

  /// The word and shape one message's meta line carries.
  let receiptWord
    (isOrg: bool, thread: ThreadState, message: Message)
    : Receipt option =
    if isOrg then
      let others =
        thread.Members
        |> List.filter(fun member' -> member'.User.Id <> message.UserId)

      let pulled =
        others
        |> List.filter(fun member' -> member'.ReadThroughId >= message.Id)

      if List.isEmpty others then
        None
      elif
        List.length pulled = List.length others && List.length others > 3
      then
        Some(Pulled "pulled by everyone")
      else
        Some(Pulled $"pulled by {List.length pulled} of {List.length others}")
    elif
      thread.PartnerReadThroughId >= message.Id
      && thread.PartnerReadThroughId > 0
    then
      let pulledAt = message.CreatedAt.LocalDateTime.ToString("h:mm tt")

      Some(Pulled $"pulled {pulledAt}")
    else
      Some Pushed

  /// The who-reacted line.
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

  /// One message's pin state as the thread holds it.
  let pinnedNow(current: ThreadState, message: Message) : bool =
    match current.Pinned with
    | Some pins -> pins |> List.exists(fun pin -> pin.Id = message.Id)
    | None -> message.PinnedAt.IsSome
