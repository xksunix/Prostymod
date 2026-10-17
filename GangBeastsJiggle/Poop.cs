using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace GangBeastsJiggle
{
    // ------------------------------------------------------------------
    //  Klawisze (przez refleksje: stary Input albo Input System - dziala w IL2CPP)
    // ------------------------------------------------------------------
    internal static class PoopKeys
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
                    if (fails >= 5) { fails = 0; mode = 0; Resolve(); }
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
                        MelonLogger.Msg("[Poop] klawisze: stary Input");
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
            if (tKeyboard != null) { mode = 2; MelonLogger.Msg("[Poop] klawisze: Input System"); }
            else { mode = 3; MelonLogger.Warning("[Poop] Nie znaleziono systemu wejscia - klawisze nie beda dzialac."); }
        }

        private static string NewName(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.G: return "gKey";
                case KeyCode.H: return "hKey";
                case KeyCode.J: return "jKey";
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
    //  Tylek jednej postaci: skad i w ktora strone leci sraczka
    // ------------------------------------------------------------------
    internal class Butt
    {
        public Component Actor;
        public Vector3 Pos, Back, Up, Vel;
        public float Unit = 0.4f;

        private Transform hips, leftArm, rightArm, head;
        private Rigidbody hipsBody;
        private static bool dumped;

        public static Butt Create(Component actor, MelonLogger.Instance log)
        {
            Transform top = actor.transform;
            var b = new Butt();
            b.Actor = actor;
            b.hips = Find(top, "actor_hips_collider", "hips");
            b.leftArm = Find(top, "actor_leftArm_collider", "leftarm");
            b.rightArm = Find(top, "actor_rightArm_collider", "rightarm");
            b.head = FindHead(top);
            if (b.hips == null || b.leftArm == null || b.rightArm == null || b.head == null)
            {
                log.Warning("[Poop] Postac '" + actor.name + "': brak kosci (biodra=" + (b.hips != null) + ", ramiona=" + (b.leftArm != null && b.rightArm != null) + ", glowa=" + (b.head != null) + ") - pomijam.");
                if (!dumped) { dumped = true; Dump(top, log); }
                return null;
            }
            b.hipsBody = b.hips.GetComponent<Rigidbody>();
            var hc = b.head.GetComponent<Collider>();
            if (hc != null)
            {
                Vector3 s = hc.bounds.size;
                float d = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                if (d > 0.05f && d < 3f) b.Unit = d;
            }
            log.Msg("[Poop] Dodano postac: " + actor.name + " (jednostka " + b.Unit.ToString("0.00") + ")");
            return b;
        }

        public bool IsAlive()
        {
            return Actor != null && hips != null && leftArm != null && rightArm != null && head != null;
        }

        // Przod = prawo x gora (tak samo jak w modzie z twarzami); tylek jest po przeciwnej stronie
        public bool Refresh(bool flip, float backOffset, float upOffset)
        {
            Vector3 right = rightArm.position - leftArm.position;
            Vector3 upv = head.position - hips.position;
            if (right.sqrMagnitude < 1e-6f || upv.sqrMagnitude < 1e-6f) return false;
            right.Normalize();
            upv.Normalize();
            Vector3 fwd = Vector3.Cross(right, upv).normalized;
            if (flip) fwd = -fwd;
            Back = -fwd;
            Up = upv;
            Pos = hips.position + Back * (backOffset * Unit) + Up * (upOffset * Unit);
            Vel = hipsBody != null ? hipsBody.velocity : Vector3.zero;
            return true;
        }

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

    internal class PoopDrop
    {
        public GameObject Go;
        public Transform T;
        public Vector3 V;
        public float Die;
        public float Grow;
        public bool Active;
    }

    internal class Splat
    {
        public GameObject Go;
        public Transform T;
        public Vector3 Center, Normal;
        public float Radius;
    }

    // ------------------------------------------------------------------
    //  G (przytrzymaj) = kupa + DUZO sraczki. H = odwroc kierunek, J = sprzatanie.
    // ------------------------------------------------------------------
    internal static class PoopFeature
    {
        private static MelonPreferences_Entry<bool> PEnabled, PFlip;
        private static MelonPreferences_Entry<float> PBackOff, PUpOff, PSpeed, PSpread, PRate, PSize;
        private static MelonPreferences_Entry<int> PMaxSplats;

        private const int MaxDrops = 700;
        private const float SplatThickness = 0.014f;

        private static MelonLogger.Instance log;
        private static readonly Dictionary<int, Butt> butts = new Dictionary<int, Butt>();
        private static readonly HashSet<int> bad = new HashSet<int>();
        private static readonly List<PoopDrop> drops = new List<PoopDrop>();
        private static readonly List<Splat> splats = new List<Splat>();
        private static readonly List<GameObject> piles = new List<GameObject>();
        private static int splatRing;
        private static float emitAcc;
        private static float scanTimer;

        private static Type actorType;
        private static bool actorTypeSearched, loggedNoActor;
        private static bool errLogged, scanErrLogged;

        private static Shader litShader;
        private static bool litSearched;
        private static MethodInfo findObjectsMethod;
        private static bool findObjectsResolved, findObjectsNeedsBool;

        private static readonly Color[] browns =
        {
            new Color(0.36f, 0.21f, 0.08f), new Color(0.45f, 0.27f, 0.1f),
            new Color(0.28f, 0.16f, 0.06f), new Color(0.5f, 0.32f, 0.12f)
        };

        public static void Init(MelonLogger.Instance logger)
        {
            log = logger;
            var cat = MelonPreferences.CreateCategory("GBPoop", "Gang Beasts Poop");
            PEnabled = cat.CreateEntry("Enabled", true);
            PFlip = cat.CreateEntry("FlipDirection", false);
            PBackOff = cat.CreateEntry("BackOffset", 0.55f);
            PUpOff = cat.CreateEntry("UpOffset", -0.1f);
            PSpeed = cat.CreateEntry("Speed", 6f);
            PSpread = cat.CreateEntry("Spread", 0.35f);
            PRate = cat.CreateEntry("Rate", 140f);
            PSize = cat.CreateEntry("Size", 1f);
            PMaxSplats = cat.CreateEntry("MaxSplats", 350);
            log.Msg("Poop dodany do Jiggle: G (przytrzymaj) = kupa i sraczka, H = odwroc kierunek, J = posprzataj.");
        }

        public static void SceneLoaded()
        {
            butts.Clear();
            bad.Clear();
            drops.Clear();
            splats.Clear();
            piles.Clear();
            splatRing = 0;
            scanTimer = 0f;
        }

        public static void Tick()
        {
            if (PEnabled == null || !PEnabled.Value) return;
            try
            {
                float dt = Time.deltaTime;

                if (PoopKeys.Down(KeyCode.H))
                {
                    PFlip.Value = !PFlip.Value;
                    log.Msg("Poop: kierunek odwrocony = " + PFlip.Value);
                    try { MelonPreferences.Save(); } catch { }
                }
                if (PoopKeys.Down(KeyCode.J)) { ClearMess(); log.Msg("Poop: posprzatane."); }

                scanTimer -= dt;
                if (scanTimer <= 0f)
                {
                    scanTimer = actorType != null ? 1f : 3f;
                    Scan();
                }

                List<int> dead = null;
                foreach (var kv in butts)
                {
                    var b = kv.Value;
                    if (!b.IsAlive() || !b.Refresh(PFlip.Value, PBackOff.Value, PUpOff.Value))
                    {
                        if (!b.IsAlive())
                        {
                            if (dead == null) dead = new List<int>();
                            dead.Add(kv.Key);
                        }
                    }
                }
                if (dead != null) foreach (var k in dead) butts.Remove(k);

                bool down = PoopKeys.Down(KeyCode.G);
                bool held = PoopKeys.Held(KeyCode.G);

                if (down)
                    foreach (var b in butts.Values) DropPile(b);

                if (held)
                {
                    emitAcc += PRate.Value * dt;
                    int n = (int)emitAcc;
                    emitAcc -= n;
                    if (n > 8) n = 8;
                    foreach (var b in butts.Values)
                        for (int i = 0; i < n; i++) Emit(b);
                }
                else emitAcc = 0f;

                TickDrops(dt);
            }
            catch (Exception e)
            {
                if (!errLogged)
                {
                    errLogged = true;
                    log.Error("Blad w Poop (kolejne takie same pomijam): " + e);
                }
            }
        }

        // ---------- Skanowanie postaci ----------

        private static void Scan()
        {
            try
            {
                if (!actorTypeSearched)
                {
                    actorTypeSearched = true;
                    actorType = FindActorType();
                    if (actorType != null)
                        log.Msg("[Poop] typ postaci: " + actorType.FullName + " (" + actorType.Assembly.GetName().Name + ")");
                }
                if (actorType == null)
                {
                    if (!loggedNoActor)
                    {
                        loggedNoActor = true;
                        log.Warning("[Poop] Nie znaleziono klasy 'Actor' - nie wiem, gdzie sa postacie.");
                    }
                    return;
                }
                foreach (var o in FindAll(actorType))
                {
                    var comp = o as Component;
                    if (comp == null) continue;
                    int id = comp.GetInstanceID();
                    if (butts.ContainsKey(id) || bad.Contains(id)) continue;
                    var b = Butt.Create(comp, log);
                    if (b != null) butts[id] = b;
                    else bad.Add(id);
                }
            }
            catch (Exception e)
            {
                if (!scanErrLogged)
                {
                    scanErrLogged = true;
                    log.Error("[Poop] Blad skanowania postaci (kolejne takie same pomijam): " + e);
                }
            }
        }

        // ---------- Kupa (kopczyk) ----------

        private static void DropPile(Butt b)
        {
            try
            {
                RaycastHit h;
                Vector3 from = b.Pos + b.Back * (0.1f * b.Unit);
                if (!Physics.Raycast(from, Vector3.down, out h, 8f, ~0, QueryTriggerInteraction.Ignore)) return;
                if (h.normal.y < 0.5f) return;

                float u = b.Unit * PSize.Value;
                Color c = browns[1];
                Vector3 p = h.point;
                // klasyczny "zakrecony" kopczyk z trzech kul
                MakePileBall(p + Vector3.up * (0.07f * u), 0.34f * u, 0.22f * u, c);
                MakePileBall(p + Vector3.up * (0.17f * u), 0.25f * u, 0.18f * u, browns[0]);
                MakePileBall(p + Vector3.up * (0.25f * u), 0.15f * u, 0.14f * u, browns[3]);

                while (piles.Count > 90)
                {
                    if (piles[0] != null) UnityEngine.Object.Destroy(piles[0]);
                    piles.RemoveAt(0);
                }
            }
            catch { }
        }

        private static void MakePileBall(Vector3 pos, float width, float height, Color c)
        {
            var go = Prim("GBPoopPile", PrimitiveType.Sphere, c);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(width, height, width);
            piles.Add(go);
        }

        // ---------- Sraczka ----------

        private static PoopDrop GetDrop()
        {
            for (int i = 0; i < drops.Count; i++)
            {
                var d = drops[i];
                if (d.Go == null) { drops.RemoveAt(i); i--; continue; }
                if (!d.Active) return d;
            }
            if (drops.Count >= MaxDrops) return null;
            var go = Prim("GBPoopDrop", PrimitiveType.Sphere, browns[UnityEngine.Random.Range(0, browns.Length)]);
            var nd = new PoopDrop { Go = go, T = go.transform, Active = false };
            go.SetActive(false);
            drops.Add(nd);
            return nd;
        }

        private static void Emit(Butt b)
        {
            var d = GetDrop();
            if (d == null) return;
            bool chunk = UnityEngine.Random.value < 0.12f;
            // do tylu i lekko w dol, z duzym rozrzutem - "fontanna"
            Vector3 dir = (b.Back + b.Up * 0.05f - Vector3.up * 0.15f + UnityEngine.Random.insideUnitSphere * PSpread.Value).normalized;
            float spd = PSpeed.Value * UnityEngine.Random.Range(0.6f, 1.25f);
            d.V = dir * spd + b.Vel * 0.5f;
            float size = (chunk ? UnityEngine.Random.Range(0.12f, 0.2f) : UnityEngine.Random.Range(0.05f, 0.11f)) * PSize.Value;
            d.T.localScale = Vector3.one * size;
            d.T.position = b.Pos;
            d.Grow = chunk ? 0.035f : 0.016f;
            d.Die = Time.time + 3f;
            d.Active = true;
            d.Go.SetActive(true);
        }

        private static void TickDrops(float dt)
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
                    if (!dynamicBody && h.normal.y > 0.5f) AddSplat(h.point, h.normal, d.Grow);
                    Kill(d);
                    continue;
                }
                d.T.position = pos + step;
            }
        }

        private static void Kill(PoopDrop d)
        {
            d.Active = false;
            if (d.Go != null) d.Go.SetActive(false);
        }

        private static void AddSplat(Vector3 point, Vector3 normal, float grow)
        {
            Splat best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < splats.Count; i++)
            {
                var p = splats[i];
                if (p.Go == null) continue;
                if (Vector3.Dot(p.Normal, normal) < 0.8f) continue;
                float d = Vector3.Distance(p.Center, point);
                if (d < p.Radius * 0.9f + 0.1f && d < bestD) { bestD = d; best = p; }
            }
            if (best != null)
            {
                if (best.Radius < 1.3f)
                {
                    best.Radius = Mathf.Min(1.3f, best.Radius + grow);
                    best.T.localScale = new Vector3(best.Radius * 2f, SplatThickness, best.Radius * 2f);
                }
                return;
            }

            Splat np;
            int max = Mathf.Max(10, PMaxSplats.Value);
            if (splats.Count < max)
            {
                var go = Prim("GBPoopSplat", PrimitiveType.Sphere, browns[UnityEngine.Random.Range(0, browns.Length)]);
                np = new Splat { Go = go, T = go.transform };
                splats.Add(np);
            }
            else
            {
                splatRing = (splatRing + 1) % splats.Count;
                np = splats[splatRing];
                if (np.Go == null)
                {
                    var go = Prim("GBPoopSplat", PrimitiveType.Sphere, browns[UnityEngine.Random.Range(0, browns.Length)]);
                    np.Go = go;
                    np.T = go.transform;
                }
            }
            np.Center = point;
            np.Normal = normal;
            np.Radius = 0.12f;
            np.T.position = point + normal * (SplatThickness * 0.6f);
            np.T.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            np.T.localScale = new Vector3(np.Radius * 2f, SplatThickness, np.Radius * 2f);
        }

        private static void ClearMess()
        {
            foreach (var s in splats)
                if (s.Go != null) { try { UnityEngine.Object.Destroy(s.Go); } catch { } }
            splats.Clear();
            splatRing = 0;
            foreach (var p in piles)
                if (p != null) { try { UnityEngine.Object.Destroy(p); } catch { } }
            piles.Clear();
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

        private static void Colorize(Renderer r, Color c)
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
                MelonLogger.Msg("[Poop] shader: " + (litShader != null ? litShader.name : "(brak - domyslny material)"));
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

        // FindObjectsOfType<T>() przez refleksje - w IL2CPP zwraca tablice Il2Cpp zamiast T[]
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
