#!/usr/bin/env python3
"""
Generates Bloodfall's UI texture kit (dark iron, worn bronze, obsidian, blood lacquer) into
Client/Assets/Resources/Textures/UI. All frames are designed for UI Toolkit 9-slicing; the slice sizes used by
the USS files are listed in SLICES (and written to Textures/UI/slices.json for reference).
"""
import json
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from uikit import *

SS = 3  # supersampling factor
SLICES = {}


def frame_height(W, H, border, radius, inset, seed, rivets=True, ornaments=True, groove=True):
    sdf_o = rounded_rect_sdf(W, H, inset, inset, W - inset, H - inset, radius)
    sdf_i = rounded_rect_sdf(W, H, inset + border, inset + border, W - inset - border, H - inset - border, max(2, radius - border * 0.6))
    ring = (sdf_o < 0) & (sdf_i > 0)
    # Bevel: rise from outer edge, plateau, fall toward the inner edge.
    d_out = -sdf_o
    d_in = sdf_i
    h = smoothstep(0, border * 0.35, d_out) * smoothstep(0, border * 0.25, d_in)
    if groove:
        # Bronze inlay groove at ~70% of the border width.
        g = np.exp(-((d_in - border * 0.28) ** 2) / (2 * (border * 0.06) ** 2))
        h -= g * 0.35
    n = tile_noise(W, H, 6, 5, seed)
    scratches = tile_noise(W, H, 40, 2, seed + 1) > 0.78
    h = (h * 0.55) + (n - 0.5) * 0.1 - scratches * 0.05
    h *= ring
    if rivets:
        step = max(border * 3.2, 48 * SS)
        for t in np.arange(border * 1.6, W - border * 1.6, step):
            for (x, y) in ((t, inset + border * 0.5), (t, H - inset - border * 0.5)):
                h += dome(W, H, x, y, border * 0.16)
        for t in np.arange(border * 1.6, H - border * 1.6, step):
            for (x, y) in ((inset + border * 0.5, t), (W - inset - border * 0.5, t)):
                h += dome(W, H, x, y, border * 0.16)
    if ornaments:
        for (cx, cy, sx, sy) in ((inset, inset, 1, 1), (W - inset, inset, -1, 1), (inset, H - inset, 1, -1), (W - inset, H - inset, -1, -1)):
            h = np.maximum(h, corner_ornament(W, H, cx, cy, sx, sy, border))
    return h, ring, sdf_o, sdf_i


def dome(W, H, x, y, r):
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
    d = np.sqrt((xs - x) ** 2 + (ys - y) ** 2) / r
    return np.clip(1 - d * d, 0, 1) * 0.9


def corner_ornament(W, H, cx, cy, sx, sy, border):
    """Gothic corner piece: a diamond boss with a spike reaching along both edges."""
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
    lx = (xs - cx) * sx
    ly = (ys - cy) * sy
    s = border * 1.45
    # Diamond boss.
    diamond = 1 - (np.abs(lx - s) + np.abs(ly - s)) / (s * 0.95)
    boss = np.clip(diamond, 0, 1) ** 0.6 * 1.25
    gem = np.clip(1 - np.sqrt((lx - s) ** 2 + (ly - s) ** 2) / (s * 0.28), 0, 1) ** 0.5 * 1.3
    boss = np.maximum(boss, gem)
    # Spikes along edges.
    spike_x = np.clip(1 - np.abs(ly - border * 0.5) / (border * 0.22) - lx / (s * 3.2), 0, 1) * (lx > 0)
    spike_y = np.clip(1 - np.abs(lx - border * 0.5) / (border * 0.22) - ly / (s * 3.2), 0, 1) * (ly > 0)
    return np.maximum(boss, np.maximum(spike_x, spike_y) * 1.05)


def metal_colors(W, H, seed, base=(38, 36, 42), variation=14):
    n = tile_noise(W, H, 5, 4, seed + 7)
    c = np.asarray(base, np.float32)[None, None, :] + (n[..., None] - 0.5) * variation * 2
    return c


