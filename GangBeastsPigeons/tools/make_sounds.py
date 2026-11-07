#!/usr/bin/env python3
"""Synteza dzwiekow golebia (gruchanie, przestraszone 'hu!', warczenie, machanie skrzydlami).
Wynik: assets/*.pcm  (16 bit, mono, 32000 Hz - ten sam format co w modzie Heads).

Dzwieki sa syntezowane (zrodlo glosowe + formanty samogloski 'u/o'), wiec nie ma tu zadnych cudzych nagran.
Jesli wolisz prawdziwe nagrania golebia: wrzuc pliki .wav (mono/stereo 16-bit) do folderu Mods/GangBeastsPigeons/ - mod wczyta je zamiast wbudowanych.
"""
import os, sys
import numpy as np

SR = 32000
OUT = sys.argv[1] if len(sys.argv) > 1 else 'assets'
rng = np.random.default_rng(7)


def smooth(x):
    x = np.clip(x, 0, 1); return x * x * (3 - 2 * x)


def env_adsr(n, a, d, s, r):
    t = np.arange(n) / SR; T = n / SR
    e = np.ones(n)
    e *= smooth(t / max(a, 1e-4))
    e *= 1 - (1 - s) * smooth((t - a) / max(d, 1e-4))
    e *= smooth((T - t) / max(r, 1e-4))
    return e


def formant_gain(freq, formants):
    g = np.zeros_like(freq)
    for fc, bw, amp in formants:
        g += amp * np.exp(-0.5 * ((freq - fc) / bw) ** 2)
    return g + 0.06


def voice(f0, formants, amp, rough=0.0, rough_hz=28.0, breath=0.04, detune=1.011, nh=16):
    """f0, amp: tablice na probke. Zrodlo harmoniczne z dwoma glosami (syrinx) przez formanty."""
    n = len(f0)
    out = np.zeros(n)
    for dt, wt in ((1.0, 1.0), (detune, 0.7)):
        ph = 2 * np.pi * np.cumsum(f0 * dt / SR)
        for h in range(1, nh + 1):
            fh = f0 * dt * h
            a = formant_gain(fh, formants) / (h ** 0.55)
            a = np.where(fh < SR * 0.45, a, 0)
            out += wt * a * np.sin(h * ph + 0.3 * h)
    t = np.arange(n) / SR
    if rough > 0:
        out *= 1 - rough * (0.5 + 0.5 * np.sin(2 * np.pi * rough_hz * t + rng.uniform(0, 6)))
    # oddech (szum przefiltrowany formantami grubo)
    nz = rng.normal(0, 1, n)
    k = np.ones(24) / 24
    nz = np.convolve(nz, k, mode='same') * 2.2
    out = out + breath * nz * (np.abs(out).max() + 1e-9)
    return out * amp


U = [(330, 90, 1.0), (880, 160, 0.55), (2200, 400, 0.12)]      # 'u' (gruchanie)
O = [(520, 110, 1.0), (950, 170, 0.6), (2300, 400, 0.12)]      # 'o'


def note(dur, f_start, f_peak, f_end, vib=5.5, vib_depth=0.012, formants=U, rough=0.0, rough_hz=28.0, amp_peak=1.0, breath=0.05, a=0.03, r=0.08, onset_dip=0.0):
    n = int(dur * SR); t = np.arange(n) / SR; x = t / dur
    # kontur wysokosci: szybkie wejscie od dolu, szczyt, powolne opadanie
    f = np.where(x < 0.25, f_start + (f_peak - f_start) * smooth(x / 0.25), f_peak + (f_end - f_peak) * smooth((x - 0.25) / 0.75))
    f = f * (1 + vib_depth * np.sin(2 * np.pi * vib * t) * smooth(x * 3))
    amp = env_adsr(n, a, dur * 0.2, 0.8, r) * amp_peak
    if onset_dip > 0:
        amp *= 1 - onset_dip * np.exp(-((x - 0.45) / 0.12) ** 2)
    return voice(f, formants, amp, rough=rough, rough_hz=rough_hz, breath=breath)


