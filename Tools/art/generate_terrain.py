#!/usr/bin/env python3
"""
Tileable terrain layer textures for the Velmoragh terrain shader (Client/Assets/Resources/Textures/Terrain).
RGB = albedo (sRGB), A = height (used for height blending and per-pixel bump). 512x512, seamless.
Layer order matches the splat maps written by Tools/mapgen/generate_velmoragh.py:
  0 grass/moss, 1 dirt/mud, 2 cobblestone road, 3 rock/cliff, 4 blood mud, 5 bone/ash, 6 leaf litter, 7 sun paving
"""
import os

import numpy as np
from PIL import Image
from scipy.spatial import cKDTree

from uikit import out_path, rng, tile_noise, normalize, smoothstep

S = 512


def save(rgb, height, name):
    rgb = np.clip(rgb, 0, 255).astype(np.uint8)
    a = (np.clip(height, 0, 1) * 255).astype(np.uint8)
    p = out_path("Textures", "Terrain", name + ".png")
    Image.fromarray(np.dstack([rgb, a]), "RGBA").save(p, optimize=True)
    print("wrote", os.path.relpath(p))


def lerp_col(a, b, t):
    a = np.asarray(a, np.float32)
    b = np.asarray(b, np.float32)
    return a[None, None, :] * (1 - t[..., None]) + b[None, None, :] * t[..., None]


def voronoi(points_n, seed, jitter=1.0):
    """Periodic Voronoi: returns (F1, F2, cell id) over an SxS tile."""
    r = rng(seed)
    g = int(np.sqrt(points_n))
    base = np.stack(np.meshgrid(np.arange(g), np.arange(g)), -1).reshape(-1, 2).astype(np.float32)
    pts = (base + 0.5 + (r.random(base.shape) - 0.5) * jitter) / g * S
    pts %= S
    tree = cKDTree(pts, boxsize=S)
    ys, xs = np.mgrid[0:S, 0:S].astype(np.float32) + 0.5
    d, idx = tree.query(np.stack([xs.ravel(), ys.ravel()], -1), k=2)
    return d[:, 0].reshape(S, S), d[:, 1].reshape(S, S), idx[:, 0].reshape(S, S)


def grass():
    n1 = tile_noise(S, S, 6, 6, seed=1001)
    n2 = tile_noise(S, S, 32, 3, seed=1002)
    blades = tile_noise(S, S, 96, 2, seed=1003)
    t = smoothstep(0.35, 0.7, n1)
    col = lerp_col((46, 50, 30), (70, 66, 38), t)
    col = col * (0.75 + 0.5 * blades[..., None]) * (0.85 + 0.3 * n2[..., None])
    # dead patches
    dead = smoothstep(0.62, 0.75, tile_noise(S, S, 4, 4, seed=1004))
    col = col * (1 - dead[..., None]) + lerp_col((78, 64, 44), (96, 80, 56), n2) * dead[..., None]
    save(col, 0.35 + 0.4 * blades * n1, "grass")


def dirt():
    n1 = tile_noise(S, S, 5, 6, seed=1101)
    n2 = tile_noise(S, S, 24, 4, seed=1102)
    pebbles_f1, _, pid = voronoi(900, 1103, 0.9)
    peb = smoothstep(4.5, 2.0, pebbles_f1) * (rng(1104).random(pid.max() + 1)[pid] > 0.6)
    col = lerp_col((58, 42, 32), (84, 64, 46), n1) * (0.8 + 0.35 * n2[..., None])
    col = col * (1 - peb[..., None] * 0.4) + np.array([96, 88, 80])[None, None, :] * peb[..., None] * 0.4
    save(col, 0.3 + 0.3 * n2 + 0.35 * peb, "dirt")


def cobble():
    f1, f2, cid = voronoi(144, 1201, 0.7)
    edge = f2 - f1
    stone = smoothstep(1.5, 6.0, edge)
    r = rng(1202)
    tint = r.random((cid.max() + 1, 1)) * 26 * np.array([[1.0, 0.96, 0.9]])
    n = tile_noise(S, S, 16, 4, seed=1203)
    base = np.array([70, 66, 64], np.float32)[None, None, :] + tint[cid] - 13
    col = base * (0.7 + 0.45 * n[..., None])
    mortar = np.array([38, 32, 28], np.float32)
    col = col * stone[..., None] + mortar[None, None, :] * (1 - stone[..., None])
    dome = np.clip(edge / 18.0, 0, 1) ** 0.5
    moss = smoothstep(0.6, 0.8, tile_noise(S, S, 6, 4, seed=1204)) * (1 - stone)
    col = col * (1 - moss[..., None] * 0.6) + np.array([44, 52, 30])[None, None, :] * moss[..., None] * 0.6
    save(col, 0.15 + 0.8 * dome * stone, "cobble")


def rock():
    n1 = tile_noise(S, S, 4, 7, seed=1301)
    ys = np.linspace(0, 1, S, endpoint=False)[:, None]
    strata = (np.sin((ys * 9 + n1 * 1.8) * np.pi * 2) * 0.5 + 0.5) ** 2
    cracks_f1, cracks_f2, _ = voronoi(16, 1302, 1.0)
    crack = smoothstep(1.6, 0.3, cracks_f2 - cracks_f1) * smoothstep(0.45, 0.6, tile_noise(S, S, 6, 3, seed=1304))
    n2 = tile_noise(S, S, 20, 4, seed=1303)
    col = lerp_col((56, 52, 54), (92, 86, 84), strata * 0.6 + n2 * 0.4)
    col *= (1 - crack[..., None] * 0.6)
    save(col, np.clip(0.3 + 0.5 * strata * n1 + 0.3 * n2 - crack * 0.4, 0, 1), "rock")


