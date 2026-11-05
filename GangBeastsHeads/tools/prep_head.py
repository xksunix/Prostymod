#!/usr/bin/env python3
"""Przygotowanie modelu glowy do moda (uruchamiane offline, wynik: assets/head.bin).

Wejscie : source/head.glb  (jedna statyczna siatka z tekstura, bez riga)
Wyjscie : assets/head.bin  (siatka w ukladzie Unity + wagi kosci szczeki/powiek + zaslepka szyi)

Uklad glTF (prawoskretny, twarz patrzy w +Z) -> Unity (lewoskretny): x -> -x oraz odwrocenie kolejnosci trojkatow.
"""
import io, json, os, struct, sys
import numpy as np
from PIL import Image

SRC = sys.argv[1] if len(sys.argv) > 1 else 'source/head.glb'
OUT = sys.argv[2] if len(sys.argv) > 2 else 'assets/head.bin'

# ---------- landmarki (wspolrzedne glTF, odczytane z renderow siatki) ----------
CREASE_Y = -0.487          # kreska ust na srodku
CREASE_CURVE = 0.75        # opadanie kresy ku kacikom
MOUTH_CX = 0.01
EYE_A = (-0.210, -0.012)   # (x,y) oko po lewej stronie obrazu z przodu (glTF)
EYE_B = (0.255, -0.012)
EYE_LO, EYE_UP = -0.045, 0.020   # dolna i gorna krawedz otwartego oka
LID_DROP = EYE_UP - EYE_LO       # o tyle zjezdza gorna powieka
HINGE_Y, HINGE_Z = -0.24, 0.08   # zawias szczeki (przed uchem)

def smooth(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)

def load_glb(path):
    d = open(path, 'rb').read()
    jl, _ = struct.unpack('<I4s', d[12:20])
    j = json.loads(d[20:20 + jl]); bo = 20 + jl + 8
    bv = j['bufferViews']
    def view(i):
        v = bv[i]; return d[bo + v.get('byteOffset', 0): bo + v.get('byteOffset', 0) + v['byteLength']]
    acc = j['accessors']; prim = j['meshes'][0]['primitives'][0]
    def accessor(ai, dtype, comps):
        a = acc[ai]; v = bv[a['bufferView']]
        raw = d[bo + v.get('byteOffset', 0) + a.get('byteOffset', 0):]
        return np.frombuffer(raw, dtype=dtype, count=a['count'] * comps).reshape(-1, comps) if comps > 1 else np.frombuffer(raw, dtype=dtype, count=a['count'])
    idx = accessor(prim['indices'], np.uint32 if acc[prim['indices']]['componentType'] == 5125 else np.uint16, 1).reshape(-1, 3).astype(np.int64)
    pos = accessor(prim['attributes']['POSITION'], np.float32, 3).astype(np.float64)
    uv = accessor(prim['attributes']['TEXCOORD_0'], np.float32, 2).astype(np.float64)
    img_view = j['images'][0]['bufferView']
    img = Image.open(io.BytesIO(view(img_view))).convert('RGB')
    return pos, uv, idx, img

def weld_groups(pos):
    key = np.round(pos * 1e5).astype(np.int64)
    _, inv = np.unique(key, axis=0, return_inverse=True)
    return inv.reshape(-1)

