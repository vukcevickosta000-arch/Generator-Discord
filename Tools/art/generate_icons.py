#!/usr/bin/env python3
"""
Generates every ability / item / status icon and hero portrait referenced by the game data
(Client/Assets/Resources/Textures/Icons/{Abilities,Items,Statuses,Portraits}).

Style: a single embossed emblem (metal / bone / blood) over a smoky radial background tinted by theme, with rim light,
inner glow and vignette - painted-looking, consistent, readable at 40 px. Glyphs are vector shapes chosen by keyword
from the content id, so new content gets a sensible icon automatically. Original procedural artwork.
"""
import json
import math
import os
import re

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

from uikit import RES, rng, tile_noise, smoothstep, shade, font

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DATA = os.path.join(ROOT, "Shared", "Runtime", "Resources", "GameData")
OUT = os.path.join(RES, "Textures", "Icons")
SS = 4  # supersample

# ------------------------------------------------------------------------------------------------ glyph primitives
# Glyphs draw into an 'L' mask in a normalised space: x,y in [-1,1] mapped to the canvas (y down).


class G:
    def __init__(self, size, sc=1.0, dy=0.0):
        self.S = size
        self.img = Image.new("L", (size, size), 0)
        self.d = ImageDraw.Draw(self.img)
        # Optional uniform scale and vertical offset of the drawing space (portraits shrink the bust to fit headgear).
        self.sc, self.dy = sc, dy

    def P(self, x, y):
        return ((x * self.sc + 1) * 0.5 * self.S, ((y + self.dy) * self.sc + 1) * 0.5 * self.S)

    def U(self, r):
        """Length in normalised units -> pixels."""
        return r * self.sc * 0.5 * self.S

    def poly(self, pts, v=255):
        self.d.polygon([self.P(x, y) for x, y in pts], fill=v)

    def circle(self, x, y, r, v=255):
        (cx, cy), rr = self.P(x, y), self.U(r)
        self.d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=v)

    def ring(self, x, y, r, w, v=255):
        (cx, cy), rr, ww = self.P(x, y), self.U(r), self.U(w)
        self.d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], outline=v, width=max(1, int(ww)))

    def line(self, pts, w, v=255):
        self.d.line([self.P(x, y) for x, y in pts], fill=v, width=max(1, int(self.U(w))), joint="curve")
        for x, y in (pts[0], pts[-1]):
            self.circle(x, y, w / 2, v)

    def rect(self, x0, y0, x1, y1, v=255):
        a, b = self.P(x0, y0), self.P(x1, y1)
        self.d.rectangle([a[0], a[1], b[0], b[1]], fill=v)

    def rrect(self, x0, y0, x1, y1, r, v=255):
        a, b = self.P(x0, y0), self.P(x1, y1)
        self.d.rounded_rectangle([a[0], a[1], b[0], b[1]], radius=self.U(r), fill=v)

    def arc(self, x, y, r, a0, a1, w, v=255):
        (cx, cy), rr = self.P(x, y), self.U(r)
        self.d.arc([cx - rr, cy - rr, cx + rr, cy + rr], a0, a1, fill=v, width=max(1, int(self.U(w))))

    def cut(self, fn):
        """Run fn drawing with value 0 (carve)."""
        fn(0)


def rot(pts, ang, cx=0, cy=0):
    c, s = math.cos(ang), math.sin(ang)
    return [((x - cx) * c - (y - cy) * s + cx, (x - cx) * s + (y - cy) * c + cy) for x, y in pts]


def sword(g, ang=-0.785, length=1.6, width=0.16, guard=0.55):
    blade = [(-width / 2, -length / 2), (0, -length / 2 - 0.18), (width / 2, -length / 2), (width / 2, length * 0.22), (-width / 2, length * 0.22)]
    g.poly(rot(blade, ang))
    g.poly(rot([(-guard / 2, length * 0.22), (guard / 2, length * 0.22), (guard / 2 - 0.05, length * 0.3), (-guard / 2 + 0.05, length * 0.3)], ang))
    g.poly(rot([(-0.05, length * 0.3), (0.05, length * 0.3), (0.05, length * 0.52), (-0.05, length * 0.52)], ang))
    cx, cy = rot([(0, length * 0.56)], ang)[0]
    g.circle(cx, cy, 0.14)


def axe(g, ang=-0.6):
    g.poly(rot([(-0.05, -0.8), (0.05, -0.8), (0.05, 0.85), (-0.05, 0.85)], ang))
    head = [(0.03, -0.72), (0.62, -0.95), (0.78, -0.45), (0.62, 0.05), (0.03, -0.2)]
    g.poly(rot(head, ang))


def shield(g, v=255):
    g.poly([(-0.62, -0.72), (0.62, -0.72), (0.6, 0.05), (0, 0.85), (-0.6, 0.05)], v)


def potion(g):
    g.circle(0, 0.25, 0.62)
    g.rect(-0.16, -0.6, 0.16, -0.18)
    g.rrect(-0.24, -0.78, 0.24, -0.58, 0.06)


def eye(g):
    g.poly([(-0.85, 0), (-0.4, -0.38), (0.4, -0.38), (0.85, 0), (0.4, 0.38), (-0.4, 0.38)])
    g.circle(0, 0, 0.62, 0)
    g.circle(0, 0, 0.36)
    g.circle(0, 0, 0.14, 0)


def ring_glyph(g, gem=True):
    g.ring(0, 0.12, 1.1, 0.2)
    if gem:
        g.poly([(-0.2, -0.46), (0, -0.72), (0.2, -0.46), (0, -0.3)])


def boot(g):
    # shaft with a folded cuff, curved instep, pointed toe, sole and heel
    g.rrect(-0.42, -0.9, 0.2, -0.68, 0.06)
    g.poly([(-0.36, -0.7), (0.14, -0.7), (0.16, 0.05), (0.35, 0.28), (0.72, 0.38), (0.86, 0.58), (0.8, 0.66), (-0.38, 0.66)])
    g.rect(-0.42, 0.62, 0.84, 0.76)
    g.rect(-0.42, 0.62, -0.12, 0.86)
    g.line([(-0.3, -0.35), (0.08, -0.35)], 0.05, 0)
    g.line([(-0.3, -0.12), (0.08, -0.12)], 0.05, 0)


