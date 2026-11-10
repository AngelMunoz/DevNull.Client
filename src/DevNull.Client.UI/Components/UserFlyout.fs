module DevNull.Client.Components.UserFlyout

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open NXUI.Extensions
open IcedTasks
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Core.ApiTypes
open DevNull.Client.ImageCache

[<Struct>]
type Source = {
  Fetch: string -> Async<ProfileDetails option>
  OnOpen: string -> unit
  OnOpenOrg: string -> unit
  Cache: ImageCacheService
}

module Parts =

  let openDelay = TimeSpan.FromMilliseconds 450.0

  let closeDelay = TimeSpan.FromMilliseconds 200.0

  // One relation badge, when the relation says so.
  let mark(text: string, shown: bool) : Control =
    TextBlock().Classes(Theme.name Theme.Chip).Text(text).IsVisible(shown)

  // The identity card: the in-page user renders at once, and the
  // fetched details fill the badges, the bio, and the counts in. The
  // card is a button: its host arms the click. The opaque surface is
  // the child border: the theme's hover wash repaints a button's own
  // presenter translucent, and the border paints over the wash.
  let card
    (source: Source, user: User, identity: ProfileDetails option)
    : Button =
    let profile = identity |> Option.map _.Profile

    let avatarOf = {
      user with
          AvatarUrl =
            profile
            |> Option.bind(fun loaded -> loaded.User.AvatarUrl)
            |> Option.orElse user.AvatarUrl
    }

    let lane = profile |> Option.bind _.Lane

    let children: Control array = [|
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(10.0)
        .Children(
          Avatar.view {
            Name = avatarOf.Name
            Url = avatarOf.AvatarUrl
            Lane = lane
            Size = 48.0
            Cache = source.Cache
            Square = false
          },
          StackPanel()
            .VerticalAlignment(VerticalAlignment.Center)
            .Spacing(2.0)
            .Children(
              TextBlock().Classes(Theme.name Theme.Subtitle).Text(user.Name),
              ProfileCard.laneHandle(lane, user.Handle)
            )
        )

      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6.0)
        .Children(
          mark(
            "Follows you",
            identity |> Option.map _.FollowsYou |> Option.defaultValue false
          ),
          mark(
            "Muted",
            identity |> Option.map _.IsMuting |> Option.defaultValue false
          )
        )

      match profile |> Option.bind _.Bio with
      | Some bio ->
        TextBlock().Text(bio).TextWrapping(TextWrapping.Wrap).MaxLines(3)
      | None -> ()

      match profile with
      | Some loaded -> ProfileCard.stats loaded
      | None -> Status.busy("Reading the profile…")
    |]

    Button()
      .Classes(Theme.name Theme.GhostButton)
      .Padding(0.0)
      .Content(
        Border()
          .Classes(Theme.name Theme.CardBack, Theme.name Theme.Hairline)
          .BorderThickness(Thickness(1.0))
          .CornerRadius(8.0)
          .Padding(12.0)
          .Width(304.0)
          .Child(StackPanel().Spacing(8.0).Children(children))
      )

let attach(source: Source, user: User, child: Control) : Control =
  let card = ContentControl().Name("UserFlyout")

  let popup =
    Popup().Placement(PlacementMode.Right).PlacementTarget(child).Child(card)

  let openTimer = DispatcherTimer(Interval = Parts.openDelay)
  let closeTimer = DispatcherTimer(Interval = Parts.closeDelay)

  // Both closes stop both timers: the close timer is one-shot, and an
  // enabled DispatcherTimer is rooted by the dispatcher — one left
  // running would tick forever and hold this whole closure alive.
  let closeNow() =
    openTimer.Stop()
    closeTimer.Stop()
    popup.IsOpen <- false

  // One fetch per open: the card rebuilds when the details land.
  let show(identity: ProfileDetails option) =
    card.Content <-
      Parts
        .card(source, user, identity)
        .OnClickHandler(fun _ args ->
          // A handled click never falls through to the commit behind.
          args.Handled <- true
          closeNow()
          source.OnOpen user.Handle)

  let fill() = asyncEx {
    let! details = source.Fetch user.Handle

    show(details)
  }

  let openNow() =
    closeTimer.Stop()
    show(None)
    popup.IsOpen <- true
    Async.StartImmediate(fill())

  openTimer.Tick.Add(fun _ ->
    openTimer.Stop()
    openNow())

  closeTimer.Tick.Add(fun _ -> closeNow())

  let trigger =
    Button()
      .Name("AuthorAvatar")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(0.0)
      .Content(child)
      .OnClickHandler(fun _ args ->
        // A handled click never opens the thread behind the avatar.
        args.Handled <- true
        closeNow()
        source.OnOpen user.Handle)

  trigger.PointerEntered.Add(fun _ ->
    closeTimer.Stop()
    openTimer.Start())

  trigger.PointerExited.Add(fun _ -> closeTimer.Start())

  card.PointerEntered.Add(fun _ -> closeTimer.Stop())

  card.PointerExited.Add(fun _ -> closeTimer.Start())

  Grid().Children(trigger, popup)

/// The author-shaped attach: a person rides the hover card, an
/// organization opens its page on click.
let attachAuthor(source: Source, author: Author, child: Control) : Control =
  match author with
  | Person user -> attach(source, user, child)
  | Organization org ->
    (Button()
      .Name("AuthorAvatar")
      .Classes(Theme.name Theme.GhostButton)
      .Padding(0.0)
      .Content(child)
      .OnClickHandler(fun _ args ->
        args.Handled <- true
        source.OnOpenOrg org.Handle))
    :> Control
