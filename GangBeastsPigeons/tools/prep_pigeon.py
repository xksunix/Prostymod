#!/usr/bin/env python3
"""Przygotowanie modelu golebia do moda (offline). Wynik: assets/pigeon.bin

Uzycie (potrzebny modul bpy: pip install bpy):
    python3 tools/prep_pigeon.py source/pigeon.blend assets/pigeon.bin [folder_podgladu]

Co robi:
 * czyta siatke, wagi kosci i kolory wierzcholkow z pliku .blend (kolory sa wypiekane do tekstury razem z lekkim cieniowaniem,
   dzieki temu golab wyglada dobrze na kazdym shaderze, takze nieoswietlonym),
 * zamienia uklad Blender (Z w gore, przod +Y) -> Unity (Y w gore, przod +Z),
 * wypieka klipy animacji: rest (rozpostarte skrzydla), fold (zlozone skrzydla), idle, flap (machanie), tuck (nogi pod siebie).
"""
import io, os, struct, sys, math
import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pigeon_rig import Rig, conj, lin2srgb, local_trs, fold_pose, MU
from mathutils import Vector

SRC = sys.argv[1] if len(sys.argv) > 1 else 'source/pigeon.blend'
OUT = sys.argv[2] if len(sys.argv) > 2 else 'assets/pigeon.bin'
PREV = sys.argv[3] if len(sys.argv) > 3 else None
TEX = 512

# kierunek swiatla zapieczonego w teksture (uklad Blendera: x prawo, y przod, z gora)
LIGHT = np.array([0.30, 0.40, 0.85]); LIGHT /= np.linalg.norm(LIGHT)


def bake_texture(uv, colors, tris):
    """uv (nloops,2), colors (nloops,3 sRGB 0-1), tris (n,3 id petli). Zwraca (obraz RGB uint8, liczba nachodzacych pikseli)."""
    img = np.zeros((TEX, TEX, 3), np.float32)
    own = -np.ones((TEX, TEX), np.int32)
    overlap = 0
    for ti, t in enumerate(tris):
        p = uv[t] * TEX
        p[:, 1] = TEX - p[:, 1]                       # v w gore -> wiersz od gory
        x0 = int(max(0, math.floor(p[:, 0].min() - 1))); x1 = int(min(TEX - 1, math.ceil(p[:, 0].max() + 1)))
        y0 = int(max(0, math.floor(p[:, 1].min() - 1))); y1 = int(min(TEX - 1, math.ceil(p[:, 1].max() + 1)))
        if x1 < x0 or y1 < y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        (ax, ay), (bx, by), (cx, cy) = p
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-9:
            continue
        w0 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / den
        w1 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / den
        w2 = 1 - w0 - w1
        eps = -0.02
        m = (w0 >= eps) & (w1 >= eps) & (w2 >= eps)
        if not m.any():
            continue
        c = (w0[..., None] * colors[t[0]] + w1[..., None] * colors[t[1]] + w2[..., None] * colors[t[2]])
        sub_own = own[y0:y1 + 1, x0:x1 + 1]
        sub_img = img[y0:y1 + 1, x0:x1 + 1]
        # wnetrze trojkata (nie margines) liczymy do nachodzenia
        inner = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        overlap += int(((sub_own >= 0) & (sub_own != ti) & inner).sum())
        sub_img[m] = c[m]
        sub_own[m] = ti
    # uzupelnienie pustych pikseli najblizszym kolorem (zeby filtrowanie nie robilo prazkow na krawedziach)
    from scipy import ndimage
    empty = own < 0
    if empty.any() and (~empty).any():
        idx = ndimage.distance_transform_edt(empty, return_distances=False, return_indices=True)
        img = img[idx[0], idx[1]]
    return np.clip(np.round(img * 255), 0, 255).astype(np.uint8), overlap


