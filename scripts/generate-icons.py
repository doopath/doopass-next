# Generates the application icon set from the master logo artwork.
#
# Usage (any Python with Pillow and numpy, e.g. the repo's bundled runtime):
#   python scripts/generate-icons.py
#
# Source of truth: assets/logo1024.png, the PNG master of the same artwork as
# assets/logo.svg. The vector master is served as-is as the SVG favicon; every
# raster icon below is a resize of the PNG master, so a new logo only needs the
# files in assets/ replaced and this script re-run -- nothing is hand-drawn.
#
# Outputs:
#   .pwa-assets/icon-192.png, icon-512.png                -> manifest "purpose: any"
#   .pwa-assets/icon-maskable-192.png, icon-maskable-512  -> manifest "purpose: maskable"
#   .pwa-assets/apple-touch-icon.png                      -> iOS home screen (180x180)
#   .pwa-assets/favicon.ico                               -> multi-size browser tab icon
#   .pwa-assets/app.ico                                   -> Win32 icon for doopass.exe
#   src/Doopass.Mobile/Resources/AppIcon/appicon.png      -> Android launcher icon
#   src/Doopass.WebUi/wwwroot/brand.svg                   -> sidebar / auth-card mark
#
# The master is a rounded tile with transparent corners. Where an opaque canvas
# is required (maskable icons, apple-touch-icon, the Win32 icon) the tile is
# *flattened*: each row's transparent corners are filled with that row's edge
# colour, so no background seam shows and no transparency is left behind.

from pathlib import Path
import shutil
import sys

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parent.parent
ASSETS = REPO / "assets"
PWA_OUT = REPO / ".pwa-assets"
MOBILE_ICON = REPO / "src" / "Doopass.Mobile" / "Resources" / "AppIcon" / "appicon.png"
# The web UI's sidebar / auth-card mark. It lives in the RCL's wwwroot so both
# the server host and the MAUI WebView (_content/Doopass.WebUi/...) resolve it.
WEBUI_BRAND = REPO / "src" / "Doopass.WebUi" / "wwwroot" / "brand.svg"

# The PNG masters the logo currently ships in, largest first.
MASTER_NAMES = ("logo1024.png", "logo2048.png", "logo512.png", "logo256.png")

# Windows .ico payloads: Explorer, the taskbar and the Alt-Tab switcher all pick
# a different one of these depending on the surface and DPI.
ICO_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]

# The favicon is downloaded on every first visit, so it carries only the tab and
# bookmark sizes; the desktop icon above keeps the large end of the range.
FAVICON_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48)]


def load_master() -> Image.Image:
    for name in MASTER_NAMES:
        path = ASSETS / name
        if path.exists():
            print(f"  master: {path.relative_to(REPO)}")
            return Image.open(path).convert("RGBA")
    sys.exit(f"error: no logo PNG found in {ASSETS} (looked for {', '.join(MASTER_NAMES)})")


def flatten(img: Image.Image) -> Image.Image:
    """Composite the tile onto its own edge colour, removing all transparency.

    The master's only transparent pixels are the rounded corners, so extending
    the edge colour of each row across that row's transparent runs reproduces
    the artwork over a full-bleed square without a visible seam.
    """
    arr = np.array(img).astype(np.float32)
    rgb = arr[..., :3]
    alpha = arr[..., 3] / 255.0

    filled = rgb.copy()
    for y in range(arr.shape[0]):
        opaque = np.nonzero(alpha[y] >= 1.0)[0]
        if opaque.size == 0:
            continue
        first, last = opaque[0], opaque[-1]
        if first > 0:
            filled[y, :first] = rgb[y, first]
        if last < filled.shape[1] - 1:
            filled[y, last + 1:] = rgb[y, last]

    out = rgb * alpha[..., None] + filled * (1.0 - alpha[..., None])
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGB")


def square(img: Image.Image, size: int) -> Image.Image:
    """Lanczos resize to a square canvas of `size` (the master is already square)."""
    return img.resize((size, size), Image.LANCZOS)


def main() -> None:
    master = load_master()
    PWA_OUT.mkdir(parents=True, exist_ok=True)

    # "any" icons keep the designer's rounded tile and its transparent corners.
    for size in (512, 192):
        square(master, size).save(PWA_OUT / f"icon-{size}.png")
        print(f"  wrote icon-{size}.png")

    # Maskable icons are cropped to a circle by launchers, so they must be
    # opaque full-bleed. The lock sits inside the central 40% of the canvas,
    # well within the 80%-diameter safe zone, so no extra scaling is needed.
    for size in (512, 192):
        flatten(square(master, size)).save(PWA_OUT / f"icon-maskable-{size}.png")
        print(f"  wrote icon-maskable-{size}.png")

    # iOS home screen: 180x180, no transparency allowed (iOS adds its own mask).
    flatten(square(master, 180)).save(PWA_OUT / "apple-touch-icon.png")
    print("  wrote apple-touch-icon.png")

    # Browser tab / bookmark icon.
    square(master, 256).save(PWA_OUT / "favicon.ico", format="ICO", sizes=FAVICON_SIZES)
    print("  wrote favicon.ico")

    # Win32 resource for the doopass.exe apphost and the file icon in Explorer.
    flattened = flatten(master)
    flattened.save(PWA_OUT / "app.ico", format="ICO", sizes=ICO_SIZES)
    print("  wrote app.ico")

    # Android launcher icon: MAUI's resizetizer masks it into the adaptive
    # foreground, so transparency is fine and `MauiIcon Color` supplies the
    # background behind the tile's corners.
    MOBILE_ICON.parent.mkdir(parents=True, exist_ok=True)
    square(master, 1024).save(MOBILE_ICON)
    print(f"  wrote {MOBILE_ICON.relative_to(REPO)}")

    # Web UI brand mark: the vector master again, copied into the RCL's wwwroot
    # so the sidebar and the login card show it in the browser and in the MAUI
    # WebView, which loads it from _content/Doopass.WebUi/brand.svg.
    svg_master = ASSETS / "logo.svg"
    if not svg_master.exists():
        sys.exit(f"error: {svg_master.relative_to(REPO)} is missing; it feeds the SVG favicon and the brand mark")
    WEBUI_BRAND.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(svg_master, WEBUI_BRAND)
    print(f"  wrote {WEBUI_BRAND.relative_to(REPO)}")

    print("Done.")


if __name__ == "__main__":
    main()