def make_frame(name, size, border, radius, fill=(14, 11, 13), fill_alpha=0.93, seed=1, rivets=True, ornaments=True,
               bronze_inlay=True, fill_noise=True, edge=(38, 36, 42), glow_color=None):
    W, H = size[0] * SS, size[1] * SS
    b, rad, inset = border * SS, radius * SS, 2 * SS
    h, ring, sdf_o, sdf_i = frame_height(W, H, b, rad, inset, seed, rivets, ornaments)
    diff, spec = shade(h, strength=4.5 / SS)
    base = metal_colors(W, H, seed, edge)
    # Thin bronze inlay line + thin bright outer lip.
    if bronze_inlay:
        d_in = sdf_i
        band = np.exp(-((d_in - b * 0.28) ** 2) / (2 * (b * 0.035) ** 2))
        base = base * (1 - band[..., None]) + np.asarray(BRONZE, np.float32) * band[..., None]
    lip = np.exp(-((-sdf_o - b * 0.08) ** 2) / (2 * (b * 0.03) ** 2))
    base = base * (1 - lip[..., None] * 0.6) + np.asarray(BRONZE_LIGHT, np.float32) * lip[..., None] * 0.6
    # Ornaments/rivets (the only high parts) are bronze, with crimson gems in the corner bosses.
    hi = np.clip((h - 0.62) * 4, 0, 1)
    base = base * (1 - hi[..., None]) + np.asarray(BRONZE_LIGHT, np.float32) * hi[..., None]
    gem = np.clip((h - 1.05) * 6, 0, 1)
    base = base * (1 - gem[..., None]) + np.asarray((200, 20, 30), np.float32) * gem[..., None]
    rgb = colorize(base, diff, spec, ambient=0.32, spec_amount=0.55)
    alpha_ring = smoothstep(0.8, -0.8, sdf_o) * (sdf_i > -1)
    # Fill.
    inside = smoothstep(0.5, -0.5, sdf_i)
    ys = np.linspace(0, 1, H, dtype=np.float32)[:, None]
    fill_rgb = np.asarray(fill, np.float32)[None, None, :] * (1.15 - ys[..., None] * 0.3)
    if fill_noise:
        fn = tile_noise(W, H, 12, 4, seed + 3)
        fill_rgb = fill_rgb + (fn[..., None] - 0.5) * 14
    # Inner shadow cast by the border onto the fill.
    shadow = np.exp(-np.maximum(-sdf_i, 0) / (b * 0.45))
    fill_rgb = fill_rgb * (1 - 0.6 * shadow[..., None])
    rgb_total = rgb * (1 - inside[..., None]) + fill_rgb * inside[..., None]
    alpha = np.maximum(alpha_ring * (1 - inside), inside * fill_alpha)
    alpha = np.where(sdf_o > 1, 0, alpha)
    img = to_image(rgb_total, alpha)
    if glow_color is not None:
        img = glow(img, 6 * SS, glow_color, 0.9)
    img = downscale(img, SS)
    img.save(out_path("Textures", "UI", name + ".png"))
    SLICES[name] = border + 4
    return img


