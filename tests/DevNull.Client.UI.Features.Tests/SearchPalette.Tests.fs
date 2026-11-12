module DevNull.Client.UI.Tests.SearchPaletteTests

open System
open Expecto
open MVUish.Avalonia
open DevNull.Client.Core.ApiTypes
open DevNull.Client.Core.Http
open DevNull.Client.ImageCache
open DevNull.Client.Workbench
open DevNull.Client.UI.Tests.Fixtures
open DevNull.Client.UI.Tests.Harness

let images = {
  new ImageCacheService with
    member _.Get(_url: string) = async { return None }
}

let matches =
  match SearchProfiles.decode searchProfilesJson with
  | Ok matches -> matches
  | Error failure ->
    failwith $"the canned people search never decoded: {failure.Message}"

type Sinks = {
  Repository: string -> unit
  Commits: string -> unit
  Close: unit -> unit
}

let noSinks: Sinks = {
  Repository = ignore
  Commits = ignore
  Close = ignore
}

let props(env: TestEnv, sinks: Sinks) : SearchPalette.Props<TestEnv> = {
  Env = env
  KeyFile = "/tmp/devnull-test-id_ed25519"
  Images = images
  OnOpenRepository = sinks.Repository
  OnSearchCommits = sinks.Commits
  OnClose = sinks.Close
}

let initialModel(sinks: Sinks) =
  let env = TestEnv(signedInStore(), FakeApi("{}") :> IHttp)
  let p = props(env, sinks)
  let model, _ = SearchPalette.init p
  p, model

let run(cmd: Cmd<SearchPalette.Msg>) =
  cmd (fun _ -> ()) (fun (_, ex) -> failwith ex.Message)

let tests =
  testList "workbench search palette" [
    testCase "the terms are shaped at the boundary"
    <| fun () ->
      Expect.equal
        (SearchPalette.peopleTermOf "  @Ana  ")
        (ValueSome "Ana")
        "the people term"

      Expect.equal
        (SearchPalette.peopleTermOf "@")
        ValueNone
        "an empty people term"

      Expect.equal
        (SearchPalette.commitTermOf "  dns  ")
        (ValueSome "dns")
        "the commit term"

      Expect.equal
        (SearchPalette.commitTermOf "ab")
        ValueNone
        "a short commit term"

      Expect.equal
        (SearchPalette.commitTermOf(String('a', 101)))
        ValueNone
        "a long commit term"

    testCase "the palette reads people only when asked"
    <| fun () ->
      let p, model = initialModel noSinks
      Expect.isFalse model.Loading "the palette read on open"
      Expect.isTrue model.People.IsEmpty "the palette had people"

      let model, _ =
        SearchPalette.update(p, SearchPalette.TermChanged "@kai", model)

      let model, _ =
        SearchPalette.update(p, SearchPalette.PeopleRequested, model)

      Expect.isTrue model.Loading "the people read did not start"
      Expect.isNone model.Notice "the valid term was refused"

      let model, _ =
        SearchPalette.update(p, SearchPalette.PeopleFetched(Ok matches), model)

      Expect.equal model.People matches.Users "the people rows"
      Expect.isTrue model.Searched "the search did not settle"
      Expect.isFalse model.Loading "the read stayed busy"

    testCase "an unshaped term is refused in words"
    <| fun () ->
      let p, model = initialModel noSinks

      let model, _ =
        SearchPalette.update(p, SearchPalette.TermChanged "@", model)

      let model, _ =
        SearchPalette.update(p, SearchPalette.PeopleRequested, model)

      Expect.isSome model.Notice "the empty term was accepted"
      Expect.isFalse model.Loading "the empty term started a read"

    testCase
      "a picked person, a commit search, and a close leave through the sinks"
    <| fun () ->
      let repositories = ResizeArray<string>()
      let commits = ResizeArray<string>()
      let closes = ResizeArray<unit>()

      let p, model =
        initialModel {
          Repository = repositories.Add
          Commits = commits.Add
          Close = fun () -> closes.Add()
        }

      let person = matches.Users.Head

      let model, personCmd =
        SearchPalette.update(p, SearchPalette.PersonPicked person, model)

      run personCmd

      Expect.equal
        (repositories |> Seq.toList)
        [ person.Handle ]
        "the person sink"

      let model, _ =
        SearchPalette.update(p, SearchPalette.TermChanged "  dns  ", model)

      let model, commitsCmd =
        SearchPalette.update(p, SearchPalette.CommitsRequested, model)

      run commitsCmd
      Expect.equal (commits |> Seq.toList) [ "dns" ] "the commit term sink"

      let model, closeCmd =
        SearchPalette.update(p, SearchPalette.CloseRequested, model)

      run closeCmd
      Expect.equal (closes |> Seq.toList) [ () ] "the close sink"
  ]