def crystal(g):
    g.poly([(0, -0.9), (0.42, -0.2), (0.25, 0.8), (-0.25, 0.8), (-0.42, -0.2)])


def heart(g):
    g.circle(-0.3, -0.2, 0.62)
    g.circle(0.3, -0.2, 0.62)
    g.poly([(-0.6, -0.05), (0.6, -0.05), (0, 0.8)])


def drop(g):
    g.circle(0, 0.25, 0.9)
    g.poly([(-0.4, 0.05), (0.4, 0.05), (0, -0.85)])


def skull(g):
    g.circle(0, -0.15, 1.2)
    g.rrect(-0.35, 0.25, 0.35, 0.72, 0.1)
    g.circle(-0.24, -0.1, 0.34, 0)
    g.circle(0.24, -0.1, 0.34, 0)
    g.poly([(0, 0.08), (-0.08, 0.25), (0.08, 0.25)], 0)
    for x in (-0.18, 0, 0.18):
        g.rect(x - 0.03, 0.5, x + 0.03, 0.72, 0)


def crown(g):
    g.poly([(-0.75, 0.45), (-0.75, -0.35), (-0.4, 0.0), (0, -0.6), (0.4, 0.0), (0.75, -0.35), (0.75, 0.45)])
    for x, y in ((-0.75, -0.42), (0, -0.68), (0.75, -0.42)):
        g.circle(x, y, 0.18)
    g.rect(-0.75, 0.5, 0.75, 0.68)


def claw(g):
    for i, x in enumerate((-0.45, 0, 0.45)):
        g.line([(x - 0.35, -0.75), (x - 0.05, -0.1), (x + 0.25, 0.75)], 0.16)


def flame(g):
    g.poly([(0, -0.9), (0.35, -0.3), (0.55, 0.2), (0.35, 0.75), (-0.35, 0.75), (-0.55, 0.2), (-0.3, -0.25), (-0.1, 0.0)])


def lightning(g):
    g.poly([(0.15, -0.95), (-0.45, 0.1), (-0.05, 0.1), (-0.25, 0.95), (0.45, -0.2), (0.05, -0.2), (0.3, -0.95)])


def moon(g):
    g.circle(0, 0, 0.85)
    g.circle(0.36, -0.22, 0.72, 0)


def sun(g):
    g.circle(0, 0, 0.8)
    for i in range(12):
        a = i * math.pi / 6
        g.poly(rot([(-0.07, -0.55), (0.07, -0.55), (0, -0.95)], a))


def chain(g):
    for i in range(3):
        y = -0.6 + i * 0.6
        g.d.ellipse([g.P(-0.3, y - 0.35)[0], g.P(-0.3, y - 0.35)[1], g.P(0.3, y + 0.35)[0], g.P(0.3, y + 0.35)[1]], outline=255, width=int(0.12 * 0.5 * g.S))


def star_burst(g, n=5, r0=0.35, r1=0.9):
    pts = []
    for i in range(n * 2):
        a = i * math.pi / n - math.pi / 2
        r = r1 if i % 2 == 0 else r0
        pts.append((math.cos(a) * r, math.sin(a) * r))
    g.poly(pts)


def lance(g, ang=-0.785):
    g.poly(rot([(-0.05, -0.45), (0.05, -0.45), (0.05, 0.95), (-0.05, 0.95)], ang))
    g.poly(rot([(0, -0.98), (0.16, -0.45), (0, -0.35), (-0.16, -0.45)], ang))


def staff(g):
    g.line([(-0.5, 0.9), (0.35, -0.55)], 0.12)
    g.circle(0.42, -0.65, 0.42)


def scroll(g):
    g.rrect(-0.55, -0.55, 0.55, 0.55, 0.05)
    g.circle(-0.55, -0.55, 0.3)
    g.circle(0.55, 0.55, 0.3)
    for y in (-0.25, 0.0, 0.25):
        g.rect(-0.35, y - 0.03, 0.35, y + 0.03, 0)


def mask(g):
    g.poly([(-0.7, -0.5), (0.7, -0.5), (0.6, 0.2), (0.2, 0.75), (-0.2, 0.75), (-0.6, 0.2)])
    g.poly([(-0.5, -0.2), (-0.12, -0.2), (-0.2, 0.05), (-0.45, 0.05)], 0)
    g.poly([(0.5, -0.2), (0.12, -0.2), (0.2, 0.05), (0.45, 0.05)], 0)
    g.poly([(-0.18, 0.4), (0.18, 0.4), (0.1, 0.55), (-0.1, 0.55)], 0)
    g.poly([(-0.3, 0.3), (-0.22, 0.62), (-0.14, 0.32)])
    g.poly([(0.3, 0.3), (0.22, 0.62), (0.14, 0.32)])


def amulet(g):
    g.arc(0, -0.35, 1.1, 200, 340, 0.08)
    g.circle(0, 0.25, 0.8)
    g.circle(0, 0.25, 0.38, 0)
    g.circle(0, 0.25, 0.22)


def gauntlet(g):
    g.rrect(-0.45, -0.1, 0.45, 0.8, 0.1)
    for i, x in enumerate((-0.36, -0.12, 0.12, 0.36)):
        g.rrect(x - 0.1, -0.75 + abs(i - 1.5) * 0.08, x + 0.1, 0.0, 0.08)
    g.rrect(0.42, 0.0, 0.75, 0.25, 0.08)


def belt(g):
    g.rrect(-0.9, -0.2, 0.9, 0.2, 0.05)
    g.rect(-0.25, -0.35, 0.25, 0.35)
    g.rect(-0.12, -0.2, 0.12, 0.2, 0)


def cloak(g):
    g.poly([(-0.25, -0.8), (0.25, -0.8), (0.75, 0.85), (-0.75, 0.85)])
    g.circle(0, -0.8, 0.35)
    g.poly([(-0.05, -0.55), (0.05, -0.55), (0.1, 0.85), (-0.1, 0.85)], 0)


