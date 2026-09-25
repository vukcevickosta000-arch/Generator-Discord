"""
Shared helpers for Bloodfall's procedural 2D art: tileable noise, embossed metal (height-field lighting),
gothic ornaments, glows. Everything is rendered supersampled and downscaled for clean edges.
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RES = os.path.join(ROOT, "Client", "Assets", "Resources")
FONTS = os.path.join(RES, "Fonts")

# Palette (sRGB 0-255)
OBSIDIAN = (14, 11, 13)
IRON = (46, 44, 48)
IRON_DARK = (24, 22, 25)
BRONZE = (156, 112, 58)
BRONZE_LIGHT = (214, 170, 102)
GOLD = (226, 186, 110)
CRIMSON = (142, 16, 28)
BLOOD = (98, 8, 16)
EMBER = (255, 96, 40)
BONE = (214, 204, 184)


def out_path(*parts):
    p = os.path.join(RES, *parts)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    return p


def rng(seed):
    return np.random.default_rng(seed)


def tile_noise(w, h, scale, octaves=4, seed=0, persistence=0.5):
    """Tileable value-noise fBm in [0,1] (wraps on both axes)."""
    r = rng(seed)
    out = np.zeros((h, w), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        cells = max(1, int(round(scale * (2 ** o))))
        g = r.random((cells, cells)).astype(np.float32)
        # bicubic-ish upsample with wrap
        ys = np.linspace(0, cells, h, endpoint=False)
        xs = np.linspace(0, cells, w, endpoint=False)
        y0 = np.floor(ys).astype(int); x0 = np.floor(xs).astype(int)
        fy = ys - y0; fx = xs - x0
        fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
        y1 = (y0 + 1) % cells; x1 = (x0 + 1) % cells
        y0 %= cells; x0 %= cells
        a = g[np.ix_(y0, x0)]; b = g[np.ix_(y0, x1)]; c = g[np.ix_(y1, x0)]; d = g[np.ix_(y1, x1)]
        top = a + (b - a) * fx[None, :]
        bot = c + (d - c) * fx[None, :]
        out += (top + (bot - top) * fy[:, None]) * amp
        total += amp
        amp *= persistence
    return out / total


def normalize(a):
    lo, hi = float(a.min()), float(a.max())
    return (a - lo) / max(1e-6, hi - lo)


def shade(height, strength=4.0, light=(-0.55, -0.65, 0.55)):
    """Lambert + spec lighting of a height field. Returns (diffuse, specular) in [0,1]."""
    gy, gx = np.gradient(height.astype(np.float32))
    nx, ny, nz = -gx * strength, -gy * strength, np.ones_like(gx)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    nx, ny, nz = nx / ln, ny / ln, nz / ln
    lx, ly, lz = light
    ll = math.sqrt(lx * lx + ly * ly + lz * lz)
    lx, ly, lz = lx / ll, ly / ll, lz / ll
    diff = np.clip(nx * lx + ny * ly + nz * lz, 0, 1)
    # Blinn spec with view straight on.
    hx, hy, hz = lx, ly, lz + 1
    hl = math.sqrt(hx * hx + hy * hy + hz * hz)
    spec = np.clip(nx * hx / hl + ny * hy / hl + nz * hz / hl, 0, 1) ** 40
    return diff, spec


def rounded_rect_sdf(w, h, x0, y0, x1, y1, r):
    """Signed distance (px) to a rounded rectangle; negative inside."""
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32) + 0.5
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    hx, hy = (x1 - x0) / 2 - r, (y1 - y0) / 2 - r
    qx = np.abs(xs - cx) - hx
    qy = np.abs(ys - cy) - hy
    outside = np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2)
    inside = np.minimum(np.maximum(qx, qy), 0)
    return outside + inside - r


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def to_image(rgb, alpha):
    rgb = np.clip(rgb, 0, 255).astype(np.uint8)
    a = np.clip(alpha * 255, 0, 255).astype(np.uint8)
    return Image.fromarray(np.dstack([rgb, a]), "RGBA")


def colorize(base, diff, spec, ambient=0.35, spec_color=(255, 230, 190), spec_amount=0.6):
    base = np.asarray(base, np.float32)
    if base.ndim == 1:
        base = base[None, None, :]
    lit = base * (ambient + (1 - ambient) * diff[..., None] * 1.25)
    lit += np.asarray(spec_color, np.float32)[None, None, :] * spec[..., None] * spec_amount
    return lit


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size)


def glow(img, radius, color, strength=1.0):
    """Adds an outer glow around the alpha of img."""
    a = img.split()[-1]
    g = a.filter(ImageFilter.GaussianBlur(radius))
    ga = np.asarray(g, np.float32) / 255.0 * strength
    layer = np.zeros((img.height, img.width, 4), np.float32)
    layer[..., :3] = color
    layer[..., 3] = np.clip(ga, 0, 1) * 255
    base = Image.fromarray(layer.astype(np.uint8), "RGBA")
    return Image.alpha_composite(base, img)


def downscale(img, factor):
    return img.resize((img.width // factor, img.height // factor), Image.LANCZOS)
