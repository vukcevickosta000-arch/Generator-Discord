#!/usr/bin/env python3
"""
Procedural audio for Bloodfall (placeholder quality, fully original): UI sounds, combat SFX, spells, deaths, structures,
ambience loops, music loops/stingers and announcer lines. Output: OGG Vorbis in Client/Assets/Resources/Audio/<Category>/.

Everything is synthesised with numpy/scipy (oscillators, filtered noise, Karplus-Strong plucks, inharmonic bells,
formant "choirs", Schroeder reverb). Announcer lines use espeak-ng (if installed) pitched down and processed into a deep,
reverberant voice; without espeak-ng they are skipped with a message. Re-run any time; the output is deterministic.
"""
import math
import os
import shutil
import subprocess
import sys
import tempfile

import numpy as np
import soundfile as sf
from scipy import signal

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Client", "Assets", "Resources", "Audio")
SR = 44100
R = np.random.default_rng(1234)


# ============================================================================================== DSP helpers

def t_axis(dur):
    return np.arange(int(dur * SR)) / SR


def env(n, a=0.005, d=0.1, s=0.0, r=0.1, sustain_time=0.0):
    """ADSR envelope with lengths in seconds, total samples n."""
    e = np.zeros(n)
    ia, idd, isus, ir = int(a * SR), int(d * SR), int(sustain_time * SR), int(r * SR)
    i = 0
    seg = min(ia, n - i); e[i:i + seg] = np.linspace(0, 1, seg, endpoint=False) if seg > 0 else 0; i += seg
    seg = min(idd, n - i); e[i:i + seg] = np.linspace(1, s, seg, endpoint=False) if seg > 0 else 0; i += seg
    seg = min(isus, n - i); e[i:i + seg] = s; i += seg
    seg = min(ir, n - i); e[i:i + seg] = np.linspace(s, 0, seg) if seg > 0 else 0; i += seg
    return e


def exp_env(n, tau):
    return np.exp(-np.arange(n) / SR / max(1e-4, tau))


def osc(freq, dur, kind="sine", phase=0.0):
    n = int(dur * SR)
    if np.isscalar(freq):
        f = np.full(n, float(freq))
    else:
        f = np.asarray(freq, np.float64)[:n]
        if len(f) < n:  # hold the final frequency (e.g. a short pitch sweep on a longer note)
            f = np.concatenate([f, np.full(n - len(f), f[-1] if len(f) else 0.0)])
    ph = 2 * np.pi * np.cumsum(f) / SR + phase
    if kind == "sine":
        return np.sin(ph)
    if kind == "saw":
        return 2 * ((ph / (2 * np.pi)) % 1.0) - 1
    if kind == "square":
        return np.sign(np.sin(ph))
    if kind == "tri":
        return 2 * np.abs(2 * ((ph / (2 * np.pi)) % 1.0) - 1) - 1
    raise ValueError(kind)


def sweep(f0, f1, dur, curve="exp"):
    n = int(dur * SR)
    if curve == "exp":
        return f0 * (f1 / f0) ** np.linspace(0, 1, n)
    return np.linspace(f0, f1, n)


def noise(dur):
    return R.standard_normal(int(dur * SR))