def hood(g):
    g.poly([(0, -0.9), (0.65, -0.1), (0.7, 0.8), (-0.7, 0.8), (-0.65, -0.1)])
    g.circle(0, 0.05, 0.7, 0)


def lantern(g):
    g.rrect(-0.35, -0.45, 0.35, 0.6, 0.08)
    g.rrect(-0.2, -0.3, 0.2, 0.45, 0.05, 0)
    g.circle(0, 0.08, 0.28)
    g.arc(0, -0.55, 0.45, 180, 360, 0.08)
    g.rect(-0.45, 0.6, 0.45, 0.72)


def rune_stone(g):
    g.poly([(-0.45, 0.85), (-0.55, -0.4), (0, -0.85), (0.55, -0.4), (0.45, 0.85)])
    g.line([(0, -0.5), (0, 0.5)], 0.08, 0)
    g.line([(-0.25, -0.2), (0, 0.05), (0.25, -0.2)], 0.08, 0)


def chalice(g):
    # goblet overflowing with blood drops
    g.poly([(-0.6, -0.7), (0.6, -0.7), (0.45, -0.2), (0.15, 0.05), (-0.15, 0.05), (-0.45, -0.2)])
    g.rect(-0.07, 0.0, 0.07, 0.55)
    g.poly([(-0.42, 0.72), (0.42, 0.72), (0.25, 0.52), (-0.25, 0.52)])
    g.circle(0.35, -0.85, 0.18)
    g.circle(-0.1, -0.92, 0.12)


def hammer(g, ang=-0.6):
    g.poly(rot([(-0.06, -0.3), (0.06, -0.3), (0.06, 0.95), (-0.06, 0.95)], ang))
    g.poly(rot([(-0.55, -0.85), (0.55, -0.85), (0.55, -0.3), (-0.55, -0.3)], ang))
    g.poly(rot([(-0.55, -0.72), (-0.72, -0.57), (-0.55, -0.42)], ang))


def fangs(g):
    g.arc(0, -0.1, 1.5, 20, 160, 0.16)
    g.poly([(-0.45, -0.05), (-0.15, -0.05), (-0.3, 0.8)])
    g.poly([(0.45, -0.05), (0.15, -0.05), (0.3, 0.8)])


def splatter(g, seed=3):
    r = rng(seed)
    g.circle(0, 0, 0.9)
    for i in range(11):
        a = r.random() * math.tau
        d = 0.5 + r.random() * 0.4
        g.circle(math.cos(a) * d, math.sin(a) * d, 0.12 + r.random() * 0.22)
        g.line([(0, 0), (math.cos(a) * d, math.sin(a) * d)], 0.08)


def hand(g):
    g.rrect(-0.4, 0.0, 0.4, 0.8, 0.2)
    for i, x in enumerate((-0.3, -0.1, 0.1, 0.3)):
        g.rrect(x - 0.08, -0.65 + abs(i - 1.5) * 0.1, x + 0.08, 0.1, 0.08)
    g.rrect(0.35, 0.05, 0.7, 0.25, 0.08)


def comet(g):
    g.circle(0.3, 0.35, 0.8)
    g.poly([(0.0, 0.1), (0.55, 0.62), (-0.9, -0.9)])
    g.poly([(0.25, 0.05), (0.62, 0.35), (-0.3, -0.95)])
    g.poly([(-0.05, 0.3), (0.35, 0.68), (-0.95, -0.3)])


def horned_helm(g):
    # helm dome with cheek guards, T-visor and sweeping horns
    g.circle(0, 0.05, 1.0)
    g.poly([(-0.5, 0.05), (0.5, 0.05), (0.42, 0.78), (0.12, 0.62), (-0.12, 0.62), (-0.42, 0.78)])
    g.rect(-0.34, 0.08, 0.34, 0.2, 0)
    g.rect(-0.06, 0.08, 0.06, 0.5, 0)
    for sgn in (-1, 1):
        g.poly([(sgn * 0.38, -0.12), (sgn * 0.62, -0.32), (sgn * 0.86, -0.72), (sgn * 0.78, -0.95), (sgn * 0.7, -0.62), (sgn * 0.48, -0.36), (sgn * 0.3, -0.26)])


def fountain(g):
    g.rrect(-0.75, 0.4, 0.75, 0.75, 0.1)
    g.rect(-0.1, -0.3, 0.1, 0.45)
    g.arc(0, -0.2, 1.0, 190, 350, 0.08)
    g.circle(0, -0.4, 0.3)


def roots(g):
    g.line([(0, -0.8), (0, 0.2)], 0.14)
    for dx in (-0.6, -0.25, 0.25, 0.6):
        g.line([(0, 0.1), (dx * 0.6, 0.45), (dx, 0.85)], 0.1)


def snail(g):
    g.circle(0.1, -0.1, 1.0)
    g.circle(0.1, -0.1, 0.55, 0)
    g.circle(0.1, -0.1, 0.25)
    g.rrect(-0.85, 0.3, 0.8, 0.55, 0.1)


def mouth_x(g):
    g.poly([(-0.8, -0.05), (0.8, -0.05), (0.6, 0.35), (-0.6, 0.35)])
    g.line([(-0.6, -0.6), (0.6, 0.7)], 0.14)
    g.line([(0.6, -0.6), (-0.6, 0.7)], 0.14)


def toad(g):
    g.circle(0, 0.2, 1.3)
    g.circle(-0.35, -0.35, 0.45)
    g.circle(0.35, -0.35, 0.45)
    g.circle(-0.35, -0.35, 0.18, 0)
    g.circle(0.35, -0.35, 0.18, 0)


def bubble(g):
    g.ring(0, 0, 1.5, 0.12)
    g.arc(0, 0, 1.1, 200, 260, 0.1)


def broken_sword(g):
    sword(g, 0.0, 1.5, 0.18, 0.6)
    g.poly([(-0.3, -0.25), (0.3, -0.45), (0.3, -0.35), (-0.3, -0.15)], 0)


def coin(g):
    g.circle(0, 0, 1.4)
    g.circle(0, 0, 1.1, 0)
    g.circle(0, 0, 0.95)
    drop_small = [(0, -0.45), (0.25, 0.1), (0, 0.35), (-0.25, 0.1)]
    g.poly(drop_small, 0)


