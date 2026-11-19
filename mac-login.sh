#!/usr/bin/env bash
# Signs in to dev-null.sh from macOS with a self-contained build.
#
# macOS only lets an .app bundle claim the devnull:// scheme (through
# Info.plist's CFBundleURLTypes), and it delivers the callback to the
# running app as an Apple Event, not an argument. This script publishes
# a self-contained build, hands it to the formal macOS packaging
# (.github/mac/bundle.sh, which owns the Info.plist and the blessed
# icon), registers the bundle with LaunchServices, and launches it. The
# app's own registrar re-claims the scheme on start.
#
# The session, the device key, and the settings are shared with the Debug
# build through ~/Library/Application Support/DevNull (the standard
# LocalApplicationData folder): after one login here,
# `dotnet run --project src/DevNull.Client.Mac` starts signed in.
#
# The build needs the .NET 11 SDK. If `dotnet` on PATH is older, point
# DOTNET at a newer one, e.g.
#   DOTNET="$HOME/Library/Application Support/dotnetup/dotnet/dotnet" ./mac-login.sh

set -euo pipefail

cd "$(dirname "$0")"

DOTNET="${DOTNET:-dotnet}"

# The app is single-instance: a second launch exits at once.
if pgrep -x DevNull.Client >/dev/null; then
  echo "An instance is already running. Close it, then run this script again." >&2
  exit 1
fi

case "$(uname -m)" in
  arm64) rid=osx-arm64 ;;
  *) rid=osx-x64 ;;
esac

"$DOTNET" publish src/DevNull.Client.Mac -c Release -r "$rid" --self-contained

publish="src/DevNull.Client.Mac/bin/Release/net11.0/$rid/publish"

# CHANGELOG.md is the version source of truth, as for the assemblies.
version="$(sed -nE 's/^## \[([0-9]+\.[0-9]+\.[0-9]+)\].*/\1/p' CHANGELOG.md | head -1)"
version="${version:-0.0.0}"

app="$(.github/mac/bundle.sh "$publish" "$version" dist)"

lsregister="/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"
"$lsregister" -f "$app"

open "$app"

echo "Launched $app. Sign in from the window; the browser callback returns to it."
echo "Session lives in ~/Library/Application Support/DevNull and is shared with dotnet run."
