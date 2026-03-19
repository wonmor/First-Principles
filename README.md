# First Principles: An Interactive Module

A **graphing calculator** and **derivative-driven platformer** built in **Unity 6**.  
Developed by [Rayan Kaissi](https://github.com/GameGenesis) and [John Seong](https://github.com/wonmor). Part of the not-for-profit, open-source **College Math For Toddlers** initiative (**MIT**).

---

## Alpha build 0.3+

The project pairs a **Cartesian graph UI** (functions + numeric derivatives) with **Limbo-style** gameplay: **platforms** follow the curve and **gaps / hazards** follow derivative rules, with **staged progression** (HUD), **level select**, and typography matched to the main equation label.

**Links:** [Demo (YouTube)](https://www.youtube.com/watch?v=yo540yl4Xhs) · [Legacy wiki](https://github.com/GameGenesis/First-Principles/wiki/First-Principles-Official-Documentation) · [Promo video](https://www.youtube.com/watch?v=k0soEFAK-CQ)

---

## Documentation (GitHub Pages)

Comprehensive docs for setup, gameplay, architecture, and troubleshooting live in **`/docs`** (Jekyll, GitHub Pages–compatible).

| | |
|--|--|
| **Browse in repo** | [`docs/index.md`](docs/index.md) |
| **Published site** | After you enable **Settings → Pages → `/docs`**: `https://<user>.github.io/First-Principles/` (set `url` / `baseurl` in [`docs/_config.yml`](docs/_config.yml)) |

Topics covered: **Unity 6000.4.0f1** project path (`First Principles/`), **Menu → Level select → Game**, controls (**arrows / WASD**, **Space** jump), **stages HUD**, package restore script, TextMeshPro, and Pages **404** fixes.

---

## Repository layout

| Path | Purpose |
|------|---------|
| **`First Principles/`** | **Unity project root** — open this folder in Unity Hub (`Assets`, `ProjectSettings`, `Packages`). |
| **`docs/`** | GitHub Pages documentation (Jekyll). |
| **`clean-unity-library.sh`** | Deletes `First Principles/Library` and stray `Packages/com.unity.*` embeds; use when packages are corrupt. |
| **`README.md`** | This file. |

---

## Quick start

1. Install **Unity 6000.4.0f1** (see `First Principles/ProjectSettings/ProjectVersion.txt`).
2. **Add** the folder **`…/First-Principles/First Principles`** in Unity Hub.
3. Open **Menu** scene, press **Play**.
4. **Play** flow: **Menu** → **Level select** → **Game** (level index is carried by `LevelSelection`).

---

## Gameplay highlights

- **Level select** — Runtime UI from `LevelSelectController` + `GameLevelCatalog` names.
- **Stages** — Derivative “pop” thresholds; **Stage *k* / *n*** HUD (same font stack as **Equation**).
- **controls** — ←/→ or A/D, **Space** / W / ↑ to jump; legacy Trans/Scale graph buttons are **off** in favour of level definitions.
- **Spawn** — Start column chooses the **lowest** safe platform among opening columns so you don’t begin at the top of the curve.

---

## Planned editions

1. **Pre-Calculus** — The nature of functions  
2. **Fundamentals of Calculus** — Limits and differentiation  
3. **Integral calculus**  
4. **Infinite series**

---

## Screenshots

<img width="1728" alt="Screen Shot 2022-03-15 at 2 57 39 PM" src="https://user-images.githubusercontent.com/35755386/158451505-71e056ee-cca4-42ee-a621-38c092c806f2.png">

<img width="1728" alt="Screen Shot 2022-03-15 at 2 57 31 PM" src="https://user-images.githubusercontent.com/35755386/158451522-61e0c14c-57c3-4819-b023-b7d7c13075aa.png">

---

## Dependencies

- [**Unity**](https://unity.com) **6** (6000.4.0f1)
- **Unity UI (uGUI)** + **TextMesh Pro** (via editor / packages; import TMP Essentials if prompted)

---

## Troubleshooting (short)

| Issue | Action |
|--------|--------|
| **Wrong project / “Untitled”** | Open **`First Principles/`** in Hub; load `Assets/Scenes/…`. |
| **UGUI / package chaos** | Quit Unity → `./clean-unity-library.sh` → reopen. Details: [`docs/troubleshooting.md`](docs/troubleshooting.md) and `First Principles/Docs/Fix-Unity-UGUI-PackageErrors.md`. |
| **GitHub Pages 404** | Set **`url`** and **`baseurl`** in `docs/_config.yml` to match your user + repo name. |

---

**MIT License** · **First Principles** · *College Math For Toddlers*
