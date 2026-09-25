#!/usr/bin/env python3
"""
Generates Bloodfall's particle / decal textures (Client/Assets/Resources/Textures/VFX). All white-or-tinted sprites are
authored white so particle colour drives the tint; alpha carries the shape. Original, procedural.
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from uikit import out_path, rng, tile_noise, smoothstep


def save(img, name):
    p = out_path("Textures", "VFX", name + ".png")
    img.save(p, optimize=True)
    print("wrote", os.path.relpath(p))


def grid(S):
    ys, xs = np.mgrid[0:S, 0:S].astype(np.float32)
    c = (S - 1) / 2
    return (xs - c) / c, (ys - c) / c


def white(alpha):
    a = np.clip(alpha, 0, 1)
    H, W = a.shape
    return Image.fromarray(np.dstack([np.full((H, W, 3), 255, np.uint8), (a * 255).astype(np.uint8)]), "RGBA")


def colored(rgb, alpha):
    a = np.clip(alpha, 0, 1)
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 255).astype(np.uint8), (a * 255).astype(np.uint8)]), "RGBA")


def soft_glow(S=128):
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    save(white(np.exp(-d * d * 4.5) * smoothstep(1.0, 0.8, d)), "soft_glow")


def hard_dot(S=64):
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    save(white(smoothstep(1.0, 0.55, d) ** 1.5), "dot")


def spark(S=64):
    x, y = grid(S)
    a = np.exp(-(x * x) * 60) * np.exp(-(y * y) * 3) + np.exp(-(x * x + y * y) * 12) * 0.6
    save(white(a), "spark")


def ember(S=64):
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    core = np.exp(-d * d * 18)
    halo = np.exp(-d * d * 4) * 0.45
    rgb = np.dstack([np.full_like(d, 255), 200 * core + 120 * (1 - core), 120 * core + 40 * (1 - core)])
    save(colored(rgb, (core + halo) * smoothstep(1, 0.8, d)), "ember")


def smoke_sheet(S=128, n=4):
    """4x4 flipbook of billowing smoke puffs (white, alpha shape)."""
    img = Image.new("RGBA", (S * n, S * n), (0, 0, 0, 0))
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    for i in range(n * n):
        t = i / (n * n - 1)
        nz = tile_noise(S, S, 3 + t * 2, 5, seed=400 + i) * 0.65 + tile_noise(S, S, 7, 4, seed=500 + i) * 0.35
        r = 0.55 + 0.35 * t
        shape = smoothstep(r, r * 0.35, d + (nz - 0.5) * 0.7)
        a = shape * (0.85 - 0.45 * t) * (0.6 + 0.6 * nz)
        light = np.clip(0.75 + (-x - y) * 0.2 + (nz - 0.5) * 0.4, 0.4, 1)
        rgb = np.dstack([light * 255] * 3)
        tile = colored(rgb, a)
        img.paste(tile, ((i % n) * S, (i // n) * S))
    save(img, "smoke_sheet")


def ring(S=256, width=0.08, name="ring"):
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    a = np.exp(-((d - (1 - width * 1.5)) / width) ** 2)
    save(white(a), name)


def shockwave(S=256):
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    a = smoothstep(0.55, 0.92, d) * smoothstep(1.0, 0.92, d)
    nz = tile_noise(S, S, 6, 4, seed=77)
    save(white(a * (0.6 + 0.6 * nz)), "shockwave")


def selection_ring(S=256):
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    ang = np.arctan2(y, x)
    base = np.exp(-((d - 0.86) / 0.035) ** 2)
    ticks = (np.cos(ang * 4) > 0.92) * smoothstep(0.98, 0.9, d) * smoothstep(0.72, 0.8, d)
    inner = np.exp(-((d - 0.78) / 0.015) ** 2) * 0.5
    save(white(base + ticks * 0.9 + inner), "selection_ring")


def range_ring(S=512):
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    ang = np.arctan2(y, x)
    dash = (np.sin(ang * 48) > -0.3).astype(np.float32)
    a = np.exp(-((d - 0.985) / 0.008) ** 2) * dash + smoothstep(0.985, 0.6, d) * 0.08 * (d < 0.99)
    save(white(a), "range_ring")


def aoe_circle(S=256):
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    a = np.exp(-((d - 0.96) / 0.025) ** 2) + smoothstep(0.98, 0.2, d) * 0.22 * (d < 0.99)
    save(white(a), "aoe_circle")


def rune_circle(S=512):
    """Arcane sigil decal (used by casts, the Vharoth seals and shrines)."""
    s2 = S * 2
    img = Image.new("L", (s2, s2), 0)
    d = ImageDraw.Draw(img)
    c = s2 / 2
    for rr, w in ((0.97, 10), (0.9, 5), (0.62, 6), (0.55, 3), (0.2, 5)):
        R = rr * c
        d.ellipse([c - R, c - R, c + R, c + R], outline=255, width=w)
    # heptagram
    pts = [(c + math.cos(a) * 0.9 * c, c + math.sin(a) * 0.9 * c) for a in [i * 2 * math.pi / 7 - math.pi / 2 for i in range(7)]]
    for i in range(7):
        d.line([pts[i], pts[(i * 3) % 7]], fill=255, width=6)
        d.line([pts[i], pts[(i * 3 + 3) % 7]], fill=255, width=6)
    # glyph ticks between rings
    r = rng(9)
    for i in range(36):
        a = i * 2 * math.pi / 36
        r0, r1 = 0.63 * c, 0.88 * c
        x0, y0 = c + math.cos(a) * r0, c + math.sin(a) * r0
        mid = (r0 + r1) / 2
        if i % 3 == 0:
            d.line([(x0, y0), (c + math.cos(a) * r1, c + math.sin(a) * r1)], fill=200, width=3)
        else:
            gx, gy = c + math.cos(a) * mid, c + math.sin(a) * mid
            kind = r.integers(0, 3)
            sz = 14
            if kind == 0:
                d.ellipse([gx - sz / 2, gy - sz / 2, gx + sz / 2, gy + sz / 2], outline=230, width=3)
            elif kind == 1:
                d.polygon([(gx, gy - sz), (gx + sz * 0.7, gy + sz * 0.6), (gx - sz * 0.7, gy + sz * 0.6)], outline=230)
            else:
                d.line([(gx - sz, gy), (gx + sz, gy)], fill=230, width=3)
                d.line([(gx, gy - sz), (gx, gy + sz)], fill=230, width=3)
    img = img.filter(ImageFilter.GaussianBlur(1.2)).resize((S, S), Image.LANCZOS)
    a = np.asarray(img, np.float32) / 255
    glow = np.asarray(img.filter(ImageFilter.GaussianBlur(6)), np.float32) / 255
    save(white(np.clip(a + glow * 0.8, 0, 1)), "rune_circle")


def slash_arc(S=512):
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    ang = np.arctan2(y, x)
    sweep = smoothstep(-2.4, 0.4, ang) * smoothstep(1.2, 0.3, ang)
    band = np.exp(-((d - 0.78) / 0.09) ** 2)
    edge = np.exp(-((d - 0.86) / 0.025) ** 2)
    a = (band * 0.7 + edge) * sweep
    save(white(a), "slash_arc")


def beam(W=256, H=64):
    ys = np.linspace(-1, 1, H, dtype=np.float32)[:, None]
    nz = tile_noise(W, H, 8, 4, seed=66)
    core = np.exp(-(ys * ys) * 40)
    body = np.exp(-(ys * ys) * 6) * (0.4 + 0.6 * nz)
    save(white(np.broadcast_to(core + body * 0.7, (H, W))), "beam")


def lightning(W=256, H=1024, seed=3):
    img = Image.new("L", (W * 2, H * 2), 0)
    d = ImageDraw.Draw(img)
    r = rng(seed)

    def bolt(x, y, angle, length, width, depth):
        segs = int(length / 30)
        for _ in range(segs):
            nx = x + math.sin(angle) * 30 + (r.random() - 0.5) * 40
            ny = y + math.cos(angle) * 30
            d.line([(x, y), (nx, ny)], fill=255, width=max(1, int(width)))
            if depth > 0 and r.random() < 0.08:
                bolt(nx, ny, angle + (r.random() - 0.5) * 1.6, length * 0.35, width * 0.5, depth - 1)
            x, y = nx, ny
            angle += (r.random() - 0.5) * 0.3

    bolt(W, 0, 0, H * 2, 7, 3)
    core = np.asarray(img.resize((W, H), Image.LANCZOS), np.float32) / 255
    glow = np.asarray(img.filter(ImageFilter.GaussianBlur(14)).resize((W, H), Image.LANCZOS), np.float32) / 255
    save(white(np.clip(core * 1.3 + glow * 1.2, 0, 1)), "lightning")


def bat_sheet(S=128, frames=4):
    """Bat silhouette flap cycle (dark, used as alpha-blended particles)."""
    img = Image.new("RGBA", (S * frames, S), (0, 0, 0, 0))
    for f in range(frames):
        t = f / frames
        flap = math.sin(t * 2 * math.pi)
        L = Image.new("L", (S * 2, S * 2), 0)
        d = ImageDraw.Draw(L)
        cx, cy = S, S
        wing_y = -flap * 40
        for sgn in (-1, 1):
            pts = [(cx, cy - 6), (cx + sgn * 30, cy - 18 + wing_y * 0.5), (cx + sgn * 70, cy - 30 + wing_y), (cx + sgn * 110, cy - 10 + wing_y * 1.1),
                   (cx + sgn * 92, cy + 4 + wing_y * 0.8), (cx + sgn * 78, cy - 4 + wing_y * 0.7), (cx + sgn * 64, cy + 12 + wing_y * 0.6),
                   (cx + sgn * 48, cy + 2 + wing_y * 0.4), (cx + sgn * 34, cy + 16 + wing_y * 0.3), (cx, cy + 14)]
            d.polygon(pts, fill=255)
        d.ellipse([cx - 14, cy - 18, cx + 14, cy + 22], fill=255)
        d.polygon([(cx - 10, cy - 14), (cx - 14, cy - 30), (cx - 4, cy - 18)], fill=255)
        d.polygon([(cx + 10, cy - 14), (cx + 14, cy - 30), (cx + 4, cy - 18)], fill=255)
        a = np.asarray(L.filter(ImageFilter.GaussianBlur(1.2)).resize((S, S), Image.LANCZOS), np.float32) / 255
        rgb = np.dstack([np.full_like(a, 18), np.full_like(a, 10), np.full_like(a, 14)])
        img.paste(colored(rgb, a), (f * S, 0))
    save(img, "bat_sheet")


def blood_sheet(S=128, n=2):
    img = Image.new("RGBA", (S * n, S * n), (0, 0, 0, 0))
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    for i in range(n * n):
        nz = tile_noise(S, S, 6, 5, seed=600 + i)
        r = rng(700 + i)
        a = smoothstep(0.55, 0.3, d + (nz - 0.5) * 0.6)
        # droplets
        drops = np.zeros_like(a)
        for _ in range(14):
            ang = r.random() * 2 * math.pi
            dist = 0.45 + r.random() * 0.45
            rad = 0.03 + r.random() * 0.06
            px, py = math.cos(ang) * dist, math.sin(ang) * dist
            drops = np.maximum(drops, smoothstep(rad, rad * 0.4, np.sqrt((x - px) ** 2 + (y - py) ** 2)))
        a = np.maximum(a, drops)
        shade_ = 0.65 + 0.35 * nz
        rgb = np.dstack([150 * shade_, 10 * shade_, 18 * shade_])
        img.paste(colored(rgb, a), ((i % n) * S, (i // n) * S))
    save(img, "blood_sheet")


def blob_shadow(S=128):
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    a = smoothstep(1.0, 0.1, d) ** 1.6 * 0.85
    save(colored(np.zeros((S, S, 3)), a), "blob_shadow")


def dust(S=128):
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    nz = tile_noise(S, S, 4, 5, seed=88)
    a = smoothstep(1.0, 0.2, d + (nz - 0.5) * 0.5) * 0.8
    save(white(a), "dust")


def orb(S=128):
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    core = smoothstep(0.35, 0.15, d)
    halo = np.exp(-d * d * 5)
    ang = np.arctan2(y, x)
    swirl = (np.sin(ang * 3 + d * 12) * 0.5 + 0.5) * smoothstep(0.8, 0.3, d) * 0.5
    save(white(np.clip(core + halo * 0.6 + swirl * 0.4, 0, 1)), "orb")


def cross_flare(S=256):
    x, y = grid(S)
    a = np.exp(-(x * x) * 400) * np.exp(-(y * y) * 1.5) + np.exp(-(y * y) * 400) * np.exp(-(x * x) * 1.5)
    d = np.sqrt(x * x + y * y)
    a += np.exp(-d * d * 30)
    save(white(np.clip(a, 0, 1)), "flare")


def fog_puff(S=256):
    x, y = grid(S)
    d = np.sqrt(x * x + y * y)
    nz = tile_noise(S, S, 3, 6, seed=91) * 0.7 + tile_noise(S, S, 8, 4, seed=92) * 0.3
    a = smoothstep(1.0, 0.1, d) * smoothstep(0.25, 0.75, nz) * 0.9
    save(white(a), "fog_puff")


def ground_crack(S=512):
    """Glowing fissure decal (Vharoth eruptions, Vorak's ultimate impact)."""
    img = Image.new("L", (S * 2, S * 2), 0)
    d = ImageDraw.Draw(img)
    r = rng(17)
    c = S

    def crack(x, y, ang, length, w, depth):
        steps = int(length / 16)
        for _ in range(steps):
            nx = x + math.cos(ang) * 16
            ny = y + math.sin(ang) * 16
            d.line([(x, y), (nx, ny)], fill=255, width=max(1, int(w)))
            x, y = nx, ny
            ang += (r.random() - 0.5) * 0.5
            w *= 0.97
            if depth > 0 and r.random() < 0.1:
                crack(x, y, ang + (r.random() - 0.5) * 1.8, length * 0.4, w * 0.7, depth - 1)

    for i in range(7):
        crack(c, c, i * 2 * math.pi / 7 + r.random() * 0.4, S * 0.9, 14, 2)
    core = np.asarray(img.resize((S, S), Image.LANCZOS), np.float32) / 255
    glow = np.asarray(img.filter(ImageFilter.GaussianBlur(10)).resize((S, S), Image.LANCZOS), np.float32) / 255
    save(white(np.clip(core + glow * 0.9, 0, 1)), "ground_crack")


def arrow_indicator(W=128, H=512):
    xs = np.linspace(-1, 1, W, dtype=np.float32)[None, :]
    ys = np.linspace(0, 1, H, dtype=np.float32)[:, None]
    body = (np.abs(xs) < 0.55) * (ys > 0.18)
    head = (np.abs(xs) < (0.18 - ys) / 0.18 * 1.0 + 0.0) * (ys <= 0.18)
    edge = (np.abs(np.abs(xs) - 0.52) < 0.04) * (ys > 0.18)
    a = body * 0.25 + head * 0.7 + edge * 0.8
    save(white(np.clip(a, 0, 1)), "line_indicator")


def main():
    soft_glow(); hard_dot(); spark(); ember(); smoke_sheet(); ring(); ring(256, 0.03, "ring_thin"); shockwave()
    selection_ring(); range_ring(); aoe_circle(); rune_circle(); slash_arc(); beam(); lightning(); bat_sheet()
    blood_sheet(); blob_shadow(); dust(); orb(); cross_flare(); fog_puff(); ground_crack(); arrow_indicator()


if __name__ == "__main__":
    main()
