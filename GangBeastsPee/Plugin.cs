using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(GangBeastsPee.PeeMod), "Gang Beasts Pee", "1.1.0", "xksunix")]

namespace GangBeastsPee
{
    public class PeeMod : MelonMod
    {
        private MelonPreferences_Entry<string> keyName;
        private MelonPreferences_Entry<float> forwardSign;
        private MelonPreferences_Entry<float> dropsPerSecond;
        private MelonPreferences_Entry<float> streamSpeed;
        private MelonPreferences_Entry<float> spawnOffset;
        private MelonPreferences_Entry<float> spawnHeight;
        private MelonPreferences_Entry<int> maxPuddles;
        private MelonPreferences_Entry<bool> nearestOnly;

        private KeyCode key = KeyCode.U;

        private sealed class Droplet
        {
            public GameObject Go;
            public Vector3 Pos;
            public Vector3 Vel;
            public float Life;
            public Transform OwnerRoot;
        }

        private readonly List<Droplet> active = new List<Droplet>();
        private readonly Stack<GameObject> dropletPool = new Stack<GameObject>();
        private readonly Queue<GameObject> puddles = new Queue<GameObject>();
        private readonly List<Transform> emitters = new List<Transform>();
        private readonly Dictionary<Transform, float> spawnAcc = new Dictionary<Transform, float>();

        private Material mat;
        private float nextScan;
        private bool inputBroken;
        private bool wasHeld;
        private int errors;
        private readonly System.Random rng = new System.Random();

        // cache for FindObjectsOfType via reflection (works on Mono and IL2CPP)
        private static bool findResolved;
        private static MethodInfo findMethod;
        private static bool findNeedsBool;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static int ToVirtualKey(KeyCode k)
        {
            int c = (int)k;
            if (c >= (int)KeyCode.A && c <= (int)KeyCode.Z) return 0x41 + (c - (int)KeyCode.A);
            if (c >= (int)KeyCode.Alpha0 && c <= (int)KeyCode.Alpha9) return 0x30 + (c - (int)KeyCode.Alpha0);
            if (c >= (int)KeyCode.F1 && c <= (int)KeyCode.F12) return 0x70 + (c - (int)KeyCode.F1);
            return 0;
        }

        private bool IsHeld()
        {
            if (!inputBroken)
            {
                try { return Input.GetKey(key); }
                catch (Exception)
                {
                    inputBroken = true;
                    LoggerInstance.Warning("Stary Input nie dziala w tej grze - przechodze na odczyt klawisza przez WinAPI.");
                }
            }
            int vk = ToVirtualKey(key);
            if (vk == 0) return false;
            try
            {
                if (!Application.isFocused) return false;
                return (GetAsyncKeyState(vk) & 0x8000) != 0;
            }
            catch (Exception e)
            {
                if (errors++ < 10) LoggerInstance.Error("GetAsyncKeyState: " + e.Message);
                return false;
            }
        }

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("GangBeastsPee", "Gang Beasts Pee");
            keyName = cat.CreateEntry("Key", "U", "Klawisz sikania (nazwa z UnityEngine.KeyCode)");
            forwardSign = cat.CreateEntry("ForwardSign", 1f, "1 lub -1 - odwroc, jesli strumien leci z tylu postaci");
            dropsPerSecond = cat.CreateEntry("DropsPerSecond", 70f, "Ile kropel na sekunde");
            streamSpeed = cat.CreateEntry("StreamSpeed", 3.2f, "Sila strumienia");
            spawnOffset = cat.CreateEntry("SpawnOffset", 0.35f, "Odleglosc startu strumienia przed biodrem");
            spawnHeight = cat.CreateEntry("SpawnHeight", -0.05f, "Wysokosc startu wzgledem biodra");
            maxPuddles = cat.CreateEntry("MaxPuddles", 500, "Limit plam (najstarsze sa recyklingowane)");
            nearestOnly = cat.CreateEntry("NearestCharacterOnly", false, "true = sika tylko postac najblizej kamery");

            try { key = (KeyCode)Enum.Parse(typeof(KeyCode), keyName.Value, true); }
            catch
            {
                LoggerInstance.Warning("Nieznany klawisz '" + keyName.Value + "', uzywam U");
                key = KeyCode.U;
            }
            LoggerInstance.Msg("Gang Beasts Pee zaladowany. Przytrzymaj " + key + ", zeby sikac.");
        }

