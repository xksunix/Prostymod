using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

[assembly: MelonInfo(typeof(GBWilly.WillyMod), "Gang Beasts Willy", "1.1.0", "Claude")]

namespace GBWilly
{
    // ------------------------------------------------------------------
    //  Klawisze przez WinAPI (stary Input w tej grze rzuca wyjatek, a Input System nie istnieje)
    // ------------------------------------------------------------------
    internal static class WinKey
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private class St { public int Frame = -1; public bool Now, Prev; }
        private static readonly Dictionary<int, St> states = new Dictionary<int, St>();
        private static int errors;

        private static int Vk(KeyCode k)
        {
            int c = (int)k;
            if (c >= (int)KeyCode.A && c <= (int)KeyCode.Z) return 0x41 + (c - (int)KeyCode.A);
            if (c >= (int)KeyCode.Alpha0 && c <= (int)KeyCode.Alpha9) return 0x30 + (c - (int)KeyCode.Alpha0);
            if (c >= (int)KeyCode.F1 && c <= (int)KeyCode.F12) return 0x70 + (c - (int)KeyCode.F1);
            switch (k)
            {
                case KeyCode.PageUp: return 0x21;
                case KeyCode.PageDown: return 0x22;
                case KeyCode.End: return 0x23;
                case KeyCode.Home: return 0x24;
            }
            return 0;
        }

        private static St Get(KeyCode k)
        {
            int vk = Vk(k);
            if (vk == 0) return null;
            St s;
            if (!states.TryGetValue(vk, out s)) { s = new St(); states[vk] = s; }
            int frame = Time.frameCount;
            if (s.Frame != frame)
            {
                s.Prev = s.Now;
                s.Frame = frame;
                bool now = false;
                try { now = Application.isFocused && (GetAsyncKeyState(vk) & 0x8000) != 0; }
                catch (Exception e) { if (errors++ < 5) MelonLogger.Error("[Willy] GetAsyncKeyState: " + e.Message); }
                s.Now = now;
            }
            return s;
        }

