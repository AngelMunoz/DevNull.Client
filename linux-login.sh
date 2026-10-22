#!/usr/bin/env bash
# Signs in to dev-null.sh from Linux with a self-contained build.
#
# The desktop session does not export DOTNET_ROOT, and that session
# launches the devnull:// callback handler. A framework-dependent
# Debug build therefore cannot start at the login callback. A
# self-contained build carries its own runtime, so the callback works.
#
# The session, the device key, and the settings are shared with the
# Debug build through ~/.local/share/DevNull: after one login here,
# `dotnet run --project src/DevNull.Client` starts signed in.

set -euo pipefail

cd "$(dirname "$0")"

# The app is single-instance: a second launch exits at once.
if pgrep -f 'DevNull\.Client/bin/(Debug|Release)' >/dev/null; then
  echo "An instance is already running. Close it, then run this script again." >&2
  exit 1
fi

case "$(uname -m)" in
  aarch64) rid=linux-arm64 ;;
  *) rid=linux-x64 ;;
esac

dotnet publish src/DevNull.Client -c Release -r "$rid" --self-contained

exec "src/DevNull.Client/bin/Release/net10.0/$rid/publish/DevNull.Client"
