module DevNull.Client.Env

open Avalonia.Threading
open Microsoft.Extensions.Logging
open DevNull.Client.AttachmentSources
open DevNull.Client.Core.Capabilities
open DevNull.Client.Core.Http
open DevNull.Client.ImageCache
open DevNull.Client.LastVisitStore
open DevNull.Client.Profiles
open DevNull.Client.RecentSearchStore
open DevNull.Client.SettingsStore
open DevNull.Client.SshKeys
open DevNull.Client.Stores
open DevNull.Client.UiClock
open DevNull.Client.Timeline
open DevNull.Client.RouteStore
open DevNull.Client.Unread
open DevNull.Client.UserSettingsStore
open DevNull.Client.TranslationCache
open DevNull.Client.Remotes
open DevNull.Client.Natives

/// The logging capability: one factory bridged to Serilog, so every
/// service that accepts a logger lands in the same sink and level.
type IProvideLogging =
  abstract LoggerFactory: ILoggerFactory

/// The dispatcher capability: the one the head captured at startup, so
/// programs, stores, and controls post to it instead of reading the
/// process global.
type IProvideDispatcher =
  abstract Dispatcher: IDispatcher

/// The head-provided inputs of the production environment.
[<Struct>]
type AppEnvInputs = {
  BaseAddress: string
  Session: SessionStore
  Natives: NativeServices
  Logging: ILoggerFactory
  Dispatcher: IDispatcher
}

/// The production environment: system clock, crypto nonce source,
/// ssh-keygen signer, http transport, the live session store, the
/// local settings store, the app services (unread poll, clock, image
/// cache), and the native services the platform head built.
type AppEnv =
  new: inputs: AppEnvInputs -> AppEnv

  interface IProvideClock
  interface IProvideNonceSource
  interface IProvideSigner
  interface IProvidePkceSource
  interface IProvideHttp
  interface IProvideSession
  interface IProvideDispatcher
  interface IProvideNatives
  interface IProvideSettings
  interface IProvideLogging
  interface IProvideUnread
  interface IProvideUserSettings
  interface IProvideTranslations
  interface IProvideRemotes
  interface IProvideTimeline
  interface IProvideUiClock
  interface IProvideRoutes
  interface IProvideImageCache
  interface IProvideAttachmentSources
  interface IProvideLastVisit
  interface IProvideRecentSearches
  interface IProvideProfiles
  interface IProvideSshKeys