def make_button(name, size, style, state, seed=5):
    """style: 'steel' | 'primary' | 'danger' | 'ghost'; state: normal | hover | pressed | disabled"""
    W, H = size[0] * SS, size[1] * SS
    b, rad, inset = 5 * SS, 4 * SS, 2 * SS
    sdf_o = rounded_rect_sdf(W, H, inset, inset, W - inset, H - inset, rad)
    sdf_i = rounded_rect_sdf(W, H, inset + b, inset + b, W - inset - b, H - inset - b, rad * 0.6)
    ring = (sdf_o < 0) & (sdf_i > 0)
    hb = smoothstep(0, b * 0.5, -sdf_o) * smoothstep(-1, b * 0.4, sdf_i)
    # Face: slightly domed (pressed = concave).
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
    face_dome = 1 - ((ys - H / 2) / (H / 2)) ** 2
    concave = state == "pressed"
    face_h = (0.35 - 0.25 * face_dome) if concave else (0.25 + 0.3 * face_dome)
    n = tile_noise(W, H, 6, 4, seed)
    inside = smoothstep(0.5, -0.5, sdf_i)
    h = hb * ring + face_h * inside + (n - 0.5) * 0.06
    diff, spec = shade(h, strength=3.5 / SS)
    if style == "primary":
        face = np.asarray((128, 12, 22), np.float32)
        border_col = np.asarray(GOLD if state != "disabled" else (110, 100, 90), np.float32)
    elif style == "danger":
        face = np.asarray((70, 16, 18), np.float32)
        border_col = np.asarray(BRONZE, np.float32)
    elif style == "ghost":
        face = np.asarray((20, 17, 20), np.float32)
        border_col = np.asarray((90, 70, 50), np.float32)
    else:
        face = np.asarray((40, 37, 42), np.float32)
        border_col = np.asarray(BRONZE, np.float32)
    if state == "hover":
        face = face * 1.35 + np.asarray((18, 4, 4), np.float32)
        border_col = np.minimum(border_col * 1.25, 255)
    elif state == "pressed":
        face = face * 0.8
    elif state == "disabled":
        face = np.asarray((34, 32, 34), np.float32)
        border_col = np.asarray((70, 64, 58), np.float32)
    base = np.where(ring[..., None], border_col[None, None, :], face[None, None, :])
    base = base + (n[..., None] - 0.5) * 16
    rgb = colorize(base, diff, spec, ambient=0.45, spec_amount=0.35 if style != "primary" else 0.6)
    # Lacquer gloss on primary buttons.
    if style == "primary" and state != "disabled":
        gloss = np.clip(1 - (ys / (H * 0.5)), 0, 1) ** 2 * inside * 0.35
        rgb = rgb + gloss[..., None] * np.asarray((255, 180, 160), np.float32)
    # Inner glow for hover.
    if state == "hover":
        edge_glow = np.exp(-np.maximum(-sdf_i, 0) / (b * 1.2)) * inside
        rgb = rgb + edge_glow[..., None] * np.asarray((140, 40, 20), np.float32) * 0.6
    alpha = smoothstep(0.8, -0.8, sdf_o)
    if style == "ghost":
        alpha = np.where(inside > 0.5, 0.75, alpha)
    img = to_image(rgb, alpha)
    if state == "hover" and style in ("primary", "steel"):
        img = glow(img, 5 * SS, (200, 40, 20), 0.55)
    img = downscale(img, SS)
    img.save(out_path("Textures", "UI", f"{name}_{state}.png"))
    SLICES[name] = 12


def make_input(name, size, focused):
    W, H = size[0] * SS, size[1] * SS
    rad, inset = 3 * SS, 1 * SS
    sdf = rounded_rect_sdf(W, H, inset, inset, W - inset, H - inset, rad)
    ys = np.mgrid[0:H, 0:W][0].astype(np.float32)
    inside = smoothstep(0.5, -0.5, sdf)
    border = np.exp(-(sdf ** 2) / (2 * (1.1 * SS) ** 2))
    base = np.zeros((H, W, 3), np.float32) + np.asarray((9, 8, 10), np.float32)
    shadow = np.exp(-np.maximum(-sdf, 0) / (5 * SS))
    top_shadow = np.clip(1 - ys / (H * 0.45), 0, 1)
    base *= (1 - 0.5 * np.maximum(shadow, top_shadow * 0.6))[..., None]
    col = np.asarray((200, 60, 40) if focused else (110, 84, 52), np.float32)
    rgb = base * (1 - border[..., None]) + col * border[..., None]
    img = to_image(rgb, np.maximum(inside * 0.95, border))
    if focused:
        img = glow(img, 4 * SS, (180, 30, 20), 0.5)
    downscale(img, SS).save(out_path("Textures", "UI", name + ("_focus" if focused else "") + ".png"))
    SLICES[name] = 8


def make_tab(name, size, active):
    W, H = size[0] * SS, size[1] * SS
    sdf = rounded_rect_sdf(W, H, 2 * SS, 2 * SS, W - 2 * SS, H + 20 * SS, 6 * SS)
    inside = smoothstep(0.5, -0.5, sdf)
    ys = np.mgrid[0:H, 0:W][0].astype(np.float32) / H
    n = tile_noise(W, H, 5, 3, 11)
    if active:
        col = np.asarray((74, 14, 20), np.float32) * (1.2 - ys[..., None] * 0.5)
        edge = np.asarray(GOLD, np.float32)
    else:
        col = np.asarray((26, 23, 27), np.float32) * (1.2 - ys[..., None] * 0.4)
        edge = np.asarray((96, 74, 46), np.float32)
    col = col + (n[..., None] - 0.5) * 10
    border = np.exp(-(sdf ** 2) / (2 * (1.2 * SS) ** 2))
    rgb = col * (1 - border[..., None]) + edge * border[..., None]
    if active:
        underline = np.exp(-((ys - 0.94) ** 2) / (2 * 0.02 ** 2))
        rgb = rgb + underline[..., None] * np.asarray((220, 60, 40), np.float32) * 0.8
    downscale(to_image(rgb, np.maximum(inside, border) * (0.95 if active else 0.85)), SS).save(out_path("Textures", "UI", name + ("_active" if active else "") + ".png"))
    SLICES[name] = 10


