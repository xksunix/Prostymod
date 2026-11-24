using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(GBSwap.SwapMod), "Gang Beasts Head Swap", "1.0.0", "Claude")]

namespace GBSwap
{
    internal class ActorRec
    {
        public Component Comp;
        public Transform Top, Hips, Head, LA, RA;
        public Renderer OrigHead;
        public SwapHead H;
    }

    public class SwapMod : MelonMod
    {
        internal static MelonPreferences_Entry<bool> PEnabled;
        internal static MelonPreferences_Entry<float> PSize, PYaw;
        internal static MelonPreferences_Entry<string> PShader;

        private HeadAssets A;
        private bool assetsTried;
        private readonly Dictionary<int, ActorRec> recs = new Dictionary<int, ActorRec>();
        private Type actorType;
        private bool actorTypeSearched, loggedRenderers;
        private float scanTimer;
        private bool menuOpen, errLogged, guiErrLogged;
        private float lastToggle = -10f;
        private string shaderLabel = "";

        public override void OnApplicationStart()
        {
            var cat = MelonPreferences.CreateCategory("GBHeadSwap", "Gang Beasts Head Swap");
            PEnabled = cat.CreateEntry("Enabled", true);
            PSize = cat.CreateEntry("HeadSize", 1.35f);
            PYaw = cat.CreateEntry("FaceYaw", 0f);
            PShader = cat.CreateEntry("ShaderName", "");
            LoggerInstance.Msg("Gang Beasts Head Swap 1.0 zaladowany. H = menu (rozmiar, obrot twarzy, material, wlacz/wylacz).");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            foreach (var r in recs.Values) { try { if (r.H != null) r.H.Destroy(); } catch { } }
            recs.Clear();
            scanTimer = 0.5f;
            LoggerInstance.Msg("Scena: " + sceneName);
        }

        public override void OnUpdate()
        {
            try
            {
                if (Inp.KeyDown(KeyCode.H)) ToggleMenu();
                if (menuOpen)
                {
                    try { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } catch { }
                    if (Inp.KeyDown(KeyCode.Escape)) menuOpen = false;
                }

                if (A == null)
                {
                    if (!assetsTried && Time.frameCount > 90) LoadAssets();
                    return;
                }
                if (!A.Ok) return;

                scanTimer -= Time.unscaledDeltaTime;
                if (scanTimer <= 0f)
                {
                    scanTimer = 1.0f;
                    ScanActors();
                }

                float dt = Mathf.Min(Time.deltaTime, 0.05f);
                foreach (var r in recs.Values)
                    if (r.H != null) r.H.Update(dt);
            }
            catch (Exception e)
            {
                if (!errLogged) { errLogged = true; LoggerInstance.Error("Blad w OnUpdate (kolejne takie same pomijam): " + e); }
            }
        }

        private void LoadAssets()
        {
            assetsTried = true;
            try
            {
                A = HeadAssets.Load(PShader.Value);
                shaderLabel = A.CurrentShaderName;
                LoggerInstance.Msg(A.Ok ? "Model glowy gotowy." : "Model glowy NIE zaladowal sie - sprawdz log powyzej.");
            }
            catch (Exception e)
            {
                A = new HeadAssets();
                LoggerInstance.Error("Blad ladowania zasobow: " + e);
            }
        }