def scepter(g):
    g.line([(-0.55, 0.85), (0.25, -0.35)], 0.12)
    crown_pts = [(0.05, -0.35), (0.15, -0.75), (0.3, -0.5), (0.45, -0.85), (0.55, -0.45), (0.55, -0.25), (0.35, -0.2)]
    g.poly(crown_pts)
    g.circle(0.32, -0.45, 0.2, 0)


def talisman(g):
    g.poly([(0, -0.9), (0.55, 0), (0, 0.9), (-0.55, 0)])
    g.poly([(0, -0.5), (0.28, 0), (0, 0.5), (-0.28, 0)], 0)
    g.circle(0, 0, 0.22)


def chainmail(g):
    g.poly([(-0.55, -0.7), (-0.2, -0.8), (0, -0.6), (0.2, -0.8), (0.55, -0.7), (0.75, -0.3), (0.5, -0.15), (0.5, 0.8), (-0.5, 0.8), (-0.5, -0.15), (-0.75, -0.3)])
    for y in np.linspace(-0.4, 0.6, 5):
        for x in np.linspace(-0.35, 0.35, 4):
            g.circle(x, y, 0.1, 0)



# --- concept heroes (Nyxara, Malgrave, Ardyn, Fenrax, Morwen, Thael)

def bat(g):
    g.circle(0, 0.05, 0.24)
    g.poly([(-0.16, -0.08), (-0.2, -0.42), (-0.04, -0.16)])
    g.poly([(0.16, -0.08), (0.2, -0.42), (0.04, -0.16)])
    for side in (-1, 1):
        g.poly([(0, -0.05), (side * 0.3, -0.5), (side * 0.95, -0.62), (side * 0.9, 0.12), (side * 0.7, -0.02),
                (side * 0.58, 0.38), (side * 0.38, 0.16), (side * 0.18, 0.42), (0, 0.2)])


def rose(g):
    g.line([(0, 0.2), (0.05, 0.95)], 0.1)
    g.poly([(0.05, 0.55), (0.45, 0.35), (0.3, 0.6)])
    g.poly([(0.02, 0.75), (-0.35, 0.6), (-0.15, 0.8)])
    g.circle(0, -0.3, 0.55)
    g.arc(0, -0.3, 0.38, 200, 520, 0.08, 0)
    g.arc(0.03, -0.32, 0.18, 0, 300, 0.07, 0)
    for x, y in ((0.12, 0.35), (-0.06, 0.62)):
        g.poly([(x - 0.04, y), (x + 0.12, y - 0.06), (x - 0.02, y + 0.06)])


def midnight(g):
    g.circle(-0.1, -0.1, 0.8)
    g.circle(0.28, -0.3, 0.68, 0)
    g.poly(rot([(-0.05, -0.95), (0.05, -0.95), (0.08, 0.45), (-0.08, 0.45)], 0.6))
    g.poly(rot([(-0.28, 0.45), (0.28, 0.45), (0.28, 0.55), (-0.28, 0.55)], 0.6))


def bones(g):
    for a in (0.785, -0.785):
        pts = rot([(-0.8, -0.07), (0.8, -0.07), (0.8, 0.07), (-0.8, 0.07)], a)
        g.poly(pts)
        for ex, ey in rot([(-0.82, -0.12), (-0.82, 0.12), (0.82, -0.12), (0.82, 0.12)], a):
            g.circle(ex, ey, 0.26)


def ribcage(g):
    g.rect(-0.07, -0.85, 0.07, 0.8)
    for i in range(5):
        y = -0.6 + i * 0.3
        w = 0.8 - abs(i - 1.5) * 0.12
        g.arc(-0.02, y + 0.35, w * 1.6, 190, 260, 0.12)
        g.arc(0.02, y + 0.35, w * 1.6, 280, 350, 0.12)


def snowflake(g):
    for i in range(6):
        a = i * math.pi / 3
        g.line(rot([(0, 0), (0, -0.9)], a), 0.11)
        g.line(rot([(0, -0.5), (0.22, -0.72)], a), 0.08)
        g.line(rot([(0, -0.5), (-0.22, -0.72)], a), 0.08)


def tombstone(g):
    g.rrect(-0.55, -0.6, 0.55, 0.8, 0.5)
    g.rect(-0.8, 0.68, 0.8, 0.9)
    g.rect(-0.06, -0.35, 0.06, 0.35, 0)
    g.rect(-0.25, -0.18, 0.25, -0.06, 0)


def aegis(g):
    g.poly([(-0.6, -0.55), (0.6, -0.55), (0.6, 0.05), (0, 0.85), (-0.6, 0.05)])
    g.circle(0, -0.1, 0.55, 0)
    g.circle(0, -0.1, 0.35)
    for i in range(8):
        a = i * math.pi / 4
        g.poly(rot([(-0.05, -0.38), (0.05, -0.38), (0, -0.62)], a, 0, 0), 255)


def banner(g):
    g.rect(-0.62, -0.95, -0.5, 0.95)
    g.circle(-0.56, -0.95, 0.16)
    g.poly([(-0.5, -0.8), (0.75, -0.8), (0.55, -0.35), (0.75, 0.1), (-0.5, 0.1)])
    g.circle(0.08, -0.35, 0.35, 0)
    g.circle(0.08, -0.35, 0.18)


def wolf_head(g, s=1.0, dx=0.0, dy=0.0):
    T = lambda pts: [(x * s + dx, y * s + dy) for x, y in pts]
    g.poly(T([(-0.55, -0.2), (-0.7, -0.9), (-0.25, -0.5), (0.25, -0.5), (0.7, -0.9), (0.55, -0.2), (0.45, 0.25),
              (0.12, 0.85), (-0.12, 0.85), (-0.45, 0.25)]))
    g.poly(T([(-0.35, -0.15), (-0.12, -0.02), (-0.32, 0.02)]), 0)
    g.poly(T([(0.35, -0.15), (0.12, -0.02), (0.32, 0.02)]), 0)
    g.circle(dx, 0.72 * s + dy, 0.09 * s, 0)