def compute_weights(pos):
    """pos: glTF (x,y,z). Zwraca (jaw, lidA, lidB) w [0,1]."""
    x, y, z = pos[:, 0], pos[:, 1], pos[:, 2]
    # ----- szczeka -----
    ax = np.abs(x - MOUTH_CX)
    y_front = -0.487 - CREASE_CURVE * (x - MOUTH_CX) ** 2           # kresa ust (do kacikow)
    y_corner = -0.487 - CREASE_CURVE * 0.17 ** 2
    z_corner = 0.78
    y_side = y_corner + 0.35 * np.maximum(0.0, z_corner - z)         # w bok: linia wznosi sie ku uchu
    w_mix = smooth((ax - 0.17) / 0.04)
    y_cut = y_front * (1 - w_mix) + y_side * w_mix
    band = 0.012 + 0.25 * np.maximum(0.0, ax - 0.14)
    band = np.minimum(band, 0.10)
    band = np.maximum(band, 0.012 + 0.20 * smooth((0.55 - z) / 0.5))  # blizej ucha - szerszy gradient
    t = (y_cut - y) / band                                            # <0 nad linia, >0 pod linia
    jaw = smooth((t + 1) / 2)
    jaw *= smooth((z - (-0.05)) / 0.35)                               # tyl glowy nie rusza sie
    # ----- powieki -----
    def lid(cx, cy):
        dx = np.abs(x - cx)
        sx = 1 - smooth((dx - 0.12) / 0.09)
        sy = np.zeros_like(y)
        inside = (y >= EYE_LO) & (y < EYE_UP)
        sy[inside] = (y[inside] - EYE_LO) / (EYE_UP - EYE_LO)
        above = y >= EYE_UP
        sy[above] = 1 - smooth((y[above] - 0.045) / 0.040)
        w = sx * sy
        w[z < 0.35] = 0
        return w
    la, lb = lid(*EYE_A), lid(*EYE_B)
    return jaw, la, lb

