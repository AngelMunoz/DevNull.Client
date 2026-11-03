module DevNull.Client.RecentSearchStore

/// The per-user list of the searches the person settled on, the
/// site's three at most, newest first.
type RecentSearchStore =
  /// This user's recent searches, newest first; an unknown user has
  /// none.
  abstract Recent: user: string -> string list

  /// Records a settled search as this user's newest, dropping the same
  /// term in any other case and keeping the site's three.
  abstract Remember: user: string * term: string -> unit

  /// Clears this user's searches.
  abstract Clear: user: string -> unit

/// Accessor for the recent-search capability.
type IProvideRecentSearches =
  abstract RecentSearches: RecentSearchStore

/// The search list over the entries it holds and the hook it persists
/// through.
module RecentSearchStore =

  /// The file-backed store over the app's data folder.
  val create: unit -> RecentSearchStore

  /// The in-memory store over the given map and persist hook.
  val createWith:
    stored: Map<string, string list> *
    persist: (Map<string, string list> -> unit) ->
      RecentSearchStore
