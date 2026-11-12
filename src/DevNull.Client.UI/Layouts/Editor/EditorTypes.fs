namespace DevNull.Client.Layouts.Editor

open DevNull.Client.Core.ApiTypes

type TabId = TabId of string

module TabId =

  let history = TabId "history"

  let ordinary(key: string) = TabId key

  let thread(commitId: string) = TabId commitId

  let repo(handle: string) = TabId("repo:" + handle)

  let key(TabId key) = key

  let isHistory id = id = history

  let isRepo id = key id |> _.StartsWith("repo:")

type EditorTab = {
  Id: TabId
  Title: string
  IsPinned: bool
  Dirty: int
  RepliesCount: int
  SeenReplies: int
}

module LogLabel =

  let ofKind(log: LogKind) =
    match log with
    | Following -> "Following"
    | Latest -> "Latest"
    | Trending -> "Trending"

module EditorTab =

  let pinned(log: LogKind) = {
    Id = TabId.history
    Title = LogLabel.ofKind log
    IsPinned = true
    Dirty = 0
    RepliesCount = 0
    SeenReplies = 0
  }

  let ordinary(id: TabId, title: string) = {
    Id = id
    Title = title
    IsPinned = false
    Dirty = 0
    RepliesCount = 0
    SeenReplies = 0
  }

  let thread(id: TabId, title: string, repliesCount: int) =
    let count = max 0 repliesCount

    {
      Id = id
      Title = title
      IsPinned = false
      Dirty = 0
      RepliesCount = count
      SeenReplies = count
    }

  let repo(id: TabId, handle: string) = {
    Id = id
    Title = $"@{handle} (repo)"
    IsPinned = true
    Dirty = 0
    RepliesCount = 0
    SeenReplies = 0
  }

  let id(tab: EditorTab) = tab.Id

  let title(tab: EditorTab) = tab.Title

  let withTitle(title: string, tab: EditorTab) = { tab with Title = title }

  let isPinned(tab: EditorTab) = tab.IsPinned

  let repliesCount(tab: EditorTab) = tab.RepliesCount

  let seenReplies(tab: EditorTab) = tab.SeenReplies

  let withReplies(count: int, tab: EditorTab) =
    let count = max 0 count

    {
      tab with
          RepliesCount = count
          Dirty = max 0 (count - tab.SeenReplies)
    }

  let markSeen(tab: EditorTab) = {
    tab with
        SeenReplies = tab.RepliesCount
        Dirty = 0
  }

  let dirty(tab: EditorTab) = tab.Dirty

  let withDirty(count: int, tab: EditorTab) = { tab with Dirty = max 0 count }

[<Struct>]
type EditorPlace = { Scroll: float; Head: string option }

module EditorPlace =

  let empty = { Scroll = 0.0; Head = None }

  let withScroll(scroll: float, place: EditorPlace) = {
    place with
        Scroll = max 0.0 scroll
  }

  let withHead(head: string option, place: EditorPlace) = {
    place with
        Head = head
  }

type WidthBand =
  | Full
  | Folded
  | Trimmed

module WidthBand =

  let ofWidth(width: float) =
    if width >= 1000.0 then Full
    elif width >= 700.0 then Folded
    else Trimmed

type EditorState = {
  SelectedRef: LogKind
  Tabs: EditorTab list
  ActiveTab: TabId
}

module EditorState =

  let initial(log: LogKind) = {
    SelectedRef = log
    Tabs = [ EditorTab.pinned log ]
    ActiveTab = TabId.history
  }

  let selectedRef(state: EditorState) = state.SelectedRef

  let selectRef(log: LogKind, state: EditorState) = {
    SelectedRef = log
    Tabs =
      state.Tabs
      |> List.map(fun tab ->
        if TabId.isHistory tab.Id then
          EditorTab.withTitle(LogLabel.ofKind log, tab)
        else
          tab)
    ActiveTab = state.ActiveTab
  }

  let tabs(state: EditorState) = state.Tabs

  let activeTab(state: EditorState) = state.ActiveTab

  let selectTab(id: TabId, state: EditorState) =
    if state.Tabs |> List.exists(fun tab -> tab.Id = id) then
      let tabs =
        state.Tabs
        |> List.map(fun tab ->
          if tab.Id = id && not(TabId.isHistory id) then
            EditorTab.markSeen tab
          else
            tab)

      {
        state with
            ActiveTab = id
            Tabs = tabs
      }
    else
      state

  let openTab(tab: EditorTab, state: EditorState) =
    if state.Tabs |> List.exists(fun existing -> existing.Id = tab.Id) then
      selectTab(tab.Id, state)
    else
      {
        state with
            Tabs = state.Tabs @ [ tab ]
            ActiveTab = tab.Id
      }

  let historyTab(tabs: EditorTab list, selected: LogKind) =
    tabs
    |> List.tryFind(fun tab -> TabId.isHistory tab.Id)
    |> Option.defaultValue(EditorTab.pinned selected)

  let withTabs(tabs: EditorTab list, state: EditorState) =
    let history = historyTab(tabs, state.SelectedRef)

    let rest = tabs |> List.filter(fun tab -> not(TabId.isHistory tab.Id))

    let tabs = history :: rest

    let active =
      if tabs |> List.exists(fun tab -> tab.Id = state.ActiveTab) then
        state.ActiveTab
      else
        TabId.history

    {
      SelectedRef = state.SelectedRef
      Tabs = tabs
      ActiveTab = active
    }

  let closeTab(id: TabId, state: EditorState) =
    let target = state.Tabs |> List.tryFind(fun tab -> tab.Id = id)

    match target with
    | Some tab when tab.IsPinned -> state
    | Some _ ->
      let tabs = state.Tabs |> List.filter(fun tab -> tab.Id <> id)

      let active =
        if state.ActiveTab = id then
          TabId.history
        else
          state.ActiveTab

      {
        SelectedRef = state.SelectedRef
        Tabs = tabs
        ActiveTab = active
      }
    | None -> state

  let setDirty(id: TabId, count: int, state: EditorState) = {
    state with
        Tabs =
          state.Tabs
          |> List.map(fun tab ->
            if tab.Id = id then EditorTab.withDirty(count, tab) else tab)
  }

  let setReplies(id: TabId, count: int, state: EditorState) = {
    state with
        Tabs =
          state.Tabs
          |> List.map(fun tab ->
            if tab.Id = id then
              EditorTab.withReplies(count, tab)
            else
              tab)
  }

  let markSeen(id: TabId, state: EditorState) = {
    state with
        Tabs =
          state.Tabs
          |> List.map(fun tab ->
            if tab.Id = id then EditorTab.markSeen tab else tab)
  }
