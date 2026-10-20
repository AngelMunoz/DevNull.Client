module DevNull.Client.Navigation

open System
open System.Threading
open IcedTasks
open Navs

let inline go(nav: INavigable, url: string, onError: string -> unit) : unit =
  asyncEx {
    match! nav.Navigate url with
    | Ok _ -> ()
    | Error error -> onError $"navigation to {url} failed: {error}"
  }
  |> Async.StartImmediate

let sectionOf(url: string) : string voption =
  let prefix prefix' =
    url.StartsWith(prefix', StringComparison.Ordinal)

  if url = "/" || prefix "/log" || prefix "/commit" then
    ValueSome "log"
  elif prefix "/notifications" then
    ValueSome "notifications"
  elif prefix "/profile" then
    ValueSome "profile"
  elif prefix "/saved" then
    ValueSome "saved"
  elif prefix "/settings" then
    ValueSome "settings"
  elif prefix "/signin" then
    ValueSome "signin"
  else
    ValueNone
