module DevNull.Client.Profiles

open DevNull.Client.Core.ApiTypes

/// Worker: one profile by username, for the hover cards.
type IProfiles =
  abstract Fetch: username: string -> Async<ProfileDetails option>

/// Accessor for the profiles capability.
type IProvideProfiles =
  abstract Profiles: IProfiles
