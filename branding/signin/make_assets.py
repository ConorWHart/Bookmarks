"""Bookmarks sign-in branding assets for Entra External ID Company Branding.

Outputs (in ./out):
  background.jpg   1920x1080, <= 300 KB  - a dim wall of bookshelves
  banner-logo.png  245x36,    <= 50 KB   - icon + "Bookmarks" wordmark, cream on transparent
  square-logo.png  240x240,   <= 50 KB   - the app icon
  favicon.png      32x32,     <= 5 KB
"""
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
HERE = os.path.dirname(os.path.abspath(__file__))
ICON = os.path.join(HERE, "..", "..", "src", "Bookmarks.Client", "wwwroot", "icon-512.png")
# Caladea (Cambria-metric serif); on Windows, C:/Windows/Fonts/georgia.ttf works too.
SERIF = os.environ.get("BOOKMARKS_SERIF", "/usr/share/fonts/truetype/crosextra/Caladea-Regular.ttf")

# The app's palette (BookmarksTheme dark + the icon's spines).
SPINES = ["#8B2E1E", "#2E5E4E", "#C9A227", "#1E3A5F", "#D9822B", "#5B3A29", "#6E2A3A",
          "#3F4E2A", "#7A4E2D", "#2C3E50", "#A0522D", "#4A3B5C", "#B8860B", "#355C4A"]
BACK = (42, 29, 19)
WOOD = (168, 116, 63)
WOOD_HI = (212, 165, 111)
WOOD_LO = (111, 71, 34)
CREAM = (237, 227, 212)


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def shade(rgb, f):
    return tuple(max(0, min(255, int(c * f))) for c in rgb)


def background():
    rnd = random.Random(7)
    w, h = 1920, 1080
    img = Image.new("RGB", (w, h), BACK)
    d = ImageDraw.Draw(img)

    # Vertical back boards.
    for x in range(0, w, 96):
        d.line([(x, 0), (x, h)], fill=shade(BACK, 0.75), width=2)

    shelf_h = 250
    plank = 22
    top = -60
    for row in range(6):
        floor = top + shelf_h * (row + 1)
        # Shadow under the plank above.
        for i in range(40):
            d.line([(0, floor - shelf_h + plank + i), (w, floor - shelf_h + plank + i)],
                   fill=shade(BACK, 0.55 + i * 0.011))
        # Books.
        x = rnd.randint(-40, 10)
        while x < w:
            if rnd.random() < 0.07:  # a gap now and then
                x += rnd.randint(30, 120)
                continue
            bw = rnd.randint(22, 52)
            bh = rnd.randint(150, 205)
            col = hex_rgb(rnd.choice(SPINES))
            col = shade(col, rnd.uniform(0.75, 1.05))
            lean = rnd.random() < 0.04
            y0 = floor - bh
            if lean:
                poly = [(x, floor), (x + bw, floor), (x + bw + 40, y0 + 12), (x + 40 - 4, y0)]
                d.polygon(poly, fill=col)
                x += bw + 46
                continue
            d.rectangle([x, y0, x + bw, floor], fill=col)
            # Spine shading and bands.
            d.rectangle([x, y0, x + 3, floor], fill=shade(col, 1.25))
            d.rectangle([x + bw - 4, y0, x + bw, floor], fill=shade(col, 0.6))
            if rnd.random() < 0.6:
                band = shade(col, 1.6) if rnd.random() < 0.5 else (214, 186, 120)
                by = y0 + rnd.randint(14, 30)
                d.rectangle([x + 4, by, x + bw - 5, by + 5], fill=band)
                d.rectangle([x + 4, floor - 34, x + bw - 5, floor - 30], fill=band)
            # A faint title stroke.
            if bw > 30 and rnd.random() < 0.7:
                ty = y0 + 50
                d.rectangle([x + bw // 2 - 2, ty, x + bw // 2 + 2, ty + rnd.randint(50, bh - 90)],
                            fill=shade(col, 1.45))
            x += bw + rnd.randint(1, 3)
        # Plank.
        d.rectangle([0, floor, w, floor + plank], fill=WOOD)
        d.rectangle([0, floor, w, floor + 4], fill=WOOD_HI)
        d.rectangle([0, floor + plank - 5, w, floor + plank], fill=WOOD_LO)

    # Soft focus, then dim towards the middle (where the sign-in box sits) and the edges.
    img = img.filter(ImageFilter.GaussianBlur(2.2))
    dark = Image.new("RGB", (w, h), (12, 9, 7))
    mask = Image.new("L", (w, h), 0)
    md = ImageDraw.Draw(mask)
    # Overall dim.
    md.rectangle([0, 0, w, h], fill=120)
    mask = mask.filter(ImageFilter.GaussianBlur(0))
    centre = Image.new("L", (w, h), 0)
    cd = ImageDraw.Draw(centre)
    cd.ellipse([w * 0.28, h * 0.08, w * 0.72, h * 0.92], fill=110)
    centre = centre.filter(ImageFilter.GaussianBlur(160))
    vignette = Image.new("L", (w, h), 120)
    vd = ImageDraw.Draw(vignette)
    vd.ellipse([-w * 0.1, -h * 0.25, w * 1.1, h * 1.25], fill=0)
    vignette = vignette.filter(ImageFilter.GaussianBlur(200))
    m = Image.new("L", (w, h))
    mp, cp, vp = mask.load(), centre.load(), vignette.load()
    out = m.load()
    for yy in range(h):
        for xx in range(w):
            out[xx, yy] = min(235, mp[xx, yy] + cp[xx, yy] + vp[xx, yy])
    img = Image.composite(dark, img, m)

    # Warm glow from the top, like a reading lamp.
    glow = Image.new("RGB", (w, h), (255, 200, 130))
    gm = Image.new("L", (w, h), 0)
    ImageDraw.Draw(gm).ellipse([w * 0.2, -h * 0.6, w * 0.8, h * 0.35], fill=38)
    gm = gm.filter(ImageFilter.GaussianBlur(180))
    img = Image.composite(glow, img, gm)

    path = os.path.join(OUT, "background.jpg")
    for q in (82, 76, 70, 64):
        img.save(path, "JPEG", quality=q, optimize=True, progressive=True)
        if os.path.getsize(path) <= 280_000:
            break
    return path


def icon(size):
    return Image.open(ICON).convert("RGBA").resize((size, size), Image.LANCZOS)


def banner():
    w, h = 245, 36
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ic = icon(34)
    img.alpha_composite(ic, (0, 1))
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype(SERIF, 27)
    d.text((44, h // 2 + 1), "Bookmarks", font=font, fill=CREAM + (255,), anchor="lm")
    path = os.path.join(OUT, "banner-logo.png")
    img.save(path, optimize=True)
    return path


def square():
    path = os.path.join(OUT, "square-logo.png")
    icon(240).save(path, optimize=True)
    return path


def favicon():
    path = os.path.join(OUT, "favicon.png")
    icon(32).save(path, optimize=True)
    return path


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for p in (background(), banner(), square(), favicon()):
        im = Image.open(p)
        print(f"{os.path.basename(p):18} {im.size[0]}x{im.size[1]}  {os.path.getsize(p) / 1024:.1f} KB")
