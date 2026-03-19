---
layout: default
title: Home
---

# First Principles — documentation

**Release: Beta 1.0**

**First Principles** is an open-source **Unity 6** project that combines a **graphing calculator** (functions and numeric derivatives on a grid) with a **Limbo-inspired 2D platformer**: platforms and hazards are driven by the curve and its derivative, with **staged progression** and per-level themes (including primer, series, and multivariable slices).

Developed by [Rayan Kaissi](https://github.com/GameGenesis) and [John Seong](https://github.com/wonmor) as part of *College Math For Toddlers* (MIT).

## Quick links

| Guide | Description |
|------|----------------|
| [Setup]({% link setup.md %}) | Unity version, clone, open the correct project folder |
| [Gameplay]({% link gameplay.md %}) | Controls, stages, level select, how the graph affects the world |
| [Architecture]({% link architecture.md %}) | Scenes, scripts, data flow |
| [Troubleshooting]({% link troubleshooting.md %}) | Package cache, TextMeshPro, GitHub Pages / `baseurl` |

## Repository layout

```
First-Principles/                 ← git repository root (this site: /docs)
├── docs/                         ← GitHub Pages source (you are here)
├── README.md
├── clean-unity-library.sh
└── First Principles/             ← Unity project (note the space)
    ├── Assets/
    ├── Packages/
    ├── ProjectSettings/
    └── ...
```

Always open the **`First Principles`** folder (the one that contains `Assets` and `ProjectSettings`) in Unity Hub — not the parent git folder alone.

## External links

- [YouTube — demo](https://www.youtube.com/watch?v=yo540yl4Xhs)
- [Wiki — official documentation (legacy)](https://github.com/GameGenesis/First-Principles/wiki/First-Principles-Official-Documentation)
- [Repository](https://github.com/GameGenesis/First-Principles)

---

*Documentation version aligned with Unity **6000.4.0f1** and the graph + platformer flow described in this site.*