def howl(g):
    wolf_head(g, 0.78, -0.18, 0.15)
    for r in (0.3, 0.52, 0.74):
        g.arc(0.3, -0.45, r, 280, 350, 0.08)


def moonfang(g):
    g.circle(0.1, -0.1, 0.9)
    g.circle(0.45, -0.35, 0.78, 0)
    wolf_head(g, 0.72, -0.05, 0.22)


def cauldron(g):
    g.circle(0, 0.22, 0.62)
    g.rrect(-0.72, -0.34, 0.72, -0.16, 0.06)
    g.line([(-0.4, 0.72), (-0.55, 0.95)], 0.12)
    g.line([(0.4, 0.72), (0.55, 0.95)], 0.12)
    g.arc(0, 0.25, 0.45, 20, 160, 0.06, 0)
    for x, y, r in ((-0.22, -0.52, 0.13), (0.12, -0.68, 0.1), (0.32, -0.48, 0.08)):
        g.ring(x, y, r, 0.06)


def veil(g):
    g.poly([(-0.75, 0.9), (-0.6, -0.3), (-0.3, -0.75), (0, -0.85), (0.3, -0.75), (0.6, -0.3), (0.75, 0.9),
            (0.4, 0.7), (0, 0.9), (-0.4, 0.7)])
    g.circle(0, -0.15, 0.3, 0)
    for i in range(-3, 4):
        g.line([(i * 0.2 - 0.3, 0.9), (i * 0.2 + 0.3, -0.6)], 0.035, 0)


def curse(g):
    g.poly([(-0.9, 0), (-0.45, -0.4), (0.45, -0.4), (0.9, 0), (0.45, 0.4), (-0.45, 0.4)])
    g.circle(0, 0, 0.62, 0)
    g.ring(0, 0, 0.5, 0.08)
    g.circle(0, 0, 0.16)
    g.line([(-0.1, 0.55), (0.1, 0.8), (-0.1, 0.95)], 0.07)


def hourglass(g):
    g.rect(-0.62, -0.9, 0.62, -0.75)
    g.rect(-0.62, 0.75, 0.62, 0.9)
    g.poly([(-0.5, -0.75), (0.5, -0.75), (0.06, 0), (0.5, 0.75), (-0.5, 0.75), (-0.06, 0)])
    g.poly([(-0.36, -0.62), (0.36, -0.62), (0.05, -0.12), (-0.05, -0.12)], 0)
    g.poly([(-0.05, 0.25), (0.05, 0.25), (0.3, 0.62), (-0.3, 0.62)], 0)


def tree(g):
    g.poly([(-0.14, 0.9), (-0.1, -0.1), (0.1, -0.1), (0.14, 0.9)])
    g.line([(0, 0.2), (-0.45, -0.2)], 0.09)
    g.line([(0, 0.05), (0.42, -0.3)], 0.09)
    for x, y, r in ((0, -0.45, 0.85), (-0.45, -0.25, 0.6), (0.45, -0.3, 0.62), (0, -0.8, 0.55)):
        g.circle(x, y, r)
    g.rect(-0.4, 0.82, 0.4, 0.92)


def leaf(g):
    g.poly(rot([(0, -0.9), (0.45, -0.35), (0.4, 0.3), (0, 0.75), (-0.4, 0.3), (-0.45, -0.35)], 0.5))
    g.line(rot([(0, -0.7), (0, 0.95)], 0.5), 0.07, 0)
    for t in (-0.35, 0.0, 0.35):
        g.line(rot([(0, t), (0.28, t - 0.25)], 0.5), 0.05, 0)
        g.line(rot([(0, t), (-0.28, t - 0.25)], 0.5), 0.05, 0)


def forest(g):
    for x, s in ((-0.5, 0.7), (0.5, 0.7), (0, 1.0)):
        g.poly([(x, -0.9 * s), (x + 0.4 * s, -0.1 * s), (x + 0.25 * s, -0.1 * s), (x + 0.5 * s, 0.5 * s),
                (x - 0.5 * s, 0.5 * s), (x - 0.25 * s, -0.1 * s), (x - 0.4 * s, -0.1 * s)])
        g.rect(x - 0.06 * s, 0.5 * s, x + 0.06 * s, 0.8)
    roots_y = 0.8
    g.rect(-0.9, roots_y, 0.9, roots_y + 0.1)

GLYPHS = {
    # abilities (by keyword)
    "feast": chalice, "charge": horned_helm, "rend": claw, "wrath": hammer, "bloodfall": comet, "fang": fangs,
    "covenant": drop, "lance": lance, "hemorrhage": splatter, "offering": hand, "exsanguinate": heart,
    "restoration": fountain, "fountain": fountain,
    # items
    "draught": potion, "potion": potion, "ember": flame, "watcher": eye, "eye": eye, "lantern": lantern, "truesight": lantern,
    "waystone": rune_stone, "circlet": crown, "crown": crown, "serpent": ring_glyph, "gauntlets": gauntlet, "gloves": gauntlet,
    "mantle": scroll, "lore": scroll, "belt": belt, "cowl": hood, "ring": ring_glyph, "band": ring_glyph, "wellspring": ring_glyph,
    "cloak": cloak, "crystal": crystal, "charm": amulet, "amulet": amulet, "blade": sword, "broadsword": sword,
    "greatsword": sword, "axe": axe, "cleaver": axe, "chainmail": chainmail, "bloodstone": crystal, "shard": crystal,
    "boots": boot, "haste": boot, "greaves": boot, "slippers": boot, "mask": mask, "voidpiercer": lance, "bulwark": shield,
    "plate": shield, "heart": heart, "stormcaller": lightning, "rod": staff, "scepter": scepter, "talisman": talisman,
    "shadowstep": talisman, "mercy": crown,
    # statuses
    "stun": lambda g: star_burst(g, 5), "bleed": drop, "slow": snail, "silence": mouth_x, "root": roots, "fear": skull,
    "hex": toad, "immune": shield, "invuln": sun, "reveal": eye, "barrier": bubble, "surge": drop, "tithe": coin,
    "bloodbound": chain, "disarm": broken_sword,
    # concept heroes
    "velvet": mask, "batwing": bat, "kiss": rose, "crimson_mark": rose, "veil": veil, "claws": claw,
    "restoring_brew": potion, "sentence": midnight,
    "ossuary": skull, "bone_shard": bones, "raise": hand, "cage": ribcage, "chill": snowflake, "legion": tombstone,
    "oathkeeper": shield, "fervor": flame, "aegis": aegis, "consecrate": sun, "judgment": hammer, "crusade": banner,
    "dazzled": lambda g: star_burst(g, 4, 0.3, 0.95), "hunger": moon, "pounce": wolf_head, "rent_armor": claw,
    "howl": howl, "unleashed": moonfang, "moonfang": moonfang, "pact": scroll, "toad": toad, "cauldron": cauldron,
    "brew": potion, "fumes": bubble, "curse": curse, "witching": hourglass, "barkskin": tree, "bark": tree,
    "treant": tree, "grove": leaf, "thael_wrath": forest,
    # Vharoth
    "break_seal": rune_stone, "vharoth_hide": horned_helm, "crush": hand, "vharoth_rain": drop,
    "vharoth_wrath": skull, "titan_wrath": skull, "bloodthirst": fangs,
}


