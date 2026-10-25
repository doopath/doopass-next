# Generates the PWA icon set (PNG) served by the web interface.
#
# Usage (any Python with Pillow installed, e.g. the repo's bundled runtime):
#   python scripts/generate-icons.py
#
# Outputs in src/Doopass.Cli/wwwroot/:
#   icon-192.png, icon-512.png                -> manifest "purpose: any"
#   icon-maskable-192.png, icon-maskable-512  -> manifest "purpose: maskable"
#   apple-touch-icon.png                      -> iOS home-screen icon (180x180)
#
# The design is a raster replica of the inline SVG favicon in
# App.razor: an indigo rounded square with a white padlock, drawn in a
# 24x24 unit coordinate system and supersampled 4x for antialiasing.

from pathlib import Path

from PIL import Image, ImageDraw

INDIGO = (79, 70, 229, 255)      # #4f46e5, matches --accent in app.css
WHITE = (255, 255, 255, 255)

SS = 4          # supersampling factor
UNIT = 24.0     # design grid size
# Output directory: pass --out <dir> to write elsewhere (e.g. a temp folder
# when the sandbox denies direct writes); defaults to the web assets folder.
import sys
OUT = Path(sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv
           else Path(__file__).resolve().parent.parent / "src" / "Doopass.Cli" / "wwwroot")
OUT.mkdir(parents=True, exist_ok=True)


def draw_background(d: ImageDraw.ImageDraw, u: float, full_bleed: bool) -> None:
    """App tile background: rounded square (any) or full-bleed square
    (maskable / apple-touch, for OS corner masks to apply over)."""
    if full_bleed:
        d.rectangle([0, 0, u, u], fill=INDIGO)
    else:
        d.rounded_rectangle([1, 1, 23, 23], radius=6, fill=INDIGO)


def draw_lock(d: ImageDraw.ImageDraw, map_x, map_y) -> None:
    """White padlock in 24-unit space, positioned via unit->pixel maps."""
    # Shackle: top half circle centered at (12, 7), radius 4, stroke 2.
    # Outer stroke edge is at radius 5 => PIL arc bbox (7, 2, 17, 12),
    # drawn from the outer ellipse inward by `width` pixels.
    r_box = 5 * map_scale
    sun = map_y(7)
    d.arc([map_x(12) - r_box, sun - r_box, map_x(12) + r_box, sun + r_box], 180, 360, fill=WHITE, width=int(2 * map_scale))
    # Vertical legs, with round caps (SVG stroke-linecap), reaching under the
    # body rect so no seam shows between shapes.
    cap_r = 1 * map_scale
    for leg_x in (8, 16):
        d.line([map_x(leg_x), map_y(6.4), map_x(leg_x), map_y(10.6)], fill=WHITE, width=int(2 * map_scale))
        d.ellipse([map_x(leg_x) - cap_r, map_y(10.6) - cap_r, map_x(leg_x) + cap_r, map_y(10.6) + cap_r], fill=WHITE)
    # Lock body.
    d.rounded_rectangle([map_x(7), map_y(10), map_x(17), map_y(18)], radius=2, fill=WHITE)


def make_icon(size_px: int, full_bleed: bool, content_scale: float, name: str) -> None:
    """Render one icon at `size_px` (drawn at SS x then downsampled)."""
    global map_scale
    big = size_px * SS
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    draw_background(d, big, full_bleed)

    # Map 24-unit design coordinates onto the canvas. The lock content sits at
    # `content_scale` of the canvas, centered: content_scale=1 fills the canvas.
    map_scale = (big / UNIT) * content_scale
    draw_lock(
        d,
        lambda ux: big / 2 + (ux - 12) * map_scale,
        lambda uy: big / 2 + (uy - 12) * map_scale,
    )

    img = img.resize((size_px, size_px), Image.LANCZOS)
    img.save(OUT / name)
    print(f"  wrote {name} ({size_px}x{size_px}, full_bleed={full_bleed}, content={content_scale:.2f})")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    print("Generating PWA icons ...")

    # "any" icons: transparent corners, rounded-tile design at full size.
    make_icon(512, full_bleed=False, content_scale=1.0, name="icon-512.png")
    make_icon(192, full_bleed=False, content_scale=1.0, name="icon-192.png")

    # Maskable: opaque full bleed; lock shrunk into the safe zone (content must
    # stay inside the central 80% circle that launchers may crop to).
    make_icon(512, full_bleed=True, content_scale=0.66, name="icon-maskable-512.png")
    make_icon(192, full_bleed=True, content_scale=0.66, name="icon-maskable-192.png")

    # iOS home screen: opaque full bleed (no transparency allowed; iOS applies
    # its own corner mask), lock slightly larger for visual weight.
    make_icon(180, full_bleed=True, content_scale=0.70, name="apple-touch-icon.png")

    print("Done.")


if __name__ == "__main__":
    main()