        public static bool Held(KeyCode k) { var s = Get(k); return s != null && s.Now; }
        public static bool Down(KeyCode k) { var s = Get(k); return s != null && s.Now && !s.Prev; }
    }

    // ------------------------------------------------------------------
    //  Klawisze (przez refleksje: stary Input albo Input System)
    // ------------------------------------------------------------------
    internal static class Keys
    {
        private static int mode;            // 0 = nie sprawdzono, 1 = stary Input, 2 = Input System, 3 = brak
        private static bool legacyTried;
        private static int fails;
        private static MethodInfo mGetKey, mGetKeyDown;
        private static Type tKeyboard;
        private static readonly Dictionary<string, PropertyInfo> propCache = new Dictionary<string, PropertyInfo>();

        public static bool Down(KeyCode k) { return Query(k, true); }
        public static bool Held(KeyCode k) { return Query(k, false); }

        private static bool Query(KeyCode k, bool down)
        {
            if (mode == 0) Resolve();
            if (mode == 4) return down ? WinKey.Down(k) : WinKey.Held(k);
            if (mode == 1)
            {
                try
                {
                    bool r = (bool)(down ? mGetKeyDown : mGetKey).Invoke(null, new object[] { k });
                    fails = 0;
                    return r;
                }
                catch
                {
                    fails++;
                    if (fails >= 3) { fails = 0; mode = 4; MelonLogger.Warning("[Willy] Stary Input nie dziala - klawisze przez WinAPI."); }
                    return false;
                }
            }
            if (mode == 2)
            {
                try
                {
                    string name = NewName(k);
                    if (name == null) return false;
                    object kb = Static(tKeyboard, "current");
                    if (kb == null) return false;
                    object ctrl = Prop(kb, name);
                    object v = Prop(ctrl, down ? "wasPressedThisFrame" : "isPressed");
                    return v is bool && (bool)v;
                }
                catch { return false; }
            }
            return false;
        }

        private static void Resolve()
        {
            if (!legacyTried)
            {
                legacyTried = true;
                Type t = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.GetName().Name.StartsWith("UnityEngine")) continue;
                    t = asm.GetType("UnityEngine.Input", false);
                    if (t != null) break;
                }
                if (t != null)
                {
                    mGetKey = t.GetMethod("GetKey", new[] { typeof(KeyCode) });
                    mGetKeyDown = t.GetMethod("GetKeyDown", new[] { typeof(KeyCode) });
                    if (mGetKey != null && mGetKeyDown != null)
                    {
                        mode = 1;
                        MelonLogger.Msg("[Willy] klawisze: stary Input");
                        return;
                    }
                }
            }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                foreach (var ty in types)
                {
                    if (ty != null && ty.FullName == "UnityEngine.InputSystem.Keyboard") { tKeyboard = ty; break; }
                }
                if (tKeyboard != null) break;
            }
            if (tKeyboard != null) { mode = 2; MelonLogger.Msg("[Willy] klawisze: Input System"); }
            else { mode = 4; MelonLogger.Msg("[Willy] klawisze: WinAPI"); }
        }

        private static string NewName(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.K: return "kKey";
                case KeyCode.F6: return "f6Key";
                case KeyCode.F7: return "f7Key";
                case KeyCode.F8: return "f8Key";
                case KeyCode.F9: return "f9Key";
                case KeyCode.F10: return "f10Key";
                case KeyCode.PageUp: return "pageUpKey";
                case KeyCode.PageDown: return "pageDownKey";
                case KeyCode.Home: return "homeKey";
                case KeyCode.End: return "endKey";
                default: return null;
            }
        }

        private static object Static(Type t, string name)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            return p == null ? null : p.GetValue(null, null);
        }

        private static object Prop(object o, string name)
        {
            if (o == null) return null;
            Type t = o.GetType();
            string key = t.FullName + "." + name;
            PropertyInfo p;
            if (!propCache.TryGetValue(key, out p))
            {
                p = t.GetProperty(name);
                propCache[key] = p;
            }
            return p == null ? null : p.GetValue(o, null);
        }
    }

    // ------------------------------------------------------------------
    //  Jeden pisiorek z jajeczkami: wszystko z kul i walca, zawieszone na sprezynach (jiggle)
    // ------------------------------------------------------------------
    internal class Willy
    {
        public Component Actor;
        public bool Peeing;

        private Transform hips, leftArm, rightArm, head;
        private Rigidbody hipsBody;
        private GameObject root;
        private Transform shaft, ballL, ballR;
        private float unit = 0.4f;
        private Color bodyColor = new Color(0.9f, 0.7f, 0.6f);
        private bool colorFound;
        private int bodyLayer = -1;

        private Vector3 tip, tipV, bl, blV, br, brV;
        private bool init;
        private float lift;

        // ostatni obliczony stan (do sikania)
        public Vector3 TipPos, AimDir, BodyVel;

        private static bool dumped;

        public static Willy Create(Component actor, MelonLogger.Instance log)
        {
            Transform top = actor.transform;
            var w = new Willy();
            w.Actor = actor;
            w.hips = Find(top, "actor_hips_collider", "hips");
            w.leftArm = Find(top, "actor_leftArm_collider", "leftarm");
            w.rightArm = Find(top, "actor_rightArm_collider", "rightarm");
            w.head = FindHead(top);
            if (w.hips == null || w.leftArm == null || w.rightArm == null || w.head == null)
            {
                log.Warning("Postac '" + actor.name + "': brak kosci (biodra=" + (w.hips != null) + ", ramiona=" + (w.leftArm != null && w.rightArm != null) + ", glowa=" + (w.head != null) + ") - pomijam.");
                if (!dumped) { dumped = true; Dump(top, log); }
                return null;
            }
            w.hipsBody = w.hips.GetComponent<Rigidbody>();

            var hc = w.head.GetComponent<Collider>();
            if (hc != null)
            {
                Vector3 s = hc.bounds.size;
                float d = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                if (d > 0.05f && d < 3f) w.unit = d;
            }

            try { w.SampleBody(top, log); }
            catch (Exception e) { log.Warning("Nie udalo sie odczytac koloru postaci: " + e.Message); }

            try { w.Build(); }
            catch (Exception e)
            {
                log.Error("Nie udalo sie zbudowac pisiorka: " + e);
                w.Destroy();
                return null;
            }
            log.Msg("Dodano pisiorka: " + actor.name + " (jednostka " + w.unit.ToString("0.00") + ")");
            return w;
        }

        private void Build()
        {
            root = new GameObject("GBWilly");
            // kolor i material takie jak ciasto postaci - mala "kluseczka" w stylu Gang Beasts
            shaft = Prim("Nub", PrimitiveType.Capsule, 1.0f);
            ballL = Prim("BallL", PrimitiveType.Sphere, 0.94f);
            ballR = Prim("BallR", PrimitiveType.Sphere, 0.94f);
        }

        private Transform Prim(string name, PrimitiveType type, float tone)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            go.transform.SetParent(root.transform, false);
            if (bodyLayer >= 0) go.layer = bodyLayer;
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = true;
                Color c = colorFound ? bodyColor : new Color(0.95f, 0.72f, 0.62f);
                // lekko jasniej niz ciasto, zeby bylo go widac na tle tulowia
                WillyMod.Colorize(r, new Color(Mathf.Min(1f, c.r * tone * 1.08f), Mathf.Min(1f, c.g * tone * 1.08f), Mathf.Min(1f, c.b * tone * 1.08f)));
            }
            return go.transform;
        }

        // Kolor postaci: bierzemy z materialu jej siatki (tulow/biodra), zeby pisiorek pasowal do gracza
        private void SampleBody(Transform top, MelonLogger.Instance log)
        {
            Renderer best = null;
            int bestScore = -1;
            string bestName = "";
            var q = new Queue<Transform>();
            q.Enqueue(top);
            int guard = 0;
            while (q.Count > 0 && guard++ < 600)
            {
                var t = q.Dequeue();
                for (int i = 0; i < t.childCount; i++) q.Enqueue(t.GetChild(i));
                string n = t.name.ToLowerInvariant();
                if (n.StartsWith("gb") || n.Contains("mouth") || n.Contains("brow") || n.Contains("tongue") || n.Contains("teeth") || n.Contains("lip")) continue;
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                int score = 0;
                if (n.Contains("chest") || n.Contains("torso") || n.Contains("body")) score = 5;
                else if (n.Contains("hips") || n.Contains("pelvis")) score = 4;
                else if (n.Contains("arm") || n.Contains("hand") || n.Contains("leg") || n.Contains("foot")) score = 2;
                else if (n.Contains("head")) score = 1;
                else if (n.Contains("skin") || n.Contains("mesh")) score = 1;
                if (score > bestScore) { bestScore = score; best = r; bestName = t.name; }
            }
            if (best == null) { log.Warning("Nie znaleziono siatki postaci - pisiorek dostanie domyslny kolor."); return; }

            try { bodyLayer = best.gameObject.layer; } catch { }
            Material m = best.sharedMaterial;
            if (m == null) return;
            string[] props = { "_Color", "_BaseColor", "_MainColor", "_TintColor", "_Tint", "_SkinColor", "_Albedo" };
            foreach (var pn in props)
            {
                bool has;
                try { has = m.HasProperty(pn); } catch { has = false; }
                if (!has) continue;
                Color c = m.GetColor(pn);
                if (c.r > 0.95f && c.g > 0.95f && c.b > 0.95f) continue;   // biel = kolor z tekstury, nie z parametru
                bodyColor = c;
                colorFound = true;
                log.Msg("Kolor postaci z '" + bestName + "' (" + pn + "): " + c.r.ToString("0.00") + ", " + c.g.ToString("0.00") + ", " + c.b.ToString("0.00"));
                return;
            }
            log.Msg("Kolor postaci z '" + bestName + "': brak prostego parametru koloru - uzywam koloru domyslnego.");
        }

        public bool IsAlive()
        {
            return Actor != null && hips != null && root != null;
        }

        public void Destroy()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
        }

        public void SetVisible(bool v)
        {
            if (root != null && root.activeSelf != v) root.SetActive(v);
        }

        // Wywolywane w LateUpdate - po fizyce i animacji
        public void Step(float dt)
        {
            Vector3 right = rightArm.position - leftArm.position;
            Vector3 upv = head.position - hips.position;
            if (right.sqrMagnitude < 1e-6f || upv.sqrMagnitude < 1e-6f) return;
            right.Normalize();
            upv.Normalize();
            Vector3 fwd = Vector3.Cross(right, upv).normalized;
            if (WillyMod.PFlip.Value) fwd = -fwd;
            // prawdziwe "prawo" po ortogonalizacji
            Vector3 side = Vector3.Cross(upv, fwd).normalized;

            float u = unit * WillyMod.PSize.Value;
            float offF = WillyMod.POffF.Value, offU = WillyMod.POffU.Value;
            Vector3 b = hips.position + fwd * (offF * unit) + upv * (offU * unit);

            lift = Mathf.Lerp(lift, Peeing ? 1f : 0f, Mathf.Clamp01(dt * 8f));
            Vector3 down = -upv;

            Vector3 tipRest = b + fwd * (0.34f * u * (1f + 0.6f * lift)) + down * (0.28f * u * (1f - lift)) + upv * (0.05f * u * lift);
            Vector3 blRest = b + down * (0.14f * u) - side * (0.10f * u) + fwd * (0.04f * u);
            Vector3 brRest = b + down * (0.14f * u) + side * (0.10f * u) + fwd * (0.04f * u);

            if (!init)
            {
                tip = tipRest; bl = blRest; br = brRest;
                tipV = blV = brV = Vector3.zero;
                init = true;
            }

            float sdt = Mathf.Min(dt, 0.033f);
            float jig = Mathf.Max(0.05f, WillyMod.PJiggle.Value);
            Spring(ref tip, ref tipV, tipRest, 160f * jig, 6.5f / Mathf.Sqrt(jig), sdt);
            Spring(ref bl, ref blV, blRest, 130f * jig, 6f / Mathf.Sqrt(jig), sdt);
            Spring(ref br, ref brV, brRest, 130f * jig, 6f / Mathf.Sqrt(jig), sdt);

            // nie pozwol odleciec za daleko od podstawy (np. po teleportacji postaci)
            tip = ClampFrom(b, tip, 0.75f * u);
            bl = ClampFrom(b, bl, 0.45f * u);
            br = ClampFrom(b, br, 0.45f * u);

            Vector3 dv = tip - b;
            float th = 0.15f * u;
            float len = Mathf.Max(th, dv.magnitude + th * 0.5f);
            Vector3 dir = dv.sqrMagnitude > 1e-8f ? dv.normalized : fwd;

            shaft.position = (b + tip) * 0.5f;
            shaft.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            shaft.localScale = new Vector3(th, len * 0.5f, th);

            float bd = 0.19f * u;
            ballL.position = bl;
            ballL.localScale = Vector3.one * bd;
            ballR.position = br;
            ballR.localScale = Vector3.one * bd;

            TipPos = tip;
            AimDir = (fwd + upv * 0.25f + dir * 0.4f).normalized;
            BodyVel = hipsBody != null ? hipsBody.velocity : Vector3.zero;
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
            Vector3 d = p - origin;
            if (d.sqrMagnitude > max * max) return origin + d.normalized * max;
            return p;
        }

        // ---------- Szukanie kosci ----------

        private static Transform Find(Transform start, string exact, string contains)
        {
            Transform cont = null;
            var q = new Queue<Transform>();
            q.Enqueue(start);
            int guard = 0;
            while (q.Count > 0 && guard++ < 600)
            {
                var t = q.Dequeue();
                if (t.name == exact) return t;
                if (cont == null && t.name.ToLowerInvariant().Contains(contains)) cont = t;
                for (int i = 0; i < t.childCount; i++) q.Enqueue(t.GetChild(i));
            }
            return cont;
        }

        private static Transform FindHead(Transform start)
        {
            var t = FindHeadIn(start);
            if (t == null && start.root != start) t = FindHeadIn(start.root);
            return t;
        }

        private static Transform FindHeadIn(Transform start)
        {
            Transform contains = null;
            var queue = new Queue<Transform>();
            queue.Enqueue(start);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 600)
            {
                var t = queue.Dequeue();
                string n = t.name.ToLowerInvariant();
                if (n == "head") return t;
                if (contains == null && n.Contains("head") && !n.Contains("phone") && !n.Contains("set")) contains = t;
                for (int i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
            }
            return contains;
        }

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
            log.Msg(new string(' ', depth * 2) + t.name);
            for (int i = 0; i < t.childCount; i++) DumpRec(t.GetChild(i), depth + 1, log, count);
        }
    }

    internal class Drop
    {
        public GameObject Go;
        public Transform T;
        public Vector3 V;
        public float Die;
        public bool Active;
    }

    internal class Puddle
    {
        public GameObject Go;
        public Transform T;
        public Vector3 Center, Normal;
        public float Radius;
    }

    public class WillyMod : MelonMod
    {
        internal static MelonPreferences_Entry<bool> PEnabled, PFlip;
        internal static MelonPreferences_Entry<float> PSize, POffF, POffU, PJiggle;
        private static MelonPreferences_Entry<float> PSpeed, PSpread, PRate;
        private static MelonPreferences_Entry<int> PMaxPuddles;

        private const int MaxDrops = 200;
        private const float PuddleThickness = 0.012f;

        private readonly Dictionary<int, Willy> willies = new Dictionary<int, Willy>();
        private readonly HashSet<int> bad = new HashSet<int>();
        private readonly List<Drop> drops = new List<Drop>();
        private readonly List<Puddle> puddles = new List<Puddle>();
        private int puddleRing;
        private float emitAcc;

        private Type actorType;
        private bool actorTypeSearched, loggedNoActor;
        private float scanTimer;
        private bool errLogged, lateErrLogged, scanErrLogged;

        private static Shader cachedShader;
        private static bool shaderSearched;
        private static MethodInfo findObjectsMethod;
        private static bool findObjectsResolved, findObjectsNeedsBool;

        public override void OnApplicationStart()
        {
            var cat = MelonPreferences.CreateCategory("GBWilly2", "Gang Beasts Willy");
            PEnabled = cat.CreateEntry("Enabled", true);
            PFlip = cat.CreateEntry("FlipForward", false);
            PSize = cat.CreateEntry("Size", 1.2f);
            POffF = cat.CreateEntry("ForwardOffset", 0.62f);
            POffU = cat.CreateEntry("UpOffset", -0.10f);
            PJiggle = cat.CreateEntry("JiggleAmount", 1.0f);
            PSpeed = cat.CreateEntry("PeeSpeed", 5f);
            PSpread = cat.CreateEntry("PeeSpread", 0.12f);
            PRate = cat.CreateEntry("PeeRate", 45f);
            PMaxPuddles = cat.CreateEntry("MaxPuddles", 300);
            LoggerInstance.Msg("Gang Beasts Willy 1.1 zaladowany. K (przytrzymaj) = sikanie. F6 wl/wyl, F7/F8 rozmiar, PgUp/PgDn wysokosc, Home/End przod/tyl, F9 sprzatanie kaluz, F10 odwroc przod.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            foreach (var w in willies.Values) { try { w.Destroy(); } catch { } }
            willies.Clear();
            bad.Clear();
            // obiekty sceny znikaja razem ze scena - czyscimy tylko listy
            drops.Clear();
            puddles.Clear();
            puddleRing = 0;
            scanTimer = 0f;
        }

        // ---------- Glowna petla ----------

        public override void OnUpdate()
        {
            try
            {
                HandleKeys();
                if (!PEnabled.Value) return;

                scanTimer -= Time.deltaTime;
                if (scanTimer <= 0f)
                {
                    scanTimer = actorType != null ? 1f : 3f;
                    Scan();
                }

                bool pee = Keys.Held(KeyCode.K);
                float dt = Time.deltaTime;

                List<int> dead = null;
                foreach (var kv in willies)
                {
                    var w = kv.Value;
                    if (!w.IsAlive())
                    {
                        if (dead == null) dead = new List<int>();
                        dead.Add(kv.Key);
                        continue;
                    }
                    w.Peeing = pee;
                }
                if (dead != null)
                    foreach (var k in dead)
                    {
                        try { willies[k].Destroy(); } catch { }
                        willies.Remove(k);
                    }

                if (pee)
                {
                    emitAcc += PRate.Value * dt;
                    int n = (int)emitAcc;
                    emitAcc -= n;
                    if (n > 4) n = 4;
                    foreach (var w in willies.Values)
                        for (int i = 0; i < n; i++) Emit(w);
                }
                else emitAcc = 0f;

                TickDrops(dt);
            }
            catch (Exception e)
            {
                if (!errLogged)
                {
                    errLogged = true;
                    LoggerInstance.Error("Blad w OnUpdate (kolejne takie same pomijam): " + e);
                }
            }
        }

        public override void OnLateUpdate()
        {
            try
            {
                float dt = Time.deltaTime;
                bool on = PEnabled.Value;
                foreach (var w in willies.Values)
                {
                    if (!w.IsAlive()) continue;
                    w.SetVisible(on);
                    if (on) w.Step(dt);
                }
            }
            catch (Exception e)
            {
                if (!lateErrLogged)
                {
                    lateErrLogged = true;
                    LoggerInstance.Error("Blad w OnLateUpdate (kolejne takie same pomijam): " + e);
                }
            }
        }

        // ---------- Skanowanie postaci ----------

        private void Scan()
        {
            try
            {
                if (!actorTypeSearched)
                {
                    actorTypeSearched = true;
                    actorType = FindActorType();
                    if (actorType != null)
                        LoggerInstance.Msg("Znaleziono typ postaci: " + actorType.FullName + " (" + actorType.Assembly.GetName().Name + ")");
                }
                if (actorType == null)
                {
                    if (!loggedNoActor)
                    {
                        loggedNoActor = true;
                        LoggerInstance.Warning("Nie znaleziono klasy 'Actor' - mod nie wie, gdzie sa postacie.");
                    }
                    return;
                }

                foreach (var o in FindAll(actorType))
                {
                    var comp = o as Component;
                    if (comp == null) continue;
                    int id = comp.GetInstanceID();
                    if (willies.ContainsKey(id) || bad.Contains(id)) continue;
                    var w = Willy.Create(comp, LoggerInstance);
                    if (w != null) willies[id] = w;
                    else bad.Add(id);
                }
            }
            catch (Exception e)
            {
                if (!scanErrLogged)
                {
                    scanErrLogged = true;
                    LoggerInstance.Error("Blad skanowania postaci (kolejne takie same pomijam): " + e);
                }
            }
        }

        // ---------- Sikanie ----------

        private Drop GetDrop()
        {
            for (int i = 0; i < drops.Count; i++)
            {
                var d = drops[i];
                if (d.Go == null) { drops.RemoveAt(i); i--; continue; }
                if (!d.Active) return d;
            }
            if (drops.Count >= MaxDrops) return null;
            var go = Prim("GBPeeDrop", PrimitiveType.Sphere, new Color(1f, 0.92f, 0.3f));
            go.transform.localScale = Vector3.one * 0.045f;
            var nd = new Drop { Go = go, T = go.transform, Active = false };
            go.SetActive(false);
            drops.Add(nd);
            return nd;
        }

        private void Emit(Willy w)
        {
            var d = GetDrop();
            if (d == null) return;
            Vector3 dir = (w.AimDir + UnityEngine.Random.insideUnitSphere * PSpread.Value).normalized;
            d.V = dir * PSpeed.Value + w.BodyVel * 0.5f;
            d.T.position = w.TipPos;
            d.Die = Time.time + 3f;
            d.Active = true;
            d.Go.SetActive(true);
        }

        private void TickDrops(float dt)
        {
            if (dt <= 0f) return;
            Vector3 g = Physics.gravity;
            for (int i = 0; i < drops.Count; i++)
            {
                var d = drops[i];
                if (!d.Active) continue;
                if (d.Go == null) { d.Active = false; continue; }
                if (Time.time > d.Die) { Kill(d); continue; }

                d.V += g * dt;
                Vector3 step = d.V * dt;
                float sl = step.magnitude;
                Vector3 pos = d.T.position;
                RaycastHit h;
                if (sl > 0.0001f && Physics.Raycast(pos, step / sl, out h, sl + 0.01f, ~0, QueryTriggerInteraction.Ignore))
                {
                    Rigidbody rb = h.rigidbody;
                    bool dynamicBody = rb != null && !rb.isKinematic;
                    if (!dynamicBody && h.normal.y > 0.5f) AddPuddle(h.point, h.normal);
                    Kill(d);
                    continue;
                }
                d.T.position = pos + step;
            }
        }

        private static void Kill(Drop d)
        {
            d.Active = false;
            if (d.Go != null) d.Go.SetActive(false);
        }

        private void AddPuddle(Vector3 point, Vector3 normal)
        {
            // dolacz do pobliskiej kaluzy i ja powieksz
            Puddle best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < puddles.Count; i++)
            {
                var p = puddles[i];
                if (p.Go == null) continue;
                if (Vector3.Dot(p.Normal, normal) < 0.8f) continue;
                float d = Vector3.Distance(p.Center, point);
                if (d < p.Radius * 0.9f + 0.1f && d < bestD) { bestD = d; best = p; }
            }
            if (best != null)
            {
                if (best.Radius < 0.9f)
                {
                    best.Radius = Mathf.Min(0.9f, best.Radius + 0.012f);
                    best.T.localScale = new Vector3(best.Radius * 2f, PuddleThickness, best.Radius * 2f);
                }
                return;
            }

            Puddle np;
            int max = Mathf.Max(10, PMaxPuddles.Value);
            if (puddles.Count < max)
            {
                var go = Prim("GBPuddle", PrimitiveType.Sphere, new Color(1f, 0.88f, 0.15f));
                np = new Puddle { Go = go, T = go.transform };
                puddles.Add(np);
            }
            else
            {
                // limit - przerabiamy najstarsza kaluze
                puddleRing = (puddleRing + 1) % puddles.Count;
                np = puddles[puddleRing];
                if (np.Go == null)
                {
                    var go = Prim("GBPuddle", PrimitiveType.Sphere, new Color(1f, 0.88f, 0.15f));
                    np.Go = go;
                    np.T = go.transform;
                }
            }
            np.Center = point;
            np.Normal = normal;
            np.Radius = 0.11f;
            np.T.position = point + normal * (PuddleThickness * 0.6f);
            np.T.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            np.T.localScale = new Vector3(np.Radius * 2f, PuddleThickness, np.Radius * 2f);
        }

        private void ClearPuddles()
        {
            foreach (var p in puddles)
                if (p.Go != null) { try { UnityEngine.Object.Destroy(p.Go); } catch { } }
            puddles.Clear();
            puddleRing = 0;
        }

        // ---------- Klawisze ----------

        private void HandleKeys()
        {
            bool changed = false;
            if (Keys.Down(KeyCode.F6)) { PEnabled.Value = !PEnabled.Value; LoggerInstance.Msg("Willy: " + (PEnabled.Value ? "WLACZONY" : "wylaczony")); changed = true; }
            if (Keys.Down(KeyCode.F7)) { PSize.Value = Mathf.Max(0.3f, PSize.Value * 0.9f); changed = true; }
            if (Keys.Down(KeyCode.F8)) { PSize.Value = Mathf.Min(4f, PSize.Value * 1.1f); changed = true; }
            if (Keys.Down(KeyCode.PageUp)) { POffU.Value += 0.02f; changed = true; }
            if (Keys.Down(KeyCode.PageDown)) { POffU.Value -= 0.02f; changed = true; }
            if (Keys.Down(KeyCode.Home)) { POffF.Value += 0.02f; changed = true; }
            if (Keys.Down(KeyCode.End)) { POffF.Value = Mathf.Max(0f, POffF.Value - 0.02f); changed = true; }
            if (Keys.Down(KeyCode.F9)) { ClearPuddles(); LoggerInstance.Msg("Kaluze posprzatane."); }
            if (Keys.Down(KeyCode.F10)) { PFlip.Value = !PFlip.Value; LoggerInstance.Msg("Kierunek przodu odwrocony: " + PFlip.Value); changed = true; }
            if (changed)
            {
                LoggerInstance.Msg("Pisiorek: rozmiar x" + PSize.Value.ToString("0.00") + ", przod=" + POffF.Value.ToString("0.00") + ", wysokosc=" + POffU.Value.ToString("0.00"));
                try { MelonPreferences.Save(); } catch { }
            }
        }

        // ---------- Pomocnicze ----------

        private static GameObject Prim(string name, PrimitiveType type, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                Colorize(r, c);
            }
            return go;
        }

        internal static void Colorize(Renderer r, Color c)
        {
            // 1) domyslny material prymitywu - zawsze jest w grze i sie renderuje
            try
            {
                Material dm = r.material;
                if (dm != null && dm.shader != null && dm.shader.isSupported)
                {
                    dm.color = c;
                    return;
                }
            }
            catch { }
            // 2) wlasny shader, ale tylko taki, ktory gra faktycznie obsluguje
            if (!shaderSearched)
            {
                shaderSearched = true;
                string[] names = { "Unlit/Color", "Sprites/Default", "Standard", "Legacy Shaders/Diffuse", "Mobile/Diffuse" };
                foreach (var n in names)
                {
                    var sh = Shader.Find(n);
                    if (sh != null && sh.isSupported) { cachedShader = sh; break; }
                }
                MelonLogger.Msg("[Willy] shader zapasowy: " + (cachedShader != null ? cachedShader.name : "(brak)"));
            }
            if (cachedShader != null)
            {
                var m = new Material(cachedShader);
                m.color = c;
                r.sharedMaterial = m;
            }
        }

        private static Shader litShader;
        private static bool litSearched;

        // Material z cieniowaniem (zeby pisiorek byl "okragly" jak reszta gry); w razie braku - plaski kolor
        internal static void ColorizeLit(Renderer r, Color c)
        {
            if (!litSearched)
            {
                litSearched = true;
                string[] names = { "Legacy Shaders/Diffuse", "Mobile/Diffuse", "Standard", "Unlit/Color", "Sprites/Default" };
                foreach (var n in names)
                {
                    var sh = Shader.Find(n);
                    if (sh != null) { litShader = sh; break; }
                }
                MelonLogger.Msg("[Willy] shader pisiorka: " + (litShader != null ? litShader.name : "(brak - domyslny material)"));
            }
            Material m;
            if (litShader != null)
            {
                m = new Material(litShader);
                m.color = c;
                r.sharedMaterial = m;
            }
            else
            {
                m = r.material;
                m.color = c;
            }
        }

        private static List<UnityEngine.Object> FindAll(Type t)
        {
            var result = new List<UnityEngine.Object>();
            if (!findObjectsResolved)
            {
                findObjectsResolved = true;
                MethodInfo noArgs = null, boolArg = null;
                foreach (var m in typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "FindObjectsOfType" || !m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 0) noArgs = m;
                    else if (ps.Length == 1 && ps[0].ParameterType == typeof(bool)) boolArg = m;
                }
                findObjectsMethod = noArgs ?? boolArg;
                findObjectsNeedsBool = noArgs == null && boolArg != null;
            }
            if (findObjectsMethod == null) return result;
            var gm = findObjectsMethod.MakeGenericMethod(t);
            object res = gm.Invoke(null, findObjectsNeedsBool ? new object[] { false } : null);
            var en = res as IEnumerable;
            if (en == null) return result;
            foreach (var o in en)
            {
                var u = o as UnityEngine.Object;
                if (u != null) result.Add(u);
            }
            return result;
        }

        private static Type FindActorType()
        {
            Type best = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = asm.GetName().Name;
                if (n.StartsWith("UnityEngine") || n.StartsWith("System") || n.StartsWith("mscorlib")
                    || n.StartsWith("MelonLoader") || n.StartsWith("Mono.") || n.StartsWith("0Harmony")
                    || n.StartsWith("Il2CppInterop") || n.StartsWith("Il2Cppmscorlib") || n.StartsWith("Il2CppSystem"))
                    continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t == null || t.Name != "Actor") continue;
                    bool isComponent;
                    try { isComponent = typeof(Component).IsAssignableFrom(t); } catch { continue; }
                    if (!isComponent) continue;
                    if (n.Contains("Assembly-CSharp")) return t;
                    if (best == null) best = t;
                }
            }
            return best;
        }
    }
}