def glyph_for(key):
    k = key.lower()
    best = None
    for word, fn in GLYPHS.items():
        if word in k and (best is None or len(word) > len(best[0])):
            best = (word, fn)
    return best[1] if best else (lambda g: star_burst(g, 8, 0.45, 0.85))


# ------------------------------------------------------------------------------------------------ rendering

THEMES = {
    "blood": ((120, 10, 20), (230, 60, 50), "bone"),
    "gold": ((110, 80, 20), (255, 200, 110), "gold"),
    "steel": ((40, 48, 60), (170, 190, 210), "steel"),
    "arcane": ((30, 30, 110), (130, 150, 255), "steel"),
    "nature": ((20, 70, 30), (130, 230, 110), "bone"),
    "shadow": ((50, 20, 80), (190, 120, 255), "steel"),
    "bone": ((70, 60, 50), (220, 200, 160), "bone"),
    "fire": ((130, 40, 10), (255, 160, 60), "gold"),
    "holy": ((110, 90, 40), (255, 240, 180), "gold"),
    "debuff": ((110, 15, 15), (255, 90, 70), "bone"),
    "buff": ((20, 80, 40), (140, 255, 150), "gold"),
}

METALS = {
    "gold": ((255, 214, 140), (120, 70, 20)),
    "steel": ((220, 228, 236), (60, 66, 80)),
    "bone": ((240, 228, 204), (110, 96, 80)),
    "crimson": ((255, 120, 110), (110, 10, 18)),
}


def background(w, h, theme, seed):
    dark, light, _ = THEMES[theme]
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    cx, cy = w / 2, h * 0.46
    d = np.sqrt(((xs - cx) / w) ** 2 + ((ys - cy) / h) ** 2) * 2
    n = tile_noise(w, h, 3, 5, seed=seed) * 0.6 + tile_noise(w, h, 7, 4, seed=seed + 1) * 0.4
    glow = np.clip(1 - d, 0, 1) ** 1.6
    base = np.array(dark, np.float32) * (0.25 + 0.35 * n[..., None]) + np.array(light, np.float32) * glow[..., None] * 0.55
    vign = smoothstep(1.35, 0.55, d)
    return base * (0.35 + 0.65 * vign[..., None])


def emboss(mask_img, metal, size):
    m = np.asarray(mask_img, np.float32) / 255.0
    inside = m > 0.5
    dist = ndimage.distance_transform_edt(inside).astype(np.float32)
    bevel = np.clip(dist / (size * 0.035), 0, 1)
    height = np.sqrt(bevel) * m
    height = ndimage.gaussian_filter(height, size * 0.004)
    diff, spec = shade(height * 6.0, strength=size * 0.05, light=(-0.6, -0.7, 0.45))
    hi, lo = METALS[metal]
    hi = np.array(hi, np.float32)
    lo = np.array(lo, np.float32)
    col = lo[None, None, :] * (1 - diff[..., None]) + hi[None, None, :] * diff[..., None]
    col += np.array([255, 245, 225], np.float32) * spec[..., None] * 0.8
    return col, m


def compose(glyph_fn, w, h, theme, seed, scale=0.78):
    _, light, metal = THEMES[theme]
    S = max(w, h) * SS
    g = G(S)
    glyph_fn(g)
    mask = g.img
    # centre the glyph and scale into the icon area
    bbox = mask.getbbox()
    if bbox:
        crop = mask.crop(bbox)
        bw, bh = crop.size
        target = int(min(w, h) * SS * scale)
        k = target / max(bw, bh)
        crop = crop.resize((max(1, int(bw * k)), max(1, int(bh * k))), Image.LANCZOS)
        canvas = Image.new("L", (w * SS, h * SS), 0)
        canvas.paste(crop, ((w * SS - crop.width) // 2, (h * SS - crop.height) // 2))
        mask = canvas
    mask = mask.resize((w * 2, h * 2), Image.LANCZOS)
    col, m = emboss(mask, metal, w * 2)
    bg = background(w * 2, h * 2, theme, seed)
    # outer glow + dark drop shadow for separation
    glow = np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(w * 0.09)), np.float32) / 255
    shadow = np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(w * 0.04)), np.float32) / 255
    shadow = np.roll(shadow, (int(w * 0.03), int(w * 0.02)), axis=(0, 1))
    out = bg * (1 - shadow[..., None] * 0.7) + np.array(light, np.float32) * glow[..., None] * 0.45
    out = out * (1 - m[..., None]) + col * m[..., None]
    img = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGB").resize((w, h), Image.LANCZOS)
    return img


# ------------------------------------------------------------------------------------------------ portraits

