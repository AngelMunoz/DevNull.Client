module DevNull.Client.RecentSearchStore

/// The per-user list of the searches the person settled on, the
/// site's three at most, newest first.
type RecentSearchStore =
  /// The file-backed store over the app's data folder.
  new: unit -> RecentSearchStore

  /// The in-memory store over the given map and persist hook.
  new:
    stored: Map<string, string list> *
    persist: (Map<string, string list> -> unit) ->
      RecentSearchStore

  /// This user's recent searches, newest first; an unknown user has
  /// none.
  member Recent: user: string -> string list

  /// Records a settled search as this user's newest, dropping the same
  /// term in any other case and keeping the site's three.
  member Remember: user: string * term: string -> unit

  /// Clears this user's searches.
  member Clear: user: string -> unit

/// Accessor for the recent-search capability.
type IProvideRecentSearches =
  abstract RecentSearches: RecentSearchStore
