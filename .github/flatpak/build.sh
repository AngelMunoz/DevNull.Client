#!/usr/bin/env bash
# Renders the flatpak manifest and builds the flatpak.
#
# Local:  build.sh <tarball>
#   The tarball's name carries the version
#   (DevNull.Client-<version>-linux-x64.tar.gz); its hash and file://
#   address fill the manifest.
# CI:     build.sh <version> <url> <sha256>
#   The manifest embeds the release URL; flatpak-builder downloads it.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo="${FLATPAK_REPO:-/tmp/devnull-flatpak-repo}"

case $# in
  1)
    tarball="$(readlink -f "$1")"
    name="$(basename "$tarball")"
    version="$(echo "$name" | sed -E 's/^DevNull\.Client-(.+)-linux-x64\.tar\.gz$/\1/')"

    if [ "$version" = "$name" ]; then
      echo "The tarball name carries no version: $name" >&2
      exit 1
    fi

    url="file://${tarball}"
    hash="$(sha256sum "$tarball" | cut -d' ' -f1)"
    ;;
  3)
    version="$1"
    url="$2"
    hash="$3"
    ;;
  *)
    echo "usage: build.sh <tarball> | build.sh <version> <url> <sha256>" >&2
    exit 1
    ;;
esac

stage="$(mktemp -d /tmp/devnull-flatpak.XXXXXX)"

mkdir -p "$stage/files"
cp -r "$here/files/." "$stage/files/"

sed -i \
  -e "s/@VERSION@/${version}/g" \
  -e "s/@DATE@/$(date +%F)/g" \
  "$stage/files/share/metainfo/me.tunaxor.devnull.unoffclient.metainfo.xml"

sed \
  -e "s|@URL@|${url}|g" \
  -e "s/@HASH@/${hash}/g" \
  "$here/me.tunaxor.devnull.unoffclient.json.in" > "$stage/manifest.json"

# The system binary in CI; the flatpak'd one on a host without it. The
# sandboxed builder sees the stage only with the filesystem grants.
if command -v flatpak-builder >/dev/null 2>&1; then
  builder=(flatpak-builder)
else
  builder=(flatpak run --filesystem=host --filesystem=/tmp org.flatpak.Builder)
fi

"${builder[@]}" \
  --force-clean \
  --state-dir="$stage/state" \
  --repo="$repo" \
  --user \
  --install \
  "$stage/build" "$stage/manifest.json"

echo "Installed me.tunaxor.devnull.unoffclient ${version} (repo: ${repo})."
