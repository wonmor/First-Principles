---
layout: page
title: Gameplay
permalink: /gameplay/
---

# Gameplay

## Modes

1. **Graphing calculator** — Functions are plotted on the Cartesian plane; numeric derivatives can be shown as a second curve (`FunctionPlotter`, `LineRendererUI`, `DerivRendererUI`).
2. **Derivative platformer** — On **Game** scene, a small character runs on **platforms** generated from the curve; **gaps / hazards** follow rules based on the **derivative** (`GraphObstacleGenerator`, `PlayerControllerUI2D`).

Level parameters (colors, function type, transformation coefficients, story text) are defined in code via **`LevelManager`** sample levels / **`LevelDefinition`**.

## Controls (platformer)

| Input | Action |
|--------|--------|
| **← / →** or **A / D** | Move |
| **Space**, **W**, or **↑** | Jump |
| **Jump** axis / gamepad | Still supported via Unity **Input Manager** |

Legacy **Trans** / **Scale** tuning buttons on the Game UI are **disabled**; tuning is driven by **level definitions**, not free sliders.

## Stages

- The run is split into **stages** (derivative **“pop”** moments at X thresholds).
- A **stage HUD** (top-left) shows **Stage *k* / *n*** using the same typography family as the equation label.
- Crossing a stage line triggers **`DerivativePopAnimator`** (visual emphasis on the derivative).

## Level flow

1. **Menu** — Entry and scene fade.
2. **LevelSelect** — `LevelSelectController` builds a list from **`GameLevelCatalog.DisplayNames`**; choosing a level calls **`LevelSelection.SetSelectedLevel`** and loads **Game**.
3. **Game** — `LevelManager` reads **`LevelSelection.ConsumeSelectedLevel`**, applies the level theme to **`FunctionPlotter`**, regenerates obstacles, and spawns / resets the player.

## Spawn position

The generator picks a **spawn column** among the “safe start” columns where the **platform top is lowest**, so the player does not always appear at the visually highest part of the early curve.

## Win / death

- **Hazards** or falling below a Y threshold causes **respawn** on the same level.
- Reaching the **finish zone** (far right) **advances** to the next sample level (wraps when last is complete).

## UI polish

- **Story** text appears at the top with the **level name** and fades (TMP).
- **Controls** hint bar at the bottom shows move / jump hints.
- **Stage** panel uses a dark “glass” backing and accent strip consistent with the Limbo-like aesthetic.
