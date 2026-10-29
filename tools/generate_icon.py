#!/usr/bin/env python3
"""Generate the timestamp converter PNG and multi-resolution Windows ICO.

The geometry and palette match assets/app-icon.svg. Render at four times the
source resolution before downsampling so even the small tray frames stay clear.
"""

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "assets"
PNG_PATH = ASSETS / "app-icon.png"
ICO_PATH = ASSETS / "app-icon.ico"

SCALE = 4
CANVAS = 512
ICO_SIZES = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)]


def s(value: int) -> int:
    return value * SCALE


def round_line(draw: ImageDraw.ImageDraw, points, fill: str, width: int) -> None:
    scaled = [(s(x), s(y)) for x, y in points]
    draw.line(scaled, fill=fill, width=s(width), joint="curve")
    radius = s(width) // 2
    for x, y in scaled:
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=fill)


def make_icon() -> Image.Image:
    image = Image.new("RGBA", (s(CANVAS), s(CANVAS)), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    navy = "#142b4a"
    white = "#f7fbff"
    cyan = "#4ed8ec"

    draw.rounded_rectangle((s(24), s(24), s(488), s(488)), radius=s(104), fill=navy)

    # A 30-unit ring, centered on r=144, matching the SVG stroke.
    draw.ellipse((s(97), s(97), s(415), s(415)), fill=white)
    draw.ellipse((s(127), s(127), s(385), s(385)), fill=navy)

    round_line(draw, [(256, 164), (256, 256), (318, 292)], white, 30)

    # A small accent at twelve o'clock keeps the image distinct from a stock clock.
    round_line(draw, [(256, 106), (256, 124)], cyan, 30)
    draw.ellipse((s(239), s(239), s(273), s(273)), fill=cyan)

    return image.resize((CANVAS, CANVAS), Image.Resampling.LANCZOS)


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    icon = make_icon()
    icon.save(PNG_PATH, "PNG", optimize=True)
    icon.save(ICO_PATH, "ICO", sizes=ICO_SIZES)
    print(f"Wrote {PNG_PATH}")
    print(f"Wrote {ICO_PATH}")


if __name__ == "__main__":
    main()
