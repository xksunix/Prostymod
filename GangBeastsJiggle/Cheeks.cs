using System;
using System.Collections.Generic;
using UnityEngine;

namespace GangBeastsJiggle
{
    /// <summary>
    /// Dwie widoczne polkule ("posladki") przyczepione do bioder postaci, na sprezynach.
    /// Zwykla klasa (nie MonoBehaviour) - w IL2CPP wlasne komponenty wymagalyby rejestracji.
    /// </summary>
    internal class Cheeks
    {
        internal static readonly List<Cheeks> All = new List<Cheeks>();

        private readonly Butt butt;
        private GameObject root;
        private Transform left, right, cleft;
        private Vector3 pl, vl, pr, vr;
        private bool init;
        private float shake, shakeDur = 1f, shakeForce = 1f;

        private Cheeks(Butt b) { butt = b; }

        public static Cheeks Create(Butt b)
        {
            var c = new Cheeks(b);
            c.Build();
            All.Add(c);
            return c;
        }

        public static void ShakeAll(float dur, float force)
        {
            for (int i = 0; i < All.Count; i++) All[i].Shake(dur, force);
        }

        public void Shake(float dur, float force)
        {
            shakeDur = Mathf.Max(0.05f, dur);
            shake = shakeDur;
            shakeForce = Mathf.Max(0f, force);
        }

        private void Build()
        {
            root = new GameObject("GBCheeks");
            Color c = butt.Body;
            left = Make("CheekL", PrimitiveType.Sphere, c);
            right = Make("CheekR", PrimitiveType.Sphere, c);
            // ciemniejsza kreska miedzy polkulami, zeby bylo widac, ze to dwie
            cleft = Make("Cleft", PrimitiveType.Capsule, new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f));
        }

        private Transform Make(string name, PrimitiveType type, Color c)
        {
            var go = PoopFeature.Prim(name, type, c);
            go.transform.SetParent(root.transform, false);
            return go.transform;
        }

        public bool IsAlive() { return root != null; }

        public void SetVisible(bool v)
        {
            if (root != null && root.activeSelf != v) root.SetActive(v);
        }

        public void Destroy()
        {
            All.Remove(this);
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
        }

        // Wywolywane w LateUpdate - po fizyce i animacji
        public void Step(float dt)
        {
            if (root == null) return;
            Vector3 up = butt.Up, back = butt.Back, side = butt.Side;
            float u = butt.Unit;
            float d = 0.26f * u * JiggleMod.BaseScale.Value;            // srednica jednej polkuli
            Vector3 baseP = butt.HipsPos + back * (JiggleMod.CheekBack.Value * u) + up * (JiggleMod.CheekUp.Value * u);
            float sep = 0.45f * d;
            Vector3 restL = baseP - side * sep;
            Vector3 restR = baseP + side * sep;

            // pierdniecie: silne, szybkie wstrzasy slabnace do zera
            float sk = 0f;
            if (shake > 0f)
            {
                shake -= dt;
                sk = Mathf.Clamp01(shake / shakeDur);
                float ph = Time.time * 40f;
                Vector3 sl = side * Mathf.Sin(ph) + up * (0.6f * Mathf.Sin(ph * 1.7f + 1f)) + back * (0.5f * Mathf.Cos(ph * 1.3f));
                Vector3 sr = side * Mathf.Sin(ph + 2.1f) + up * (0.6f * Mathf.Sin(ph * 1.7f + 3f)) + back * (0.5f * Mathf.Cos(ph * 1.3f + 1.7f));
                float amp = 0.22f * u * shakeForce * sk;
                restL += sl * amp;
                restR += sr * amp;
            }

            if (!init)
            {
                pl = restL; pr = restR;
                vl = vr = Vector3.zero;
                init = true;
            }

            float jig = Mathf.Max(0.3f, JiggleMod.JiggleScale.Value * 2f);
            float k = 150f / jig, c = 7f / Mathf.Sqrt(jig);
            float sdt = Mathf.Min(dt, 0.033f);
            Spring(ref pl, ref vl, restL, k, c, sdt);
            Spring(ref pr, ref vr, restR, k, c, sdt);

            // nie pozwol odleciec (np. po teleportacji postaci)
            pl = ClampFrom(restL, pl, 0.45f * u);
            pr = ClampFrom(restR, pr, 0.45f * u);

            float dd = d * (1f + 0.15f * sk * shakeForce * Mathf.Sin(Time.time * 36f));
            left.position = pl;
            left.localScale = Vector3.one * dd;
            right.position = pr;
            right.localScale = Vector3.one * dd;

            cleft.position = (pl + pr) * 0.5f + back * (0.40f * d);
            cleft.rotation = Quaternion.FromToRotation(Vector3.up, up);
            cleft.localScale = new Vector3(0.05f * d, 0.40f * d, 0.05f * d);
        }

        private static void Spring(ref Vector3 pos, ref Vector3 vel, Vector3 rest, float k, float c, float dt)
        {
            Vector3 acc = (rest - pos) * k - vel * c;
            vel += acc * dt;
            pos += vel * dt;
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z))
            {
                pos = rest;
                vel = Vector3.zero;
            }
        }

        private static Vector3 ClampFrom(Vector3 origin, Vector3 p, float max)
        {
            Vector3 dv = p - origin;
            if (dv.sqrMagnitude > max * max) return origin + dv.normalized * max;
            return p;
        }
    }
}