def bp(x, lo, hi, order=2):
    sos = signal.butter(order, [max(20, lo), min(SR / 2 - 100, hi)], btype="band", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def lp(x, f, order=2):
    sos = signal.butter(order, min(SR / 2 - 100, f), btype="low", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def hp(x, f, order=2):
    sos = signal.butter(order, max(20, f), btype="high", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def sweep_filter(x, f0, f1, q=4.0, kind="lp", blocks=64):
    """Time-varying filter by processing overlapping blocks with interpolated cutoff."""
    out = np.zeros_like(x)
    n = len(x)
    bl = max(256, n // blocks)
    win = np.hanning(bl * 2)
    for start in range(-bl, n, bl):
        s0, s1 = max(0, start), min(n, start + bl * 2)
        if s1 <= s0:
            continue
        frac = min(1.0, max(0.0, (start + bl) / max(1, n)))
        fc = f0 * (f1 / f0) ** frac
        seg = x[s0:s1]
        sos = signal.butter(2, min(SR / 2 - 200, max(30, fc)), btype="low" if kind == "lp" else "high", fs=SR, output="sos")
        y = signal.sosfilt(sos, seg)
        w = win[(s0 - start):(s0 - start) + len(y)]
        out[s0:s1] += y * w
    return out


def pluck(freq, dur, damping=0.996, bright=0.5):
    n = int(dur * SR)
    period = max(2, int(SR / freq))
    buf = R.uniform(-1, 1, period) * bright + np.sin(np.linspace(0, 2 * np.pi, period)) * (1 - bright)
    out = np.zeros(n)
    for i in range(n):
        out[i] = buf[i % period]
        buf[i % period] = damping * 0.5 * (buf[i % period] + buf[(i + 1) % period])
    return out


def bell(freq, dur, decay=1.2, partials=((1, 1.0), (2.76, 0.5), (5.4, 0.25), (8.93, 0.12), (13.3, 0.06))):
    t = t_axis(dur)
    x = np.zeros_like(t)
    for ratio, amp in partials:
        x += amp * np.sin(2 * np.pi * freq * ratio * t) * np.exp(-t * ratio ** 0.6 / decay)
    return x


def formant(x, vowel="ah"):
    table = {"ah": (700, 1220, 2600), "oh": (500, 900, 2400), "oo": (320, 800, 2300), "eh": (530, 1840, 2480)}
    f1, f2, f3 = table[vowel]
    return bp(x, f1 * 0.8, f1 * 1.25) * 1.0 + bp(x, f2 * 0.85, f2 * 1.15) * 0.5 + bp(x, f3 * 0.9, f3 * 1.1) * 0.25


def reverb(x, room=0.8, wet=0.35, pre=0.02):
    """Schroeder reverb: 4 combs + 2 allpasses (per channel)."""
    x = np.asarray(x, np.float64)
    combs = [1116, 1188, 1277, 1356]
    allp = [556, 441]
    pad = np.concatenate([np.zeros(int(pre * SR)), x, np.zeros(int(room * 2.5 * SR))])
    y = np.zeros_like(pad)
    for c in combs:
        d = int(c * (0.7 + room * 0.6))
        g = 0.70 + 0.25 * room
        a = np.zeros(d + 1); a[0] = 1; a[d] = -g
        y += signal.lfilter([1.0], a, pad)
    y /= len(combs)
    for c in allp:
        g = 0.5
        b = np.zeros(c + 1); b[0] = -g; b[c] = 1
        a = np.zeros(c + 1); a[0] = 1; a[c] = -g
        y = signal.lfilter(b, a, y)
    y = lp(y, 6000)
    dry = np.concatenate([x, np.zeros(len(y) - len(x))])
    return dry * (1 - wet) + y * wet


def dist(x, drive=2.0):
    return np.tanh(x * drive) / np.tanh(drive)


def fade(x, fin=0.002, fout=0.02):
    n = len(x)
    a, b = min(n, int(fin * SR)), min(n, int(fout * SR))
    if a > 0:
        x[:a] *= np.linspace(0, 1, a)
    if b > 0:
        x[-b:] *= np.linspace(1, 0, b)
    return x


def trim_silence(x, thresh=1e-3):
    mag = np.abs(x) if x.ndim == 1 else np.abs(x).max(axis=1)
    idx = np.where(mag > thresh)[0]
    if len(idx) == 0:
        return x
    return x[: idx[-1] + int(0.05 * SR)]


def normalize(x, peak=0.89):
    m = np.max(np.abs(x))
    return x * (peak / m) if m > 1e-9 else x


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n) if parts[0].ndim == 1 else np.zeros((n, parts[0].shape[1]))
    for p in parts:
        out[: len(p)] += p
    return out


def at(x, offset, total):
    out = np.zeros(total) if x.ndim == 1 else np.zeros((total, x.shape[1]))
    o = int(offset * SR)
    if o < total:
        seg = x[: total - o]
        out[o:o + len(seg)] += seg
    return out


def stereo(x, width=0.3, delay_ms=12):
    d = int(delay_ms / 1000 * SR)
    l = x
    r = np.concatenate([np.zeros(d), x[:-d]]) if d > 0 else x
    mid = (l + r) / 2
    side = (l - r) / 2 * width
    return np.stack([mid + side, mid - side], axis=1)


def write(cat, name, x, loop=False):
    x = np.asarray(x, np.float64)
    if loop:
        # crossfade the reverb tail into the start so the loop is seamless
        cf = int(1.5 * SR)
        if len(x) > cf * 3:
            head, tail = x[:cf].copy(), x[-cf:].copy()
            w = np.linspace(0, 1, cf)
            w = w[:, None] if x.ndim == 2 else w
            x = x[:-cf].copy()
            x[:cf] = head * w + tail * (1 - w)
    else:
        x = trim_silence(x)
        x = fade(x) if x.ndim == 1 else np.stack([fade(x[:, 0].copy()), fade(x[:, 1].copy())], axis=1)
    x = normalize(x, 0.85 if cat in ("Music", "Ambience") else 0.92)
    path = os.path.join(OUT, cat, name + ".ogg")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    data = np.ascontiguousarray(x.astype(np.float32))
    channels = 1 if data.ndim == 1 else data.shape[1]
    # libsndfile's Vorbis encoder can crash on very large single writes; stream in blocks instead.
    with sf.SoundFile(path, "w", samplerate=SR, channels=channels, format="OGG", subtype="VORBIS") as f:
        for i in range(0, len(data), 8192):
            f.write(data[i:i + 8192])
    return path


# ============================================================================================== building blocks

def whoosh(dur=0.3, f0=400, f1=2500, amp=1.0):
    n = noise(dur)
    x = sweep_filter(n, f0, f1, kind="lp")
    e = np.sin(np.linspace(0, np.pi, len(x))) ** 1.5
    return x * e * amp


def metal_hit(freq=900, dur=0.5, amp=1.0):
    return bell(freq, dur, decay=0.18, partials=((1, 1), (1.47, 0.7), (2.09, 0.5), (2.56, 0.4), (3.9, 0.25))) * amp


def flesh_hit(dur=0.18, amp=1.0):
    x = lp(noise(dur), 900) * exp_env(int(dur * SR), 0.04)
    thump = osc(sweep(160, 60, dur), dur) * exp_env(int(dur * SR), 0.05)
    return (x * 0.8 + thump) * amp


def thud(freq=90, dur=0.4, amp=1.0):
    return osc(sweep(freq * 1.8, freq, dur), dur) * exp_env(int(dur * SR), dur / 3) * amp + lp(noise(dur), 400) * exp_env(int(dur * SR), 0.05) * 0.5 * amp


def rumble(dur=2.0, amp=1.0, f=180):
    x = lp(noise(dur), f, 4)
    return x * env(len(x), 0.05, dur * 0.3, 0.6, dur * 0.6, dur * 0.1) * amp


def debris(dur=1.5, amp=1.0, count=40):
    total = int(dur * SR)
    out = np.zeros(total)
    for _ in range(count):
        o = R.uniform(0, dur * 0.8)
        f = R.uniform(400, 3000)
        clk = bp(noise(0.05), f * 0.7, f * 1.3) * exp_env(int(0.05 * SR), 0.01)
        out += at(clk * R.uniform(0.2, 1.0), o, total)
    return out * amp


def growl(dur=0.6, f=90, amp=1.0, vowel="oh"):
    t = t_axis(dur)
    vib = 1 + 0.08 * np.sin(2 * np.pi * 7 * t) + 0.04 * R.standard_normal(len(t)).cumsum() / 3000
    x = osc(f * vib, dur, "saw") + 0.3 * noise(dur)
    x = formant(x, vowel)
    return dist(x * env(len(x), 0.03, 0.1, 0.8, dur * 0.4, dur * 0.5), 2.5) * amp


def chord_pad(freqs, dur, kind="saw", cutoff=1800, detune=0.004, amp=1.0):
    x = np.zeros(int(dur * SR))
    for f in freqs:
        for dt in (-detune, 0, detune):
            x += osc(f * (1 + dt), dur, kind, R.uniform(0, 6.28))
    x = lp(x, cutoff, 2) / (len(freqs) * 3)
    return x * env(len(x), dur * 0.25, 0.1, 1.0, dur * 0.4, dur * 0.35) * amp


def choir(freqs, dur, vowel="ah", amp=1.0):
    x = np.zeros(int(dur * SR))
    t = t_axis(dur)
    for f in freqs:
        for k in range(3):
            vib = 1 + 0.006 * np.sin(2 * np.pi * (5 + k * 0.4) * t + k)
            x += osc(f * vib * (1 + (k - 1) * 0.003), dur, "saw", R.uniform(0, 6.28))
    x = formant(x, vowel) / (len(freqs) * 3)
    return x * env(len(x), dur * 0.3, 0.1, 1.0, dur * 0.35, dur * 0.35) * amp


def brass(freq, dur, amp=1.0):
    t = t_axis(dur)
    vib = 1 + 0.004 * np.sin(2 * np.pi * 5.5 * t) * np.clip(t / 0.3, 0, 1)
    x = osc(freq * vib, dur, "saw") + 0.5 * osc(freq * 2.001 * vib, dur, "saw")
    e = env(len(x), 0.04, 0.15, 0.75, dur * 0.6, 0.2)
    bright = sweep_filter(x, 3500, 900, kind="lp")
    return bright * e * amp


def taiko(dur=0.8, f=70, amp=1.0):
    body = osc(sweep(f * 2.2, f, 0.25), dur) * exp_env(int(dur * SR), 0.18)
    skin = lp(noise(dur), 1200) * exp_env(int(dur * SR), 0.03)
    return (body + skin * 0.6) * amp


def note(name):
    names = {"C": -9, "C#": -8, "Db": -8, "D": -7, "D#": -6, "Eb": -6, "E": -5, "F": -4, "F#": -3, "Gb": -3, "G": -2, "G#": -1, "Ab": -1, "A": 0, "A#": 1, "Bb": 1, "B": 2}
    n, o = name[:-1], int(name[-1])
    return 440.0 * 2 ** ((names[n] + (o - 4) * 12) / 12)


# ============================================================================================== UI

def ui():
    out = []
    click = mix(bp(noise(0.03), 1800, 6000) * exp_env(int(0.03 * SR), 0.006), metal_hit(2400, 0.08, 0.25))
    out.append(write("UI", "click", click))
    out.append(write("UI", "hover", bp(noise(0.02), 3000, 8000) * exp_env(int(0.02 * SR), 0.004) * 0.4))
    tab = mix(bp(noise(0.08), 500, 3000) * exp_env(int(0.08 * SR), 0.02), metal_hit(1300, 0.1, 0.15))
    out.append(write("UI", "tab", tab))
    err = (osc(110, 0.25, "square") + osc(116, 0.25, "square")) * env(int(0.25 * SR), 0.005, 0.05, 0.6, 0.12, 0.08)
    out.append(write("UI", "error", lp(err, 1600) * 0.6))
    notify = mix(bell(note("E5"), 0.8, 0.4) * 0.6, at(bell(note("B5"), 0.8, 0.4) * 0.6, 0.09, int(0.9 * SR)))
    out.append(write("UI", "notify", reverb(notify, 0.4, 0.25)))
    out.append(write("UI", "panel_open", reverb(whoosh(0.22, 300, 3000, 0.8), 0.3, 0.2)))
    out.append(write("UI", "panel_close", reverb(whoosh(0.2, 3000, 300, 0.7), 0.3, 0.2)))
    mf = mix(brass(note("D3"), 1.4, 0.9), brass(note("A3"), 1.4, 0.6), at(bell(note("D5"), 2.5, 1.2), 0.05, int(2.6 * SR)), taiko(1.0, 55, 1.2))
    out.append(write("UI", "match_found", reverb(mf, 0.7, 0.35)))
    unsheathe = mix(hp(whoosh(0.35, 2000, 8000), 1500), at(metal_hit(1800, 0.6, 0.6), 0.25, int(0.9 * SR)))
    lock = mix(unsheathe, at(bell(note("A5"), 1.0, 0.6) * 0.5, 0.3, int(1.4 * SR)))
    out.append(write("UI", "lock_in", reverb(lock, 0.5, 0.25)))
    out.append(write("UI", "buy", coins(5)))
    out.append(write("UI", "shop_open", mix(bp(noise(0.3), 300, 2000) * env(int(0.3 * SR), 0.02, 0.1, 0.4, 0.1, 0.08) * 0.5, at(coins(3), 0.1, int(0.8 * SR)))))
    out.append(write("UI", "ping", reverb(bell(note("G5"), 1.0, 0.35), 0.5, 0.3)))
    return out


def coins(count=4):
    total = int(0.8 * SR)
    out = np.zeros(total)
    for i in range(count):
        f = R.uniform(2800, 4200)
        out += at(bell(f, 0.35, 0.08, ((1, 1), (1.52, 0.6), (2.31, 0.3))) * R.uniform(0.4, 1.0), i * 0.055 + R.uniform(0, 0.02), total)
    return out


# ============================================================================================== combat SFX

def sfx():
    out = []
    W = lambda name, x: out.append(write("Sfx", name, x))
    W("sword_light", mix(whoosh(0.18, 800, 5000, 0.6), at(mix(metal_hit(1500, 0.3, 0.5), flesh_hit(0.15, 0.6)), 0.12, int(0.5 * SR))))
    W("sword_heavy", mix(whoosh(0.3, 400, 3500, 0.8), at(mix(metal_hit(900, 0.5, 0.6), flesh_hit(0.25, 1.0), thud(80, 0.3, 0.5)), 0.2, int(0.8 * SR))))
    W("vorak_greatsword", reverb(mix(whoosh(0.35, 250, 3000, 1.0), at(mix(metal_hit(700, 0.6, 0.7), flesh_hit(0.3, 1.2), thud(60, 0.4, 0.8)), 0.25, int(1.0 * SR))), 0.4, 0.15))
    W("hammer_heavy", mix(whoosh(0.3, 300, 2000, 0.6), at(mix(thud(70, 0.5, 1.2), metal_hit(600, 0.4, 0.4)), 0.2, int(0.9 * SR))))
    W("stone_hit", mix(thud(110, 0.25, 0.8), debris(0.3, 0.5, 8)))
    bowtw = pluck(180, 0.35, 0.99, 0.2) * 0.8
    W("bow", mix(bowtw, at(whoosh(0.4, 3000, 1200, 0.5), 0.02, int(0.5 * SR))))
    W("crossbow", mix(metal_hit(2600, 0.1, 0.4), pluck(140, 0.3, 0.985, 0.3) * 0.8, at(whoosh(0.3, 3500, 1500, 0.4), 0.02, int(0.4 * SR))))
    W("ballista", mix(thud(60, 0.4, 0.8), pluck(70, 0.6, 0.99, 0.3), at(whoosh(0.5, 2000, 600, 0.6), 0.05, int(0.7 * SR))))
    W("catapult", mix(thud(50, 0.6, 1.0), bp(noise(0.3), 200, 1200) * exp_env(int(0.3 * SR), 0.08) * 0.6, at(whoosh(0.7, 800, 200, 0.6), 0.1, int(0.9 * SR))))
    W("bite", mix(flesh_hit(0.12, 0.8), growl(0.25, 150, 0.4, "eh")))
    W("bite_heavy", mix(flesh_hit(0.2, 1.0), growl(0.4, 90, 0.6, "oh")))
    W("claw_light", mix(hp(whoosh(0.12, 3000, 7000, 0.6), 2000), flesh_hit(0.1, 0.5)))
    W("claw_heavy", mix(hp(whoosh(0.2, 2000, 6000, 0.8), 1000), flesh_hit(0.18, 0.9)))
    W("claw_bone", mix(hp(whoosh(0.15, 2500, 6000, 0.6), 1500), debris(0.2, 0.6, 6)))
    W("roar_wyrm", reverb(growl(1.2, 70, 1.0, "ah"), 0.6, 0.3))
    dark = lambda f: reverb(mix(chord_pad([f, f * 1.5], 0.6, "saw", 1500, 0.01, 0.6) * exp_env(int(0.6 * SR), 0.2), bp(noise(0.5), 200, 1500) * exp_env(int(0.5 * SR), 0.12) * 0.5), 0.5, 0.3)
    W("spell_dark", dark(110))
    W("ilyra_bolt", reverb(mix(osc(sweep(900, 300, 0.25), 0.25, "tri") * exp_env(int(0.25 * SR), 0.08) * 0.6, bp(noise(0.25), 800, 3000) * exp_env(int(0.25 * SR), 0.06) * 0.4), 0.4, 0.25))
    W("tower_dawn", reverb(mix(bell(note("E5"), 0.6, 0.2) * 0.5, osc(sweep(1400, 700, 0.3), 0.3, "sine") * exp_env(int(0.3 * SR), 0.1) * 0.6, hp(noise(0.3), 3000) * exp_env(int(0.3 * SR), 0.05) * 0.3), 0.5, 0.3))
    W("tower_dusk", reverb(mix(osc(sweep(500, 150, 0.35), 0.35, "saw") * exp_env(int(0.35 * SR), 0.12) * 0.5, bp(noise(0.35), 200, 1200) * exp_env(int(0.35 * SR), 0.1) * 0.6), 0.5, 0.3))
    # casts
    W("vorak_charge", reverb(mix(whoosh(0.6, 200, 2500, 1.0), rumble(0.6, 0.5, 250), growl(0.5, 80, 0.4, "ah")), 0.4, 0.2))
    W("vorak_rend", reverb(mix(whoosh(0.3, 600, 5000, 0.9), at(mix(flesh_hit(0.3, 1.2), bp(noise(0.4), 300, 2500) * exp_env(int(0.4 * SR), 0.1) * 0.6), 0.15, int(0.8 * SR))), 0.4, 0.2))
    W("vorak_bloodfall", reverb(mix(whoosh(0.8, 150, 2000, 0.8), at(mix(thud(45, 1.2, 1.5), rumble(1.5, 0.8, 160), debris(1.0, 0.6, 20), flesh_hit(0.4, 1.0)), 0.7, int(2.5 * SR))), 0.8, 0.3))
    W("ilyra_lance", reverb(mix(osc(sweep(300, 1200, 0.3), 0.3, "saw") * env(int(0.3 * SR), 0.01, 0.1, 0.5, 0.1, 0.1) * 0.3, hp(whoosh(0.35, 1500, 6000, 0.8), 800)), 0.5, 0.3))
    W("ilyra_hemorrhage", reverb(mix(choir([note("D3"), note("A3"), note("F4")], 1.2, "oo", 0.5), at(mix(flesh_hit(0.4, 1.2), bp(noise(0.6), 150, 1500) * exp_env(int(0.6 * SR), 0.15)), 0.9, int(1.8 * SR))), 0.7, 0.35))
    W("ilyra_offering", reverb(mix(choir([note("A3"), note("E4"), note("A4")], 1.0, "ah", 0.6), bell(note("A5"), 1.5, 0.8) * 0.3), 0.7, 0.4))
    W("ilyra_exsanguinate", reverb(mix(chord_pad([note("D2"), note("A2"), note("D3"), note("F3")], 2.0, "saw", 900, 0.01, 0.8), bp(noise(2.0), 300, 900) * env(int(2.0 * SR), 0.5, 0.2, 0.8, 0.8, 0.5) * 0.4), 0.7, 0.35))
    # deaths
    W("death_human", reverb(mix(growl(0.6, 140, 0.6, "ah") * sweep(1, 0.6, 0.6, "lin"), flesh_hit(0.3, 0.6), at(thud(90, 0.4, 0.8), 0.35, int(1.0 * SR))), 0.4, 0.2))
    W("death_bone", mix(debris(0.8, 1.0, 30), thud(120, 0.3, 0.4)))
    W("death_large", reverb(mix(growl(1.2, 60, 0.9, "oh"), at(mix(thud(45, 0.8, 1.4), rumble(0.8, 0.5, 200)), 0.8, int(2.0 * SR))), 0.5, 0.25))
    W("death_monster", reverb(mix(growl(0.8, 100, 0.8, "eh"), flesh_hit(0.3, 0.6)), 0.4, 0.2))
    W("death_small", mix(growl(0.3, 300, 0.5, "eh"), flesh_hit(0.15, 0.4)))
    W("death_stone", mix(thud(80, 0.6, 1.0), debris(0.9, 0.8, 25)))
    W("death_wood", mix(thud(100, 0.5, 0.8), bp(noise(0.8), 300, 1500) * exp_env(int(0.8 * SR), 0.2) * 0.6))
    W("ward_break", mix(bell(1800, 0.4, 0.1) * 0.5, debris(0.3, 0.6, 10)))
    W("structure_collapse", reverb(mix(rumble(3.0, 1.2, 150), debris(2.5, 0.9, 80), thud(40, 1.5, 1.2)), 0.8, 0.3))
    W("core_destroyed", reverb(mix(rumble(5.0, 1.4, 120), debris(4.0, 1.0, 140), thud(35, 2.5, 1.5), at(choir([note("D2"), note("A2"), note("D3")], 4.0, "ah", 0.7), 0.3, int(5.0 * SR))), 0.9, 0.35))
    # economy / match
    W("gold", coins(4))
    horn_x = mix(brass(note("D2"), 2.6, 1.0), brass(note("D3"), 2.6, 0.5), brass(note("A2"), 2.6, 0.4))
    W("horn", reverb(horn_x, 0.9, 0.4))
    lvl = np.zeros(int(1.6 * SR))
    for i, n in enumerate(["D5", "F5", "A5", "D6"]):
        lvl += at(bell(note(n), 1.2, 0.5) * 0.6, i * 0.08, len(lvl))
    W("level_up", reverb(lvl, 0.6, 0.35))
    return out


# ============================================================================================== ambience

def ambience():
    out = []
    dur = 40.0
    t = t_axis(dur)
    gust = 0.5 + 0.5 * np.sin(2 * np.pi * t / 9.0) * np.sin(2 * np.pi * t / 5.3 + 1)
    wind_l = sweep_filter(noise(dur), 300, 300) * 0 + bp(noise(dur), 200, 900) * (0.4 + 0.6 * gust)
    wind_r = bp(noise(dur), 220, 1000) * (0.4 + 0.6 * np.roll(gust, int(1.3 * SR)))
    whistle = bp(noise(dur), 1100, 1500) * (gust ** 3) * 0.25
    out.append(write("Ambience", "menu_wind", np.stack([wind_l + whistle, wind_r + whistle * 0.8], axis=1), loop=True))
    # bats
    total = int(2.0 * SR)
    b = np.zeros(total)
    for _ in range(26):
        o = R.uniform(0, 1.6)
        f = R.uniform(5000, 9000)
        ch = osc(sweep(f, f * 0.7, 0.03), 0.03, "sine") * exp_env(int(0.03 * SR), 0.008)
        b += at(ch * R.uniform(0.2, 0.6), o, total)
    flutter = bp(noise(2.0), 150, 600) * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 14 * t_axis(2.0)))) * env(total, 0.2, 0.3, 0.6, 0.6, 0.8) * 0.5
    out.append(write("Ambience", "bats", reverb(b + flutter, 0.5, 0.3)))
    # thunder
    th = mix(bp(noise(0.25), 800, 6000) * exp_env(int(0.25 * SR), 0.05) * 0.8, rumble(5.0, 1.2, 140), at(rumble(3.0, 0.6, 90), 1.0, int(5.0 * SR)))
    out.append(write("Ambience", "thunder", reverb(th, 0.9, 0.35)))
    # battlefield night: low drone, distant wind, crows, embers
    dur = 48.0
    drone = chord_pad([note("D1"), note("A1"), note("D2")], dur, "saw", 260, 0.003, 0.5)
    drone = drone / (np.max(np.abs(drone)) + 1e-9) * 0.25
    wind = bp(noise(dur), 150, 700) * (0.4 + 0.3 * np.sin(2 * np.pi * t_axis(dur) / 11)) * 0.3
    crows = np.zeros(int(dur * SR))
    for i in range(6):
        o = R.uniform(2, dur - 3)
        caw = formant(osc(sweep(520, 380, 0.35), 0.35, "saw") + 0.3 * noise(0.35), "ah") * env(int(0.35 * SR), 0.02, 0.1, 0.6, 0.1, 0.15)
        crows += at(caw * 0.35, o, len(crows))
        crows += at(caw * 0.3, o + 0.45, len(crows))
    mono = drone + wind + reverb(crows, 0.8, 0.5)[: int(dur * SR)]
    out.append(write("Ambience", "velmoragh_night", stereo(mono, 0.5), loop=True))
    return out


