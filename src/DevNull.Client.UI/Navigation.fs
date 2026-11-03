module DevNull.Client.Navigation

open System
open IcedTasks
open Navs

let go(nav: INavigable, url: string, onError: string -> unit) : unit =
  asyncEx {
    match! nav.Navigate url with
    | Ok _ -> ()
    | Error error -> onError $"navigation to {url} failed: {error}"
  }
  |> Async.StartImmediate

type Route =
  | Exact of string
  | Prefix of string

let routes: (Route * string) list = [
  Exact "/", "log"
  Prefix "/log", "log"
  Prefix "/commit", "log"
  Prefix "/notifications", "notifications"
  Prefix "/remotes", "remotes"
  Prefix "/profile", "profile"
  Prefix "/saved", "saved"
  Prefix "/settings", "settings"
  Prefix "/signin", "signin"
]

let routeMatches(url: string, route: Route) : bool =
  match route with
  | Exact path -> url = path
  | Prefix path -> url.StartsWith(path, StringComparison.Ordinal)

let sectionOf(url: string) : string voption =
  routes
  |> List.tryFind(fun (route, _) -> routeMatches(url, route))
  |> Option.map snd
  |> ValueOption.ofOption
