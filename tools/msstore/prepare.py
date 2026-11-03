#!/usr/bin/env python3
"""Stage a Microsoft Store MSIX layout from the Windows build (BuildHelper.BuildWindowsStore).

  python3 tools/msstore/prepare.py --name <Identity Name> --publisher "CN=..." --publisher-display "<name>"

Writes Builds/MSStore/Layout/ (game files + AppxManifest.xml + Assets/*.png tile logos). Pack it on Windows
with the Windows SDK:  MakeAppx pack /d Builds\\MSStore\\Layout /p FirstPrinciples.msix
(Partner Center signs Store packages, so no certificate is needed for submission.)
Without --name the manifest keeps the __PLACEHOLDERS__ so the layout can be reviewed early.
"""
import argparse, os, re, shutil
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "Builds", "MSStore", "Windows")
OUT = os.path.join(ROOT, "Builds", "MSStore", "Layout")
ICON = os.path.join(ROOT, "FirstPrinciplesLogo.png")
HERE = os.path.dirname(os.path.abspath(__file__))


def version():
    s = open(os.path.join(ROOT, "First Principles", "Assets", "Editor", "BuildAndroid.cs")).read()
    return re.search(r'Version = "([0-9.]+)"', s).group(1)


def tile(size, canvas=None, scale=0.8):
    w, h = canvas or (size, size)
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    side = int(min(w, h) * scale)
    ic = Image.open(ICON).convert("RGBA").resize((side, side), Image.LANCZOS)
    img.paste(ic, ((w - side) // 2, (h - side) // 2), ic)
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--name", default="__IDENTITY_NAME__")
    ap.add_argument("--publisher", default="__PUBLISHER__")
    ap.add_argument("--publisher-display", default="__PUBLISHER_DISPLAY__")
    a = ap.parse_args()
    if not os.path.isdir(SRC):
        raise SystemExit("Build Builds/MSStore/Windows first (BuildHelper.BuildWindowsStore).")
    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    shutil.copytree(SRC, OUT, ignore=shutil.ignore_patterns("*_DoNotShip*", "*_ButDontShipItWithYourGame*"))
    assets = os.path.join(OUT, "Assets")
    os.makedirs(assets, exist_ok=True)
    tile(50, scale=1.0).save(os.path.join(assets, "StoreLogo.png"))
    tile(44, scale=1.0).save(os.path.join(assets, "Square44x44Logo.png"))
    tile(150).save(os.path.join(assets, "Square150x150Logo.png"))
    tile(150, (310, 150)).save(os.path.join(assets, "Wide310x150Logo.png"))
    tile(310).save(os.path.join(assets, "LargeTile.png"))
    m = open(os.path.join(HERE, "AppxManifest.template.xml")).read()
    m = (m.replace("__IDENTITY_NAME__", a.name).replace("__PUBLISHER_DISPLAY__", a.publisher_display)
          .replace("__PUBLISHER__", a.publisher).replace("__VERSION__", version()))
    open(os.path.join(OUT, "AppxManifest.xml"), "w").write(m)
    print("Layout ready:", OUT)


if __name__ == "__main__":
    main()