def make_divider(name, width=512, height=24):
    W, H = width * SS, height * SS
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cy = H // 2
    for x in range(W):
        t = abs(x - W / 2) / (W / 2)
        a = int(220 * (1 - t) ** 1.5)
        d.line([(x, cy - SS), (x, cy + SS)], fill=(*BRONZE, a))
    s = 7 * SS
    d.polygon([(W / 2, cy - s), (W / 2 + s, cy), (W / 2, cy + s), (W / 2 - s, cy)], fill=(*GOLD, 255), outline=(60, 40, 20, 255))
    d.polygon([(W / 2, cy - s * 0.45), (W / 2 + s * 0.45, cy), (W / 2, cy + s * 0.45), (W / 2 - s * 0.45, cy)], fill=(*CRIMSON, 255))
    for side in (-1, 1):
        x = W / 2 + side * s * 2.2
        d.ellipse([x - 2.2 * SS, cy - 2.2 * SS, x + 2.2 * SS, cy + 2.2 * SS], fill=(*BRONZE_LIGHT, 230))
    img = glow(img, 3 * SS, (160, 30, 20), 0.35)
    downscale(img, SS).save(out_path("Textures", "UI", name + ".png"))


def make_checkbox():
    for on in (False, True):
        W = 28 * SS
        img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        d.rounded_rectangle([2 * SS, 2 * SS, W - 2 * SS, W - 2 * SS], radius=4 * SS, fill=(10, 9, 11, 240), outline=(*BRONZE, 255), width=2 * SS)
        if on:
            d.line([(7 * SS, 14 * SS), (12 * SS, 20 * SS), (21 * SS, 8 * SS)], fill=(230, 60, 40, 255), width=3 * SS, joint="curve")
        img = glow(img, 2 * SS, (160, 30, 20), 0.4 if on else 0.0)
        downscale(img, SS).save(out_path("Textures", "UI", f"checkbox_{'on' if on else 'off'}.png"))


def make_scrollbar():
    for name, w, h, col in (("scroll_thumb", 12, 64, (92, 70, 44)), ("scroll_track", 12, 64, (16, 14, 16))):
        W, H = w * SS, h * SS
        img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        d.rounded_rectangle([2 * SS, 2 * SS, W - 2 * SS, H - 2 * SS], radius=4 * SS, fill=(*col, 235), outline=(40, 30, 20, 255) if name == "scroll_thumb" else None, width=SS)
        downscale(img, SS).save(out_path("Textures", "UI", name + ".png"))
    SLICES["scroll"] = 6


def make_stone_background(name="bg_stone", size=512, seed=21):
    n1 = tile_noise(size, size, 4, 6, seed)
    n2 = tile_noise(size, size, 16, 4, seed + 1)
    cracks = np.abs(tile_noise(size, size, 3, 3, seed + 2) - 0.5) < 0.012
    h = n1 * 0.7 + n2 * 0.3 - cracks * 0.4
    diff, spec = shade(h, strength=6)
    base = np.asarray((30, 27, 30), np.float32)[None, None, :] + (n2[..., None] - 0.5) * 20
    rgb = colorize(base, diff, spec, ambient=0.5, spec_amount=0.1)
    Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB").save(out_path("Textures", "UI", name + ".png"))


def make_vignette(name="vignette", size=512):
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float32) / size - 0.5
    d = np.sqrt(xs ** 2 + ys ** 2) / 0.7071
    a = np.clip((d - 0.35) / 0.65, 0, 1) ** 1.6
    rgb = np.zeros((size, size, 3), np.float32)
    to_image(rgb, a * 0.95).save(out_path("Textures", "UI", name + ".png"))


