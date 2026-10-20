---
layout: page
title: Architecture
permalink: /architecture/
---

# Architecture

## Scenes (build order)

Configured in `First Principles/ProjectSettings/EditorBuildSettings.asset`:

- **Menu** — `SceneFader`, entry to level select or other scenes.
- **LevelSelect** — Runtime UI + `LevelSelectController`.
- **Game** — Graph UI, `GameManager` (function plotter + faders + `LevelManager`).

## High-level diagram

```
Menu                    LevelSelect              Game
────                    ───────────              ────
SceneFader              LevelSelectController    Canvas + graph renderers
        LoadLevelSelect   │  (scroll list +       FunctionPlotter
                          │   MathArticlesOverlay / LearningArticleLibrary)
                                 └──► LevelSelection (static)
                                            │
                                            ▼
                                      LevelManager
                                            ├── Applies LevelDefinition → FunctionPlotter / colors
                                            ├── GraphObstacleGenerator → platforms / hazards
                                            ├── PlayerControllerUI2D
                                            └── DerivativePopAnimator (deriv line)
```

## Core scripts (`Assets/Scripts`)

### Game (`Game/`)

| Script | Role |
|--------|------|
| `LevelDefinition` | Scriptable-style level config: function params, derivative rules, colors, story (also built at runtime in samples). |
| `LevelManager` | Orchestrates levels, HUD (stage + controls + story), theme, obstacle regen, restart / advance. |
| `GameLevelCatalog` | Display names + level count. |
| `LevelSelection` | Static bridge: selected index from LevelSelect → Game. |
| `LevelSelectController` | Builds **scrollable** list UI, **Math tips** button; loads **Game**. |
| `LearningArticleLibrary` | Rich-text article snippets (curriculum + engineering) for the overlay. |
| `MathArticlesOverlay` | Full-screen scroll reader on level select. |
| `DeviceLayout`, `MobileUiRoots`, `CanvasSafeAreaBootstrap` | Safe-area insets & mobile layout. |
| `MobileTouchControls`, `MobileInputBridge` | Touch game controls + bridge to `PlayerControllerUI2D`. |
| `GraphObstacleGenerator` | Samples curve/derivative columns → `GraphWorld` (platforms, hazards, finish, spawn). |
| `PlayerControllerUI2D` | Grid-space movement, jump, collisions vs `GridRect` list. |
| `DerivativePopAnimator` | Short “pop” on derivative renderer at stage crossings. |

### Functions (`Functions/`)

| Script | Role |
|--------|------|
| `FunctionPlotter` | Samples `FunctionType`, pushes points to line/derivative renderers, equation TMP. |

### UI (`UI/`)

| Script | Role |
|--------|------|
| `LineRendererUI` / `DerivRendererUI` | UI-space polylines. |
| `GridRendererUI` | Grid mesh. |
| `LabelManager` / axis labels | Axis numbers. |
| `SceneFader` | Fade + `LoadGame` / `LoadLevelSelect` / `LoadMenu`. |

## Coordinate spaces

- **Graph:** points use the plotter’s logical X and grid Y (see grid size on `GridRendererUI`).
- **Platforms:** `GraphObstacleGenerator` maps column index → rectangles in the same grid space; `PlayerControllerUI2D` converts grid ↔ `RectTransform` pixels under **Cartesian Plane**.

## Persistence

- **Level index:** in-memory only (`LevelSelection`), cleared after **Consume** on Game load.
- **Full-game unlock:** `PlayerPrefs` key `fp_ent_full` holds a device-bound token (cache only — the store is re-queried on every launch). Debug override `fp_ent_debug`, grandfather flag `fp_ent_legacy`.

## Monetization (`Scripts/Monetization/`)

Free tier = levels **0–13** (*Core & series* + *Multivar & integration*). Everything else is unlocked by one **non-consumable** in-app purchase, product id **`firstprinciples_full_unlock`** (same id on App Store Connect and Google Play).

| Script | Role |
|--------|------|
| `LevelAccessPolicy` | Pure rules (free range, category state, next playable level, platform mode, grandfathering). Covered by `Assets/Editor/Tests/LevelAccessPolicyTests.cs`. |
| `Entitlements` | Runtime service: `HasFullGame`, `IsLevelUnlocked`, `BuyFullGame`, `RestorePurchases`, `Changed` event, local cache, debug override. |
| `UnityIapEntitlementStore` | Unity IAP 5 (`com.unity.purchasing`) adapter — StoreKit 2 on iOS, Play Billing on Android, fake store in the Editor. |
| `PaywallOverlay` | Runtime-built unlock sheet (Buy with localized price, **Restore purchases**, Not now). |
| `LockGlyph` | Procedural padlock sprite for locked rows. |

**Platforms:** iOS / Android are gated; the Editor is gated (fake store) so the paywall can be tested; macOS / Windows / Linux standalone builds are always unlocked (they are distributed as complete builds, not through a store with this product — define `FP_IAP_GATE_STANDALONE` to change that). `FP_UNLOCK_ALL` forces everything open (press / review builds).

**Grandfathering:** devices that ran a pre-paywall build (≤ 1.2) keep all levels (detected on the first launch of the paywall build via the existing `fp_language` pref).

**Testing:** *Tools ▸ First Principles ▸ IAP* (Editor) toggles the override / clears the cache; in Development builds tap the paywall title 7×. Release builds ignore the override.

## APIs deprecated in Unity 6

Project code prefers **`FindAnyObjectByType`** and parameterless **`FindObjectsByType`** over deprecated `FindObjectOfType` / sort-mode overloads where applicable.
