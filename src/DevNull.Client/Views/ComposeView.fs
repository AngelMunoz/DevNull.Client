module DevNull.Client.Views.ComposeView

open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions
open IcedTasks
open MVUish.Avalonia
open Navs
open DevNull.Client
open DevNull.Client.Components
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.Navigation
open DevNull.Client.Stores
open DevNull.Client.Theme

type Props<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession> = {
  Env: 'env
  Session: SignedInSession
  Target: Composer.Target
  Nav: INavigable
}

let view(props: Props<'env>) : Control =

  let title =
    match props.Target with
    | Composer.Fresh -> "Commit"
    | Composer.ReplyTo _ -> "Reply"
    | Composer.Quoting _ -> "Quote a commit"

  let composer =
    Composer.Page {
      Env = props.Env
      Session = props.Session
      Target = props.Target
      OnCommitted =
        fun commit -> Navigation.go(props.Nav, "/commit/" + commit.Id, ignore)
    }

  ScrollViewer()
    .Padding(Theme.pagePadding)
    .Content(
      StackPanel()
        .MaxWidth(720.0)
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .Children(
          TextBlock().Classes(Theme.name Theme.Title).Text(title),
          composer
        )
    )

type Page<'env
  when 'env :> IProvideClock
  and 'env :> IProvideNonceSource
  and 'env :> IProvideSigner
  and 'env :> IProvideHttp
  and 'env :> IProvideSession>(props: Props<'env>) as this =
  inherit UserControl()

  do this.Content <- view props
