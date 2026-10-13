using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace GBFaces
{
    // Twarz jednej postaci: usta + dwie brwi przyczepione do glowy.
    internal class FaceController
    {
        private static bool dumped;

        public Component Actor;
        public Transform Head;
        public float Laugh;          // ile sekund jeszcze sie smieje

        private GameObject root;
        private Mesh mouthM, lipM, browLM, browRM;
        private Renderer mouthR, lipR;

        private float open, smile, angry, raise;
        private float time;
        private bool wasKnocked;

        private MemberInfo stateMember;
        private readonly List<MemberInfo> angerFlags = new List<MemberInfo>();
        private readonly List<MemberInfo> strainFlags = new List<MemberInfo>();
        private readonly bool hasActorLogic;

        private static Material darkMat, blackMat;

        private FaceController(Component actor, Transform head, bool hasActorLogic)
        {
            Actor = actor;
            Head = head;
            this.hasActorLogic = hasActorLogic;
        }

        // ---------- Tworzenie ----------

        public static FaceController Create(Component actor, bool isActor, MelonLogger.Instance log)
        {
            Transform head = isActor ? FindHead(actor.transform) : actor.transform;
            if (head == null)
            {
                log.Warning("Postac '" + actor.name + "' - nie znaleziono kosci glowy (szukam nazwy z 'head').");
                if (!dumped) { dumped = true; Dump(actor.transform, log); }
                return null;
            }

            var fc = new FaceController(actor, head, isActor);
            fc.Build();
            if (isActor) fc.Reflect(log);

            log.Msg("Dodano twarz: " + actor.name + " -> " + Path(head));
            if (!dumped)
            {
                dumped = true;
                Dump(head, log);
            }
            return fc;
        }

        private void Build()
        {
            if (darkMat == null) darkMat = MakeMaterial(new Color(0.12f, 0.01f, 0.03f));
            if (blackMat == null) blackMat = MakeMaterial(new Color(0.02f, 0.02f, 0.02f));
            darkMat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            blackMat.hideFlags = HideFlags.DontUnloadUnusedAsset;

            root = new GameObject("GBFace");
            root.transform.SetParent(Head, false);
            root.layer = Head.gameObject.layer;

            mouthM = NewPart("Mouth", darkMat, out mouthR);
            lipM = NewPart("Lip", blackMat, out lipR);
            Renderer tmp;
            browLM = NewPart("BrowL", blackMat, out tmp);
            browRM = NewPart("BrowR", blackMat, out tmp);
        }

        private Mesh NewPart(string name, Material mat, out Renderer rend)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.layer = root.layer;
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var m = new Mesh();
            m.name = name;
            mf.sharedMesh = m;
            rend = mr;
            return m;
        }

        private static Material MakeMaterial(Color c)
        {
            string[] names = { "Unlit/Color", "Sprites/Default", "Standard", "Legacy Shaders/Diffuse", "Diffuse" };
            foreach (var n in names)
            {
                var sh = Shader.Find(n);
                if (sh == null) continue;
                var m = new Material(sh);
                m.color = c;
                return m;
            }
            // Ostatnia deska ratunku: material z jakiegos renderera w scenie
            var any = UnityEngine.Object.FindObjectOfType(typeof(Renderer)) as Renderer;
            if (any != null && any.sharedMaterial != null)
            {
                var m = new Material(any.sharedMaterial);
                m.color = c;
                return m;
            }
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        // ---------- Odczyt stanu postaci przez refleksje ----------

        private void Reflect(MelonLogger.Instance log)
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = Actor.GetType();

            // stan postaci: najpierw "actorState", potem cokolwiek enumowego z "state" w nazwie
            foreach (var f in t.GetFields(bf))
                if (string.Equals(f.Name, "actorState", StringComparison.OrdinalIgnoreCase)) stateMember = f;
            foreach (var p in t.GetProperties(bf))
                if (stateMember == null && p.CanRead && string.Equals(p.Name, "actorState", StringComparison.OrdinalIgnoreCase)) stateMember = p;
            if (stateMember == null)
            {
                foreach (var f in t.GetFields(bf))
                    if (f.FieldType.IsEnum && f.Name.ToLowerInvariant().Contains("state")) { stateMember = f; break; }
            }

            foreach (var f in t.GetFields(bf))
                if (f.FieldType == typeof(bool)) ClassifyFlag(f, f.Name);
            foreach (var p in t.GetProperties(bf))
                if (p.PropertyType == typeof(bool) && p.CanRead && p.GetIndexParameters().Length == 0) ClassifyFlag(p, p.Name);

            log.Msg("Stan postaci: " + (stateMember != null ? stateMember.Name : "(nie znaleziono)")
                + ", flagi gniewu: " + angerFlags.Count + ", flagi wysilku: " + strainFlags.Count);
        }

        private void ClassifyFlag(MemberInfo m, string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("punch") || n.Contains("attack") || n.Contains("fight") || n.Contains("kick"))
                angerFlags.Add(m);
            else if (n.Contains("grab") || n.Contains("hold") || n.Contains("carry") || n.Contains("climb"))
                strainFlags.Add(m);
        }

        private static object Read(MemberInfo m, object o)
        {
            try
            {
                var f = m as FieldInfo;
                if (f != null) return f.GetValue(o);
                var p = m as PropertyInfo;
                if (p != null) return p.GetValue(o, null);
            }
            catch { }
            return null;
        }

        private static bool AnyTrue(List<MemberInfo> list, object o)
        {
            foreach (var m in list)
            {
                var v = Read(m, o);
                if (v is bool && (bool)v) return true;
            }
            return false;
        }

        // ---------- Aktualizacja ----------

        public bool IsAlive()
        {
            return Actor != null && Head != null && root != null;
        }

        public void Destroy()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
        }

        // Zwraca true w klatce, w ktorej postac wlasnie dostala nokaut
        public bool Tick(float dt)
        {
            time += dt;
            if (Laugh > 0f) Laugh -= dt;

            string state = "";
            if (hasActorLogic && stateMember != null)
            {
                var v = Read(stateMember, Actor);
                if (v != null) state = v.ToString().ToLowerInvariant();
            }

            bool knocked = state.Contains("unconscious") || state.Contains("dead") || state.Contains("knock") || state == "ko";
            bool falling = state.Contains("fall") || state.Contains("ragdoll");
            bool fighting = hasActorLogic && AnyTrue(angerFlags, Actor);
            bool straining = hasActorLogic && AnyTrue(strainFlags, Actor);

            float tOpen, tSmile, tAngry, tRaise;
            if (knocked)
            {
                tOpen = 1f; tSmile = 0f; tAngry = 0f; tRaise = 1f;      // szeroko otwarte usta, brwi w gore
            }
            else if (fighting)
            {
                tOpen = 0.18f; tSmile = -0.9f; tAngry = 1f; tRaise = 0f; // zacisniete, zly
            }
            else if (Laugh > 0f)
            {
                tOpen = 0.4f + 0.35f * Mathf.Abs(Mathf.Sin(time * 14f)); // usta ruszaja sie w rytm
                tSmile = 1f; tAngry = 0f; tRaise = 0.35f;
            }
            else if (falling)
            {
                tOpen = 0.7f; tSmile = -0.3f; tAngry = 0f; tRaise = 0.8f;
            }
            else if (straining)
            {
                tOpen = 0.12f; tSmile = -0.5f; tAngry = 0.6f; tRaise = 0f;
            }
            else
            {
                tOpen = 0f; tSmile = 0.35f; tAngry = 0f; tRaise = 0f;
            }

            float k = 1f - Mathf.Exp(-14f * dt);
            open = Mathf.Lerp(open, tOpen, k);
            smile = Mathf.Lerp(smile, tSmile, k);
            angry = Mathf.Lerp(angry, tAngry, k);
            raise = Mathf.Lerp(raise, tRaise, k);

            Apply();

            bool newKo = knocked && !wasKnocked;
            wasKnocked = knocked;
            return newKo;
        }

        private void Apply()
        {
            int fwd = Mathf.Clamp(FaceMod.PFwd.Value, 0, 5);
            int up = Mathf.Clamp(FaceMod.PUp.Value, 0, 5);
            if (up / 2 == fwd / 2) up = (fwd + 2) % 6;

            root.transform.localRotation = Quaternion.LookRotation(FaceMod.Axes[fwd], FaceMod.Axes[up]);
            float s = Mathf.Max(0.0001f, Head.lossyScale.x);
            root.transform.localScale = Vector3.one * (FaceMod.PSize.Value / s);
            root.transform.localPosition = Vector3.zero;

            float push = FaceMod.PPush.Value;
            float my = FaceMod.PMouthY.Value;
            float by = FaceMod.PBrowY.Value;
            float bx = FaceMod.PBrowX.Value;

            // ----- usta -----
            var b = new MeshBuilder(push);
            float openH = open * 0.34f;
            float mouthW = 0.22f + 0.05f * Mathf.Max(0f, smile) + 0.04f * open;
            bool showMouth = openH > 0.012f;
            mouthR.enabled = showMouth;
            if (showMouth)
            {
                b.Ellipse(0f, my - openH * 0.2f, mouthW, openH * 0.5f, 0.004f);
                b.ApplyTo(mouthM);
            }

            // ----- warga / usmiech (rysowana, gdy usta zamkniete lub prawie) -----
            lipR.enabled = open < 0.45f;
            if (lipR.enabled)
            {
                var l = new MeshBuilder(push);
                l.Arc(0f, my, mouthW, smile * 0.07f, 0.022f, 0.009f);
                l.ApplyTo(lipM);
            }

            // ----- brwi -----
            float tiltDeg = angry * 24f - raise * 8f;
            float lift = raise * 0.06f - angry * 0.025f;
            var br = new MeshBuilder(push);
            br.Brow(bx, by + lift, 0.17f, 0.032f, tiltDeg, 0.009f);
            br.ApplyTo(browRM);
            var bl = new MeshBuilder(push);
            bl.Brow(-bx, by + lift, 0.17f, 0.032f, -tiltDeg, 0.009f);
            bl.ApplyTo(browLM);
        }

        // ---------- Pomocnicze ----------

        private static Transform FindHead(Transform start)
        {
            // Przeszukaj ten obiekt, a jesli nic - caly korzen
            var t = FindHeadIn(start);
            if (t == null && start.root != start) t = FindHeadIn(start.root);
            return t;
        }

        private static Transform FindHeadIn(Transform start)
        {
            Transform contains = null;
            var queue = new Queue<Transform>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                string n = t.name.ToLowerInvariant();
                if (n == "head") return t;
                if (contains == null && n.Contains("head") && !n.Contains("phone") && !n.Contains("set")) contains = t;
                for (int i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
            }
            return contains;
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        // Jednorazowy zrzut hierarchii do logu - pomaga ustawic twarz, jesli cos nie pasuje
        private static void Dump(Transform t, MelonLogger.Instance log)
        {
            try
            {
                var top = t;
                for (int i = 0; i < 2 && top.parent != null; i++) top = top.parent;
                log.Msg("--- hierarchia (zrzut) ---");
                DumpRec(top, 0, log, new int[] { 0 });
                log.Msg("--- koniec zrzutu ---");
            }
            catch { }
        }

        private static void DumpRec(Transform t, int depth, MelonLogger.Instance log, int[] count)
        {
            if (count[0]++ > 80 || depth > 6) return;
            string comps = "";
            foreach (var c in t.GetComponents<Component>())
                if (c != null) comps += c.GetType().Name + " ";
            log.Msg(new string(' ', depth * 2) + t.name + "  [" + comps.Trim() + "]");
            for (int i = 0; i < t.childCount; i++) DumpRec(t.GetChild(i), depth + 1, log, count);
        }
    }

    // Prosty budowniarz siatek 2D owiniety lekko na kuli glowy (dwustronne trojkaty).
    internal class MeshBuilder
    {
        private readonly List<Vector3> v = new List<Vector3>();
        private readonly List<int> tr = new List<int>();
        private readonly float push;

        public MeshBuilder(float push)
        {
            this.push = Mathf.Max(0.1f, push);
        }

        private Vector3 P(float x, float y, float eps)
        {
            float z = push - (x * x + y * y) / (2f * push) + eps;
            return new Vector3(x, y, z);
        }

        private int Vtx(float x, float y, float eps)
        {
            v.Add(P(x, y, eps));
            return v.Count - 1;
        }

        private void Tri(int a, int b, int c)
        {
            tr.Add(a); tr.Add(b); tr.Add(c);
            tr.Add(a); tr.Add(c); tr.Add(b);   // druga strona
        }

        public void Ellipse(float cx, float cy, float rx, float ry, float eps)
        {
            const int n = 24;
            int c = Vtx(cx, cy, eps);
            int first = v.Count;
            for (int i = 0; i < n; i++)
            {
                float a = (float)i / n * Mathf.PI * 2f;
                Vtx(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry, eps);
            }
            for (int i = 0; i < n; i++)
                Tri(c, first + i, first + (i + 1) % n);
        }

        // Luk: smile > 0 -> konce w gore (usmiech), < 0 -> konce w dol (grymas)
        public void Arc(float cx, float cy, float halfW, float curve, float thick, float eps)
        {
            const int n = 12;
            int prevTop = -1, prevBot = -1;
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n * 2f - 1f;           // -1..1
                float x = cx + t * halfW;
                float y = cy + curve * (t * t - 0.5f);
                int top = Vtx(x, y + thick * 0.5f, eps);
                int bot = Vtx(x, y - thick * 0.5f, eps);
                if (prevTop >= 0)
                {
                    Tri(prevTop, prevBot, top);
                    Tri(top, prevBot, bot);
                }
                prevTop = top; prevBot = bot;
            }
        }

        // Brew: prostokat o dlugosci len, obrocony o angleDeg wokol srodka
        public void Brow(float cx, float cy, float len, float thick, float angleDeg, float eps)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            float dx = Mathf.Cos(a), dy = Mathf.Sin(a);
            float nx = -dy, ny = dx;
            float hl = len * 0.5f, ht = thick * 0.5f;
            int p0 = Vtx(cx - dx * hl - nx * ht, cy - dy * hl - ny * ht, eps);
            int p1 = Vtx(cx + dx * hl - nx * ht, cy + dy * hl - ny * ht, eps);
            int p2 = Vtx(cx + dx * hl + nx * ht, cy + dy * hl + ny * ht, eps);
            int p3 = Vtx(cx - dx * hl + nx * ht, cy - dy * hl + ny * ht, eps);
            Tri(p0, p1, p2);
            Tri(p0, p2, p3);
        }

        public void ApplyTo(Mesh m)
        {
            m.Clear();
            m.SetVertices(v);
            m.SetTriangles(tr, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
        }
    }
}
