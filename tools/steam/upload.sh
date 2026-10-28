#!/bin/bash
# Upload Builds/Steam/{Windows,macOS} to Steam with steamcmd and set the build live on a
# BETA branch only (never "default"). Usage:
#   STEAM_APPID=... STEAM_DEPOT_WIN=... STEAM_DEPOT_MAC=... STEAM_USER=<build account> tools/steam/upload.sh
# Optional: STEAM_BRANCH (default "beta"; must exist in Steamworks > SteamPipe > Builds).
# steamcmd asks for the password and Steam Guard code interactively the first time.
set -euo pipefail
: "${STEAM_APPID:?set STEAM_APPID}" "${STEAM_DEPOT_WIN:?}" "${STEAM_DEPOT_MAC:?}" "${STEAM_USER:?}"
BRANCH="${STEAM_BRANCH-beta}"   # STEAM_BRANCH= (empty) uploads without setting any branch live
[ "$BRANCH" = "default" ] && { echo "Refusing to set live on default from a script."; exit 1; }
HERE="$(cd "$(dirname "$0")" && pwd)"; ROOT="$(cd "$HERE/../.." && pwd)"
OUT="$HERE/output"; mkdir -p "$OUT"
VER=$(grep -m1 "public const string Version" "$ROOT/First Principles/Assets/Editor/BuildAndroid.cs" | sed -E 's/.*"(.*)".*/\1/')
sed -e "s#__APPID__#$STEAM_APPID#" -e "s#__DEPOT_WIN__#$STEAM_DEPOT_WIN#" -e "s#__DEPOT_MAC__#$STEAM_DEPOT_MAC#" \
    -e "s#__BRANCH__#$BRANCH#" -e "s#__VERSION__#$VER#" -e "s#__OUTPUT__#$OUT#" -e "s#__CONTENT__#$ROOT/Builds/Steam#" \
    "$HERE/app_build.vdf.template" > "$OUT/app_build_$STEAM_APPID.vdf"
SC="$HERE/steamcmd/steamcmd.sh"
if [ ! -x "$SC" ]; then
  mkdir -p "$HERE/steamcmd"
  curl -sSL https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz | tar -xz -C "$HERE/steamcmd"
fi
"$SC" +login "$STEAM_USER" +run_app_build "$OUT/app_build_$STEAM_APPID.vdf" +quit
if [ -n "$BRANCH" ]; then echo "Uploaded. Live on branch '$BRANCH' only; promote to default in Steamworks after testing."; else echo "Uploaded, not live yet. Steamworks > SteamPipe > Builds: pick the new build, set it live on default, Preview Change, then confirm."; fi
