# Steam release checklist: First Principles 1.3

The Steam build is **premium**: one purchase on Steam, every level unlocked, no in-game store.
Standalone builds run in `EntitlementPlatformMode.AlwaysUnlocked`, which also turns on the Plus
features (unlimited hints, daily-streak shield). Steam has no store subscriptions, so the mobile
"First Principles Plus" subscription does not exist on Steam.

Future chapters can ship either as free updates (default; the mobile Full Game also promises
"every level we add later") or as paid DLC. If you choose DLC, gate the new chapter's level range
with `SteamApps.BIsDlcInstalled(new AppId_t(<dlc app id>))` in `SteamBridge` and treat it like a
locked category in `LevelAccessPolicy`.

## 1. One-time Steamworks setup (partner.steamgames.com)

- [ ] Create the app and note the **App ID**. Put it in `Assets/Scripts/Platform/SteamBridge.cs`
      (`SteamAppId`, currently `0` = development, skips `RestartAppIfNecessary`), rebuild.
- [ ] **Depots**: one per OS.
      - Windows depot (e.g. `<appid>+1`): contents of `Builds/Steam/Windows/`.
      - macOS depot (e.g. `<appid>+2`): `Builds/Steam/macOS/First Principles.app`.
- [ ] **Launch options** (Installation > General):
      - Windows: executable `First Principles.exe`, OS Windows, 64-bit only.
      - macOS: executable `First Principles.app`, OS macOS.
- [ ] **Steam Cloud** (Application > Steam Cloud): enable, byte quota 1 MB, file count 4.
      The game writes `progress.json` through `ISteamRemoteStorage` itself (no Auto-Cloud rules
      needed) and merges the cloud copy at launch, keeping the best stars and stats.
- [ ] **Stats** (Application > Stats & Achievements), type INT, "Set by" client:
      `stars`, `levels_cleared`, `daily_best_streak`.
- [ ] **Achievements** (API names must match exactly; icons 256x256 JPG, achieved + unachieved):

| API name | Display name | Description |
|---|---|---|
| FP_FIRST_CLEAR | First principles | Clear your first level. |
| FP_THREE_STARS | Flawless derivative | Earn three stars on any level. |
| FP_CORE_DONE | Series complete | Clear every Core & series level. |
| FP_RIEMANN | Riemann rider | Clear all four area and Riemann-sum levels. |
| FP_STARS_25 | Constellation | Collect 25 stars. |
| FP_STARS_75 | Star chart | Collect 75 stars. |
| FP_PERFECT_10 | Perfectionist | Three-star 10 different levels. |
| FP_AIR_50 | Ride the slope | Double-jump off the f′ line 50 times. |
| FP_FALLS_100 | Limits exist | Fall 100 times and keep going. |
| FP_DAILY_1 | Daily practice | Complete a daily challenge. |
| FP_DAILY_7 | Seven-day streak | Complete daily challenges 7 days in a row. |
| FP_HINT_1 | Ask the tutor | Use your first hint. |
| FP_AERO | Cleared for takeoff | Clear every Aerospace level. |
| FP_BIG_O | Asymptotically optimal | Clear every Big O level. |
| FP_BOSSES | Final boss | Clear all four final boss levels. |
| FP_ALL | Q.E.D. | Clear every level in the game. |

  The list lives in code at `AchievementCatalog.All` (`Assets/Scripts/Progress/ProgressRules.cs`).
  Publish the stats/achievements schema after adding them.
- [ ] **Steam Input**: the game reads gamepads through Unity's Input System. Under Steam Input,
      opt in to "Gamepad" default configs so Xbox / PlayStation / Switch Pro / Steam Deck all map.
- [ ] **Steam Deck**: 1280x800 works (the UI scales); the default window is resizable. Request
      Deck verification after the first public build.

## 2. Build (on the Mac)

```bash
U=/Applications/Unity/Hub/Editor/6000.6.5f1/Unity.app/Contents/MacOS/Unity
P="$HOME/Documents/GitHub2/First-Principles/First Principles"
"$U" -batchmode -nographics -quit -projectPath "$P" -executeMethod BuildHelper.BuildWindows  -logFile -
"$U" -batchmode -nographics -quit -projectPath "$P" -executeMethod BuildHelper.BuildMacSteam -logFile -
```

