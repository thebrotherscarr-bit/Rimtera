"""The image work behind the beauty and size pass, shared by beauty_pass.py and the ComfyUI nodes (nodes/rimtera_nodes.py).

- reference_sheet / animation_template: lay sprites out on a flat magenta grid for the model to redraw or animate.
- cut_sheet: splits a generated grid into cells by connected shapes, not by fixed cell borders, so a pose that leans
  over its cell edge stays whole and slivers of the neighbouring cell are not picked up.
- finish: one pixel density and one finish for every sprite: box-downscaled from the large generation to its exact
  game size, hard alpha, a small palette, stray pixels removed and a dark selective outline.
- frames_from_sheet: the 8 animation frames of a character, scaled by one factor to its base sprite, mapped to the
  base sprite's own colours, feet on one ground line and centred on the body, so frames match the still sprite.
"""
import numpy as np
from PIL import Image
from scipy import ndimage

from sprite_post import remove_background

MAGENTA = (255, 0, 255)
DENSITY = 40  # art pixels per tile: the game's largest zoom (TileSize 40), so sprites are only ever scaled down


def trim(image: Image.Image) -> Image.Image:
    box = image.getbbox()
    return image.crop(box) if box else image


def place_on_magenta(sprite: Image.Image, width: int, height: int, fill: float = 0.8, ground: int | None = None) -> Image.Image:
    """The sprite scaled up by a whole factor (pixels stay square) to about `fill` of the cell, centred, on magenta.
    With `ground`, its feet stand that many pixels above the bottom edge."""
    sprite = trim(sprite.convert("RGBA"))
    factor = max(1, int(min(width * fill / sprite.width, height * fill / sprite.height)))
    if sprite.width * factor > width or sprite.height * factor > height:  # larger than the cell even at 1x
        scale = min(width * fill / sprite.width, height * fill / sprite.height)
        big = sprite.resize((max(1, int(sprite.width * scale)), max(1, int(sprite.height * scale))), Image.BOX)
    else:
        big = sprite.resize((sprite.width * factor, sprite.height * factor), Image.NEAREST)
    canvas = Image.new("RGBA", (width, height), MAGENTA + (255,))
    top = height - ground - big.height if ground is not None else (height - big.height) // 2
    canvas.alpha_composite(big, ((width - big.width) // 2, max(0, top)))
    return canvas.convert("RGB")


def reference_sheet(sprites, columns: int, rows: int, width: int, height: int) -> Image.Image:
    """Old sprites in a grid (left to right, top to bottom), the reference a redraw keeps the designs of."""
    cell_w, cell_h = width // columns, height // rows
    sheet = Image.new("RGB", (width, height), MAGENTA)
    for index, sprite in enumerate(sprites):
        if sprite is not None:
            sheet.paste(place_on_magenta(sprite, cell_w, cell_h), ((index % columns) * cell_w, (index // columns) * cell_h))
    return sheet


def style_sheet(sprites, width: int = 768, height: int = 384) -> Image.Image:
    """The style anchors side by side: shows the model the look every redraw should share."""
    return reference_sheet(sprites, max(1, len(sprites)), 1, width, height)


def animation_template(sprite: Image.Image, width: int = 1024, height: int = 512) -> Image.Image:
    """The character 8 times in a 4x2 grid, same size and ground line in every cell: the model only changes poses,
    so frames stay on-model, on-grid and at one scale."""
    cell_w, cell_h = width // 4, height // 2
    cell = place_on_magenta(sprite, cell_w, cell_h, fill=0.72, ground=cell_h // 10)
    sheet = Image.new("RGB", (width, height), MAGENTA)
    for index in range(8):
        sheet.paste(cell, ((index % 4) * cell_w, (index // 4) * cell_h))
    return sheet


def cut_sheet(sheet: Image.Image, columns: int, rows: int, tolerance: int = 60, min_area: float = 0.0015):
    """Cells of a generated grid as trimmed RGBA images (None for an empty cell). Each connected shape goes to the
    cell holding its centre; specks smaller than `min_area` of a cell are dropped."""
    image = remove_background(sheet.convert("RGB"), tolerance)
    pixels = np.array(image)
    solid = pixels[:, :, 3] > 0
    # Join shapes a few pixels apart (a weapon tip, an antenna) so they travel with their body.
    labels, count = ndimage.label(ndimage.binary_dilation(solid, iterations=3), structure=np.ones((3, 3)))
    labels = np.where(solid, labels, 0)
    cell_w, cell_h = image.width / columns, image.height / rows
    cells = [np.zeros_like(solid) for _ in range(columns * rows)]
    for label, (ys, xs) in enumerate(ndimage.find_objects(labels), start=1):
        mask = labels[ys, xs] == label
        area = int(mask.sum())
        if area < min_area * cell_w * cell_h:
            continue
        cy, cx = ndimage.center_of_mass(mask)
        col = min(columns - 1, int((xs.start + cx) / cell_w))
        row = min(rows - 1, int((ys.start + cy) / cell_h))
        cells[row * columns + col][ys, xs] |= mask
    result = []
    for mask in cells:
        if not mask.any():
            result.append(None)
            continue
        cell = pixels.copy()
        cell[~mask] = 0
        result.append(trim(Image.fromarray(cell, "RGBA")))
    return result


def _hard_alpha(image: Image.Image) -> Image.Image:
    alpha = image.getchannel("A").point(lambda a: 255 if a >= 128 else 0)
    image = image.copy()
    image.putalpha(alpha)
    return image


def _quantize(image: Image.Image, colours: int, palette: Image.Image | None) -> Image.Image:
    alpha = image.getchannel("A")
    rgb = image.convert("RGB")
    if palette is not None:
        rgb = rgb.quantize(palette=palette, dither=Image.Dither.NONE)
    else:
        rgb = rgb.quantize(colors=colours, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    out = rgb.convert("RGBA")
    out.putalpha(alpha)
    return out


def _despeckle(image: Image.Image) -> Image.Image:
    """Drops lone opaque pixels with no opaque neighbour, and fills lone holes, left by downscaling."""
    pixels = np.array(image)
    solid = pixels[:, :, 3] > 0
    neighbours = ndimage.convolve(solid.astype(np.uint8), np.array([[1, 1, 1], [1, 0, 1], [1, 1, 1]]), mode="constant")
    pixels[solid & (neighbours == 0)] = 0
    return Image.fromarray(pixels, "RGBA")


def _outline(image: Image.Image) -> Image.Image:
    """A one-pixel selective outline: each transparent pixel touching the sprite takes a dark shade of the colour it
    touches. Edges that are already dark are left alone, so sprites drawn with outlines do not get a double line."""
    padded = Image.new("RGBA", (image.width + 2, image.height + 2), (0, 0, 0, 0))
    padded.alpha_composite(image, (1, 1))
    pixels = np.array(padded).astype(np.int32)
    solid = pixels[:, :, 3] > 0
    out = pixels.copy()
    h, w = solid.shape
    for y, x in zip(*np.nonzero(~solid)):
        touching = [pixels[ny, nx] for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1))
                    if 0 <= ny < h and 0 <= nx < w and solid[ny, nx]]
        if not touching:
            continue
        edge = min(touching, key=lambda c: int(c[0]) + int(c[1]) + int(c[2]))
        if 0.299 * edge[0] + 0.587 * edge[1] + 0.114 * edge[2] < 55:
            continue
        out[y, x] = (int(edge[0] * 0.28), int(edge[1] * 0.28), int(edge[2] * 0.34), 255)
    return trim(Image.fromarray(out.astype(np.uint8), "RGBA"))


def finish(sprite: Image.Image, max_width: int, max_height: int, colours: int = 32, outline: bool = True,
           palette: Image.Image | None = None) -> Image.Image:
    """A large generated sprite brought to game size: fitted in max_width x max_height with a box filter (averaging
    the model's soft pixels into clean ones), hard alpha, `colours` colours (or the given palette), outlined.
    The outline adds one pixel each side, so the art itself is fitted a pixel smaller."""
    sprite = trim(sprite.convert("RGBA"))
    inner_w, inner_h = max(1, max_width - (2 if outline else 0)), max(1, max_height - (2 if outline else 0))
    scale = min(inner_w / sprite.width, inner_h / sprite.height, 1.0)
    small = sprite.resize((max(1, round(sprite.width * scale)), max(1, round(sprite.height * scale))), Image.BOX)
    small = _despeckle(_quantize(_hard_alpha(small), colours, palette))
    return _outline(small) if outline else trim(small)


def palette_of(sprite: Image.Image) -> Image.Image:
    """The sprite's opaque colours as a palette image, for mapping its animation frames onto exactly those colours."""
    pixels = np.array(sprite.convert("RGBA")).reshape(-1, 4)
    colours = np.unique(pixels[pixels[:, 3] > 0][:, :3], axis=0)[:256]
    palette = Image.new("P", (1, 1))
    flat = [int(v) for colour in colours for v in colour]
    palette.putpalette(flat + flat[:3] * (256 - len(colours)))
    return palette


def body_anchor(sprite: Image.Image) -> int:
    """The x of the body's middle: the median opaque column of the bottom 40% (feet and body), so a swung weapon or
    a shot does not shift where a frame is centred."""
    solid = np.array(sprite.getchannel("A")) > 0
    low = solid[int(solid.shape[0] * 0.6):]
    xs = np.nonzero(low if low.any() else solid)[1]
    return int(np.median(xs)) if len(xs) else sprite.width // 2


def centre_on_body(sprites):
    """Pads each sprite to one canvas with the body anchor in the middle and the feet on the bottom row."""
    anchors = [body_anchor(sprite) for sprite in sprites]
    half = max(max(anchor, sprite.width - anchor) for sprite, anchor in zip(sprites, anchors))
    height = max(sprite.height for sprite in sprites)
    out = []
    for sprite, anchor in zip(sprites, anchors):
        canvas = Image.new("RGBA", (half * 2, height), (0, 0, 0, 0))
        canvas.alpha_composite(sprite, (half - anchor, height - sprite.height))
        out.append(canvas)
    return out


def frames_from_sheet(sheet: Image.Image, base: Image.Image, outline: bool = True):
    """The 8 frames (idle 1-2, move 1-4, attack 1-2) of a generated 4x2 sheet, matched to the finished base sprite:
    one scale for all (the median idle and move height becomes the base's height), the base's colours, its outline,
    and centred on the body. Missing cells are None."""
    cells = cut_sheet(sheet, 4, 2)
    present = [cell for cell in cells[:6] if cell is not None] or [cell for cell in cells if cell is not None]
    if not present:
        return [None] * 8
    base = trim(base.convert("RGBA"))
    art_height = base.height - (2 if outline else 0)
    scale = art_height / sorted(cell.height for cell in present)[len(present) // 2]
    palette = palette_of(base)
    frames = []
    for cell in cells:
        if cell is None:
            frames.append(None)
            continue
        size = (max(1, round(cell.width * scale)), max(1, round(cell.height * scale)))
        small = _despeckle(_quantize(_hard_alpha(cell.resize(size, Image.BOX)), 0, palette))
        frames.append(_outline(small) if outline else trim(small))
    return frames