def seq(parts, gaps):
    out = []
    for i, p in enumerate(parts):
        out.append(p)
        if i < len(gaps): out.append(np.zeros(int(gaps[i] * SR)))
    return np.concatenate(out)


def norm(x, peak=0.8):
    x = x - np.mean(x)
    return x / (np.abs(x).max() + 1e-9) * peak


def lowpass(x, fc):
    from scipy.signal import butter, sosfilt
    return sosfilt(butter(4, fc / (SR / 2), 'low', output='sos'), x)


def bandpass(x, lo, hi):
    from scipy.signal import butter, sosfilt
    return sosfilt(butter(3, [lo / (SR / 2), hi / (SR / 2)], 'band', output='sos'), x)


def softclip(x, k=1.3):
    return np.tanh(k * x) / np.tanh(k)


# ---------- gruchanie ----------
def coo_classic(f0=420):
    # "cu - ROOOO - coo": krotkie, dlugie z rolka, krotkie
    a = note(0.16, f0 * 0.85, f0 * 1.05, f0 * 0.95, amp_peak=0.55)
    b = note(0.62, f0 * 0.9, f0 * 1.12, f0 * 0.8, rough=0.55, rough_hz=24, vib_depth=0.02, amp_peak=1.0)
    c = note(0.30, f0 * 0.95, f0 * 1.08, f0 * 0.78, amp_peak=0.8)
    return seq([a, b, c], [0.07, 0.06])

def coo_double(f0=450):
    a = note(0.26, f0 * 0.9, f0 * 1.1, f0 * 0.92, amp_peak=0.9, rough=0.25, rough_hz=30)
    b = note(0.34, f0 * 0.92, f0 * 1.14, f0 * 0.78, amp_peak=1.0, rough=0.3, rough_hz=26)
    return seq([a, b], [0.11])

def coo_roll(f0=400):
    parts = [note(0.20, f0 * 0.85, f0 * 1.05, f0 * 0.95, amp_peak=0.6),
             note(0.78, f0 * 0.9, f0 * 1.18, f0 * 0.85, rough=0.7, rough_hz=21, vib_depth=0.025, amp_peak=1.0),
             note(0.20, f0 * 0.95, f0 * 1.1, f0 * 0.9, amp_peak=0.75),
             note(0.20, f0 * 0.95, f0 * 1.08, f0 * 0.86, amp_peak=0.7),
             note(0.34, f0 * 0.95, f0 * 1.05, f0 * 0.72, amp_peak=0.8, rough=0.3)]
    return seq(parts, [0.05, 0.07, 0.07, 0.07])

def coo_short(f0=470):
    return note(0.22, f0 * 0.9, f0 * 1.12, f0 * 0.9, amp_peak=0.9, rough=0.2, rough_hz=32)

def coo_trio(f0=430):
    parts = [note(0.14, f0 * 0.9, f0 * 1.08, f0 * 0.95, amp_peak=0.7) for _ in range(2)]
    parts.append(note(0.40, f0 * 0.9, f0 * 1.15, f0 * 0.75, rough=0.5, rough_hz=25, amp_peak=1.0))
    return seq(parts, [0.06, 0.07])

# ---------- reakcje ----------
def hurt_hoo(f0=620):
    # przestraszone krotkie "hu!" - wyzsze, ostrzejsze, z szarpnieciem wysokosci
    a = note(0.17, f0 * 0.8, f0 * 1.35, f0 * 1.0, formants=O, amp_peak=1.0, breath=0.12, a=0.01, r=0.05, vib_depth=0.03)
    b = note(0.25, f0 * 1.0, f0 * 1.25, f0 * 0.7, formants=O, amp_peak=0.9, breath=0.1, a=0.01, rough=0.4, rough_hz=36)
    return seq([a, b], [0.04])

def hurt_oop(f0=560):
    a = note(0.12, f0 * 0.9, f0 * 1.4, f0 * 1.2, formants=O, amp_peak=1.0, breath=0.15, a=0.008, r=0.04)
    return a