def main():
    pos_g, uv, idx_g, img = load_glb(SRC)
    n = len(pos_g)
    print('wierzcholki', n, 'trojkaty', len(idx_g))
    jaw, la, lb = compute_weights(pos_g)

    # ----- normalne (wygladzone przez szwy UV) w ukladzie glTF -----
    p0, p1, p2 = pos_g[idx_g[:, 0]], pos_g[idx_g[:, 1]], pos_g[idx_g[:, 2]]
    fn = np.cross(p1 - p0, p2 - p0)           # CCW => na zewnatrz
    grp = weld_groups(pos_g)
    acc = np.zeros((grp.max() + 1, 3))
    for k in range(3):
        np.add.at(acc, grp[idx_g[:, k]], fn)
    nrm = acc[grp]
    nrm /= (np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-12)

    # ----- wnetrze ust: osobna, druga siatka (wlaczana tylko przy otwartych ustach) -----
    # ciemny blok w atlasie tekstury (miejsce nieuzywane przez zadne UV)
    W, H = img.size
    cov = np.zeros((H, W), bool)
    for a_, b_, c_ in idx_g:
        xs = uv[[a_, b_, c_], 0] * W; ys = uv[[a_, b_, c_], 1] * H
        x0, x1 = int(max(0, np.floor(xs.min()) - 1)), int(min(W - 1, np.ceil(xs.max()) + 1))
        y0, y1 = int(max(0, np.floor(ys.min()) - 1)), int(min(H - 1, np.ceil(ys.max()) + 1))
        cov[y0:y1 + 1, x0:x1 + 1] = True
    BLK = 12
    cs = np.pad(cov.astype(np.int32), ((1, 0), (1, 0))).cumsum(0).cumsum(1)
    blk = cs[BLK:, BLK:] - cs[:-BLK, BLK:] - cs[BLK:, :-BLK] + cs[:-BLK, :-BLK]
    free = np.argwhere(blk == 0)
    by, bx = free[len(free) // 2]
    print('wolny blok tekstury:', bx, by)
    arr = np.array(img); arr[by:by + BLK, bx:bx + BLK] = (38, 6, 12); img = Image.fromarray(arr)
    dark_uv = np.array([(bx + BLK / 2) / W, (by + BLK / 2) / H])

    cen = pos_g[idx_g].mean(1)
    wj = jaw[idx_g]
    ycrease = CREASE_Y - CREASE_CURVE * (cen[:, 0] - MOUTH_CX) ** 2
    sel = ((wj.max(1) - wj.min(1)) >= 0.15) & (np.abs(cen[:, 0] - MOUTH_CX) < 0.17) & \
          (np.abs(cen[:, 1] - ycrease) < 0.04) & (cen[:, 2] > 0.6)
    sel_idx = np.nonzero(sel)[0]
    print('trojkaty wnetrza ust:', len(sel_idx))
    used = np.unique(idx_g[sel_idx].reshape(-1))
    remap = -np.ones(n, np.int64); remap[used] = np.arange(len(used))
    d_pos = pos_g[used] + nrm[used] * 0.004          # minimalnie przed skora (bez z-fightingu)
    d_nrm = nrm[used]
    d_uv = np.tile(dark_uv, (len(used), 1))
    d_w = np.stack([jaw[used], la[used], lb[used]], 1)
    d_idx = remap[idx_g[sel_idx]]

    # ----- konwersja do Unity: x -> -x, odwrocenie trojkatow -----
    flip = np.array([-1, 1, 1])
    pos_u = pos_g * flip; nrm_u = nrm * flip
    idx_u = idx_g[:, [0, 2, 1]]
    # kolejnosc trojkatow: od tylu do przodu wzdluz osi twarzy (+Z) - dla shaderow bez zapisu glebi
    cz = pos_u[idx_u].mean(1)[:, 2]
    idx_u = idx_u[np.argsort(cz, kind='stable')]
    d_pos_u = d_pos * flip; d_nrm_u = d_nrm * flip; d_idx_u = d_idx[:, [0, 2, 1]]

    assert len(pos_u) < 65535 and len(d_pos_u) < 65535
    def surf_z(x, y):
        m = (np.abs(pos_g[:, 0] - x) < 0.03) & (np.abs(pos_g[:, 1] - y) < 0.03) & (pos_g[:, 2] > 0.3)
        return float(pos_g[m, 2].max()) if m.any() else 0.8
    mouth_z = surf_z(MOUTH_CX, CREASE_Y)
    eyeA_z = surf_z(*EYE_A); eyeB_z = surf_z(*EYE_B)
    print('z ust', mouth_z, 'z oczu', eyeA_z, eyeB_z)
    lm = np.array([
        HINGE_Y, HINGE_Z,                                  # 0,1 zawias szczeki (y,z)
        -EYE_A[0], EYE_A[1], eyeA_z,                       # 2,3,4 oko A (x w Unity)
        -EYE_B[0], EYE_B[1], eyeB_z,                       # 5,6,7 oko B
        LID_DROP,                                          # 8
        -MOUTH_CX, CREASE_Y, mouth_z,                      # 9,10,11 srodek ust
        0.17,                                              # 12 polowa szerokosci ust
        CREASE_CURVE,                                      # 13
        float(pos_u[:, 1].max() - pos_u[:, 1].min()),      # 14 wysokosc modelu
        0.0], dtype=np.float32)

    buf = io.BytesIO(); img.save(buf, format='PNG', optimize=True, compress_level=9); png = buf.getvalue()
    print('PNG', img.size, len(png) // 1024, 'KB')

    q = lambda a: np.clip(np.round(a * 255), 0, 255).astype(np.uint8)
    with open(OUT, 'wb') as f:
        f.write(b'GBH2')
        f.write(struct.pack('<iiiiiii', len(pos_u), len(idx_u) * 3, len(d_pos_u), len(d_idx_u) * 3, img.size[0], img.size[1], len(lm)))
        f.write(lm.tobytes())
        # siatka glowna
        f.write(pos_u.astype('<f4').tobytes()); f.write(nrm_u.astype('<f4').tobytes()); f.write(uv.astype('<f4').tobytes())
        f.write(np.stack([q(jaw), q(la), q(lb)], 1).tobytes())
        f.write(idx_u.astype('<u2').reshape(-1).tobytes())
        # siatka wnetrza ust
        f.write(d_pos_u.astype('<f4').tobytes()); f.write(d_nrm_u.astype('<f4').tobytes()); f.write(d_uv.astype('<f4').tobytes())
        f.write(np.stack([q(d_w[:, 0]), q(d_w[:, 1]), q(d_w[:, 2])], 1).tobytes())
        f.write(d_idx_u.astype('<u2').reshape(-1).tobytes())
        f.write(struct.pack('<i', len(png))); f.write(png)
    print('zapisano', OUT, os.path.getsize(OUT) // 1024, 'KB')
    np.savez('/tmp/head_prepped.npz', pos_g=pos_g, uv=uv, idx_g=idx_g, jaw=jaw, la=la, lb=lb, lm=lm,
             d_pos=d_pos, d_uv=d_uv, d_idx=d_idx, d_w=d_w)

if __name__ == '__main__':
    main()
