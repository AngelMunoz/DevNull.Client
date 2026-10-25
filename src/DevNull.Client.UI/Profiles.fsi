module DevNull.Client.Profiles

open DevNull.Client.Core.ApiTypes

/// Worker: one profile by username, for the hover cards. `None`
/// covers both the absent profile and a failed fetch: the card keeps
/// the in-page identity either way.
type IProfiles =
  abstract Fetch: username: string -> Async<ProfileDetails option>

/// Accessor for the profiles capability.
type IProvideProfiles =
  abstract Profiles: IProfiles