Outputs: `Builds/Steam/Windows/` and `Builds/Steam/macOS/First Principles.app` (universal
x86_64 + arm64). Both define `FP_STEAM`, which compiles `SteamBridge`; Steamworks.NET ships
the native Steam API libraries for Windows and macOS (package `com.rlabrecque.steamworks.net` 2025.164.1).

Delete before uploading: `*_BurstDebugInformation_DoNotShip` folders (Windows) if present.

**macOS signing**: Steam does not require notarization, but unsigned apps trigger Gatekeeper
warnings. Recommended: sign with a *Developer ID Application* certificate (not in this Mac's
keychain today) and notarize:

```bash
codesign --deep --force --options runtime --timestamp -s "Developer ID Application: Wonmo Seong (Z64KRUX3W3)" "Builds/Steam/macOS/First Principles.app"
ditto -c -k --keepParent "Builds/Steam/macOS/First Principles.app" fp-mac.zip
xcrun notarytool submit fp-mac.zip --key ~/.appstoreconnect/private_keys/AuthKey_<ID>.p8 --key-id <ID> --issuer <issuer> --wait
xcrun stapler staple "Builds/Steam/macOS/First Principles.app"
```

## 3. Upload with steamcmd (ContentBuilder)

Create `steam/app_build_<appid>.vdf`:

```
"AppBuild"
{
  "AppID" "<appid>"
  "Desc" "First Principles 1.3"
  "BuildOutput" "../output/"
  "ContentRoot" "../../Builds/Steam/"
  "SetLive" ""
  "Depots"
  {
    "<appid+1>" { "FileMapping" { "LocalPath" "Windows/*" "DepotPath" "." "recursive" "1" } "FileExclusion" "*_DoNotShip*" }
    "<appid+2>" { "FileMapping" { "LocalPath" "macOS/*"   "DepotPath" "." "recursive" "1" } }
  }
}
```

```bash
steamcmd +login <steam_build_account> +run_app_build "$PWD/steam/app_build_<appid>.vdf" +quit
```

Then in Steamworks > SteamPipe > Builds, set the build live on the `default` branch (or a
`beta` branch first).

## 4. Store page items

- [ ] Capsules: header 920x430, small 462x174, main 1232x706, vertical 748x896, library hero
      3840x1240, library logo 1280x720 (transparent), library capsule 600x900.
- [ ] Screenshots (at least 5, 1920x1080): gameplay, level-complete stars, daily challenge,
      level select chapters, hint markers. QA shots are in `~/Documents/jeb-build/shots/first-principles/`.
- [ ] Trailer (optional): existing promo https://www.youtube.com/watch?v=k0soEFAK-CQ.
- [ ] Short description (300 chars max), for example:
      "A platformer where the level IS the graph. Walk on f(x), leap the pits where the slope
      f′(x) drops, double-jump off the derivative curve. 59 levels from Maclaurin series to
      Riemann sums, AP Calculus BC, aerospace and Big O, with stars, daily challenges and hints."
- [ ] Tags: Education, Puzzle Platformer, Math, Casual, 2D, Minimalist, Singleplayer.
- [ ] Supported languages: English, French, German, Spanish, Italian, Czech, Polish, Russian,
      Arabic, Hindi, Urdu, Chinese (Simplified), Korean, Japanese (interface + subtitles; no audio).
- [ ] Controller support: Full controller support.
- [ ] Achievements: 16. Steam Cloud: yes.
- [ ] Content survey (mature content: none), pricing (suggest the Full Game tier, e.g. USD 4.99
      to 9.99), release date, legal (Orch Aerospace & Game Genesis, see `CREDITS.md`).
- [ ] Credits / third-party notices from `CREDITS.md` (Unity, TextMesh Pro, OFL fonts,
      Steamworks.NET MIT).

## 5. Smoke test before setting live

- [ ] Install through Steam on Windows and macOS: the game launches, the Steam overlay works
      (Shift+Tab), achievements pop (clear level 1 -> "First principles").
- [ ] Clear a level on one machine, launch on the other: stars carry over (Steam Cloud).
- [ ] Gamepad: menus, level select, gameplay, pause, level-complete card.
- [ ] No store / paywall UI appears anywhere (premium build).