def blood_mud():
    n1 = tile_noise(S, S, 5, 6, seed=1401)
    n2 = tile_noise(S, S, 18, 4, seed=1402)
    pools = smoothstep(0.55, 0.7, n1)
    col = lerp_col((52, 22, 20), (74, 30, 26), n2)
    col = col * (1 - pools[..., None]) + np.array([70, 8, 12])[None, None, :] * pools[..., None]
    save(col, 0.5 - pools * 0.35 + n2 * 0.2, "blood_mud")


def ash():
    n1 = tile_noise(S, S, 7, 6, seed=1501)
    n2 = tile_noise(S, S, 40, 3, seed=1502)
    col = lerp_col((70, 68, 66), (104, 100, 96), n1) * (0.85 + 0.25 * n2[..., None])
    # bone fragments
    r = rng(1503)
    img = np.zeros((S, S), np.float32)
    for _ in range(55):
        cx, cy = r.random(2) * S
        ang = r.random() * np.pi
        L = 4 + r.random() * 11
        w = 1.5 + r.random() * 2.5
        ys, xs = np.mgrid[0:S, 0:S].astype(np.float32)
        dx = (xs - cx + S / 2) % S - S / 2
        dy = (ys - cy + S / 2) % S - S / 2
        u = dx * np.cos(ang) + dy * np.sin(ang)
        v = -dx * np.sin(ang) + dy * np.cos(ang)
        bone = smoothstep(1.0, 0.0, np.maximum(np.abs(u) - L, 0) / 2 + np.maximum(np.abs(v) - w, 0))
        img = np.maximum(img, bone)
    col = col * (1 - img[..., None]) + np.array([196, 186, 164])[None, None, :] * img[..., None]
    save(col, 0.3 + 0.2 * n2 + 0.5 * img, "ash")


def leaves():
    r = rng(1601)
    n = tile_noise(S, S, 10, 4, seed=1602)
    col = lerp_col((54, 36, 24), (70, 48, 30), n)
    height = 0.2 + 0.2 * n
    ys, xs = np.mgrid[0:S, 0:S].astype(np.float32)
    palette = np.array([[120, 58, 26], [140, 88, 34], [96, 40, 22], [110, 96, 44], [78, 30, 20]], np.float32)
    for i in range(380):
        cx, cy = r.random(2) * S
        ang = r.random() * np.pi
        L = 5 + r.random() * 7
        W = L * 0.45
        x0, y0 = int(cx - 16), int(cy - 16)
        sub_y = (np.arange(y0, y0 + 32) % S)
        sub_x = (np.arange(x0, x0 + 32) % S)
        yy, xx = np.meshgrid(np.arange(y0, y0 + 32), np.arange(x0, x0 + 32), indexing="ij")
        dx, dy = xx - cx, yy - cy
        u = dx * np.cos(ang) + dy * np.sin(ang)
        v = -dx * np.sin(ang) + dy * np.cos(ang)
        leaf = smoothstep(1.0, 0.7, (u / L) ** 2 + (v / W) ** 2)
        c = palette[r.integers(0, len(palette))] * (0.8 + 0.4 * r.random())
        region = np.ix_(sub_y, sub_x)
        col[region] = col[region] * (1 - leaf[..., None]) + c[None, None, :] * leaf[..., None]
        height[region] = np.maximum(height[region], leaf * (0.5 + 0.1 * (i / 380)))
    save(col, height, "leaves")


def paving():
    # Large offset slabs (running bond), sun-bleached with cracks.
    ys, xs = np.mgrid[0:S, 0:S].astype(np.float32)
    rows = 4
    rh = S / rows
    row = np.floor(ys / rh)
    off = (row % 2) * (S / 8)
    cols = 4
    cw = S / cols
    lx = (xs + off) % cw
    ly = ys % rh
    gap = 3.0
    ex = np.minimum(lx, cw - lx)
    ey = np.minimum(ly, rh - ly)
    slab = smoothstep(gap * 0.5, gap * 1.8, np.minimum(ex, ey))
    cid = (row * cols + np.floor(((xs + off) % S) / cw)).astype(int)
    tint = rng(1701).random((cid.max() + 1, 1)) * 20
    n = tile_noise(S, S, 12, 5, seed=1702)
    base = np.array([150, 140, 118], np.float32)[None, None, :] + tint[cid] - 10
    col = base * (0.78 + 0.3 * n[..., None])
    _, f2c, _ = voronoi(49, 1703, 1.0)
    f1c, _, _ = voronoi(49, 1703, 1.0)
    crack = smoothstep(1.4, 0.2, f2c - f1c) * (tile_noise(S, S, 5, 3, seed=1704) > 0.55)
    col *= (1 - crack[..., None] * 0.5)
    col = col * slab[..., None] + np.array([60, 54, 44])[None, None, :] * (1 - slab[..., None])
    save(col, 0.2 + 0.6 * slab - crack * 0.2 + 0.1 * n, "paving")


def main():
    grass(); dirt(); cobble(); rock(); blood_mud(); ash(); leaves(); paving()


if __name__ == "__main__":
    main()