        public override void OnUpdate()
        {
            try
            {
                float dt = Time.deltaTime;
                bool held = IsHeld();
                if (held != wasHeld)
                {
                    wasHeld = held;
                    if (held) { ScanEmitters(); nextScan = Time.time + 1f; LoggerInstance.Msg("Sikanie START, postaci: " + emitters.Count); }
                }

                if (held)
                {
                    if (Time.time >= nextScan)
                    {
                        nextScan = Time.time + 1f;
                        ScanEmitters();
                    }
                    EmitFromAll(dt);
                }
                StepDroplets(dt);
            }
            catch (Exception e)
            {
                if (errors++ < 10) LoggerInstance.Error("BLAD w OnUpdate: " + e);
            }
        }

        private sealed class Body
        {
            public Transform Hips, LeftThigh, RightThigh;
        }

        private readonly Dictionary<Transform, Body> bodies = new Dictionary<Transform, Body>();

        private void ScanEmitters()
        {
            emitters.Clear();
            bodies.Clear();
            var byRoot = new Dictionary<int, Body>();
            var rootOf = new Dictionary<int, Transform>();

            foreach (var o in FindAll(typeof(Transform)))
            {
                var t = o as Transform;
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                string n = t.name.ToLowerInvariant();
                bool hips = n.Contains("hips") || n.Contains("pelvis");
                bool lt = n.Contains("leftthigh");
                bool rt = n.Contains("rightthigh");
                if (!hips && !lt && !rt) continue;

                var root = t.root;
                if (root == null) continue;
                int id = root.GetInstanceID();
                Body b;
                if (!byRoot.TryGetValue(id, out b)) { b = new Body(); byRoot[id] = b; rootOf[id] = root; }
                if (hips && b.Hips == null) b.Hips = t;
                else if (lt && b.LeftThigh == null) b.LeftThigh = t;
                else if (rt && b.RightThigh == null) b.RightThigh = t;
            }

            foreach (var kv in byRoot)
            {
                if (kv.Value.Hips == null) continue;
                emitters.Add(kv.Value.Hips);
                bodies[kv.Value.Hips] = kv.Value;
            }

            if (nearestOnly.Value && emitters.Count > 1 && Camera.main != null)
            {
                Transform best = null;
                float bd = float.MaxValue;
                foreach (var e in emitters)
                {
                    float d = (e.position - Camera.main.transform.position).sqrMagnitude;
                    if (d < bd) { bd = d; best = e; }
                }
                emitters.Clear();
                if (best != null) emitters.Add(best);
            }
        }

        // Przod postaci z ulozenia ud (osie kosci w tej grze sa dowolne), awaryjnie z osi biodra
        private Vector3 FacingOf(Transform hips)
        {
            Body b;
            if (bodies.TryGetValue(hips, out b) && b.LeftThigh != null && b.RightThigh != null)
            {
                Vector3 right = b.RightThigh.position - b.LeftThigh.position;
                right.y = 0f;
                if (right.sqrMagnitude > 0.0001f)
                    return Vector3.Cross(right.normalized, Vector3.up);
            }
            Vector3 f = hips.forward;
            f.y = 0f;
            return f;
        }

        private void EmitFromAll(float dt)
        {
            foreach (var e in emitters)
            {
                if (e == null) continue;
                float acc;
                spawnAcc.TryGetValue(e, out acc);
                acc += dropsPerSecond.Value * dt;
                int n = (int)acc;
                acc -= n;
                spawnAcc[e] = acc;
                for (int i = 0; i < n; i++) SpawnDroplet(e);
            }
        }

        private void SpawnDroplet(Transform pelvis)
        {
            Vector3 fwd = FacingOf(pelvis) * forwardSign.Value;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            Vector3 pos = pelvis.position + fwd * spawnOffset.Value + Vector3.up * spawnHeight.Value;
            Vector3 spread = new Vector3(Rand(-0.08f, 0.08f), Rand(0.05f, 0.30f), Rand(-0.08f, 0.08f));
            Vector3 vel = (fwd + spread) * streamSpeed.Value * Rand(0.85f, 1.1f);

            var d = new Droplet
            {
                Go = GetDropletObject(),
                Pos = pos,
                Vel = vel,
                Life = 3f,
                OwnerRoot = pelvis.root
            };
            d.Go.transform.position = pos;
            d.Go.SetActive(true);
            active.Add(d);
        }

