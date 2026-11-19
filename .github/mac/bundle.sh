#!/usr/bin/env bash
# Assembles the macOS .app bundle from a publish output.
#
# This is the formal macOS packaging: the Info.plist template lives here
# and the icon is rendered from the blessed source the Flatpak also uses
# (.github/flatpak/icon-source.png). mac-login.sh (dev sign-in) and a
# release workflow both call this, so the bundle never depends on the
# login script.
#
# usage: bundle.sh <publish-dir> <version> [output-dir]
#   The .app lands at <output-dir>/DevNull.Client.app (default: dist/).
#
# Signing: ad-hoc by default (local dev; Apple Silicon requires a
# signature, Intel runs unsigned too). A release sets CODESIGN_IDENTITY
# to a real certificate; notarization stays a separate release step.

set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"

if [ $# -lt 2 ]; then
  echo "usage: bundle.sh <publish-dir> <version> [output-dir]" >&2
  exit 1
fi

publish="$1"
version="$2"
out="${3:-$root/dist}"
app="$out/DevNull.Client.app"

if [ ! -d "$publish" ]; then
  echo "No publish output at $publish" >&2
  exit 1
fi

rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

# The blessed icon, rendered into the sizes macOS expects.
iconset="$(mktemp -d)/AppIcon.iconset"
mkdir -p "$iconset"
source_png="$root/.github/flatpak/icon-source.png"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$source_png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  retina=$((size * 2))
  sips -z "$retina" "$retina" "$source_png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"
rm -rf "$(dirname "$iconset")"

# The payload: the publish output is the executable's home.
cp -R "$publish/." "$app/Contents/MacOS/"

# The bundle metadata, version stamped.
sed "s/@VERSION@/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"

codesign --force --deep --sign "${CODESIGN_IDENTITY:--}" "$app" >&2

echo "$app"