def angry_growl(f0=250):
    a = note(0.9, f0 * 1.0, f0 * 1.2, f0 * 0.8, formants=O, rough=0.9, rough_hz=42, amp_peak=1.0, breath=0.12, vib_depth=0.03)
    b = note(0.35, f0 * 1.1, f0 * 1.45, f0 * 0.9, formants=O, rough=0.6, rough_hz=34, amp_peak=0.9, breath=0.1)
    return seq([a, b], [0.07])

def angry_short(f0=300):
    return seq([note(0.35, f0, f0 * 1.3, f0 * 0.85, formants=O, rough=0.85, rough_hz=45, amp_peak=1.0, breath=0.12),
                note(0.3, f0 * 1.1, f0 * 1.4, f0 * 0.8, formants=O, rough=0.7, rough_hz=40, amp_peak=0.9, breath=0.12)], [0.05])

def sleepy(f0=380):
    return note(1.3, f0 * 1.1, f0 * 1.15, f0 * 0.6, vib=4.0, vib_depth=0.03, amp_peak=0.9, breath=0.08, a=0.12, r=0.35)

def laugh_coo(f0=440):
    parts = [note(0.13, f0 * (0.9 + 0.04 * i), f0 * (1.1 + 0.05 * i), f0 * 0.95, amp_peak=0.8) for i in range(5)]
    return seq(parts, [0.055] * 4)

# ---------- skrzydla ----------
def flap(n_flaps=5, spacing=0.167, vol=1.0):
    n = int((n_flaps * spacing + 0.25) * SR)
    out = np.zeros(n)
    for k in range(n_flaps):
        s = int(k * spacing * SR + rng.integers(0, 300))
        L = int(0.14 * SR)
        t = np.arange(L) / SR
        env = np.exp(-t / 0.032) * smooth(t / 0.004)
        w = bandpass(rng.normal(0, 1, L), 180, 3200) * env
        thump = np.sin(2 * np.pi * (95 - 220 * t) * t) * np.exp(-t / 0.03) * 0.6
        out[s:s + L] += (w * 1.4 + thump) * (0.75 + 0.25 * rng.random()) * (1.0 if k else 1.25)
    return out * vol

def wing_clap():
    L = int(0.30 * SR); t = np.arange(L) / SR
    w = bandpass(rng.normal(0, 1, L), 250, 4500) * np.exp(-t / 0.045) * smooth(t / 0.002)
    w2 = np.roll(w, int(0.05 * SR)) * 0.6
    return softclip((w + w2) * 1.5)

def finish(x, peak=0.8, lp=7000):
    x = lowpass(x, lp)
    # krotkie wejscie/wyjscie, zeby nie trzaskalo
    n = len(x); fade = int(0.004 * SR)
    x[:fade] *= np.linspace(0, 1, fade); x[-fade:] *= np.linspace(1, 0, fade)
    return norm(x, peak)

def save(name, x):
    pcm = (np.clip(x, -1, 1) * 32767).astype('<i2')
    with open(os.path.join(OUT, name + '.pcm'), 'wb') as f:
        f.write(pcm.tobytes())
    print('%-10s %.2f s' % (name, len(pcm) / SR))

if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    save('coo0', finish(coo_classic(420)))
    save('coo1', finish(coo_double(450)))
    save('coo2', finish(coo_roll(400)))
    save('coo3', finish(coo_short(470)))
    save('coo4', finish(coo_trio(430)))
    save('hurt0', finish(hurt_hoo(620)))
    save('hurt1', finish(hurt_oop(560), lp=8000))
    save('angry0', finish(angry_growl(250)))
    save('angry1', finish(angry_short(300)))
    save('sleep0', finish(sleepy(380)))
    save('laugh0', finish(laugh_coo(440)))
    save('flap0', finish(flap(5, 0.167), 0.7, 9000))
    save('flap1', finish(wing_clap(), 0.8, 9000))
