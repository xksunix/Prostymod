using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

[assembly: MelonInfo(typeof(GBGrab.GrabMod), "Gang Beasts Grab", "1.0.0", "Claude")]

namespace GBGrab
{
    // ------------------------------------------------------------------
    //  Odczyt myszy. Gang Beasts jest w IL2CPP, a gra moze uzywac starego Input
    //  albo nowego Input System - wiec probujemy po kolei (przez refleksje).
    // ------------------------------------------------------------------
    internal static class Inp
    {
        private static int mode;               // 0 = nie sprawdzono, 1 = stary Input, 2 = Input System, 3 = brak
        private static bool legacyTried;
        private static int fails;
        private static MethodInfo mGetBtn, mGetKeyDown;
        private static PropertyInfo pMousePos, pScroll;
        private static Type tMouse, tKeyboard;
        private static readonly Dictionary<string, PropertyInfo> propCache = new Dictionary<string, PropertyInfo>();
        private static readonly Dictionary<Type, MethodInfo> readCache = new Dictionary<Type, MethodInfo>();

        public static bool Left, Right, F2, F3, F4;
        public static Vector2 Pos;
        public static float Scroll;

        public static string ModeName
        {
            get { return mode == 1 ? "stary Input" : mode == 2 ? "Input System" : mode == 3 ? "BRAK (mysz niedostepna)" : "?"; }
        }

        public static void Poll()
        {
            Left = Right = F2 = F3 = F4 = false;
            Scroll = 0f;
            if (mode == 0) Resolve();

            if (mode == 1)
            {
                try { LegacyPoll(); fails = 0; return; }
                catch
                {
                    fails++;
                    if (fails < 5) return;
                    fails = 0;
                    mode = 0;       // stary Input nie dziala - sprobuj nowego
                    Resolve();
                }
            }
            if (mode == 2)
            {
                try { NewPoll(); fails = 0; }
                catch
                {
                    fails++;
                    if (fails >= 60) { mode = 3; MelonLogger.Warning("[Grab] Nie moge odczytac myszy ani starym Input, ani Input System."); }
                }
            }
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
                    mGetBtn = t.GetMethod("GetMouseButton", new[] { typeof(int) });
                    mGetKeyDown = t.GetMethod("GetKeyDown", new[] { typeof(KeyCode) });
                    pMousePos = t.GetProperty("mousePosition");
                    pScroll = t.GetProperty("mouseScrollDelta");
                    if (mGetBtn != null && pMousePos != null)
                    {
                        mode = 1;
                        MelonLogger.Msg("[Grab] mysz: stary Input");
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
                    if (ty == null) continue;
                    string fn = ty.FullName;
                    if (fn == "UnityEngine.InputSystem.Mouse") tMouse = ty;
                    else if (fn == "UnityEngine.InputSystem.Keyboard") tKeyboard = ty;
                }
                if (tMouse != null && tKeyboard != null) break;
            }
            if (tMouse != null)
            {
                mode = 2;
                MelonLogger.Msg("[Grab] mysz: Input System");
            }
            else
            {
                mode = 3;
                MelonLogger.Warning("[Grab] Nie znaleziono zadnego systemu wejscia dla myszy.");
            }
        }

        private static bool KeyDown(KeyCode k)
        {
            if (mGetKeyDown == null) return false;
            return (bool)mGetKeyDown.Invoke(null, new object[] { k });
        }

        private static void LegacyPoll()
        {
            Left = (bool)mGetBtn.Invoke(null, new object[] { 0 });
            Right = (bool)mGetBtn.Invoke(null, new object[] { 1 });
            Vector3 mp = (Vector3)pMousePos.GetValue(null, null);
            Pos = new Vector2(mp.x, mp.y);
            if (pScroll != null)
            {
                Vector2 sd = (Vector2)pScroll.GetValue(null, null);
                Scroll = sd.y;
            }
            F2 = KeyDown(KeyCode.F2);
            F3 = KeyDown(KeyCode.F3);
            F4 = KeyDown(KeyCode.F4);
        }

        // ----- Input System (przez refleksje) -----

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