# ============================================================================================== music

def progression_track(chords, bar, bars_per_chord, voices, extras=None, dur_tail=4.0):
    total = int((len(chords) * bars_per_chord * bar + dur_tail) * SR)
    mixb = np.zeros(total)
    for i, ch in enumerate(chords):
        start = i * bars_per_chord * bar
        length = bars_per_chord * bar + 1.5
        for v in voices:
            mixb += at(v(ch, length), start, total)
    if extras:
        mixb += extras(total)
    return mixb


def music():
    out = []
    # --- menu theme: D minor choir + strings, bells and distant thunder (~64 s loop)
    bar = 4.0
    prog = [["D3", "F3", "A3"], ["Bb2", "D3", "F3"], ["G2", "Bb2", "D3"], ["A2", "C#3", "E3"],
            ["D3", "F3", "A3"], ["F2", "A2", "C3"], ["G2", "Bb2", "D3"], ["A2", "C#3", "E3"]]
    ch = lambda names: [note(n) for n in names]
    voices = [
        lambda c, L: choir(ch(c), L, "ah", 0.9),
        lambda c, L: chord_pad([f / 2 for f in ch(c)], L, "saw", 700, 0.005, 0.6),
    ]

    def bells(total):
        b = np.zeros(total)
        melody = ["A4", "F4", "D4", "E4", "F4", "D5", "C5", "A4"]
        for i, n in enumerate(melody):
            b += at(bell(note(n), 4.0, 1.6) * 0.18, i * bar * 1 + 0.5, total)
        return b

    menu = progression_track(prog, bar, 2 // 2, voices, bells, 5.0)
    menu = reverb(menu, 0.9, 0.45)
    out.append(write("Music", "menu_theme", stereo(menu, 0.6), loop=True))

    # --- client theme: calmer harp arpeggios over pads (~48 s loop)
    prog2 = [["D3", "F3", "A3"], ["C3", "E3", "G3"], ["Bb2", "D3", "F3"], ["A2", "C#3", "E3"]] * 2
    voices2 = [lambda c, L: chord_pad(ch(c), L, "tri", 1200, 0.004, 0.5)]

    def harp(total):
        h = np.zeros(total)
        for i, c in enumerate(prog2):
            fs = ch(c) + [ch(c)[0] * 2]
            for k in range(8):
                f = fs[k % len(fs)] * (2 if k >= 4 else 1)
                h += at(pluck(f, 1.2, 0.997, 0.35) * 0.25, i * 6.0 + k * 0.375, total)
        return h

    client = progression_track(prog2, 6.0, 1, voices2, harp, 4.0)
    out.append(write("Music", "client_theme", stereo(reverb(client, 0.8, 0.4), 0.5), loop=True))

    # --- hero select: taiko ostinato + low brass stabs, 90 bpm (32 s loop)
    beat = 60 / 90
    total = int(32 * SR + 3 * SR)
    hs = np.zeros(total)
    pattern = [1, 0, 0.6, 0, 1, 0.5, 0.6, 0]
    for b in range(int(32 / beat)):
        for s, v in enumerate(pattern):
            if v > 0 and (b * len(pattern) + s) % 1 == 0:
                pass
        v = pattern[b % len(pattern)]
        if v > 0:
            hs += at(taiko(0.9, 60 if v == 1 else 90, v), b * beat, total)
    for i, n in enumerate(["D2", "D2", "F2", "E2"] * 2):
        hs += at(brass(note(n), beat * 1.5, 0.5), i * beat * 6, total)
    hs += at(chord_pad([note("D2"), note("A2")], 32, "saw", 400, 0.004, 0.25), 0, total)
    out.append(write("Music", "hero_select", stereo(reverb(hs, 0.6, 0.3), 0.4), loop=True))

    # --- loading: drone + heartbeat (30 s loop)
    total = int(33 * SR)
    ld = at(chord_pad([note("D2"), note("A2"), note("D3")], 30, "saw", 350, 0.003, 0.5), 0, total)
    for i in range(int(30 / 1.1)):
        ld += at(thud(55, 0.25, 0.5), i * 1.1, total)
        ld += at(thud(50, 0.25, 0.35), i * 1.1 + 0.28, total)
    out.append(write("Music", "loading", stereo(reverb(ld, 0.7, 0.3), 0.5), loop=True))

    # --- victory stinger: D major fanfare
    total = int(9 * SR)
    v = np.zeros(total)
    for i, (n, d) in enumerate([("D4", 0.4), ("A4", 0.4), ("D5", 0.8), ("F#5", 0.4), ("A5", 2.5)]):
        start = sum(x[1] for x in [("D4", 0.4), ("A4", 0.4), ("D5", 0.8), ("F#5", 0.4), ("A5", 2.5)][:i])
        v += at(brass(note(n), d + 0.3, 0.7), start, total)
    v += at(choir([note("D3"), note("F#3"), note("A3"), note("D4")], 5.0, "ah", 0.7), 2.0, total)
    v += at(taiko(1.2, 55, 1.2), 2.0, total)
    out.append(write("Music", "victory", stereo(reverb(v, 0.8, 0.35), 0.5)))

    # --- defeat stinger: descending choir
    total = int(9 * SR)
    d = np.zeros(total)
    for i, c in enumerate([["D3", "F3", "A3"], ["C3", "Eb3", "G3"], ["Bb2", "D3", "F3"], ["A2", "D3", "F3"]]):
        d += at(choir(ch(c), 2.6, "oo", 0.8), i * 1.8, total)
    d += at(bell(note("D4"), 5.0, 2.0) * 0.3, 0.2, total)
    out.append(write("Music", "defeat", stereo(reverb(d, 0.9, 0.45), 0.5)))
    return out


# ============================================================================================== announcer

ANNOUNCER = {
    "first_blood": "First blood!", "double_kill": "Double kill!", "triple_kill": "Triple kill!", "quad_kill": "Quad kill!",
    "annihilation": "Massacre!", "streak_3": "Bloodletting!", "streak_4": "Ravaging!", "streak_5": "Slaughterous!",
    "streak_6": "Unbroken!", "streak_7": "Crimson terror!", "streak_8": "Harbinger!", "streak_9": "Titanborn!",
    "streak_10": "Beyond death!", "shutdown": "Shut down!", "team_wipe": "Blood harvest!",
    "tower_fallen_ally": "Your tower has fallen.", "tower_fallen_enemy": "An enemy tower has fallen.",
    "barracks_fallen_ally": "Your barracks have fallen.", "barracks_fallen_enemy": "Enemy barracks destroyed.",
    "victory": "Victory.", "defeat": "Defeat.", "battle_begins": "Let the blood flow.", "creeps_spawned": "The armies march.",
    "player_disconnected": "A player has disconnected.", "player_reconnected": "A player has reconnected.",
    "player_abandoned": "A player has abandoned the battle.", "vharoth_tremor": "The earth trembles.",
    "vharoth_seal_broken": "A seal is broken.", "vharoth_awakened": "Vharoth awakens!", "vharoth_blood_moon": "The blood moon rises.",
    "vharoth_slain": "Vharoth has fallen.", "denied": "Denied.", "nightfall": "Night falls.", "daybreak": "Dawn breaks.",
    "mega_creeps": "Mega creeps approach.",
}


def announcer():
    exe = shutil.which("espeak-ng") or shutil.which("espeak")
    if not exe:
        print("espeak-ng not found: announcer lines skipped (install espeak-ng and re-run)")
        return []
    out = []
    with tempfile.TemporaryDirectory() as tmp:
        for key, text in ANNOUNCER.items():
            wav = os.path.join(tmp, key + ".wav")
            subprocess.run([exe, "-v", "en-gb+m3", "-s", "125", "-p", "18", "-a", "180", "-w", wav, text], check=True)
            x, sr = sf.read(wav)
            if x.ndim > 1:
                x = x.mean(axis=1)
            # resample to SR with a pitch drop (slower, deeper), add a sub-octave double and grit, then a cathedral reverb
            factor = 0.82
            n = int(len(x) * SR / sr / factor)
            x = signal.resample(x, n)
            sub = signal.resample(x, int(len(x) * 1.0))
            sub = lp(sub, 500) * 0.5
            y = dist(hp(x, 90) * 1.6, 1.8) * 0.8 + sub
            y = mix(y, at(y * 0.25, 0.012, len(y)))
            y = reverb(y, 0.75, 0.3)
            out.append(write("Announcer", key, stereo(y, 0.2)))
    return out


def main():
    groups = [("UI", ui), ("Sfx", sfx), ("Ambience", ambience), ("Music", music), ("Announcer", announcer)]
    only = sys.argv[1:]
    total = 0
    for name, fn in groups:
        if only and name not in only:
            continue
        files = fn()
        total += len(files)
        print(f"{name}: {len(files)} clips")
    print(f"wrote {total} clips to {os.path.relpath(OUT, ROOT)}")


if __name__ == "__main__":
    main()
