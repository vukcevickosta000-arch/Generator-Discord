#!/usr/bin/env python3
"""
Generates the layered 2.5D menu backdrop for Bloodfall (Client/Assets/Resources/Textures/Backdrop):
night sky with stars, blood moon, drifting cloud bands, far mountains, the ruined castle of Velmoragh (with a separate
window-glow layer the shader flickers), a near ridge of ruins/dead trees/graves, and a tileable fog sheet.

The client places each layer on a quad at a different depth and sways the camera slightly, producing parallax.
Original artwork, generated procedurally - no external assets.
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from uikit import out_path, rng, tile_noise, normalize, smoothstep

SS = 2  # supersampling for silhouettes


def save(img, name):
    p = out_path("Textures", "Backdrop", name + ".png")
    img.save(p, optimize=True)
    print("wrote", os.path.relpath(p))


def fbm1d(n, seed, octaves=6, base=4):
    r = rng(seed)
    out = np.zeros(n, np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        cells = base * 2 ** o
        g = r.random(cells + 1).astype(np.float32)
        xs = np.linspace(0, cells, n, endpoint=False)
        i = np.floor(xs).astype(int)
        f = xs - i
        f = f * f * (3 - 2 * f)
        out += (g[i] + (g[i + 1] - g[i]) * f) * amp
        tot += amp
        amp *= 0.5
    return out / tot


# ---------------------------------------------------------------------------------------------------------------- sky

def make_sky(W=2048, H=1024):
    y = np.linspace(0, 1, H, dtype=np.float32)[:, None]
    x = np.linspace(0, 1, W, dtype=np.float32)[None, :]
    # zenith -> horizon gradient: near-black violet to a smouldering crimson band near the horizon.
    zen = np.array([6, 5, 12], np.float32)
    mid = np.array([22, 10, 26], np.float32)
    hor = np.array([92, 22, 26], np.float32)
    t = y ** 1.6
    col = zen * (1 - t[..., None]) + mid * t[..., None]
    band = smoothstep(0.55, 1.0, y)[..., None]
    col = col * (1 - band) + hor * band
    col = np.broadcast_to(col, (H, W, 3)).copy()
    # moon glow (moon itself is a separate sprite) at 0.72, 0.28
    mx, my = 0.72, 0.28
    d = np.sqrt(((x - mx) * 2.0) ** 2 + (y - my) ** 2)
    halo = np.exp(-d * 5.5)[..., None]
    col += np.array([120, 30, 28], np.float32) * halo * 0.9
    col += np.array([60, 20, 30], np.float32) * np.exp(-d * 2.0)[..., None] * 0.5
    # nebula wisps
    neb = tile_noise(W, H, 3, 6, seed=11)
    neb2 = tile_noise(W, H, 6, 5, seed=12)
    wisps = smoothstep(0.52, 0.85, neb * 0.7 + neb2 * 0.3) * (1 - y) * 0.9
    col += np.array([46, 20, 52], np.float32) * wisps[..., None]
    # stars (fewer near horizon and moon)
    r = rng(5)
    stars = np.zeros((H, W), np.float32)
    n = 2600
    sx = r.integers(0, W, n)
    sy = (r.random(n) ** 1.7 * H * 0.8).astype(int)
    mag = r.random(n) ** 6
    for X, Y, m in zip(sx, sy, mag):
        stars[Y, X] = max(stars[Y, X], 0.25 + m * 1.6)
    big = Image.fromarray((np.clip(stars, 0, 1) * 255).astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(0.7))
    s = np.asarray(big, np.float32) / 255.0 * 2.2 + stars * 0.6
    s *= (1 - np.clip(halo[..., 0] * 1.4, 0, 1))
    col += np.array([200, 205, 230], np.float32) * np.clip(s, 0, 1.2)[..., None]
    # subtle dithering noise to avoid banding
    col += (r.random((H, W, 1)) - 0.5) * 3
    save(Image.fromarray(np.clip(col, 0, 255).astype(np.uint8), "RGB"), "sky")


def make_moon(S=512):
    ys, xs = np.mgrid[0:S, 0:S].astype(np.float32)
    c = S / 2
    r = S * 0.34
    dx, dy = (xs - c) / r, (ys - c) / r
    d2 = dx * dx + dy * dy
    disk = d2 <= 1
    nz = np.sqrt(np.clip(1 - d2, 0, 1))
    # craters / maria from noise
    tex = tile_noise(S, S, 5, 6, seed=31)
    tex2 = tile_noise(S, S, 14, 4, seed=32)
    maria = smoothstep(0.45, 0.65, tex) * 0.35 + tex2 * 0.15
    light = np.clip(-dx * 0.35 - dy * 0.25 + nz * 0.9, 0, 1)
    base = np.array([236, 120, 96], np.float32)
    rgb = base * (0.55 + 0.45 * light)[..., None] * (1 - maria)[..., None]
    limb = smoothstep(1.0, 0.85, np.sqrt(d2))
    alpha = np.where(disk, 1.0, 0.0) * limb
    # halo
    dist = np.sqrt(d2)
    halo = np.exp(-(dist - 1).clip(0) * 3.2) * (dist > 0.98) * smoothstep(1.46, 1.15, dist)
    rgb = np.where(disk[..., None], rgb, np.array([255, 90, 70], np.float32))
    a = np.maximum(alpha, halo * 0.55)
    img = Image.fromarray(np.dstack([np.clip(rgb, 0, 255), np.clip(a * 255, 0, 255)]).astype(np.uint8), "RGBA")
    save(img, "moon")


def make_clouds(name, W, H, seed, density, color, tint_bottom):
    n = tile_noise(W, H, 4, 7, seed=seed) * 0.65 + tile_noise(W, H, 9, 5, seed=seed + 1) * 0.35
    y = np.linspace(0, 1, H, dtype=np.float32)[:, None]
    band = np.exp(-((y - 0.5) / 0.28) ** 2)
    a = smoothstep(1 - density, 1 - density + 0.28, n) * band
    shade_ = np.clip(1.0 - (n - 0.5) * 1.6, 0, 1)
    top = np.array(color, np.float32)
    bot = np.array(tint_bottom, np.float32)
    rgb = top * (1 - y[..., None]) + bot * y[..., None]
    rgb = rgb * (0.6 + 0.4 * shade_[..., None])
    rgb = np.broadcast_to(rgb, (H, W, 3))
    img = Image.fromarray(np.dstack([np.clip(rgb, 0, 255), np.clip(a * 235, 0, 255)]).astype(np.uint8), "RGBA")
    save(img, name)


# ---------------------------------------------------------------------------------------------------------- silhouettes

def make_mountains(W=2048, H=512):
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for layer, (seed, base, amp, col) in enumerate([
        (41, 0.42, 0.34, (36, 16, 26, 255)),
        (42, 0.58, 0.26, (24, 11, 18, 255)),
    ]):
        h = fbm1d(W, seed, 7, 3)
        h = normalize(h) ** 1.4
        top = (base - h * amp) * H
        arr = np.zeros((H, W), np.float32)
        ys = np.arange(H)[:, None]
        arr[ys >= top[None, :]] = 1
        m = Image.fromarray((arr * 255).astype(np.uint8), "L")
        c = Image.new("RGBA", (W, H), col)
        # atmospheric lightening towards the peaks
        grad = np.linspace(1.4, 0.8, H)[:, None] * np.ones((1, W))
        carr = np.asarray(c, np.float32)
        carr[..., :3] *= grad[..., None]
        c = Image.fromarray(np.clip(carr, 0, 255).astype(np.uint8), "RGBA")
        img.paste(c, (0, 0), m)
    save(img, "mountains")


def spire(d, x, base_y, w, h, col):
    d.polygon([(x - w / 2, base_y), (x, base_y - h), (x + w / 2, base_y)], fill=col)


def battlements(d, x0, x1, y, size, col):
    x = x0
    while x < x1:
        d.rectangle([x, y - size, min(x1, x + size * 0.7), y], fill=col)
        x += size * 1.4


def make_castle(W=2048, H=1024):
    """Ruined gothic castle on a crag. Separate emissive layer for lit windows."""
    W2, H2 = W * SS, H * SS
    sil = Image.new("L", (W2, H2), 0)
    win = Image.new("L", (W2, H2), 0)
    d = ImageDraw.Draw(sil)
    dw = ImageDraw.Draw(win)
    r = rng(77)
    S = SS * 0.74  # geometry unit (keeps the tallest spire inside the canvas)
    # crag
    crag = fbm1d(W2, 90, 6, 5)
    cx = W2 * 0.5
    xs = np.arange(W2)
    prof = (0.80 - 0.14 * np.exp(-((xs - cx) / (W2 * 0.20)) ** 2) + (crag - 0.5) * 0.08) * H2
    pts = [(0, H2)] + [(float(x), float(prof[x])) for x in range(0, W2, 4)] + [(W2, H2)]
    d.polygon(pts, fill=255)
    ground = lambda x: float(prof[int(np.clip(x, 0, W2 - 1))])

    def tower(x, w, h, roof=True, roof_h=None, windows=3, broken=False):
        gy = ground(x) + 30 * S
        top = gy - h
        d.rectangle([x - w / 2, top, x + w / 2, gy], fill=255)
        if roof and not broken:
            spire(d, x, top + 2, w * 1.35, roof_h or w * 2.6, 255)
            # little finials
            d.rectangle([x - 1.5 * S, top - (roof_h or w * 2.6) - 14 * S, x + 1.5 * S, top - (roof_h or w * 2.6) + 4], fill=255)
        elif broken:
            # jagged broken top
            jag = [(x - w / 2, top)]
            n = 6
            for i in range(n + 1):
                jag.append((x - w / 2 + w * i / n, top - r.random() * w * 0.8))
            jag.append((x + w / 2, top))
            d.polygon(jag, fill=255)
        else:
            battlements(d, x - w / 2 - 4 * S, x + w / 2 + 4 * S, top, 9 * S, 255)
        # windows
        for i in range(windows):
            wy = top + h * (0.18 + i * 0.22)
            if wy > gy - 20 * S:
                break
            if r.random() < 0.6:
                ww, wh = w * 0.11, w * 0.26
                v = int(110 + r.random() * 130)
                dw.rectangle([x - ww / 2, wy, x + ww / 2, wy + wh], fill=v)
                dw.ellipse([x - ww / 2, wy - ww / 2, x + ww / 2, wy + ww / 2], fill=v)

    def wall(x0, x1, h):
        g = max(ground(x0), ground(x1)) + 20 * S
        d.rectangle([x0, g - h, x1, g], fill=255)
        battlements(d, x0, x1, g - h, 8 * S, 255)

    # curtain walls
    wall(cx - 420 * S, cx - 150 * S, 120 * S)
    wall(cx + 150 * S, cx + 430 * S, 110 * S)
    # outer towers
    tower(cx - 430 * S, 46 * S, 230 * S, roof=True, windows=3)
    tower(cx - 300 * S, 38 * S, 190 * S, roof=False, windows=2, broken=True)
    tower(cx + 300 * S, 40 * S, 210 * S, roof=True, windows=3)
    tower(cx + 440 * S, 52 * S, 250 * S, roof=False, windows=3)
    # keep + cathedral
    tower(cx - 150 * S, 70 * S, 330 * S, roof=True, roof_h=210 * S, windows=4)
    tower(cx + 150 * S, 64 * S, 300 * S, roof=True, roof_h=190 * S, windows=4)
    kx = cx
    gy = ground(kx) + 30 * S
    d.rectangle([kx - 120 * S, gy - 280 * S, kx + 120 * S, gy], fill=255)
    # cathedral nave roof
    d.polygon([(kx - 128 * S, gy - 278 * S), (kx, gy - 380 * S), (kx + 128 * S, gy - 278 * S)], fill=255)
    # central spire (the tallest point)
    d.rectangle([kx - 26 * S, gy - 520 * S, kx + 26 * S, gy - 300 * S], fill=255)
    spire(d, kx, gy - 518 * S, 70 * S, 260 * S, 255)
    d.rectangle([kx - 2 * S, gy - 800 * S, kx + 2 * S, gy - 760 * S], fill=255)
    # rose window + tall lancets
    dw.ellipse([kx - 22 * S, gy - 246 * S, kx + 22 * S, gy - 202 * S], fill=235)
    # tracery: dark spokes across the rose window
    for k in range(8):
        a = k * math.pi / 4
        dw.line([(kx, gy - 224 * S), (kx + math.cos(a) * 22 * S, gy - 224 * S + math.sin(a) * 22 * S)], fill=0, width=int(2 * S))
    for i in (-1, 1):
        for j, off in enumerate((52, 84)):
            dw.rectangle([kx + i * off * S - 5 * S, gy - 200 * S, kx + i * off * S + 5 * S, gy - 130 * S], fill=190 - j * 40)
            dw.ellipse([kx + i * off * S - 5 * S, gy - 205 * S, kx + i * off * S + 5 * S, gy - 195 * S], fill=190 - j * 40)
    # flying buttresses
    for i in (-1, 1):
        for k in range(3):
            bx = kx + i * (140 + k * 40) * S
            d.polygon([(bx, gy - (200 - k * 40) * S), (bx + i * 26 * S, gy), (bx + i * 10 * S, gy), (bx - i * 4 * S, gy - (170 - k * 40) * S)], fill=255)
    # small chapels / roofs between towers for a denser skyline
    for bx, bw, bh in ((-235, 60, 150), (235, 70, 140), (-360, 40, 120), (370, 44, 130)):
        x = cx + bx * S
        g0 = ground(x) + 30 * S
        d.rectangle([x - bw * S / 2, g0 - bh * S, x + bw * S / 2, g0], fill=255)
        d.polygon([(x - bw * S / 2 - 4 * S, g0 - bh * S), (x, g0 - bh * S - bw * S * 0.8), (x + bw * S / 2 + 4 * S, g0 - bh * S)], fill=255)
        if r.random() < 0.8:
            dw.rectangle([x - 4 * S, g0 - bh * S * 0.7, x + 4 * S, g0 - bh * S * 0.45], fill=int(120 + r.random() * 90))
    sil = sil.filter(ImageFilter.GaussianBlur(0.6 * S)).resize((W, H), Image.LANCZOS)
    win = win.resize((W, H), Image.LANCZOS)
    s = np.asarray(sil, np.float32) / 255
    # silhouette colour: very dark with a faint crimson rim from the moon (upper right)
    blurred = np.asarray(sil.filter(ImageFilter.GaussianBlur(3)), np.float32) / 255
    gy_, gx_ = np.gradient(blurred)
    rim = np.clip(-gy_ * 1.0 + gx_ * 0.9, 0, 1) * 6 * s
    stone = tile_noise(W, H, 40, 4, seed=78)
    base = np.array([18, 10, 15], np.float32)[None, None, :] * (0.75 + 0.5 * stone[..., None])
    rgb = base + np.array([150, 40, 36], np.float32) * np.clip(rim, 0, 0.8)[..., None]
    w = np.asarray(win, np.float32) / 255 * s
    rgb = rgb * (1 - w[..., None]) + np.array([255, 150, 70], np.float32) * w[..., None]
    save(Image.fromarray(np.dstack([np.clip(rgb, 0, 255), s * 255]).astype(np.uint8), "RGBA"), "castle")
    # emissive mask (window glow + bloom halo), white on transparent
    glow = Image.fromarray((w * 255).astype(np.uint8), "L")
    halo = glow.filter(ImageFilter.GaussianBlur(10))
    g = np.clip(np.asarray(glow, np.float32) + np.asarray(halo, np.float32) * 1.6, 0, 255)
    save(Image.fromarray(np.dstack([np.full_like(g, 255), np.full_like(g, 170), np.full_like(g, 90), g]).astype(np.uint8), "RGBA"), "castle_windows")


def dead_tree(d, x, y, h, r, S):
    def branch(x0, y0, ang, length, width, depth):
        x1 = x0 + math.cos(ang) * length
        y1 = y0 - math.sin(ang) * length
        d.line([(x0, y0), (x1, y1)], fill=255, width=max(1, int(width)))
        if depth <= 0 or length < 6 * S:
            return
        n = 2 if r.random() < 0.7 else 3
        for i in range(n):
            branch(x1, y1, ang + (r.random() - 0.5) * 1.3, length * (0.55 + r.random() * 0.25), width * 0.62, depth - 1)
    branch(x, y, math.pi / 2 + (r.random() - 0.5) * 0.2, h * 0.45, h * 0.07, 5)


def make_ruins(W=2048, H=512):
    W2, H2 = W * SS, H * SS
    S = SS
    img = Image.new("L", (W2, H2), 0)
    d = ImageDraw.Draw(img)
    r = rng(123)
    h = fbm1d(W2, 124, 6, 4)
    top = (0.62 + (h - 0.5) * 0.35) * H2
    # dip in the middle so the castle stays visible
    xs = np.arange(W2)
    top += np.exp(-((xs - W2 / 2) / (W2 * 0.18)) ** 2) * H2 * 0.25
    d.polygon([(0, H2)] + [(float(x), float(top[x])) for x in range(0, W2, 4)] + [(W2, H2)], fill=255)
    g = lambda x: float(top[int(np.clip(x, 0, W2 - 1))])
    # dead trees on both flanks
    for _ in range(9):
        side = r.random() < 0.5
        x = (r.random() * 0.3 + (0.0 if side else 0.7)) * W2
        dead_tree(d, x, g(x) + 6 * S, (140 + r.random() * 180) * S, r, S)
    # gravestones + celtic crosses
    for _ in range(26):
        x = r.random() * W2
        if abs(x - W2 / 2) < W2 * 0.12:
            continue
        y = g(x) + 4 * S
        w_ = (10 + r.random() * 12) * S
        hh = (18 + r.random() * 26) * S
        tilt = (r.random() - 0.5) * 0.3
        if r.random() < 0.35:
            d.rectangle([x - w_ * 0.18, y - hh * 1.5, x + w_ * 0.18, y], fill=255)
            d.rectangle([x - w_ * 0.55, y - hh * 1.2, x + w_ * 0.55, y - hh * 1.05], fill=255)
        else:
            pts = [(x - w_ / 2, y), (x - w_ / 2 + tilt * hh, y - hh), (x + w_ / 2 + tilt * hh, y - hh), (x + w_ / 2, y)]
            d.polygon(pts, fill=255)
            d.ellipse([x - w_ / 2 + tilt * hh, y - hh - w_ / 2, x + w_ / 2 + tilt * hh, y - hh + w_ / 2], fill=255)
    # broken arch on the right, pillar stumps on the left
    ax = W2 * 0.83
    ay = g(ax) + 10 * S
    d.rectangle([ax - 80 * S, ay - 190 * S, ax - 56 * S, ay], fill=255)
    d.rectangle([ax + 56 * S, ay - 150 * S, ax + 80 * S, ay], fill=255)
    d.arc([ax - 80 * S, ay - 250 * S, ax + 80 * S, ay - 120 * S], 180, 290, fill=255, width=22 * S)
    for k in range(4):
        px = W2 * (0.08 + k * 0.05)
        ph = (40 + r.random() * 90) * S
        d.rectangle([px - 12 * S, g(px) - ph, px + 12 * S, g(px) + 10], fill=255)
    img = img.filter(ImageFilter.GaussianBlur(0.5 * S)).resize((W, H), Image.LANCZOS)
    a = np.asarray(img, np.float32) / 255
    rgb = np.zeros((H, W, 3), np.float32) + np.array([8, 5, 8], np.float32)
    blurred = np.asarray(img.filter(ImageFilter.GaussianBlur(3)), np.float32) / 255
    gy_, gx_ = np.gradient(blurred)
    rim = np.clip(-gy_ * 1.2 + gx_ * 0.5, 0, 1) * 5 * a
    rgb += np.array([110, 28, 28], np.float32) * np.clip(rim, 0, 0.7)[..., None]
    save(Image.fromarray(np.dstack([rgb, a * 255]).astype(np.uint8), "RGBA"), "ruins")


def make_fog(W=1024, H=256):
    n = tile_noise(W, H, 5, 6, seed=201) * 0.7 + tile_noise(W, H, 11, 4, seed=202) * 0.3
    y = np.linspace(0, 1, H, dtype=np.float32)[:, None]
    fade = np.clip(np.sin(y * math.pi), 0, 1) ** 1.5
    a = smoothstep(0.3, 0.8, n) * fade
    rgb = np.broadcast_to(np.array([150, 120, 140], np.float32), (H, W, 3))
    save(Image.fromarray(np.dstack([rgb, np.clip(a * 200, 0, 255)]).astype(np.uint8), "RGBA"), "fog")


def main():
    make_sky()
    make_moon()
    make_clouds("clouds_far", 2048, 512, 301, 0.42, (70, 36, 58), (120, 40, 40))
    make_clouds("clouds_near", 2048, 512, 311, 0.36, (40, 22, 34), (90, 30, 30))
    make_mountains()
    make_castle()
    make_ruins()
    make_fog()


if __name__ == "__main__":
    main()
