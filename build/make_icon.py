"""Draws the Document Scanner icon (a flatbed scanner with its lid open and the scan light on).

Run: python3 build/make_icon.py [out_dir] [--preview]
Writes scanner.ico (16-256 px) and splash.png into src/DocumentScanner.App/Resources by default.
Each size is drawn on its own canvas at 8x and reduced; sizes under 32 px drop the inner detail so
the 16 px tray icon stays readable.
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

PLATEN = (35, 40, 46, 255)      # PlatenBrush
LAMP = (8, 112, 127, 255)       # LampBrush
LAMP_DARK = (5, 80, 91, 255)    # SystemAccentColorTertiary
GLOW = (127, 227, 242, 255)     # LampGlowBrush
PAPER = (255, 255, 255, 255)
INK = (90, 104, 120, 255)       # MutedBrush
SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
SS = 8


def draw(size: int) -> Image.Image:
    s = size * SS
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    detail = size >= 32
    u = s / 16  # one unit on a 16-unit grid, so the 16 px icon lands on whole pixels

    def r(x0, y0, x1, y1):
        return [x0 * u, y0 * u, x1 * u, y1 * u]

    def poly(*pts):
        return [(x * u, y * u) for x, y in pts]

    # Lid, raised and tilted back from its hinge at the rear of the body.
    d.polygon(poly((1, 8.5), (15, 8.5), (13.5, 1), (2.5, 1)), fill=LAMP)
    if detail:
        d.polygon(poly((2.6, 7.4), (13.4, 7.4), (12.4, 2.1), (3.6, 2.1)), fill=LAMP_DARK)

    # Body.
    body = r(0.5, 8, 15.5, 15.5)
    d.rounded_rectangle(body, radius=1.6 * u, fill=PLATEN)

    # A document on the glass.
    d.rectangle(r(2, 9, 14, 11.5 if not detail else 11.6), fill=PAPER)
    if detail:
        d.rectangle(r(3, 9.7, 9.5, 10.1), fill=INK)
        d.rectangle(r(3, 10.7, 12, 11.1), fill=INK)

    # The scan light, with a soft glow kept inside the body.
    if detail:
        glow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
        ImageDraw.Draw(glow).rectangle(r(1.5, 12.4, 14.5, 13.8), fill=GLOW)
        glow = glow.filter(ImageFilter.GaussianBlur(0.6 * u))
        mask = Image.new("L", (s, s), 0)
        ImageDraw.Draw(mask).rounded_rectangle(body, radius=1.6 * u, fill=255)
        clipped = Image.new("RGBA", (s, s), (0, 0, 0, 0))
        clipped.paste(glow, (0, 0), Image.composite(glow.getchannel("A"), Image.new("L", (s, s), 0), mask))
        img.alpha_composite(clipped)
    d.rectangle(r(1.5, 12.5 if detail else 12, 14.5, 13.5 if detail else 14), fill=GLOW)
    return img.resize((size, size), Image.LANCZOS)


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    args = [a for a in sys.argv[1:] if a != "--preview"]
    out = Path(args[0]) if args else root / "src/DocumentScanner.App/Resources"
    out.mkdir(parents=True, exist_ok=True)
    frames = {n: draw(n) for n in SIZES}
    frames[256].save(out / "scanner.ico", format="ICO", sizes=[(n, n) for n in SIZES],
                     append_images=[frames[n] for n in SIZES[:-1]])

    # Splash: icon and name on the platen colour, drawn by Windows before WPF has loaded.
    splash = Image.new("RGBA", (360, 200), PLATEN)
    splash.alpha_composite(draw(128), (116, 22))
    try:
        font = ImageFont.truetype("DejaVuSans.ttf", 18)
    except OSError:
        font = ImageFont.load_default()
    ImageDraw.Draw(splash).text((180, 174), "Document Scanner", fill=(230, 234, 238, 255), font=font, anchor="mm")
    splash.convert("RGB").save(out / "splash.png")

    if "--preview" not in sys.argv:
        return
    preview = Image.new("RGBA", (sum(SIZES) + 10 * len(SIZES) + 10, 276), (244, 246, 248, 255))
    x = 10
    for n in SIZES:
        preview.alpha_composite(frames[n], (x, 266 - n))
        x += n + 10
    preview.save(out / "icon-preview.png")


if __name__ == "__main__":
    main()
