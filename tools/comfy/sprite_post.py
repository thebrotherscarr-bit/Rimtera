"""Turns a generated image into a game sprite: flat background removed, trimmed, downscaled to pixel size.

Generations are prompted onto a flat magenta (or any flat) background. The background colour is sampled from
the border, flood-filled from the edges within a tolerance (so interior pixels of the same colour survive),
then the subject is trimmed, scaled so its longest side is `size` pixels with a box filter (averaging the
model's soft "fake pixels" into real ones), quantised to a small palette, and alpha is made hard.
"""
from collections import deque

from PIL import Image


def _border_colour(image: Image.Image):
    pixels = image.load()
    w, h = image.size
    samples = [pixels[x, y] for x in range(0, w, max(1, w // 32)) for y in (0, h - 1)]
    samples += [pixels[x, y] for y in range(0, h, max(1, h // 32)) for x in (0, w - 1)]
    samples.sort(key=lambda c: (c[0], c[1], c[2]))
    return samples[len(samples) // 2][:3]


def remove_background(image: Image.Image, tolerance=60) -> Image.Image:
    image = image.convert("RGBA")
    w, h = image.size
    pixels = image.load()
    bg = _border_colour(image)

    def near(c):
        return abs(c[0] - bg[0]) + abs(c[1] - bg[1]) + abs(c[2] - bg[2]) <= tolerance

    seen = bytearray(w * h)
    queue = deque()
    for x in range(w):
        queue.extend(((x, 0), (x, h - 1)))
    for y in range(h):
        queue.extend(((0, y), (w - 1, y)))
    while queue:
        x, y = queue.popleft()
        index = y * w + x
        if seen[index]:
            continue
        seen[index] = 1
        if not near(pixels[x, y]):
            continue
        pixels[x, y] = (0, 0, 0, 0)
        if x > 0: queue.append((x - 1, y))
        if x < w - 1: queue.append((x + 1, y))
        if y > 0: queue.append((x, y - 1))
        if y < h - 1: queue.append((x, y + 1))
    # Soften the magenta fringe left on anti-aliased edges: bright magenta anywhere, and darker magenta-hued
    # pixels where they touch transparency (repeated so two-pixel fringes go too). Interior magenta, such as a
    # magenta creature's body, is kept.
    for y in range(h):
        for x in range(w):
            r, g, b, a = pixels[x, y]
            if a and r > 180 and b > 180 and g < 110:
                pixels[x, y] = (0, 0, 0, 0)

    def magenta_hued(c):
        r, g, b, _ = c
        return r - g > 50 and b - g > 50 and abs(r - b) < 90

    for _ in range(2):
        edge = []
        for y in range(h):
            for x in range(w):
                c = pixels[x, y]
                if not c[3] or not magenta_hued(c):
                    continue
                if any(not (0 <= nx < w and 0 <= ny < h) or pixels[nx, ny][3] == 0
                       for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1))):
                    edge.append((x, y))
        for x, y in edge:
            pixels[x, y] = (0, 0, 0, 0)
    return image


def to_sprite(image: Image.Image, size=48, colours=24, tolerance=60) -> Image.Image:
    image = remove_background(image, tolerance)
    box = image.getbbox()
    if box:
        image = image.crop(box)
    scale = size / max(image.size)
    target = (max(1, round(image.width * scale)), max(1, round(image.height * scale)))
    small = image.resize(target, Image.BOX)
    alpha = small.getchannel("A").point(lambda a: 255 if a >= 128 else 0)
    rgb = small.convert("RGB").quantize(colors=colours, method=Image.Quantize.MEDIANCUT).convert("RGB")
    sprite = rgb.convert("RGBA")
    sprite.putalpha(alpha)
    box = sprite.getbbox()
    return sprite.crop(box) if box else sprite


if __name__ == "__main__":
    import sys
    source, out = sys.argv[1], sys.argv[2]
    size = int(sys.argv[3]) if len(sys.argv) > 3 else 48
    to_sprite(Image.open(source), size).save(out)
    print("wrote", out)