def portrait(hero, faction, w=256, h=320, seed=0):
    """Tarot-style bust silhouette against a faction halo with hero-specific headgear."""
    pal = {
        "CrimsonCourt": ((70, 6, 14), (255, 70, 60)),
        "AshenLegion": ((30, 40, 30), (150, 230, 140)),
        "WildCovenant": ((20, 40, 60), (170, 210, 255)),
        "Dawnguard": ((70, 55, 20), (255, 220, 140)),
    }.get(faction, ((40, 30, 40), (220, 200, 220)))
    dark, light = [np.array(c, np.float32) for c in pal]
    W, H = w * 2, h * 2
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
    cx, cy = W / 2, H * 0.36
    d = np.sqrt(((xs - cx) / W) ** 2 + ((ys - cy) / H * 1.1) ** 2) * 2
    n = tile_noise(W, H, 3, 5, seed=seed) * 0.6 + tile_noise(W, H, 8, 4, seed=seed + 7) * 0.4
    halo = np.clip(1 - d * 1.15, 0, 1) ** 1.3
    bg = dark * (0.3 + 0.5 * n[..., None]) + light * halo[..., None] * 0.9
    rays = (np.sin(np.arctan2(ys - cy, xs - cx) * 18) * 0.5 + 0.5) ** 6 * halo * 0.25
    bg += light * rays[..., None]

    S = W
    g = G(S, sc=0.78, dy=0.4)
    # bust: shoulders + neck + head in normalised coords (portrait is taller: map y to 0..H)
    sy = H / W

    def P(x, y):
        return (x, y)

    head_y = -0.35
    g.poly([(-1.0, 1.3), (-0.95, 0.75), (-0.55, 0.45), (-0.2, 0.35), (0.2, 0.35), (0.55, 0.45), (0.95, 0.75), (1.0, 1.3)])
    g.rect(-0.14, 0.05, 0.14, 0.42)
    g.circle(0, head_y, 0.62)
    hid = hero["id"]
    if "vorak" in hid:
        # horned war helm + pauldron spikes + greatsword hilt over shoulder
        g.line([(-0.22, head_y - 0.15), (-0.55, head_y - 0.5), (-0.5, head_y - 0.85)], 0.09)
        g.line([(0.22, head_y - 0.15), (0.55, head_y - 0.5), (0.5, head_y - 0.85)], 0.09)
        g.poly([(-0.95, 0.75), (-0.75, 0.2), (-0.55, 0.5)])
        g.poly([(0.95, 0.75), (0.75, 0.2), (0.55, 0.5)])
        g.poly([(0.55, 0.5), (0.62, -0.7), (0.7, -0.7), (0.72, 0.5)])
        g.rect(0.48, -0.55, 0.84, -0.5)
    elif "ilyra" in hid:
        # tall pointed hood draping to the shoulders, high collar, staff with orb
        g.poly([(0.08, head_y - 0.62), (0.4, head_y - 0.12), (0.62, 0.55), (-0.62, 0.55), (-0.4, head_y - 0.12)])
        g.poly([(-0.3, 0.2), (-0.55, -0.15), (-0.25, 0.05)])
        g.poly([(0.3, 0.2), (0.55, -0.15), (0.25, 0.05)])
        g.line([(-0.78, 1.2), (-0.66, -0.55)], 0.06)
        g.circle(-0.66, -0.66, 0.22)
    elif "nyxara" in hid:
        # tiara, tall fan collar, rapier held upright
        for i, x in enumerate((-0.3, -0.15, 0, 0.15, 0.3)):
            g.poly([(x - 0.06, head_y - 0.5), (x, head_y - (0.95 if i == 2 else 0.75)), (x + 0.06, head_y - 0.5)])
        g.poly([(-0.2, 0.35), (-0.85, -0.55), (-0.55, 0.45)])
        g.poly([(0.2, 0.35), (0.85, -0.55), (0.55, 0.45)])
        g.line([(0.78, 1.2), (0.7, -0.9)], 0.035)
        g.circle(0.77, 0.95, 0.2)
    elif "malgrave" in hid:
        # bone crown, high collar, scythe over the shoulder
        for x in (-0.36, -0.18, 0, 0.18, 0.36):
            g.line([(x, head_y - 0.45), (x * 1.2, head_y - 0.85)], 0.07)
        g.poly([(-0.25, 0.3), (-0.7, -0.45), (-0.5, 0.4)])
        g.poly([(0.25, 0.3), (0.7, -0.45), (0.5, 0.4)])
        g.line([(-0.8, 1.2), (-0.62, -0.9)], 0.06)
        g.poly([(-0.62, -0.9), (0.1, -0.75), (-0.55, -0.72)])
    elif "ardyn" in hid:
        # crested helm, sunburst behind the head, mace
        g.rect(-0.05, head_y - 0.95, 0.05, head_y - 0.3)
        g.poly([(-0.36, head_y - 0.05), (0, head_y - 0.62), (0.36, head_y - 0.05)])
        g.ring(0, head_y, 0.72, 0.05)
        g.poly([(-1.0, 0.8), (-0.75, 0.3), (-0.45, 0.55)])
        g.poly([(1.0, 0.8), (0.75, 0.3), (0.45, 0.55)])
        g.line([(0.8, 1.2), (0.72, -0.35)], 0.06)
        g.circle(0.72, -0.45, 0.28)
    elif "fenrax" in hid:
        # wolf-pelt hood with ears and a shaggy mantle
        g.poly([(-0.42, head_y - 0.2), (-0.5, head_y - 0.85), (-0.15, head_y - 0.45)])
        g.poly([(0.42, head_y - 0.2), (0.5, head_y - 0.85), (0.15, head_y - 0.45)])
        for i in range(9):
            x = -0.9 + i * 0.225
            g.poly([(x - 0.12, 0.5), (x, 0.2 - (i % 2) * 0.12), (x + 0.12, 0.5)])
    elif "morwen" in hid:
        # crooked wide-brimmed witch's hat and a gnarled staff
        g.poly([(-0.95, head_y - 0.3), (0.95, head_y - 0.3), (0.85, head_y - 0.18), (-0.85, head_y - 0.18)])
        g.poly([(-0.42, head_y - 0.3), (0.4, head_y - 0.3), (0.15, head_y - 0.9), (0.45, head_y - 1.2), (0.05, head_y - 1.0)])
        g.line([(-0.8, 1.2), (-0.7, 0.2), (-0.8, -0.3), (-0.66, -0.6)], 0.06)
        g.circle(-0.66, -0.68, 0.18)
    elif "thael" in hid:
        # branching antlers and a bark mantle
        for side in (-1, 1):
            g.line([(side * 0.25, head_y - 0.4), (side * 0.6, head_y - 0.95), (side * 0.9, head_y - 1.25)], 0.07)
            g.line([(side * 0.45, head_y - 0.7), (side * 0.35, head_y - 1.15)], 0.05)
            g.line([(side * 0.72, head_y - 1.05), (side * 0.95, head_y - 0.95)], 0.05)
        g.poly([(-1.0, 0.8), (-0.85, 0.3), (-0.6, 0.5), (-0.5, 0.25), (0.5, 0.25), (0.6, 0.5), (0.85, 0.3), (1.0, 0.8)])
    else:
        g.poly([(0, head_y - 0.55), (0.35, head_y - 0.2), (-0.35, head_y - 0.2)])
    BW = int(W * 0.78)
    mask = g.img.resize((BW, BW), Image.LANCZOS)
    # paste the bust into the portrait canvas: centred, bottom-aligned (shoulders run off the bottom edge)
    canvas = Image.new("L", (W, H), 0)
    ox, oy = (W - BW) // 2, H - BW + int(H * 0.1)
    canvas.paste(mask, (ox, oy))
    m = np.asarray(canvas, np.float32) / 255
    # silhouette with rim light from the halo
    blur = np.asarray(canvas.filter(ImageFilter.GaussianBlur(3)), np.float32) / 255
    gy, gx = np.gradient(blur)
    rim = np.clip(-gy * 2.5 + np.abs(gx) * 1.2, 0, 1) * m
    body = np.array([12, 9, 12], np.float32)[None, None, :] + light * rim[..., None] * 1.6
    out = bg * (1 - m[..., None]) + body * m[..., None]
    # eyes glow for vampire/undead factions
    if faction in ("CrimsonCourt", "AshenLegion"):
        e = G(W, sc=0.78, dy=0.4)
        e.circle(-0.12, head_y + 0.02, 0.07)
        e.circle(0.12, head_y + 0.02, 0.07)
        em = Image.new("L", (W, H), 0)
        em.paste(e.img.resize((BW, BW)), (ox, oy))
        eg = np.asarray(em.filter(ImageFilter.GaussianBlur(4)), np.float32) / 255
        out += np.array([255, 60, 50] if faction == "CrimsonCourt" else [120, 255, 140], np.float32) * eg[..., None] * 1.5
    # vignette + frame shade
    vg = smoothstep(1.2, 0.6, np.sqrt(((xs - W / 2) / W) ** 2 + ((ys - H / 2) / H) ** 2) * 2)
    out *= (0.4 + 0.6 * vg)[..., None]
    img = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGB").resize((w, h), Image.LANCZOS)
    return img


