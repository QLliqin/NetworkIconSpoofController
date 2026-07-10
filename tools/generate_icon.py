#!/usr/bin/env python3
"""Generate the project PNG and multi-resolution Windows ICO assets."""

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


def make_icon() -> Image.Image:
    image = Image.new("RGBA", (s(CANVAS), s(CANVAS)), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    charcoal = "#24282d"
    green = "#18a477"
    white = "#f7faf9"
    amber = "#f4b942"

    draw.rounded_rectangle(
        (s(28), s(28), s(484), s(484)),
        radius=s(108),
        fill=charcoal,
    )

    cx, cy, radius = s(238), s(232), s(142)
    globe_box = (cx - radius, cy - radius, cx + radius, cy + radius)
    draw.ellipse(globe_box, fill=green, outline=white, width=s(24))
    draw.ellipse(
        (cx - s(68), cy - radius, cx + s(68), cy + radius),
        outline=white,
        width=s(22),
    )
    draw.ellipse(
        (cx - radius, cy - s(64), cx + radius, cy + s(64)),
        outline=white,
        width=s(22),
    )
    draw.line(
        (s(96), cy, s(380), cy),
        fill=white,
        width=s(22),
    )

    power_cx, power_cy = s(382), s(374)
    draw.ellipse(
        (
            power_cx - s(78),
            power_cy - s(78),
            power_cx + s(78),
            power_cy + s(78),
        ),
        fill=charcoal,
    )
    draw.ellipse(
        (
            power_cx - s(62),
            power_cy - s(62),
            power_cx + s(62),
            power_cy + s(62),
        ),
        fill=amber,
    )
    draw.line(
        (power_cx, s(332), power_cx, s(380)),
        fill=charcoal,
        width=s(18),
    )
    draw.arc(
        (s(340), s(338), s(424), s(422)),
        start=310,
        end=230,
        fill=charcoal,
        width=s(18),
    )

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
