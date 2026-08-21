# -*- coding: utf-8 -*-
"""Draws the İhale Radar mark and writes every size each platform wants.

One script rather than a folder of exported pngs, so the mark can be adjusted
and every platform regenerated from the same source. Run it from ui/:

    python branding/make_icon.py

Design notes, because they are decisions and not taste:
 * The sweep is a filled wedge fading to nothing, not a line - at 48 px a line
   disappears and the wedge still reads as motion.
 * Three rings, not five. Five turn into grey mush below 64 px.
 * The catch (one bright dot) sits off-centre on the leading edge of the sweep,
   which is what makes the picture a radar rather than a target.
 * Nothing is drawn in the outer 8% - Android's round and squircle masks crop
   there, and a ring clipped by the mask looks like a mistake.
"""
import math
import os
from PIL import Image, ImageDraw

BACKGROUND = (11, 31, 42)      # deep slate, so the mark holds on light and dark
RING = (39, 87, 105)
SWEEP = (56, 214, 168)         # the interface's own accent green
CATCH = (255, 214, 92)

SUPERSAMPLE = 8


def draw(size, transparent=False, margin_ratio=0.08):
    s = size * SUPERSAMPLE
    image = Image.new("RGBA", (s, s), (0, 0, 0, 0) if transparent else BACKGROUND + (255,))
    canvas = ImageDraw.Draw(image)

    centre = s / 2
    usable = s * (1 - 2 * margin_ratio)
    radius = usable / 2

    # The sweep first, so the rings sit on top of it and stay legible.
    sweep = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    pen = ImageDraw.Draw(sweep)
    start, span = -80.0, 62.0
    steps = 44
    for i in range(steps):
        a0 = start + span * i / steps
        a1 = start + span * (i + 1) / steps + 0.6
        alpha = int(150 * (i / steps) ** 2)
        pen.pieslice(
            [centre - radius, centre - radius, centre + radius, centre + radius],
            a0, a1, fill=SWEEP + (alpha,))
    image.alpha_composite(sweep)

    # The leading edge, drawn solid: it is the thing that says "now".
    edge = math.radians(-18)
    canvas.line(
        [centre, centre,
         centre + radius * math.cos(edge), centre + radius * math.sin(edge)],
        fill=SWEEP + (255,), width=max(2, int(s * 0.022)))

    width = max(2, int(s * 0.018))
    for factor in (1.0, 0.66, 0.33):
        r = radius * factor
        canvas.ellipse([centre - r, centre - r, centre + r, centre + r],
                       outline=RING + (255,), width=width)

    # The catch: one notice, found.
    ca, cr = math.radians(-30), radius * 0.62
    dot = radius * 0.115
    cx, cy = centre + cr * math.cos(ca), centre + cr * math.sin(ca)
    canvas.ellipse([cx - dot * 2.1, cy - dot * 2.1, cx + dot * 2.1, cy + dot * 2.1],
                   fill=CATCH + (60,))
    canvas.ellipse([cx - dot, cy - dot, cx + dot, cy + dot], fill=CATCH + (255,))

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

    for folder, size in (("mipmap-mdpi", 48), ("mipmap-hdpi", 72), ("mipmap-xhdpi", 96),
                         ("mipmap-xxhdpi", 144), ("mipmap-xxxhdpi", 192)):
        write(os.path.join(ui, "android", "app", "src", "main", "res", folder, "ic_launcher.png"),
              draw(size))

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
