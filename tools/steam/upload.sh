#!/bin/bash
# Upload Builds/Steam/{Windows,macOS} to Steam with steamcmd and set the build live on a
# BETA branch only (never "default"). Usage:
#   STEAM_APPID=... STEAM_DEPOT_WIN=... STEAM_DEPOT_MAC=... STEAM_USER=<build account> tools/steam/upload.sh
# Optional: STEAM_BRANCH (default "beta"; must exist in Steamworks > SteamPipe > Builds).
# steamcmd asks for the password and Steam Guard code interactively the first time.
set -euo pipefail
: "${STEAM_APPID:?set STEAM_APPID}" "${STEAM_DEPOT_WIN:?}" "${STEAM_DEPOT_MAC:?}" "${STEAM_USER:?}"
BRANCH="${STEAM_BRANCH:-beta}"
[ "$BRANCH" = "default" ] && { echo "Refusing to set live on default from a script."; exit 1; }
HERE="$(cd "$(dirname "$0")" && pwd)"; ROOT="$(cd "$HERE/../.." && pwd)"
OUT="$HERE/output"; mkdir -p "$OUT"
VER=$(grep -m1 "bundleVersion:" "$ROOT/First Principles/ProjectSettings/ProjectSettings.asset" | awk '{print $2}')
sed -e "s#__APPID__#$STEAM_APPID#" -e "s#__DEPOT_WIN__#$STEAM_DEPOT_WIN#" -e "s#__DEPOT_MAC__#$STEAM_DEPOT_MAC#" \
    -e "s#__BRANCH__#$BRANCH#" -e "s#__VERSION__#$VER#" -e "s#__OUTPUT__#$OUT#" -e "s#__CONTENT__#$ROOT/Builds/Steam#" \
    "$HERE/app_build.vdf.template" > "$OUT/app_build_$STEAM_APPID.vdf"
SC="$HERE/steamcmd/steamcmd.sh"
if [ ! -x "$SC" ]; then
  mkdir -p "$HERE/steamcmd"
  curl -sSL https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz | tar -xz -C "$HERE/steamcmd"
fi
"$SC" +login "$STEAM_USER" +run_app_build "$OUT/app_build_$STEAM_APPID.vdf" +quit
echo "Uploaded. Live on branch '$BRANCH' only; promote to default in Steamworks after testing."