        private static object Static(Type t, string name)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            return p == null ? null : p.GetValue(null, null);
        }

        private static object Read(object control)
        {
            if (control == null) return null;
            Type t = control.GetType();
            MethodInfo m;
            if (!readCache.TryGetValue(t, out m))
            {
                m = t.GetMethod("ReadValue", Type.EmptyTypes);
                readCache[t] = m;
            }
            return m == null ? null : m.Invoke(control, null);
        }

        private static bool Flag(object control, string name)
        {
            object v = Prop(control, name);
            return v is bool && (bool)v;
        }

        private static void NewPoll()
        {
            object ms = Static(tMouse, "current");
            if (ms != null)
            {
                Left = Flag(Prop(ms, "leftButton"), "isPressed");
                Right = Flag(Prop(ms, "rightButton"), "isPressed");
                object pv = Read(Prop(ms, "position"));
                if (pv is Vector2) Pos = (Vector2)pv;
                object sv = Read(Prop(ms, "scroll"));
                if (sv is Vector2) Scroll = ((Vector2)sv).y;
            }
            if (tKeyboard != null)
            {
                object kb = Static(tKeyboard, "current");
                if (kb != null)
                {
                    F2 = Flag(Prop(kb, "f2Key"), "wasPressedThisFrame");
                    F3 = Flag(Prop(kb, "f3Key"), "wasPressedThisFrame");
                    F4 = Flag(Prop(kb, "f4Key"), "wasPressedThisFrame");
                }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Efekty wizualne (kule, odlamki, blysk swiatla) - bez kolizji
    // ------------------------------------------------------------------
    internal class FxItem
    {
        public GameObject Go;
        public Transform T;
        public Light Lamp;
        public float Born, Life, S0, S1, Lamp0;
    }

    internal class Debris
    {
        public Transform T;
        public Vector3 V;
        public float Die;
    }

    public class GrabMod : MelonMod
    {
        private static MelonPreferences_Entry<bool> PEnabled, PForceCursor, PTear;
        private static MelonPreferences_Entry<float> PGain, PMaxSpeed, PThrowBoost, PMaxThrow, PHold;
        private static MelonPreferences_Entry<float> PPunchSpeed, PPunchRadius;
        private static MelonPreferences_Entry<float> PBoomRadius, PBoomSpeed, PBoomUp, PVolume;

        private Camera cam;
        private float camTimer;

        // chwytanie
        private Rigidbody grabbed;
        private Vector3 localPoint;
        private Vector3 pressWorldPoint;
        private float dist;
        private float pressTime;
        private Vector2 pressPos;
        private bool dragging;
        private Vector3 target, prevTarget, tVel;
        private bool hasPrevTarget;
        private bool leftPrev, rightPrev;

        // efekty
        private readonly List<FxItem> fx = new List<FxItem>();
        private readonly List<Debris> debris = new List<Debris>();
        private static Shader cachedShader;
        private static bool shaderSearched;
        private static AudioClip boomClip;
        private static bool boomTried;

        private static MethodInfo findObjectsMethod;
        private static bool findObjectsResolved;
        private static bool findObjectsNeedsBool;

        private bool errLogged, fixedErrLogged;

        public override void OnApplicationStart()
        {
            var cat = MelonPreferences.CreateCategory("GBGrab", "Gang Beasts Grab");
            PEnabled = cat.CreateEntry("Enabled", true);
            PForceCursor = cat.CreateEntry("ForceCursor", true);
            PTear = cat.CreateEntry("TearApart", false);
            PGain = cat.CreateEntry("GrabStrength", 14f);
            PMaxSpeed = cat.CreateEntry("GrabMaxSpeed", 35f);
            PThrowBoost = cat.CreateEntry("ThrowBoost", 1.4f);
            PMaxThrow = cat.CreateEntry("MaxThrowSpeed", 45f);
            PHold = cat.CreateEntry("HoldDelay", 0.18f);
            PPunchSpeed = cat.CreateEntry("PunchSpeed", 18f);
            PPunchRadius = cat.CreateEntry("PunchRadius", 0.9f);
            PBoomRadius = cat.CreateEntry("ExplosionRadius", 7f);
            PBoomSpeed = cat.CreateEntry("ExplosionSpeed", 28f);
            PBoomUp = cat.CreateEntry("ExplosionUp", 0.4f);
            PVolume = cat.CreateEntry("ExplosionVolume", 1f);
            LoggerInstance.Msg("Gang Beasts Grab 1.0 zaladowany. LPM przytrzymaj = chwyc i przenos (rolka = dalej/blizej), puszczenie w ruchu = rzut, szybkie klikniecie = cios. PPM = WYBUCH. F2 wl/wyl, F3/F4 mniejszy/wiekszy wybuch.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            grabbed = null;
            dragging = false;
            cam = null;
            camTimer = 0f;
            ClearFx();
        }

        // ---------- Petla glowna ----------

        public override void OnUpdate()
        {
            try
            {
                Inp.Poll();

                if (Inp.F2)
                {
                    PEnabled.Value = !PEnabled.Value;
                    if (!PEnabled.Value) { grabbed = null; dragging = false; }
                    LoggerInstance.Msg("Grab: " + (PEnabled.Value ? "WLACZONY" : "wylaczony"));
                    SavePrefs();
                }
                if (Inp.F3)
                {
                    PBoomRadius.Value = Mathf.Max(1.5f, PBoomRadius.Value - 1f);
                    LoggerInstance.Msg("Promien wybuchu: " + PBoomRadius.Value.ToString("0.0"));
                    SavePrefs();
                }
                if (Inp.F4)
                {
                    PBoomRadius.Value = Mathf.Min(25f, PBoomRadius.Value + 1f);
                    LoggerInstance.Msg("Promien wybuchu: " + PBoomRadius.Value.ToString("0.0"));
                    SavePrefs();
                }

                TickFx(Time.deltaTime);

                bool left = Inp.Left, right = Inp.Right;
                bool leftDown = left && !leftPrev;
                bool rightDown = right && !rightPrev;
                leftPrev = left;
                rightPrev = right;

                if (!PEnabled.Value) return;

                if (PForceCursor.Value)
                {
                    try { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } catch { }
                }

                Camera c = GetCam();
                if (c == null) return;
                Ray ray = c.ScreenPointToRay(new Vector3(Inp.Pos.x, Inp.Pos.y, 0f));

                if (leftDown) StartGrab(ray);

                if (grabbed != null)
                {
                    if (left) UpdateGrab(ray);
                    else EndGrab(ray);
                }
                else
                {
                    dragging = false;
                }

                if (rightDown) Explode(ray);
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

        public override void OnFixedUpdate()
        {
            try
            {
                if (!dragging || grabbed == null) return;
                Vector3 cur = grabbed.transform.TransformPoint(localPoint);
                Vector3 v = (target - cur) * PGain.Value;
                float m = PMaxSpeed.Value;
                if (v.sqrMagnitude > m * m) v = v.normalized * m;
                grabbed.velocity = v;
                grabbed.angularVelocity = grabbed.angularVelocity * 0.8f;
            }
            catch (Exception e)
            {
                if (!fixedErrLogged)
                {
                    fixedErrLogged = true;
                    LoggerInstance.Error("Blad w OnFixedUpdate (kolejne takie same pomijam): " + e);
                }
            }
        }

        private static void SavePrefs()
        {
            try { MelonPreferences.Save(); } catch { }
        }

        // ---------- Kamera ----------

        private Camera GetCam()
        {
            if (cam != null && cam.enabled && cam.gameObject.activeInHierarchy) return cam;
            cam = null;
            camTimer -= Time.unscaledDeltaTime;
            if (camTimer > 0f) return null;
            camTimer = 0.5f;

            Camera m = Camera.main;
            if (m != null && m.enabled) { cam = m; return cam; }

            Camera best = null;
            foreach (var o in FindAll(typeof(Camera)))
            {
                var c = o as Camera;
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (c.targetTexture != null) continue;
                if (best == null || c.depth > best.depth) best = c;
            }
            cam = best;
            return cam;
        }

        // ---------- Chwytanie / rzut / cios ----------

        // Promien leci przez rzeczy bez fizyki (sciany, ozdoby), ale zatrzymuje sie na podlodze
        private static Rigidbody FindBody(Ray ray, out RaycastHit best)
        {
            best = new RaycastHit();
            Vector3 o = ray.origin;
            Vector3 d = ray.direction;
            float remaining = 300f;
            for (int i = 0; i < 4; i++)
            {
                RaycastHit h;
                if (!Physics.Raycast(o, d, out h, remaining, ~0, QueryTriggerInteraction.Ignore)) break;
                Rigidbody rb = h.rigidbody;
                if (rb != null && !rb.isKinematic)
                {
                    best = h;
                    // zlicz dotychczasowy dystans od kamery
                    return rb;
                }
                if (h.normal.y > 0.7f) break;   // podloga - dalej nie szukamy
                float adv = h.distance + 0.03f;
                o += d * adv;
                remaining -= adv;
                if (remaining <= 0f) break;
            }
            return null;
        }

        private void StartGrab(Ray ray)
        {
            RaycastHit hit;
            Rigidbody rb = FindBody(ray, out hit);
            if (rb == null) return;

            grabbed = rb;
            localPoint = rb.transform.InverseTransformPoint(hit.point);
            pressWorldPoint = hit.point;
            dist = Vector3.Distance(ray.origin, hit.point);
            pressTime = Time.unscaledTime;
            pressPos = Inp.Pos;
            dragging = false;
            hasPrevTarget = false;
            tVel = Vector3.zero;
            target = hit.point;
        }

        private void UpdateGrab(Ray ray)
        {
            if (grabbed == null) { dragging = false; return; }

            float held = Time.unscaledTime - pressTime;
            float moved = (Inp.Pos - pressPos).magnitude;
            if (!dragging && (held > PHold.Value || moved > 14f))
            {
                dragging = true;
                hasPrevTarget = false;
                tVel = Vector3.zero;
            }

            if (Inp.Scroll != 0f)
                dist = Mathf.Clamp(dist + Mathf.Sign(Inp.Scroll) * 1.2f, 1.5f, 250f);

            target = ray.origin + ray.direction * dist;

            if (dragging)
            {
                float dt = Time.deltaTime;
                if (hasPrevTarget && dt > 0.0001f)
                {
                    Vector3 raw = (target - prevTarget) / dt;
                    tVel = Vector3.Lerp(tVel, raw, 0.3f);
                }
                prevTarget = target;
                hasPrevTarget = true;
            }
        }

        private void EndGrab(Ray ray)
        {
            Rigidbody rb = grabbed;
            bool wasDragging = dragging;
            grabbed = null;
            dragging = false;
            if (rb == null) return;

            if (!wasDragging) Punch(rb, ray);
            else Throw(rb);
        }

        private void Throw(Rigidbody rb)
        {
            Vector3 v = tVel * PThrowBoost.Value;
            float m = PMaxThrow.Value;
            if (v.sqrMagnitude > m * m) v = v.normalized * m;
            if (v.magnitude < 0.8f) return;     // puszczone w miejscu - po prostu spada

            var all = FindAll(typeof(Rigidbody));
            foreach (var b in Ragdoll(rb, all))
            {
                if (b == null || b.isKinematic) continue;
                if (b == rb) b.velocity = v;
                else b.velocity = Vector3.Lerp(b.velocity, v, 0.8f);
            }
        }

        private void Punch(Rigidbody hitBody, Ray ray)
        {
            Vector3 point = hitBody.transform.TransformPoint(localPoint);
            Vector3 h = new Vector3(ray.direction.x, 0f, ray.direction.z);
            if (h.sqrMagnitude < 0.0001f) h = Vector3.forward;
            Vector3 dir = (h.normalized + Vector3.up * 0.35f).normalized;

            float radius = PPunchRadius.Value;
            float speed = PPunchSpeed.Value;
            var all = FindAll(typeof(Rigidbody));
            foreach (var o in all)
            {
                var b = o as Rigidbody;
                if (b == null || b.isKinematic) continue;
                float f;
                if (b == hitBody) f = 1f;
                else
                {
                    float d = Vector3.Distance(b.position, point);
                    if (d > radius) continue;
                    f = Mathf.Clamp01(1f - d / radius) * 0.7f;
                }
                b.AddForce(dir * speed * f, ForceMode.VelocityChange);
            }
            // reszta postaci tez lekko dostaje (zeby cios "wchodzil" w cale cialo)
            foreach (var b in Ragdoll(hitBody, all))
            {
                if (b == null || b.isKinematic || b == hitBody) continue;
                b.AddForce(dir * speed * 0.25f, ForceMode.VelocityChange);
            }
            SpawnFx(point, new Color(1f, 0.95f, 0.6f), 0.15f, 0.8f, 0.16f, false);
        }

        // Wszystkie ciala fizyczne tej samej "postaci" (wspolny korzen hierarchii), o ile to rozsadna liczba
        private static List<Rigidbody> Ragdoll(Rigidbody rb, List<UnityEngine.Object> all)
        {
            var res = new List<Rigidbody>();
            Transform root = rb.transform.root;
            foreach (var o in all)
            {
                var b = o as Rigidbody;
                if (b == null) continue;
                if (b.transform.root == root) res.Add(b);
            }
            if (res.Count > 30 || res.Count == 0)
            {
                res.Clear();
                res.Add(rb);
            }
            return res;
        }

        // ---------- WYBUCH ----------

        private void Explode(Ray ray)
        {
            RaycastHit h;
            if (!Physics.Raycast(ray, out h, 300f, ~0, QueryTriggerInteraction.Ignore)) return;

            Vector3 center = h.point + h.normal * 0.4f;
            float radius = PBoomRadius.Value;
            float speed = PBoomSpeed.Value;
            float up = PBoomUp.Value;
            bool tear = PTear.Value;

            int affected = 0;
            var all = FindAll(typeof(Rigidbody));
            foreach (var o in all)
            {
                var b = o as Rigidbody;
                if (b == null || b.isKinematic) continue;
                Vector3 offs = b.position - center;
                float d = offs.magnitude;
                if (d > radius) continue;

                Vector3 dir = d > 0.05f ? offs / d : UnityEngine.Random.onUnitSphere;
                dir = (dir + Vector3.up * up).normalized;
                float f = 1f - d / radius;
                float v = speed * (0.35f + 0.65f * f);

                if (b.IsSleeping()) b.WakeUp();
                b.AddForce(dir * v, ForceMode.VelocityChange);
                b.AddTorque(UnityEngine.Random.insideUnitSphere * (10f * f), ForceMode.VelocityChange);

                if (tear && d < radius * 0.5f)
                {
                    try
                    {
                        var j = b.GetComponent<Joint>();
                        if (j != null) UnityEngine.Object.Destroy(j);
                    }
                    catch { }
                }
                affected++;
            }

            SpawnFx(center, new Color(1f, 0.45f, 0.05f), 0.6f, radius * 1.6f, 0.5f, true);
            SpawnFx(center, new Color(1f, 0.9f, 0.5f), 0.4f, radius * 1.0f, 0.3f, false);
            SpawnDebris(center, radius);
            PlayBoom(center);
            LoggerInstance.Msg("BOOM! promien " + radius.ToString("0.0") + ", poruszone obiekty: " + affected);
        }

        // ---------- Efekty ----------

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
                MelonLogger.Msg("[Grab] shader efektow: " + (cachedShader != null ? cachedShader.name : "(brak - domyslny material)"));
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
                m = r.material;
                m.color = c;
            }
        }

        private static GameObject MakeBall(string name, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                Colorize(r, color);
            }
            return go;
        }

        private void SpawnFx(Vector3 pos, Color color, float s0, float s1, float life, bool lamp)
        {
            try
            {
                var go = MakeBall("GBGrabFx", color);
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * s0;
                var item = new FxItem { Go = go, T = go.transform, Born = Time.time, Life = life, S0 = s0, S1 = s1 };
                if (lamp)
                {
                    try
                    {
                        var lg = go.AddComponent<Light>();
                        lg.color = new Color(1f, 0.7f, 0.3f);
                        lg.range = s1 * 1.4f;
                        lg.intensity = 6f;
                        item.Lamp = lg;
                        item.Lamp0 = 6f;
                    }
                    catch { }
                }
                fx.Add(item);
            }
            catch (Exception e)
            {
                LoggerInstance.Warning("Nie udalo sie stworzyc efektu: " + e.Message);
            }
        }

        private void SpawnDebris(Vector3 pos, float radius)
        {
            try
            {
                Color[] cols =
                {
                    new Color(1f, 0.5f, 0.05f), new Color(1f, 0.85f, 0.2f),
                    new Color(0.25f, 0.2f, 0.18f), new Color(1f, 0.3f, 0.05f)
                };
                for (int i = 0; i < 14; i++)
                {
                    var go = MakeBall("GBGrabDebris", cols[UnityEngine.Random.Range(0, cols.Length)]);
                    go.transform.position = pos;
                    go.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.1f, 0.3f);
                    Vector3 dir = (UnityEngine.Random.onUnitSphere + Vector3.up * 0.8f).normalized;
                    debris.Add(new Debris
                    {
                        T = go.transform,
                        V = dir * UnityEngine.Random.Range(0.6f, 1.4f) * radius * 1.6f,
                        Die = Time.time + UnityEngine.Random.Range(0.8f, 1.5f)
                    });
                }
            }
            catch { }
        }

        private void TickFx(float dt)
        {
            for (int i = fx.Count - 1; i >= 0; i--)
            {
                var it = fx[i];
                if (it.Go == null) { fx.RemoveAt(i); continue; }
                float p = (Time.time - it.Born) / it.Life;
                if (p >= 1f)
                {
                    UnityEngine.Object.Destroy(it.Go);
                    fx.RemoveAt(i);
                    continue;
                }
                float s;
                if (p < 0.6f)
                {
                    float q = p / 0.6f;
                    s = Mathf.Lerp(it.S0, it.S1, 1f - (1f - q) * (1f - q));
                }
                else
                {
                    s = it.S1 * (1f - (p - 0.6f) / 0.4f);
                }
                it.T.localScale = Vector3.one * Mathf.Max(0.001f, s);
                if (it.Lamp != null) it.Lamp.intensity = it.Lamp0 * (1f - p);
            }

            for (int i = debris.Count - 1; i >= 0; i--)
            {
                var d = debris[i];
                if (d.T == null || Time.time > d.Die)
                {
                    if (d.T != null) UnityEngine.Object.Destroy(d.T.gameObject);
                    debris.RemoveAt(i);
                    continue;
                }
                d.V += Physics.gravity * dt;
                d.T.position += d.V * dt;
            }
        }

        private void ClearFx()
        {
            foreach (var it in fx)
                if (it.Go != null) { try { UnityEngine.Object.Destroy(it.Go); } catch { } }
            fx.Clear();
            foreach (var d in debris)
                if (d.T != null) { try { UnityEngine.Object.Destroy(d.T.gameObject); } catch { } }
            debris.Clear();
        }

        // ---------- Dzwiek wybuchu (generowany, bez plikow) ----------

        private void PlayBoom(Vector3 pos)
        {
            try
            {
                if (boomClip == null && !boomTried)
                {
                    boomTried = true;
                    boomClip = BuildBoom();
                    if (boomClip == null) LoggerInstance.Warning("Dzwiek wybuchu niedostepny w tej wersji gry (efekt dziala bez dzwieku).");
                }
                if (boomClip != null) AudioSource.PlayClipAtPoint(boomClip, pos, Mathf.Clamp01(PVolume.Value));
            }
            catch { }
        }

        private static AudioClip BuildBoom()
        {
            try
            {
                int rate = 22050;
                int n = (int)(rate * 1.1f);
                float[] data = new float[n];
                var rnd = new System.Random(1234);
                float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)rate;
                    float env = Mathf.Exp(-t * 4.2f);
                    float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                    lp += (noise - lp) * Mathf.Lerp(0.55f, 0.04f, Mathf.Clamp01(t * 2.2f));
                    float thump = Mathf.Sin(2f * Mathf.PI * (70f - 40f * t) * t);
                    data[i] = Mathf.Clamp((lp * 1.6f + thump * 0.9f) * env, -1f, 1f);
                }
                AudioClip clip = AudioClip.Create("gbgrab_boom", n, 1, rate, false);
                foreach (var m in typeof(AudioClip).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "SetData") continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 2 || ps[1].ParameterType != typeof(int)) continue;
                    object arg = ps[0].ParameterType == typeof(float[])
                        ? (object)data
                        : Activator.CreateInstance(ps[0].ParameterType, new object[] { data });
                    m.Invoke(clip, new object[] { arg, 0 });
                    return clip;
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Grab] nie udalo sie zrobic dzwieku: " + e.Message);
            }
            return null;
        }

        // ---------- FindObjectsOfType<T>() przez refleksje (w IL2CPP zwraca tablice Il2Cpp) ----------

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
    }
}