def main():
    R = Rig(SRC)
    me = R.mesh.data
    nb = len(R.names)

    # ---------- siatka ----------
    me.calc_loop_triangles()
    polys = me.polygons
    assert all(len(p.vertices) == 3 for p in polys), 'siatka musi byc z trojkatow'
    tris_l = np.array([list(p.loop_indices) for p in polys], np.int64)         # (n,3) id petli
    nl = len(me.loops)
    vidx = np.array([l.vertex_index for l in me.loops], np.int64)
    co = np.array([tuple(v.co) for v in me.vertices], np.float64)               # Blender
    cn = np.array([tuple(n.vector) for n in me.corner_normals], np.float64)     # normalne w rogach (Blender)
    fn = np.array([tuple(p.normal) for p in polys], np.float64)                 # normalne scian
    # wlasny atlas UV: kazdy trojkat dostaje wlasne pole w teksturze (oryginalne UV czesciowo sie nakladaja),
    # a kolory w rogach trojkata sa interpolowane dokladnie tak jak w Blenderze
    uv = np.zeros((nl, 2), np.float64)
    cell = 22; cols = TEX // cell
    pad = 3.0
    for ti, t in enumerate(tris_l):
        c = ti // 2
        ox, oy = (c % cols) * cell, (c // cols) * cell
        if ti % 2 == 0:
            corners = [(pad, pad), (cell - pad, pad), (pad, cell - pad)]
        else:
            corners = [(cell - pad, cell - pad), (pad, cell - pad), (cell - pad, pad)]
        for k in range(3):
            uv[t[k]] = ((ox + corners[k][0]) / TEX, 1.0 - (oy + corners[k][1]) / TEX)
    assert (len(tris_l) + 1) // 2 <= cols * (TEX // cell), 'za malo miejsca w atlasie'
    ccol = np.array([tuple(c.color)[:3] for c in me.color_attributes['Col'].data], np.float64)  # liniowe
    base_srgb = lin2srgb(ccol)

    # cieniowanie zapieczone w teksture (plaskie, wg normalnej sciany - pasuje do niskopoligonowego wygladu)
    face_shade = 0.60 + 0.40 * np.clip(0.5 * (fn @ LIGHT) + 0.5, 0, 1) ** 1.15
    loop_shade = np.zeros(nl)
    for ti, t in enumerate(tris_l):
        loop_shade[t] = face_shade[ti]
    tex_col = np.clip(base_srgb * loop_shade[:, None], 0, 1)
    tex, ov = bake_texture(uv, tex_col, tris_l)
    print('tekstura', tex.shape, 'nachodzace piksele UV:', ov)

    # wagi kosci (max 4 na wierzcholek)
    gname = [g.name for g in R.mesh.vertex_groups]
    gmap = [R.names.index(n) for n in gname]
    vb = np.zeros((len(me.vertices), 4), np.uint8); vw = np.zeros((len(me.vertices), 4), np.float64)
    for v in me.vertices:
        ws = sorted(((g.weight, gmap[g.group]) for g in v.groups if g.weight > 1e-4), reverse=True)[:4]
        if not ws:
            raise SystemExit('wierzcholek bez wag: %d' % v.index)
        tot = sum(w for w, _ in ws)
        for k, (w, b) in enumerate(ws):
            vb[v.index, k] = b; vw[v.index, k] = w / tot

    # wierzcholki Unity (po jednym na rog trojkata)
    P = co[vidx][:, [0, 2, 1]]
    N = cn[:, [0, 2, 1]]
    nrm_len = np.linalg.norm(N, axis=1, keepdims=True); N = N / np.maximum(nrm_len, 1e-9)
    IDX = tris_l[:, [0, 2, 1]].reshape(-1)                                      # odbicie ukladu -> odwrocona kolejnosc
    assert nl < 65535
    BI = vb[vidx]; BW = np.clip(np.round(vw[vidx] * 255), 0, 255).astype(np.uint8)

    # ---------- klipy ----------
    clips = []   # (nazwa, fps, petla, [klatki: lista (poz, rot) dla kazdej kosci])

    def frame_trs(world):
        return [(p, q) for (p, q, s) in local_trs(world, R.parent)]

    R.clear_action()
    rest_world = R.pose_world()
    clips.append(('rest', 24.0, False, [frame_trs(rest_world)]))
    clips.append(('fold', 24.0, False, [frame_trs(fold_pose(R, swing=76, roll=80, elbow=14))]))

    def sample(action, f0, f1):
        out = []
        for f in range(int(f0), int(f1) + 1):
            R.pose_action(action, f)
            out.append(frame_trs(R.pose_world()))
        return out

    clips.append(('idle', 24.0, True, sample('idle-animation', 0, 12)))
    clips.append(('flap', 24.0, True, sample('flap-wings-animation', 0, 24)))
    clips.append(('tuck', 24.0, False, sample('legs-up-animation', 1, 4)))
    # rozmiary / punkt odniesienia (uklad Unity, poza spoczynkowa ze zlozonymi skrzydlami)
    R.clear_action(); fold_pose(R, swing=76, roll=80, elbow=14)
    Vf = R.deformed_vertices()[:, [0, 2, 1]]
    th = R.arm.data.bones['thorax-bone'].head_local
    pivot = np.array([th.x, th.z, th.y], np.float32)
    info = np.array([Vf[:, 1].min(), Vf[:, 1].max(), Vf[:, 2].min(), Vf[:, 2].max(), Vf[:, 0].min(), Vf[:, 0].max(), *pivot], np.float32)
    print('zlozony golab (Unity): y %.3f..%.3f, z %.3f..%.3f, x %.3f..%.3f, pivot' % tuple(info[:6]), pivot)

    # skala kosci w spoczynku powinna byc ~1
    sc = [s for (_, _, s) in local_trs(rest_world, R.parent)]
    print('max odchylenie skali kosci:', max(float(np.abs(s - 1).max()) for s in sc))

    # ---------- zapis ----------
    buf = io.BytesIO(); Image.fromarray(tex).save(buf, format='PNG', optimize=True, compress_level=9); png = buf.getvalue()
    with open(OUT, 'wb') as f:
        f.write(b'GBP1')
        f.write(struct.pack('<iiiiii', len(P), len(IDX), nb, len(clips), TEX, TEX))
        f.write(info.astype('<f4').tobytes())
        for i, n in enumerate(R.names):
            nm = n.encode('ascii')
            rt = clips[0][3][0][i]
            f.write(struct.pack('<B', len(nm))); f.write(nm)
            f.write(struct.pack('<i', R.parent[i]))
            f.write(np.concatenate([rt[0], rt[1]]).astype('<f4').tobytes())
        f.write(P.astype('<f4').tobytes()); f.write(N.astype('<f4').tobytes()); f.write(uv.astype('<f4').tobytes())
        f.write(BI.astype(np.uint8).tobytes()); f.write(BW.tobytes())
        f.write(IDX.astype('<u2').tobytes())
        for (name, fps, loop, frames) in clips:
            nm = name.encode('ascii')
            f.write(struct.pack('<B', len(nm))); f.write(nm)
            f.write(struct.pack('<fBi', fps, 1 if loop else 0, len(frames)))
            for fr in frames:
                f.write(np.concatenate([np.concatenate([p, q]) for (p, q) in fr]).astype('<f4').tobytes())
        f.write(struct.pack('<i', len(png))); f.write(png)
    print('zapisano', OUT, os.path.getsize(OUT) // 1024, 'KB; wierzcholkow', len(P), 'trojkatow', len(IDX) // 3, 'klipow', len(clips))

    if PREV:
        os.makedirs(PREV, exist_ok=True)
        Image.fromarray(tex).save(os.path.join(PREV, 'pigeon_tex.png'))
        np.savez(os.path.join(PREV, 'baked.npz'), P=P, N=N, IDX=IDX, uv=uv, tex=tex, BI=BI, BW=BW)


if __name__ == '__main__':
    main()
