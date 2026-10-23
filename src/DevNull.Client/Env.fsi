module DevNull.Client.Env

open Microsoft.Extensions.Logging
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.Profiles
open DevNull.Client.SettingsStore
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.UiClock
open DevNull.Client.Timeline
open DevNull.Client.RouteStore
open DevNull.Client.Unread

/// The logging capability: one factory bridged to Serilog, so every
/// service that accepts a logger lands in the same sink and level.
type IProvideLogging =
  abstract LoggerFactory: ILoggerFactory

/// The production environment: system clock, crypto nonce source,
/// ssh-keygen signer, http transport, the live session store, the
/// local settings store, and the app services (unread poll, clock,
/// image cache).
type AppEnv =
  new:
    baseAddress: string * session: SessionStore * logging: ILoggerFactory ->
      AppEnv

  interface IProvideClock
  interface IProvideNonceSource
  interface IProvideSigner
  interface IProvidePkceSource
  interface IProvideHttp
  interface IProvideSession
  interface IProvideSettings
  interface IProvideLogging
  interface IProvideUnread
  interface IProvideTimeline
  interface IProvideUiClock
  interface IProvideRoutes
  interface IProvideImageCache
  interface IProvideAttachmentSources
  interface IProvideLastVisit
  interface IProvideProfiles
  interface IProvideSshKeys
