using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace GBFaces
{
    // Twarz jednej postaci: usta + dwie brwi przyczepione do glowy.
    // Zbudowana z wbudowanych ksztaltow Unity (kula, szescian) - bez wlasnych siatek,
    // bo te w IL2CPP wymagaja tablic Il2Cpp.
    internal class FaceController
    {
        private const int LipSegments = 9;
        private static bool dumped;
        private static Shader cachedShader;
        private static bool shaderSearched;

        public Component Actor;
        public Transform Head;
        public float Laugh;          // ile sekund jeszcze sie smieje

        private GameObject root;
        private Transform mouth, browL, browR;
        private Transform[] lip = new Transform[LipSegments];
        private Renderer mouthR;
        private Renderer[] lipR = new Renderer[LipSegments];

        private float open, smile, angry, raise;
        private float time;
        private bool wasKnocked;

        private MemberInfo stateMember;
        private readonly List<MemberInfo> angerFlags = new List<MemberInfo>();
        private readonly List<MemberInfo> strainFlags = new List<MemberInfo>();
        private readonly bool hasActorLogic;

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
            try
            {
                fc.Build();
            }
            catch (Exception e)
            {
                log.Error("Nie udalo sie zbudowac twarzy: " + e);
                try { fc.Destroy(); } catch { }
                return null;
            }
            if (isActor)
            {
                try { fc.Reflect(log); }
                catch (Exception e) { log.Warning("Nie udalo sie odczytac stanu postaci (twarz bedzie statyczna): " + e.Message); }
            }

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
            root = new GameObject("GBFace");
            root.transform.SetParent(Head, false);
            root.layer = Head.gameObject.layer;

            var dark = new Color(0.12f, 0.01f, 0.03f);
            var black = new Color(0.02f, 0.02f, 0.02f);

            mouth = Prim("Mouth", PrimitiveType.Sphere, dark, out mouthR);
            for (int i = 0; i < LipSegments; i++)
                lip[i] = Prim("Lip" + i, PrimitiveType.Sphere, black, out lipR[i]);
            Renderer tmp;
            browL = Prim("BrowL", PrimitiveType.Cube, black, out tmp);
            browR = Prim("BrowR", PrimitiveType.Cube, black, out tmp);
        }

        private Transform Prim(string name, PrimitiveType type, Color color, out Renderer rend)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;

            // zadnych kolizji - twarz ma tylko wygladac
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);

            go.transform.SetParent(root.transform, false);
            go.layer = root.layer;

            var r = go.GetComponent<Renderer>();
            rend = r;
            if (r != null)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                Colorize(r, color);
            }
            return go.transform;
        }

        private static void Colorize(Renderer r, Color c)
        {
            if (!shaderSearched)
            {
                shaderSearched = true;
                string[] names = { "Unlit/Color", "Sprites/Default", "Standard", "Legacy Shaders/Diffuse" };
                foreach (var n in names)
                {
                    var sh = Shader.Find(n);
                    if (sh != null) { cachedShader = sh; break; }
                }
            }

            Material m;
            if (cachedShader != null)
            {
                m = new Material(cachedShader);
                m.color = c;
                r.sharedMaterial = m;
            }
            else
            {
                m = r.material;     // domyslny material ksztaltu
                m.color = c;
            }
        }

        // ---------- Odczyt stanu postaci przez refleksje ----------

        private void Reflect(MelonLogger.Instance log)
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = Actor.GetType();
            var seen = new List<string>();

            // stan postaci: najpierw "actorState", potem cokolwiek enumowego z "state" w nazwie
            foreach (var f in t.GetFields(bf))
                if (string.Equals(f.Name, "actorState", StringComparison.OrdinalIgnoreCase)) stateMember = f;
            foreach (var p in t.GetProperties(bf))
                if (stateMember == null && p.CanRead && p.GetIndexParameters().Length == 0
                    && string.Equals(p.Name, "actorState", StringComparison.OrdinalIgnoreCase)) stateMember = p;
            if (stateMember == null)
            {
                foreach (var p in t.GetProperties(bf))
                {
                    if (p.CanRead && p.GetIndexParameters().Length == 0 && p.PropertyType.IsEnum
                        && p.Name.ToLowerInvariant().Contains("state")) { stateMember = p; break; }
                }
            }
            if (stateMember == null)
            {
                foreach (var f in t.GetFields(bf))
                    if (f.FieldType.IsEnum && f.Name.ToLowerInvariant().Contains("state")) { stateMember = f; break; }
            }

            foreach (var f in t.GetFields(bf))
                if (f.FieldType == typeof(bool)) { ClassifyFlag(f, f.Name); seen.Add(f.Name); }
            foreach (var p in t.GetProperties(bf))
                if (p.PropertyType == typeof(bool) && p.CanRead && p.GetIndexParameters().Length == 0)
                { ClassifyFlag(p, p.Name); seen.Add(p.Name); }

            log.Msg("Stan postaci: " + (stateMember != null ? stateMember.Name : "(nie znaleziono)")
                + ", flagi gniewu: " + angerFlags.Count + ", flagi wysilku: " + strainFlags.Count);

            // jednorazowo: nazwy czlonkow typu bool/enum, zeby latwo dopasowac reakcje
            if (!membersLogged)
            {
                membersLogged = true;
                var enums = new List<string>();
                foreach (var p in t.GetProperties(bf))
                    if (p.PropertyType.IsEnum) enums.Add(p.Name + ":" + p.PropertyType.Name);
                log.Msg("Czlonki enum: " + string.Join(", ", enums.ToArray()));
                if (seen.Count > 80) seen.RemoveRange(80, seen.Count - 80);
                log.Msg("Czlonki bool: " + string.Join(", ", seen.ToArray()));
            }
        }

        private static bool membersLogged;

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

        // Pozycja na lekko wygietej "kuli" glowy: z maleje ku brzegom
        private static Vector3 Pos(float x, float y, float push, float eps)
        {
            float z = push - (x * x + y * y) / (2f * push) + eps;
            return new Vector3(x, y, z);
        }

        private void Apply()
        {
            int fwd = Mathf.Clamp(FaceMod.PFwd.Value, 0, 5);
            int up = Mathf.Clamp(FaceMod.PUp.Value, 0, 5);
            if (up / 2 == fwd / 2) up = (fwd + 2) % 6;

            var rt = root.transform;
            rt.localRotation = Quaternion.LookRotation(FaceMod.Axes[fwd], FaceMod.Axes[up]);
            float s = Mathf.Max(0.0001f, Head.lossyScale.x);
            rt.localScale = Vector3.one * (FaceMod.PSize.Value / s);
            rt.localPosition = Vector3.zero;

            float push = Mathf.Max(0.1f, FaceMod.PPush.Value);
            float my = FaceMod.PMouthY.Value;
            float by = FaceMod.PBrowY.Value;
            float bx = FaceMod.PBrowX.Value;

            // ----- usta (ciemna, splaszczona kula) -----
            float openH = open * 0.34f;
            float mouthW = 0.22f + 0.05f * Mathf.Max(0f, smile) + 0.04f * open;
            bool showMouth = openH > 0.03f;
            if (mouthR != null) mouthR.enabled = showMouth;
            if (showMouth)
            {
                mouth.localPosition = Pos(0f, my - openH * 0.2f, push, 0.004f);
                mouth.localRotation = Quaternion.identity;
                mouth.localScale = new Vector3(mouthW * 2f, openH, 0.05f);
            }

            // ----- warga / usmiech: luk z malych kulek (gdy usta zamkniete lub prawie) -----
            bool showLip = open < 0.45f;
            float curve = smile * 0.07f;
            for (int i = 0; i < LipSegments; i++)
            {
                if (lipR[i] != null) lipR[i].enabled = showLip;
                if (!showLip) continue;
                float t = (float)i / (LipSegments - 1) * 2f - 1f;           // -1..1
                float x = t * mouthW;
                float y = my + curve * (t * t - 0.5f);
                lip[i].localPosition = Pos(x, y, push, 0.009f);
                lip[i].localRotation = Quaternion.identity;
                lip[i].localScale = new Vector3(0.05f, 0.05f, 0.03f);
            }

            // ----- brwi (cienkie czarne sześciany) -----
            float tiltDeg = angry * 24f - raise * 8f;
            float lift = raise * 0.06f - angry * 0.025f;
            browR.localPosition = Pos(bx, by + lift, push, 0.009f);
            browR.localRotation = Quaternion.Euler(0f, 0f, tiltDeg);
            browR.localScale = new Vector3(0.17f, 0.034f, 0.03f);
            browL.localPosition = Pos(-bx, by + lift, push, 0.009f);
            browL.localRotation = Quaternion.Euler(0f, 0f, -tiltDeg);
            browL.localScale = new Vector3(0.17f, 0.034f, 0.03f);
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
            string flags = "";
            try
            {
                if (t.GetComponent<Renderer>() != null) flags += " [Renderer]";
                if (t.GetComponent<Rigidbody>() != null) flags += " [Rigidbody]";
            }
            catch { }
            log.Msg(new string(' ', depth * 2) + t.name + flags);
            for (int i = 0; i < t.childCount; i++) DumpRec(t.GetChild(i), depth + 1, log, count);
        }
    }
}