def make_gradient(name, top, bottom, w=8, h=256, alpha_top=1.0, alpha_bottom=1.0):
    t = np.linspace(0, 1, h, dtype=np.float32)[:, None]
    rgb = np.asarray(top, np.float32)[None, None, :] * (1 - t[..., None]) + np.asarray(bottom, np.float32)[None, None, :] * t[..., None]
    rgb = np.repeat(rgb, w, axis=1)
    a = (alpha_top * (1 - t) + alpha_bottom * t).repeat(w, axis=1)
    to_image(rgb, a).save(out_path("Textures", "UI", name + ".png"))


def make_logo():
    W, H = 1400 * 2, 380 * 2
    title_font = font("CinzelDecorative-Black.ttf", 250)
    sub_font = font("MarcellusSC-Regular.ttf", 64)
    mask = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(mask)
    text = "BLOODFALL"
    bbox = d.textbbox((0, 0), text, font=title_font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    tx, ty = (W - tw) / 2 - bbox[0], 40 - bbox[1] + 20
    d.text((tx, ty), text, font=title_font, fill=255)
    m = np.asarray(mask, np.float32) / 255
    # Bevelled metal letters: height from blurred mask.
    hgt = np.asarray(mask.filter(ImageFilter.GaussianBlur(7)), np.float32) / 255
    diff, spec = shade(hgt, strength=16)
    ys = np.mgrid[0:H, 0:W][0].astype(np.float32)
    ty0, ty1 = ty + bbox[1], ty + bbox[3]
    t = np.clip((ys - ty0) / max(1, ty1 - ty0), 0, 1)
    # Crimson metal gradient: bright blood red at top -> deep crimson -> almost black at base.
    top = np.asarray((236, 70, 58), np.float32)
    mid = np.asarray((150, 14, 26), np.float32)
    bot = np.asarray((52, 4, 10), np.float32)
    grad = np.where(t[..., None] < 0.5, top * (1 - t[..., None] * 2) + mid * (t[..., None] * 2), mid * (1 - (t[..., None] - 0.5) * 2) + bot * ((t[..., None] - 0.5) * 2))
    n = tile_noise(W, H, 30, 3, 91)
    grad = grad * (0.85 + 0.3 * n[..., None])
    rgb = colorize(grad, diff, spec, ambient=0.4, spec_color=(255, 210, 190), spec_amount=0.9)
    # Dark bronze outline.
    outline = np.asarray(mask.filter(ImageFilter.MaxFilter(9)), np.float32) / 255
    outline_rgb = np.asarray((40, 24, 14), np.float32)
    ring = np.clip(outline - m, 0, 1)
    rgb = rgb * m[..., None] + outline_rgb * ring[..., None]
    alpha = np.clip(m + ring, 0, 1)
    img = to_image(rgb, alpha)
    # Subtitle with flanking rules.
    d2 = ImageDraw.Draw(img)
    sub = "WAR OF THE ANCIENTS"
    sb = d2.textbbox((0, 0), sub, font=sub_font)
    sw = sb[2] - sb[0]
    sy = ty1 + 40
    d2.text(((W - sw) / 2 - sb[0], sy), sub, font=sub_font, fill=(*BONE, 255))
    ly = sy + (sb[3] - sb[1]) / 2 + sb[1]
    for side in (-1, 1):
        x0 = W / 2 + side * (sw / 2 + 30)
        x1 = W / 2 + side * (sw / 2 + 260)
        d2.line([(x0, ly), (x1, ly)], fill=(*BRONZE, 255), width=4)
        d2.polygon([(x0 + side * 4, ly), (x0 + side * 22, ly - 10), (x0 + side * 40, ly), (x0 + side * 22, ly + 10)], fill=(*CRIMSON, 255), outline=(*GOLD, 255))
    img = glow(img, 26, (170, 10, 16), 0.9)
    img = downscale(img, 2)
    img.save(out_path("Textures", "UI", "logo_bloodfall.png"))
    # Small wordmark for the client top bar.
    small = img.resize((img.width // 3, img.height // 3), Image.LANCZOS)
    small.save(out_path("Textures", "UI", "logo_bloodfall_small.png"))


RANKS = [
    ("initiate", (120, 110, 100), (70, 64, 60)),
    ("ironbound", (150, 150, 160), (60, 60, 70)),
    ("bloodforged", (190, 40, 40), (80, 14, 18)),
    ("warlord", (210, 150, 70), (100, 60, 20)),
    ("dread_knight", (120, 150, 190), (30, 40, 70)),
    ("crimson_lord", (235, 60, 60), (110, 10, 20)),
    ("immortal", (240, 210, 140), (130, 90, 30)),
    ("abyssal_sovereign", (170, 90, 230), (40, 10, 70)),
]


def make_rank_emblems():
    for i, (name, c1, c2) in enumerate(RANKS):
        S = 256 * SS
        ys, xs = np.mgrid[0:S, 0:S].astype(np.float32)
        cx = cy = S / 2
        ang = np.arctan2(ys - cy, xs - cx)
        r = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2) / (S / 2)
        # Shield / star silhouette: more points for higher ranks.
        points = 4 + i
        star = 0.78 + 0.12 * np.cos(ang * points)
        shield = r < star * (0.82 + 0.02 * i)
        h = np.clip(1 - r / (star * 0.9), 0, 1) ** 0.5 * shield
        ring_mask = np.abs(r - 0.52) < 0.05
        h = h + ring_mask * 0.3 * shield
        diff, spec = shade(h, strength=12 / SS * 3)
        t = np.clip(r, 0, 1)
        base = np.asarray(c1, np.float32) * (1 - t[..., None]) + np.asarray(c2, np.float32) * t[..., None]
        rgb = colorize(base, diff, spec, ambient=0.35, spec_amount=0.9)
        alpha = shield.astype(np.float32)
        img = to_image(rgb, alpha)
        d = ImageDraw.Draw(img)
        # Tier pips / central gem.
        g = S * 0.12
        d.ellipse([cx - g, cy - g, cx + g, cy + g], fill=(*c1, 255), outline=(20, 10, 10, 255), width=3 * SS)
        d.ellipse([cx - g * 0.5, cy - g * 0.7, cx + g * 0.2, cy - g * 0.1], fill=(255, 255, 255, 110))
        img = glow(img, 8 * SS, c1, 0.6)
        downscale(img, SS).resize((128, 128), Image.LANCZOS).save(out_path("Textures", "UI", "Ranks", f"rank_{name}.png"))


def make_hud_frames():
    # Big bottom HUD plate (9-sliced horizontally), portrait frame, slot frames, bar frames.
    make_frame("hud_plate", (256, 160), border=22, radius=10, fill=(12, 10, 12), fill_alpha=0.96, seed=31)
    make_frame("hud_slot", (72, 72), border=6, radius=5, fill=(8, 7, 8), fill_alpha=0.9, seed=32, rivets=False, ornaments=False)
    make_frame("hud_slot_ult", (72, 72), border=7, radius=5, fill=(10, 6, 8), fill_alpha=0.9, seed=33, rivets=False, ornaments=True, edge=(60, 30, 30))
    make_frame("hud_portrait", (160, 160), border=12, radius=8, fill=(6, 5, 6), fill_alpha=1.0, seed=34, rivets=True, ornaments=True)
    make_frame("hud_bar", (128, 24), border=4, radius=3, fill=(6, 5, 6), fill_alpha=0.95, seed=35, rivets=False, ornaments=False, bronze_inlay=False)
    make_frame("hud_minimap", (256, 256), border=14, radius=6, fill=(0, 0, 0), fill_alpha=0.0, seed=36)
    make_frame("tooltip", (128, 128), border=6, radius=4, fill=(10, 8, 10), fill_alpha=0.97, seed=37, rivets=False, ornaments=False)
    make_gradient("bar_hp", (78, 190, 60), (30, 96, 24))
    make_gradient("bar_hp_enemy", (220, 50, 40), (110, 16, 14))
    make_gradient("bar_mana", (70, 120, 230), (24, 44, 120))
    make_gradient("bar_blood", (200, 30, 50), (90, 6, 16))
    make_gradient("bar_xp", (230, 190, 90), (140, 90, 30))
    make_gradient("bar_bg", (18, 14, 16), (8, 6, 8))
    make_gradient("fade_bottom", (0, 0, 0), (0, 0, 0), alpha_top=0.0, alpha_bottom=0.92)
    make_gradient("fade_top", (0, 0, 0), (0, 0, 0), alpha_top=0.92, alpha_bottom=0.0)
    make_gradient("glow_red", (160, 20, 20), (40, 4, 6), alpha_top=0.55, alpha_bottom=0.0)
    # Cooldown sweep is drawn with a UI Toolkit generateVisualContent painter at runtime.


def make_cursors():
    for name, color, kind in (("cursor_default", BRONZE_LIGHT, "arrow"), ("cursor_attack", (230, 60, 40), "sword"), ("cursor_cast", (200, 120, 255), "target"), ("cursor_ally", (120, 220, 120), "arrow")):
        S = 64 * SS
        img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        if kind == "arrow":
            pts = [(2 * SS, 2 * SS), (2 * SS, 42 * SS), (13 * SS, 31 * SS), (21 * SS, 48 * SS), (28 * SS, 45 * SS), (20 * SS, 28 * SS), (36 * SS, 28 * SS)]
            d.polygon(pts, fill=(*color, 255), outline=(20, 10, 8, 255))
            d.line(pts + [pts[0]], fill=(20, 10, 8, 255), width=2 * SS)
        elif kind == "sword":
            d.line([(4 * SS, 4 * SS), (44 * SS, 44 * SS)], fill=(210, 210, 220, 255), width=6 * SS)
            d.line([(4 * SS, 4 * SS), (44 * SS, 44 * SS)], fill=(20, 10, 8, 255), width=2 * SS)
            d.line([(34 * SS, 48 * SS), (48 * SS, 34 * SS)], fill=(*color, 255), width=6 * SS)
            d.line([(44 * SS, 44 * SS), (56 * SS, 56 * SS)], fill=(90, 50, 20, 255), width=7 * SS)
        else:
            c = 30 * SS
            d.ellipse([c - 20 * SS, c - 20 * SS, c + 20 * SS, c + 20 * SS], outline=(*color, 255), width=3 * SS)
            for a in range(4):
                ang = a * math.pi / 2
                d.line([(c + math.cos(ang) * 8 * SS, c + math.sin(ang) * 8 * SS), (c + math.cos(ang) * 26 * SS, c + math.sin(ang) * 26 * SS)], fill=(*color, 255), width=3 * SS)
        img = glow(img, 2 * SS, (0, 0, 0), 0.9)
        downscale(img, SS).resize((32, 32), Image.LANCZOS).save(out_path("Textures", "UI", "Cursors", name + ".png"))


def make_faction_crests():
    crests = {
        "crest_crimson_court": ((150, 16, 30), "bat"),
        "crest_ashen_legion": ((120, 130, 110), "skull"),
        "crest_wild_covenant": ((70, 130, 70), "moon"),
        "crest_dawnguard": ((200, 160, 80), "sun"),
    }
    for name, (col, motif) in crests.items():
        S = 256 * SS
        img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        # Shield shape.
        shield = [(S * 0.15, S * 0.12), (S * 0.85, S * 0.12), (S * 0.85, S * 0.52), (S * 0.5, S * 0.92), (S * 0.15, S * 0.52)]
        d.polygon(shield, fill=(20, 16, 18, 255))
        inner = [(S * 0.2, S * 0.17), (S * 0.8, S * 0.17), (S * 0.8, S * 0.5), (S * 0.5, S * 0.85), (S * 0.2, S * 0.5)]
        d.polygon(inner, fill=(*[int(c * 0.35) for c in col], 255))
        d.line(shield + [shield[0]], fill=(*BRONZE, 255), width=6 * SS)
        cx, cy = S * 0.5, S * 0.45
        if motif == "bat":
            wing = [(cx, cy - S * 0.05), (cx - S * 0.28, cy - S * 0.12), (cx - S * 0.22, cy + S * 0.02), (cx - S * 0.12, cy - S * 0.02), (cx - S * 0.06, cy + S * 0.1),
                    (cx, cy + S * 0.04), (cx + S * 0.06, cy + S * 0.1), (cx + S * 0.12, cy - S * 0.02), (cx + S * 0.22, cy + S * 0.02), (cx + S * 0.28, cy - S * 0.12)]
            d.polygon(wing, fill=(*col, 255))
        elif motif == "skull":
            d.ellipse([cx - S * 0.13, cy - S * 0.16, cx + S * 0.13, cy + S * 0.08], fill=(*col, 255))
            d.rectangle([cx - S * 0.08, cy + S * 0.04, cx + S * 0.08, cy + S * 0.14], fill=(*col, 255))
            for sx in (-1, 1):
                d.ellipse([cx + sx * S * 0.07 - S * 0.04, cy - S * 0.06, cx + sx * S * 0.07 + S * 0.04, cy + S * 0.01], fill=(20, 16, 18, 255))
        elif motif == "moon":
            d.ellipse([cx - S * 0.17, cy - S * 0.17, cx + S * 0.17, cy + S * 0.17], fill=(*col, 255))
            d.ellipse([cx - S * 0.09, cy - S * 0.2, cx + S * 0.21, cy + S * 0.12], fill=(*[int(c * 0.35) for c in col], 255))
        else:
            for a in range(12):
                ang = a * math.pi / 6
                d.polygon([(cx + math.cos(ang - 0.12) * S * 0.1, cy + math.sin(ang - 0.12) * S * 0.1), (cx + math.cos(ang) * S * 0.24, cy + math.sin(ang) * S * 0.24), (cx + math.cos(ang + 0.12) * S * 0.1, cy + math.sin(ang + 0.12) * S * 0.1)], fill=(*col, 255))
            d.ellipse([cx - S * 0.1, cy - S * 0.1, cx + S * 0.1, cy + S * 0.1], fill=(*col, 255))
        arr = np.asarray(img).astype(np.float32)
        a = arr[..., 3] / 255
        hgt = np.asarray(img.split()[-1].filter(ImageFilter.GaussianBlur(4 * SS)), np.float32) / 255
        diff, spec = shade(hgt + a * 0.2, strength=8)
        rgb = arr[..., :3] * (0.55 + 0.7 * diff[..., None]) + spec[..., None] * 120
        img = to_image(rgb, a)
        img = glow(img, 6 * SS, col, 0.5)
        downscale(img, SS).resize((128, 128), Image.LANCZOS).save(out_path("Textures", "UI", "Crests", name + ".png"))


def main():
    make_frame("panel_frame", (192, 192), border=30, radius=8, fill_alpha=0.9, seed=1)
    make_frame("panel_frame_solid", (192, 192), border=30, radius=8, fill_alpha=1.0, seed=2)
    make_frame("panel_frame_thin", (96, 96), border=10, radius=5, fill_alpha=0.86, seed=3, rivets=False, ornaments=False)
    make_frame("panel_card", (128, 128), border=12, radius=6, fill=(20, 16, 19), fill_alpha=0.95, seed=4, rivets=False, ornaments=True)
    make_frame("panel_highlight", (128, 128), border=12, radius=6, fill=(40, 10, 14), fill_alpha=0.95, seed=5, rivets=False, ornaments=True, edge=(80, 40, 30), glow_color=(160, 20, 20))
    make_frame("panel_header", (384, 56), border=10, radius=4, fill=(38, 12, 16), fill_alpha=0.97, seed=6, rivets=True, ornaments=False)
    make_frame("dialog_frame", (256, 256), border=34, radius=10, fill=(12, 9, 11), fill_alpha=0.98, seed=7)
    for style in ("steel", "primary", "danger", "ghost"):
        for state in ("normal", "hover", "pressed", "disabled"):
            make_button("btn_" + style, (192, 56), style, state)
    make_input("input", (160, 40), False)
    make_input("input", (160, 40), True)
    make_tab("tab", (160, 44), False)
    make_tab("tab", (160, 44), True)
    make_divider("divider")
    make_checkbox()
    make_scrollbar()
    make_stone_background()
    make_vignette()
    make_hud_frames()
    make_logo()
    make_rank_emblems()
    make_cursors()
    make_faction_crests()
    with open(out_path("Textures", "UI", "slices.json"), "w") as f:
        json.dump(SLICES, f, indent=2)
    print("UI kit generated:", len(os.listdir(os.path.join(RES, "Textures", "UI"))), "entries")


if __name__ == "__main__":
    main()
