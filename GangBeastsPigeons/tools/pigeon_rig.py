"""Wspolne funkcje do czytania modelu golebia z pliku .blend (uruchamiane przez Blendera jako modul python: pip install bpy).

Uklad Blendera: prawoskretny, Z w gore, golab patrzy w +Y, jego prawa strona to +X.
Uklad Unity   : lewoskretny, Y w gore, przod = +Z, prawo = +X.
Przeksztalcenie: (x, y, z) -> (x, z, y)  (zamiana osi Y i Z = odbicie, wiec trzeba tez odwrocic kolejnosc trojkatow).
"""
import math
import numpy as np
import bpy
from mathutils import Matrix, Vector, Quaternion

# macierz zamiany osi (jest swoja wlasna odwrotnoscia)
MU = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))


def conj(m):
    """Macierz przeksztalcenia zapisana w ukladzie Blendera -> ta sama w ukladzie Unity."""
    return MU @ m @ MU


def lin2srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, 12.92 * c, 1.055 * np.power(c, 1 / 2.4) - 0.055)


class Rig:
    def __init__(self, path):
        bpy.ops.wm.open_mainfile(filepath=path)
        self.sc = bpy.context.scene
        self.arm = bpy.data.objects['pigeon-armature']
        self.mesh = bpy.data.objects['pigeon-model']
        self.bones = list(self.arm.data.bones)
        self.names = [b.name for b in self.bones]
        self.parent = [self.names.index(b.parent.name) if b.parent else -1 for b in self.bones]
        self.arm.animation_data_create()

    # ---------- poza ----------
    def reset_pose(self):
        for pb in self.arm.pose.bones:
            pb.location = (0, 0, 0)
            pb.rotation_mode = 'QUATERNION'
            pb.rotation_quaternion = (1, 0, 0, 0)
            pb.scale = (1, 1, 1)
        bpy.context.view_layer.update()

    def pose_action(self, action_name, frame):
        """Czysta poza (reszta kosci w spoczynku) + akcja na danej klatce."""
        self.reset_pose()
        ad = self.arm.animation_data
        act = bpy.data.actions[action_name]
        ad.action = act
        if len(act.slots):
            ad.action_slot = act.slots[0]
        self.sc.frame_set(int(math.floor(frame)), subframe=frame - math.floor(frame))
        bpy.context.view_layer.update()

    def clear_action(self):
        self.arm.animation_data.action = None
        self.reset_pose()

    def pose_world(self):
        """Macierze kosci w przestrzeni modelu (Blender), w kolejnosci self.names."""
        return [pb.matrix.copy() for pb in (self.arm.pose.bones[n] for n in self.names)]

    def rest_world(self):
        return [b.matrix_local.copy() for b in self.bones]

    # ---------- siatka w pozie ----------
    def deformed_vertices(self):
        dg = bpy.context.evaluated_depsgraph_get()
        ev = self.mesh.evaluated_get(dg)
        me = ev.to_mesh()
        v = np.array([tuple(p.co) for p in me.vertices], dtype=np.float64)
        ev.to_mesh_clear()
        return v


def local_trs(world_list, parent, convert=True):
    """world_list: macierze kosci (przestrzen modelu, Blender). Zwraca liste (poz[3], rot[x,y,z,w]) wzgledem rodzica, w ukladzie Unity."""
    out = []
    W = [conj(m) if convert else m for m in world_list]
    for i, w in enumerate(W):
        loc = w if parent[i] < 0 else W[parent[i]].inverted() @ w
        t, q, s = loc.decompose()
        out.append((np.array([t.x, t.y, t.z]), np.array([q.x, q.y, q.z, q.w]), np.array([s.x, s.y, s.z])))
    return out


# ---------- wlasna poza: zlozone skrzydla (golab stoi) ----------
def fold_pose(rig, swing=78.0, roll=82.0, elbow=14.0, elbow_roll=0.0, droop=0.0):
    """Sklada skrzydla przy ciele: obrot wokol osi rozpietosci (pioro w dol) + odchylenie do tylu.
    Wszystko w przestrzeni modelu Blendera; zwraca macierze kosci (Blender)."""
    rig.clear_action()
    arm = rig.arm
    for side, sgn in (('right', 1.0), ('left', -1.0)):
        b1 = arm.data.bones[side + '-wing-1']
        b2 = arm.data.bones[side + '-wing-2']
        h1 = b1.head_local.copy(); h2 = b2.head_local.copy()

        def about(pivot, rot):
            return Matrix.Translation(pivot) @ rot @ Matrix.Translation(-pivot)

        # obrot wokol +X o +roll obraca tylna krawedz (-Y) w dol (-Z) - tak samo dla obu skrzydel (odbicie lustrzane nie zmienia obrotu wokol X),
        # a odchylenie do tylu (wokol Z) ma przeciwny znak po obu stronach
        rx = Matrix.Rotation(math.radians(roll), 4, 'X')
        rz = Matrix.Rotation(math.radians(-swing * sgn), 4, 'Z')
        p1 = about(h1, rz @ rx)
        w1 = p1 @ b1.matrix_local
        pb1 = arm.pose.bones[b1.name]
        pb1.matrix = w1
        bpy.context.view_layer.update()

        ez = Matrix.Rotation(math.radians(-elbow * sgn), 4, 'Z')
        ex = Matrix.Rotation(math.radians(elbow_roll), 4, 'X')
        w2 = p1 @ about(h2, ez @ ex) @ b2.matrix_local
        pb2 = arm.pose.bones[b2.name]
        pb2.matrix = w2
        bpy.context.view_layer.update()
    return rig.pose_world()