# ------------------------------------------------------------------------------------------------ data scan

def load_json(path):
    return json.loads(re.sub(r"(?m)^\s*//.*$", "", open(path, encoding="utf-8").read()))


def scan():
    heroes, abilities, items, statuses = {}, {}, {}, {}
    for d, _, files in os.walk(DATA):
        for f in files:
            if not f.endswith(".json"):
                continue
            j = load_json(os.path.join(d, f))
            for h in j.get("heroes", []) + ([j["hero"]] if "hero" in j else []):
                heroes[h["id"]] = h
            for a in j.get("abilities", []):
                abilities[a["id"]] = a
                for s in a.get("statuses") or []:
                    statuses[s["id"]] = s
            for s in j.get("statuses", []):
                statuses[s["id"]] = s
            for i in j.get("items", []):
                items[i["id"]] = i
                if i.get("active"):
                    for s in i["active"].get("statuses") or []:
                        statuses[s["id"]] = s
    return heroes, abilities, items, statuses


ITEM_THEME = {
    "Consumables": "blood", "Wards": "gold", "Attributes": "nature", "Armaments": "steel", "Arcane": "arcane", "Boots": "bone",
    "Offense": "fire", "Defense": "steel", "Magic": "shadow", "Support": "holy", "Relics": "blood",
}


def save(img, *parts):
    p = os.path.join(OUT, *parts)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    img.save(p, optimize=True)
    return p


def main():
    heroes, abilities, items, statuses = scan()
    n = 0
    ability_faction = {}
    for h in heroes.values():
        for aid in h.get("abilities", []):
            ability_faction[aid] = h.get("faction", "CrimsonCourt")
    for i, a in enumerate(sorted(abilities.values(), key=lambda x: x["id"])):
        icon = a.get("icon")
        if not icon:
            continue
        fac = ability_faction.get(a["id"])
        theme = {"CrimsonCourt": "blood", "AshenLegion": "bone", "WildCovenant": "nature", "Dawnguard": "holy"}.get(fac, "arcane")
        if a["id"].startswith("vharoth_"):
            theme = "blood"
        if a.get("isUltimate"):
            theme = "fire" if theme == "blood" else theme
        save(compose(glyph_for(icon), 128, 128, theme, 100 + i), "Abilities", icon + ".png")
        n += 1
    for i, it in enumerate(sorted(items.values(), key=lambda x: x["id"])):
        icon = it.get("icon") or it["id"]
        save(compose(glyph_for(icon), 128, 108, ITEM_THEME.get(it.get("category"), "steel"), 300 + i, 0.72), "Items", icon + ".png")
        n += 1
    for i, s in enumerate(sorted(statuses.values(), key=lambda x: x["id"])):
        icon = s.get("icon")
        if not icon:
            continue
        save(compose(glyph_for(icon), 64, 64, "debuff" if s.get("isDebuff") else "buff", 600 + i, 0.7), "Statuses", icon + ".png")
        n += 1
    for i, h in enumerate(sorted(heroes.values(), key=lambda x: x["id"])):
        key = h.get("portrait") or "portrait_" + h["id"].replace("hero_", "")
        save(portrait(h, h.get("faction", "CrimsonCourt"), seed=900 + i), "Portraits", key + ".png")
        n += 1
    print(f"wrote {n} icons/portraits to {os.path.relpath(OUT, ROOT)}")


if __name__ == "__main__":
    main()