        private void StepDroplets(float dt)
        {
            if (dt <= 0f) return;
            Vector3 g = Physics.gravity;

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var d = active[i];
                d.Life -= dt;

                Vector3 newPos = d.Pos + d.Vel * dt + 0.5f * g * dt * dt;
                d.Vel += g * dt;

                Vector3 delta = newPos - d.Pos;
                float dist = delta.magnitude;
                bool kill = d.Life <= 0f || d.Go == null;

                RaycastHit hit;
                if (!kill && dist > 0.0001f && Physics.Raycast(d.Pos, delta / dist, out hit, dist))
                {
                    bool self = hit.collider != null && d.OwnerRoot != null &&
                                hit.collider.transform.root == d.OwnerRoot;
                    if (!self)
                    {
                        AddPuddle(hit.point, hit.normal);
                        kill = true;
                    }
                }

                if (kill)
                {
                    ReleaseDroplet(d);
                    active.RemoveAt(i);
                }
                else
                {
                    d.Pos = newPos;
                    d.Go.transform.position = newPos;
                }
            }
        }

        private void AddPuddle(Vector3 point, Vector3 normal)
        {
            GameObject go = null;
            if (puddles.Count >= maxPuddles.Value) go = puddles.Dequeue();
            if (go == null) go = MakePuddleObject();

            float size = Rand(0.10f, 0.22f);
            go.transform.position = point + normal * 0.012f;
            go.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Rand(0f, 360f));
            go.transform.localScale = new Vector3(size, size, 1f);
            puddles.Enqueue(go);
        }

        private GameObject MakePuddleObject()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "PeeMark";
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            go.GetComponent<Renderer>().sharedMaterial = GetMaterial();
            return go;
        }

        private GameObject GetDropletObject()
        {
            while (dropletPool.Count > 0)
            {
                var g = dropletPool.Pop();
                if (g != null) return g;
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "PeeDrop";
            go.transform.localScale = Vector3.one * 0.05f;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            go.GetComponent<Renderer>().sharedMaterial = GetMaterial();
            return go;
        }

        private void ReleaseDroplet(Droplet d)
        {
            if (d.Go == null) return;
            d.Go.SetActive(false);
            dropletPool.Push(d.Go);
        }

        private Material GetMaterial()
        {
            if (mat != null) return mat;

            string[] names = { "Sprites/Default", "Unlit/Color", "Legacy Shaders/Diffuse", "Standard" };
            Shader sh = null;
            foreach (var n in names)
            {
                sh = Shader.Find(n);
                if (sh != null) { LoggerInstance.Msg("Pee: uzywam shadera " + n); break; }
            }
            if (sh == null) LoggerInstance.Error("Pee: nie znaleziono zadnego shadera - siki beda niewidoczne.");

            mat = new Material(sh);
            mat.color = new Color(1f, 0.88f, 0.1f, 0.9f);
            return mat;
        }

        private float Rand(float a, float b)
        {
            return a + (float)rng.NextDouble() * (b - a);
        }

        // FindObjectsOfType<T>() przez refleksje - w IL2CPP zwraca tablice Il2Cpp zamiast T[]
        private static List<UnityEngine.Object> FindAll(Type t)
        {
            var result = new List<UnityEngine.Object>();
            if (!findResolved)
            {
                findResolved = true;
                MethodInfo noArgs = null, boolArg = null;
                foreach (var m in typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "FindObjectsOfType" || !m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 0) noArgs = m;
                    else if (ps.Length == 1 && ps[0].ParameterType == typeof(bool)) boolArg = m;
                }
                findMethod = noArgs ?? boolArg;
                findNeedsBool = noArgs == null && boolArg != null;
            }
            if (findMethod == null) return result;

            object res = findMethod.MakeGenericMethod(t).Invoke(null, findNeedsBool ? new object[] { false } : null);
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
