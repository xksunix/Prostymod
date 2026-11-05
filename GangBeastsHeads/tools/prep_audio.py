#!/usr/bin/env python3
"""Wyciaga dzwiek z filmikow (ffmpeg), przycina cisze, normalizuje i zapisuje jako surowy PCM16 mono 32 kHz.
Uzycie: prep_audio.py <folder_z_mp4> <folder_wyjsciowy>
Kolejnosc: 7 klipow gadania (clip0..clip6) wg nazw plikow + klip gryzienia (bite) - ostatni (najkrotszy, bez tonu)."""
import glob, os, subprocess, sys, wave
import numpy as np
SR = 32000
src, out = sys.argv[1], sys.argv[2]
files = sorted(glob.glob(os.path.join(src, '*.mp4')), key=lambda f: os.path.basename(f).split('-', 1)[-1])  # wg znacznika czasu VID_...
clips = []
for f in files:
    tmp = '/tmp/_clip.wav'
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', f, '-vn', '-ac', '1', '-ar', str(SR), tmp], check=True)
    w = wave.open(tmp); x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
    hop = int(SR * 0.01); env = np.array([np.sqrt(np.mean(x[i:i + hop] ** 2)) for i in range(0, len(x) - hop, hop)])
    act = env > max(env.max() * 0.05, 0.003)
    a = max(0, np.argmax(act) * hop - hop * 3); b = min(len(x), (len(act) - np.argmax(act[::-1])) * hop + hop * 3)
    x = x[a:b]
    fade = int(SR * 0.012); x[:fade] *= np.linspace(0, 1, fade); x[-fade:] *= np.linspace(1, 0, fade)
    x *= 0.9 / (np.abs(x).max() + 1e-9)
    # glosnosc na zblizonym poziomie (RMS ~ 0.12)
    rms = np.sqrt(np.mean(x ** 2)); x = np.clip(x * min(3.0, 0.12 / (rms + 1e-9)), -0.95, 0.95)
    # "dzwiecznosc" (autokorelacja 70-400 Hz): mowa ma ton, chrupanie/gryzienie prawie nie
    fr = int(SR * 0.04); lo, hi = int(SR / 400), int(SR / 70); tot = pit = 0
    for i in range(0, len(x) - fr, hop * 2):
        sgm = x[i:i + fr] - x[i:i + fr].mean()
        if np.sqrt(np.mean(sgm ** 2)) < 0.02: continue
        ac = np.correlate(sgm, sgm, 'full')[fr - 1:]
        if ac[0] <= 0: continue
        tot += 1; pit += (ac[lo:hi].max() / ac[0]) > 0.45
    clips.append((os.path.basename(f), x, pit / max(1, tot)))
os.makedirs(out, exist_ok=True)
bite_i = int(np.argmin([c[2] for c in clips]))      # najmniej "dzwieczny" klip = odglos gryzienia
for f in glob.glob(os.path.join(out, '*.pcm')): os.remove(f)
k = 0
for i, (n, x, v) in enumerate(clips):
    if i == bite_i: name = 'bite'
    else: name = 'clip%d' % k; k += 1
    (np.clip(x, -1, 1) * 32767).astype('<i2').tofile(os.path.join(out, name + '.pcm'))
    print(name, n, '%.2fs' % (len(x) / SR), 'dzwiecznosc=%.2f' % v)