        private void ScanActors()
        {
            try
            {
                if (!actorTypeSearched)
                {
                    actorTypeSearched = true;
                    actorType = FindActorType();
                    LoggerInstance.Msg(actorType != null ? "Typ postaci: " + actorType.FullName : "Nie znaleziono klasy 'Actor' - mod nie ma komu podmienic glow.");
                }
                if (actorType == null) return;

                var alive = new HashSet<int>();
                foreach (var o in Il2.FindAll(actorType))
                {
                    var c = o as Component;
                    if (c == null) continue;
                    int id = c.GetInstanceID();
                    alive.Add(id);
                    if (recs.ContainsKey(id)) continue;

                    Transform top = c.transform;
                    Transform head = Il2.FindBone(top, "actor_head_collider");
                    Transform hips = Il2.FindBone(top, "actor_hips_collider");
                    if (head == null || hips == null) continue;
                    var rec = new ActorRec
                    {
                        Comp = c, Top = top, Head = head, Hips = hips,
                        LA = Il2.FindBone(top, "actor_leftArm_collider"),
                        RA = Il2.FindBone(top, "actor_rightArm_collider")
                    };
                    Transform hm = Il2.FindBone(top, "actor_head_skinnedMesh");
                    if (hm != null) rec.OrigHead = hm.GetComponent<Renderer>();
                    if (!loggedRenderers) { loggedRenderers = true; LogRenderers(top); }
                    rec.H = SwapHead.Create(this, A, rec);
                    recs[id] = rec;
                    LoggerInstance.Msg("Podmieniono glowe postaci (oryginalna siatka glowy: " + (rec.OrigHead != null ? "znaleziona" : "NIE znaleziona") + ").");
                }

                var gone = new List<int>();
                foreach (var kv in recs)
                    if (!alive.Contains(kv.Key) || kv.Value.Comp == null) gone.Add(kv.Key);
                foreach (int id in gone)
                {
                    try { if (recs[id].H != null) recs[id].H.Destroy(); } catch { }
                    recs.Remove(id);
                }
            }
            catch (Exception e) { LoggerInstance.Warning("Blad skanowania postaci: " + e.Message); }
        }

        private void LogRenderers(Transform top)
        {
            try
            {
                var q = new Queue<Transform>();
                q.Enqueue(top);
                var names = new List<string>();
                int n = 0;
                while (q.Count > 0 && n++ < 400)
                {
                    var t = q.Dequeue();
                    var r = t.GetComponent<Renderer>();
                    if (r != null) names.Add(t.name + (r.enabled ? "" : "(off)"));
                    for (int i = 0; i < t.childCount; i++) q.Enqueue(t.GetChild(i));
                }
                LoggerInstance.Msg("Renderery postaci: " + string.Join(", ", names.ToArray()));
            }
            catch { }
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


        // ---------- Menu (klawisz H) ----------

        private void ToggleMenu()
        {
            if (Time.unscaledTime - lastToggle < 0.25f) return;
            lastToggle = Time.unscaledTime;
            menuOpen = !menuOpen;
        }

        private static void SavePrefs() { try { MelonPreferences.Save(); } catch { } }

        public override void OnGUI()
        {
            try
            {
                Event e = Event.current;
                if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.H) ToggleMenu();
                if (!menuOpen) return;
                DrawMenu();
            }
            catch (Exception ex)
            {
                if (!guiErrLogged) { guiErrLogged = true; LoggerInstance.Error("Blad menu (IMGUI): " + ex); }
            }
        }

