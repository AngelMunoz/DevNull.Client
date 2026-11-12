module DevNull.Client.UI.Tests.EditorTypes

open Expecto
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Layouts.Editor

let private state =
  let tabs = [
    EditorTab.pinned Following
    EditorTab.ordinary(TabId.ordinary "a1b2", "a1b2 @ana")
    EditorTab.ordinary(TabId.ordinary "55aa", "55aa @cy")
  ]

  EditorState.withTabs(tabs, EditorState.initial Following)

let tests =
  testList "editor types" [
    testCase "the pinned tab cannot close" (fun () ->
      let closed = EditorState.closeTab(TabId.history, state)

      Expect.equal
        (EditorState.tabs closed |> List.map EditorTab.id)
        (EditorState.tabs state |> List.map EditorTab.id)
        "the pinned tab left")

    testCase
      "an ordinary tab closes and the history tab takes the focus"
      (fun () ->
        let active = EditorState.selectTab(TabId.ordinary "55aa", state)

        let closed = EditorState.closeTab(TabId.ordinary "55aa", active)

        Expect.equal
          (EditorState.tabs closed |> List.map EditorTab.id)
          [ TabId.history; TabId.ordinary "a1b2" ]
          "the ordinary tab stayed"

        Expect.equal
          (EditorState.activeTab closed)
          TabId.history
          "the focus stayed on the closed tab")

    testCase "the width ladder splits at 1000 and 700" (fun () ->
      Expect.equal (WidthBand.ofWidth 1200.0) Full "1200"
      Expect.equal (WidthBand.ofWidth 1000.0) Full "1000"
      Expect.equal (WidthBand.ofWidth 999.0) Folded "999"
      Expect.equal (WidthBand.ofWidth 700.0) Folded "700"
      Expect.equal (WidthBand.ofWidth 699.0) Trimmed "699"
      Expect.equal (WidthBand.ofWidth 380.0) Trimmed "380")

    testCase "opening the same thread tab twice keeps one" (fun () ->
      let id = TabId.thread "commit"

      let opened =
        EditorState.openTab(
          EditorTab.thread(id, "a1b2 @ana", 3),
          EditorState.initial Following
        )

      let again =
        EditorState.openTab(EditorTab.thread(id, "a1b2 @ana", 9), opened)

      Expect.equal (EditorState.tabs again |> List.length) 2 "the tab doubled"
      Expect.equal (EditorState.activeTab again) id "the thread took the focus")

    testCase "a ref switch retitles the pinned tab" (fun () ->
      let selected = EditorState.selectRef(Trending, state)

      Expect.equal (EditorState.selectedRef selected) Trending "the ref stayed"

      Expect.equal
        (EditorState.tabs selected
         |> List.find(fun tab -> EditorTab.id tab = TabId.history)
         |> EditorTab.title)
        "Trending"
        "the pinned title stayed")
  ]
