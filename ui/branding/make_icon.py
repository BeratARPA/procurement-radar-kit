# -*- coding: utf-8 -*-
"""Draws the İhale Radar mark and writes every size each platform wants.

One script rather than a folder of exported pngs, so the mark can be adjusted
and every platform regenerated from the same source. Run it from ui/:

    python branding/make_icon.py

Design notes, because they are decisions and not taste:
 * The first mark was a radar and only a radar: three rings, a sweep, a dot. It
   could have been any radar app on any phone. What this one actually does is
   find a tender notice, so the notice is now the subject and the radar is the
   thing happening to it - a sheet of paper lit by a sweep.
 * The sheet is the largest shape and sits slightly left of centre, because the
   sweep needs the upper right to come from and an icon with its subject dead
   centre reads as a logo rather than a picture.
 * The sweep is a filled wedge fading to nothing, not a line - at 48 px a line
   disappears and the wedge still reads as motion.
 * Two arcs, not three rings. Full rings drew a target; arcs that run off the
   edge read as range and leave the middle clear for the sheet. Below 64 px the
   third one turned to mush anyway.
 * The lines on the sheet stop being legible around 64 px and that is fine: what
   survives is a bright rounded shape with a cut corner against a dark ground,
   which is still a piece of paper and still nobody else's icon.
 * The catch is one amber dot on the leading edge. It is the only warm colour in
   the mark, so at any size the eye lands on the thing that was found.
 * Nothing is drawn in the outer 8% - Android's round and squircle masks crop
   there, and a ring clipped by the mask looks like a mistake.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

BACKGROUND = (11, 31, 42)      # deep slate, so the mark holds on light and dark
BACKGROUND_EDGE = (6, 18, 26)  # the corners fall away, which keeps it from looking flat
ARC = (44, 96, 116)
SWEEP = (56, 214, 168)         # the interface's own accent green
CATCH = (255, 214, 92)
SHEET = (244, 246, 248)
SHEET_FOLD = (198, 212, 219)
SHEET_RULE = (120, 145, 158)

SUPERSAMPLE = 8


def _ground(s):
    """A radial fall-off, built small and scaled up because it is only a blur."""
    small = 64
    layer = Image.new("RGB", (small, small), BACKGROUND_EDGE)
    pen = ImageDraw.Draw(layer)
    steps = 28
    for i in range(steps, 0, -1):
        t = i / steps
        r = small * 0.78 * t
        colour = tuple(
            int(BACKGROUND_EDGE[c] + (BACKGROUND[c] - BACKGROUND_EDGE[c]) * (1 - t) ** 1.4)
            for c in range(3))
        pen.ellipse([small / 2 - r, small / 2 - r, small / 2 + r, small / 2 + r], fill=colour)

    return layer.resize((s, s), Image.LANCZOS).convert("RGBA")


def _sheet(s, radius):
    """The notice: a sheet with a cut corner and three rules, drawn upright."""
    w = radius * 0.94
    h = w * 1.28
    pad = int(max(w, h) * 0.5)
    layer = Image.new("RGBA", (int(w) + pad * 2, int(h) + pad * 2), (0, 0, 0, 0))
    pen = ImageDraw.Draw(layer)

    x0, y0 = pad, pad
    x1, y1 = pad + w, pad + h
    cut = w * 0.30                      # the folded corner, top right
    corner = w * 0.09

    pen.rounded_rectangle([x0, y0, x1, y1], radius=corner, fill=SHEET + (255,))
    # Cut the corner away, then lay the fold over it, so the notch is a real
    # silhouette rather than a triangle painted on a square.
    pen.polygon([(x1 - cut, y0 - 2), (x1 + 2, y0 - 2), (x1 + 2, y0 + cut)], fill=(0, 0, 0, 0))
    pen.polygon([(x1 - cut, y0), (x1 - cut, y0 + cut), (x1, y0 + cut)], fill=SHEET_FOLD + (255,))

    rule = h * 0.058
    left = x0 + w * 0.16
    for i, width_ratio in enumerate((0.68, 0.68, 0.42)):
        top = y0 + h * (0.46 + i * 0.17)
        pen.rounded_rectangle(
            [left, top, left + w * width_ratio, top + rule],
            radius=rule / 2, fill=SHEET_RULE + (255,))

    return layer


def draw(size, transparent=False, margin_ratio=0.08):
    s = size * SUPERSAMPLE
    if transparent:
        image = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    else:
        image = _ground(s)

    centre = s / 2
    usable = s * (1 - 2 * margin_ratio)
    radius = usable / 2

    # Everything that scans lives on the right, so the sheet has the left to
    # itself. An earlier version put the sheet in the middle and the radar had
    # nowhere to be: the sweep vanished behind the paper and the mark was a
    # document icon with some blue lines near it.
    origin_x = centre - radius * 0.30
    outer = radius * 0.92

    # The sweep first, so everything else sits on top of it and stays legible.
    sweep = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    pen = ImageDraw.Draw(sweep)
    start, span = -58.0, 54.0
    steps = 48
    box = [origin_x - outer, centre - outer, origin_x + outer, centre + outer]
    for i in range(steps):
        a0 = start + span * i / steps
        a1 = start + span * (i + 1) / steps + 0.7
        alpha = int(135 * (i / steps) ** 2)
        pen.pieslice(box, a0, a1, fill=SWEEP + (alpha,))
    image.alpha_composite(sweep)

    canvas = ImageDraw.Draw(image)

    # Two arcs on the right only, opening away from the sheet. Closed rings drew
    # a target; arcs read as range. They are sized to finish inside the safe
    # area - an earlier pair ran past it and the mask cut them off mid-curve,
    # which looks like a mistake rather than a crop.
    width = max(2, int(s * 0.023))
    # The inner arc is deliberately the shorter of the two: any wider and its
    # ends curl back behind the paper, leaving two stubs that read as a stray
    # mark rather than an arc.
    for factor, spread in ((0.92, 58), (0.64, 33)):
        r = radius * factor
        canvas.arc([origin_x - r, centre - r, origin_x + r, centre + r],
                   -spread, spread, fill=ARC + (255,), width=width)

    # The leading edge, drawn solid: it is the thing that says "now".
    edge = math.radians(-27)
    canvas.line(
        [origin_x, centre,
         origin_x + outer * math.cos(edge), centre + outer * math.sin(edge)],
        fill=SWEEP + (255,), width=max(2, int(s * 0.023)))

    # The notice, tilted a little so it looks caught rather than placed.
    sheet = _sheet(s, radius).rotate(9, resample=Image.BICUBIC, expand=False)
    image.alpha_composite(
        sheet,
        dest=(int(centre - sheet.size[0] / 2 - radius * 0.32),
              int(centre - sheet.size[1] / 2 + radius * 0.03)))

    # The catch: one notice, found. It sits where the leading edge meets the
    # outer arc, which is the only place in the picture that means "just now".
    #
    # The glow is a blurred disc, not concentric discs at low alpha. The first
    # attempt was the latter and amber over the dark ground came out as a muddy
    # brown ring - a fried egg rather than a light.
    ca = math.radians(-27)
    cx = origin_x + outer * math.cos(ca)
    cy = centre + outer * math.sin(ca)
    dot = radius * 0.088

    glow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse(
        [cx - dot * 1.9, cy - dot * 1.9, cx + dot * 1.9, cy + dot * 1.9],
        fill=CATCH + (110,))
    image.alpha_composite(glow.filter(ImageFilter.GaussianBlur(dot * 0.9)))

    ImageDraw.Draw(image).ellipse(
        [cx - dot, cy - dot, cx + dot, cy + dot], fill=CATCH + (255,))

    return image.resize((size, size), Image.LANCZOS)


def write(path, image):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    image.save(path)
    print("  ", path)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ui = os.path.dirname(here)

    write(os.path.join(here, "icon.png"), draw(1024))
    # Android adaptive foreground: the mask eats the edges, so the mark is
    # drawn smaller inside a transparent square and Android supplies the colour.
    write(os.path.join(here, "icon_foreground.png"), draw(1024, transparent=True, margin_ratio=0.26))

    for size in (192, 512):
        write(os.path.join(ui, "web", "icons", "Icon-%d.png" % size), draw(size))
        write(os.path.join(ui, "web", "icons", "Icon-maskable-%d.png" % size),
              draw(size, margin_ratio=0.20))
    write(os.path.join(ui, "web", "favicon.png"), draw(64))

    res = os.path.join(ui, "android", "app", "src", "main", "res")

    for folder, size in (("mipmap-mdpi", 48), ("mipmap-hdpi", 72), ("mipmap-xhdpi", 96),
                         ("mipmap-xxhdpi", 144), ("mipmap-xxxhdpi", 192)):
        write(os.path.join(res, folder, "ic_launcher.png"), draw(size))

    # The adaptive foreground, one per density. Android composes it over the
    # colour in values/colors.xml and then applies whatever mask the launcher
    # uses, so the mark is drawn smaller: the guaranteed safe zone is the middle
    # 66 of 108 dp and anything outside it can be cropped by a circle, a
    # squircle or a teardrop. Without these the launcher takes the square png
    # and shrinks it onto a plate, which is what made the old icon look pasted on.
    for folder, size in (("mipmap-mdpi", 108), ("mipmap-hdpi", 162), ("mipmap-xhdpi", 216),
                         ("mipmap-xxhdpi", 324), ("mipmap-xxxhdpi", 432)):
        # 0.17, not the 0.22 this started at. The mark is round, and a round
        # mark at 66% of the canvas sits inside every mask a launcher applies;
        # at 56% it was correct and timid, a stamp floating in its tile.
        write(os.path.join(res, folder, "ic_launcher_foreground.png"),
              draw(size, transparent=True, margin_ratio=0.17))

    ios = os.path.join(ui, "ios", "Runner", "Assets.xcassets", "AppIcon.appiconset")
    if os.path.isdir(ios):
        for name in os.listdir(ios):
            if not name.endswith(".png"):
                continue
            existing = Image.open(os.path.join(ios, name))
            # iOS icons carry no alpha; the deep background fills it.
            write(os.path.join(ios, name), draw(existing.size[0]).convert("RGB"))

    macos = os.path.join(ui, "macos", "Runner", "Assets.xcassets", "AppIcon.appiconset")
    if os.path.isdir(macos):
        for name in os.listdir(macos):
            if name.endswith(".png"):
                existing = Image.open(os.path.join(macos, name))
                write(os.path.join(macos, name), draw(existing.size[0]))

    icon = os.path.join(ui, "windows", "runner", "resources", "app_icon.ico")
    draw(256).save(icon, sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("  ", icon)


if __name__ == "__main__":
    main()