        private void DrawMenu()
        {
            float s = Mathf.Max(1f, Screen.height / 720f);
            Matrix4x4 oldM = GUI.matrix;
            Color oldBg = GUI.backgroundColor;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            float vw = Screen.width / s;
            const float W = 430f, H = 300f;
            float x = (vw - W) * 0.5f, y = 30f;

            GUI.Box(new Rect(x, y, W, H), "GLOWA NA POSTACIACH   (H lub Esc = zamknij)");

            GUI.backgroundColor = PEnabled.Value ? Color.green : Color.red;
            if (GUI.Button(new Rect(x + 14, y + 34, W - 28, 30), "Podmiana glow: " + (PEnabled.Value ? "WLACZONA" : "WYLACZONA")))
            { PEnabled.Value = !PEnabled.Value; SavePrefs(); }
            GUI.backgroundColor = oldBg;

            Row(x, y + 78, "Rozmiar glowy", "x" + PSize.Value.ToString("0.00"),
                () => { PSize.Value = Mathf.Max(0.5f, PSize.Value * 0.92f); SavePrefs(); },
                () => { PSize.Value = Mathf.Min(3f, PSize.Value * 1.08f); SavePrefs(); });

            Row(x, y + 116, "Obrot twarzy", Mathf.RoundToInt(PYaw.Value) + " st.",
                () => { PYaw.Value = Mathf.Repeat(PYaw.Value - 90f, 360f); SavePrefs(); },
                () => { PYaw.Value = Mathf.Repeat(PYaw.Value + 90f, 360f); SavePrefs(); });

            GUI.Label(new Rect(x + 14, y + 158, W - 28, 20), "Twarz z tylu / z boku? Zmien obrot twarzy o 90 st.");

            GUI.Label(new Rect(x + 14, y + 184, W - 28, 20), "Material: " + shaderLabel);
            if (GUI.Button(new Rect(x + 14, y + 206, W - 28, 30), "Zmien material (jesli glowa jest niewidoczna lub dziwna)"))
            {
                if (A != null && A.Ok)
                {
                    shaderLabel = A.CycleShader();
                    PShader.Value = shaderLabel;
                    foreach (var r in recs.Values) if (r.H != null) r.H.ApplyMaterials();
                    SavePrefs();
                    LoggerInstance.Msg("Material glow: " + shaderLabel);
                }
            }
            if (GUI.Button(new Rect(x + 14, y + 250, W - 28, 30), "Zamknij")) menuOpen = false;

            GUI.matrix = oldM;
            GUI.backgroundColor = oldBg;
        }

        private static void Row(float x, float y, string label, string value, Action minus, Action plus)
        {
            GUI.Label(new Rect(x + 14, y + 4, 150, 22), label);
            if (GUI.Button(new Rect(x + 170, y, 34, 28), "-")) minus();
            GUI.Label(new Rect(x + 212, y + 4, 90, 22), value);
            if (GUI.Button(new Rect(x + 306, y, 34, 28), "+")) plus();
        }
    }

    // ---------- Klawiatura: stary Input i Input System przez refleksje (nie wiadomo, ktory gra uzywa) ----------
    internal static class Inp
    {
        private static MethodInfo legacyDown;
        private static bool legacyTried;
        private static int legacyFails;
        private static Type tKeyboard;
        private static bool kbTried;
        private static int kbFails;
        private static readonly Dictionary<KeyCode, string> kbNames = new Dictionary<KeyCode, string>
        {
            { KeyCode.H, "hKey" },
            { KeyCode.LeftArrow, "leftArrowKey" },
            { KeyCode.RightArrow, "rightArrowKey" },
            { KeyCode.Escape, "escapeKey" }
        };

        public static bool KeyDown(KeyCode k)
        {
            bool a = Legacy(k);
            bool b = Sys(k);
            return a || b;
        }

        private static bool Legacy(KeyCode k)
        {
            if (legacyFails >= 5) return false;
            try
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
                    if (t != null) legacyDown = t.GetMethod("GetKeyDown", new[] { typeof(KeyCode) });
                }
                if (legacyDown == null) return false;
                return (bool)legacyDown.Invoke(null, new object[] { k });
            }
            catch { legacyFails++; return false; }
        }

        private static bool Sys(KeyCode k)
        {
            if (kbFails >= 20) return false;
            string name;
            if (!kbNames.TryGetValue(k, out name)) return false;
            try
            {
                if (!kbTried)
                {
                    kbTried = true;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (!asm.GetName().Name.Contains("InputSystem")) continue;
                        tKeyboard = asm.GetType("UnityEngine.InputSystem.Keyboard", false);
                        if (tKeyboard != null) break;
                    }
                }
                if (tKeyboard == null) return false;
                var curP = tKeyboard.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
                object kb = curP == null ? null : curP.GetValue(null, null);
                if (kb == null) return false;
                var keyP = kb.GetType().GetProperty(name);
                object ctl = keyP == null ? null : keyP.GetValue(kb, null);
                if (ctl == null) return false;
                var pp = ctl.GetType().GetProperty("wasPressedThisFrame");
                return pp != null && (bool)pp.GetValue(ctl, null);
            }
            catch { kbFails++; return false; }
        }
    }
}
