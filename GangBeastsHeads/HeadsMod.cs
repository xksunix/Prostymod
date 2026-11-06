using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(GBHeads.HeadsMod), "Gang Beasts Flying Heads", "1.1.0", "Claude")]

namespace GBHeads
{
    // Dane jednej postaci z gry (ofiara ataku glowy)
    internal class ActorInfo
    {
        public Component Comp;
        public Transform Top, Hips, Chest, Head;
    }

    public class HeadsMod : MelonMod
    {
        internal static MelonPreferences_Entry<int> PCount;
        internal static MelonPreferences_Entry<float> PSize, PAttack, PVolume;
        internal static MelonPreferences_Entry<bool> PAttackOn, PTalk;
        internal static MelonPreferences_Entry<string> PShader;

        private HeadAssets A;
        private bool assetsTried;
        private readonly List<HeadObj> heads = new List<HeadObj>();
        private readonly Dictionary<int, ActorInfo> actorMap = new Dictionary<int, ActorInfo>();
        private readonly List<ActorInfo> actors = new List<ActorInfo>();

        private Type actorType;
        private bool actorTypeSearched;
        private float scanTimer, ensureTimer, camTimer;
        private float sceneTime;
        private float spawnRetryAt;
        private bool errLogged, guiErrLogged;

        private Camera cam;
        private bool menuOpen;
        private float lastToggle = -10f;
        private string shaderLabel = "";

        internal Camera Cam { get { return cam; } }
        // warstwa fizyki postaci - glowy uzywaja jej, zeby zderzac sie z postaciami i mapa tak samo jak one
        internal int ActorLayer
        {
            get
            {
                foreach (var a in actors)
                    if (a.Hips != null) { try { return a.Hips.gameObject.layer; } catch { } }
                return -1;
            }
        }
        internal float Depth = 12f;
        internal float CharH = 1.7f;
        internal float HeadWorld { get { return CharH * Mathf.Clamp(PSize.Value, 0.2f, 4f); } }

        // ---------- Start ----------

        public override void OnApplicationStart()
        {
            var cat = MelonPreferences.CreateCategory("GBHeads", "Gang Beasts Flying Heads");
            PCount = cat.CreateEntry("HeadCount", 3);
            PSize = cat.CreateEntry("HeadSize", 1.1f);
            PAttack = cat.CreateEntry("AttackEverySeconds", 7f);
            PAttackOn = cat.CreateEntry("AttacksEnabled", true);
            PTalk = cat.CreateEntry("TalkEnabled", true);
            PVolume = cat.CreateEntry("Volume", 0.8f);
            PShader = cat.CreateEntry("ShaderName", "");
            LoggerInstance.Msg("Gang Beasts Flying Heads 1.0 zaladowany. H = menu (liczba glow 0-10, rozmiar, ataki, glosnosc).");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            foreach (var h in heads) { try { h.Destroy(); } catch { } }
            heads.Clear();
            actorMap.Clear();
            actors.Clear();
            cam = null;
            camTimer = 0f;
            scanTimer = 0f;
            sceneTime = Time.time;
            LoggerInstance.Msg("Scena: " + sceneName);
        }

        // ---------- Petla ----------

        public override void OnUpdate()
        {
            try
            {
                if (Inp.KeyDown(KeyCode.H)) ToggleMenu();
                if (menuOpen)
                {
                    try { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } catch { }
                    if (Inp.KeyDown(KeyCode.LeftArrow)) SetCount(PCount.Value - 1);
                    if (Inp.KeyDown(KeyCode.RightArrow)) SetCount(PCount.Value + 1);
                    if (Inp.KeyDown(KeyCode.Escape)) menuOpen = false;
                }

                if (A == null)
                {
                    // silnik musi juz dzialac i sceny byc zaladowane, zeby zebrac shadery z gry
                    if (!assetsTried && Time.frameCount > 90) LoadAssets();
                    return;
                }
                if (!A.Ok) return;

                float dt = Time.deltaTime;

                scanTimer -= Time.unscaledDeltaTime;
                if (scanTimer <= 0f)
                {
                    scanTimer = 1.2f;
                    ScanActors();
                }

                cam = GetCam();
                if (cam == null) return;

                ensureTimer -= Time.unscaledDeltaTime;
                if (ensureTimer <= 0f)
                {
                    ensureTimer = 0.5f;
                    EnsureHeads();
                }

                for (int i = 0; i < heads.Count; i++) heads[i].Update(dt);
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

        private void LoadAssets()
        {
            assetsTried = true;
            try
            {
                A = HeadAssets.Load(PShader.Value);
                shaderLabel = A.CurrentShaderName;
                LoggerInstance.Msg(A.Ok ? "Zasoby glow gotowe." : "Zasoby glow NIE zaladowaly sie - sprawdz log powyzej.");
            }
            catch (Exception e)
            {
                A = new HeadAssets();
                LoggerInstance.Error("Blad ladowania zasobow: " + e);
            }
        }

        // ---------- Glowy ----------

        private void EnsureHeads()
        {
            if (Time.time - sceneTime < 0.8f) return;
            int want = Mathf.Clamp(PCount.Value, 0, 10);

            heads.RemoveAll(h => h.Root == null);
            while (heads.Count > want)
            {
                heads[heads.Count - 1].Destroy();
                heads.RemoveAt(heads.Count - 1);
            }
            if (heads.Count < want && Time.time >= spawnRetryAt)
            {
                while (heads.Count < want)
                {
                    HeadObj h = HeadObj.Create(this, A, PickViewport(null));
                    if (h == null) { spawnRetryAt = Time.time + 10f; break; }
                    heads.Add(h);
                }
            }
        }

        internal Vector3 ViewportToWorld(Vector2 vp)
        {
            if (cam == null) return Vector3.zero;
            return cam.ViewportToWorldPoint(new Vector3(vp.x, vp.y, Mathf.Max(1f, Depth) * 0.92f));
        }

        // Losuje punkt na ekranie (unika innych glow)
        internal Vector2 PickViewport(HeadObj self)
        {
            float visH = cam != null ? 2f * Mathf.Max(1f, Depth) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) : 10f;
            float hf = Mathf.Clamp(HeadWorld / Mathf.Max(0.5f, visH), 0.05f, 0.6f);
            float yMax = Mathf.Max(0.6f, 0.97f - hf * 0.55f);
            float yMin = Mathf.Min(yMax - 0.05f, 0.45f + hf * 0.45f);
            float xMin = 0.08f + hf * 0.3f, xMax = 0.92f - hf * 0.3f;

            Vector2 best = new Vector2(0.5f, 0.75f);
            float bestD = -1f;
            for (int i = 0; i < 6; i++)
            {
                var c = new Vector2(UnityEngine.Random.Range(xMin, xMax), UnityEngine.Random.Range(yMin, yMax));
                float md = 9f;
                foreach (var o in heads)
                {
                    if (o == self) continue;
                    md = Mathf.Min(md, Vector2.Distance(c, o.Vp));
                }
                if (md > bestD) { bestD = md; best = c; }
            }
            return best;
        }

        internal ActorInfo PickVictim()
        {
            var ok = new List<ActorInfo>();
            foreach (var a in actors)
            {
                if (a.Comp == null || a.Chest == null) continue;
                bool active = true;
                try { active = a.Comp.gameObject.activeInHierarchy; } catch { }
                if (active) ok.Add(a);
            }
            if (ok.Count == 0) return null;
            return ok[UnityEngine.Random.Range(0, ok.Count)];
        }

        internal bool IsClipPlaying(int clip)
        {
            foreach (var h in heads)
                if (h.IsClipPlaying(clip)) return true;
            return false;
        }

        internal float VolumeFor()
        {
            return Mathf.Clamp01(PVolume.Value) * Mathf.Clamp(2f / Mathf.Sqrt(Mathf.Max(1, heads.Count)), 0.3f, 1f);
        }

        // ---------- Postacie z gry ----------

        private void ScanActors()
        {
            try
            {
                if (!actorTypeSearched)
                {
                    actorTypeSearched = true;
                    actorType = FindActorType();
                    LoggerInstance.Msg(actorType != null
                        ? "Typ postaci: " + actorType.FullName
                        : "Nie znaleziono klasy 'Actor' - glowy nie beda mialy kogo atakowac (dalej lataja i gadaja).");
                }
                if (actorType == null) return;

                var alive = new HashSet<int>();
                foreach (var o in Il2.FindAll(actorType))
                {
                    var c = o as Component;
                    if (c == null) continue;
                    int id = c.GetInstanceID();
                    alive.Add(id);
                    if (actorMap.ContainsKey(id)) continue;
                    Transform top = c.transform;
                    Transform hips = Il2.FindBone(top, "actor_hips_collider");
                    if (hips == null) continue;
                    var info = new ActorInfo
                    {
                        Comp = c,
                        Top = top,
                        Hips = hips,
                        Chest = Il2.FindBone(top, "actor_chest_collider") ?? hips,
                        Head = Il2.FindBone(top, "actor_head_collider")
                    };
                    actorMap[id] = info;
                }

                var gone = new List<int>();
                foreach (var kv in actorMap)
                    if (!alive.Contains(kv.Key) || kv.Value.Comp == null) gone.Add(kv.Key);
                foreach (int id in gone) actorMap.Remove(id);

                actors.Clear();
                actors.AddRange(actorMap.Values);

                // rozmiar postaci i odleglosc od kamery (do skalowania glow i ustawiania ich w kadrze)
                if (actors.Count > 0)
                {
                    float sumH = 0f; int nH = 0;
                    float sumD = 0f; int nD = 0;
                    Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
                    Vector3 camFwd = cam != null ? cam.transform.forward : Vector3.forward;
                    foreach (var a in actors)
                    {
                        if (a.Hips == null) continue;
                        if (a.Head != null) { sumH += (a.Head.position - a.Hips.position).magnitude * 2.4f; nH++; }
                        if (cam != null) { sumD += Vector3.Dot(a.Hips.position - camPos, camFwd); nD++; }
                    }
                    if (nH > 0) CharH = Mathf.Lerp(CharH, Mathf.Clamp(sumH / nH, 0.6f, 6f), 0.3f);
                    if (nD > 0) Depth = Mathf.Lerp(Depth, Mathf.Clamp(sumD / nD, 3f, 90f), 0.3f);
                }
            }
            catch (Exception e)
            {
                LoggerInstance.Warning("Blad skanowania postaci: " + e.Message);
            }
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

        private Camera GetCam()
        {
            if (cam != null && cam.enabled && cam.gameObject.activeInHierarchy) return cam;
            cam = null;
            camTimer -= Time.unscaledDeltaTime;
            if (camTimer > 0f) return null;
            camTimer = 0.5f;

            Camera m = Camera.main;
            if (m != null && m.enabled) return m;

            Camera best = null;
            foreach (var o in Il2.FindAll(typeof(Camera)))
            {
                var c = o as Camera;
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (c.targetTexture != null) continue;
                if (best == null || c.depth > best.depth) best = c;
            }
            return best;
        }

        // ---------- Menu (klawisz H) ----------

        private void ToggleMenu()
        {
            if (Time.unscaledTime - lastToggle < 0.25f) return;     // trzy drogi wykrywania H - bez podwojnego przelaczania
            lastToggle = Time.unscaledTime;
            menuOpen = !menuOpen;
            LoggerInstance.Msg("Menu glow: " + (menuOpen ? "otwarte" : "zamkniete"));
        }

        private void SetCount(int n)
        {
            PCount.Value = Mathf.Clamp(n, 0, 10);
            SavePrefs();
        }

        private static void SavePrefs()
        {
            try { MelonPreferences.Save(); } catch { }
        }

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
                if (!guiErrLogged)
                {
                    guiErrLogged = true;
                    LoggerInstance.Error("Blad menu (IMGUI): " + ex);
                }
            }
        }

        private void DrawMenu()
        {
            float s = Mathf.Max(1f, Screen.height / 720f);
            Matrix4x4 oldM = GUI.matrix;
            Color oldBg = GUI.backgroundColor;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));

            float vw = Screen.width / s;
            const float W = 430f, H = 388f;
            float x = (vw - W) * 0.5f, y = 30f;

            GUI.Box(new Rect(x, y, W, H), "LATAJACE GLOWY   (H lub Esc = zamknij)");

            // liczba glow
            GUI.Label(new Rect(x + 14, y + 30, W - 28, 20), "Ile glow na kazdej mapie:");
            for (int i = 0; i <= 10; i++)
            {
                GUI.backgroundColor = PCount.Value == i ? Color.green : oldBg;
                if (GUI.Button(new Rect(x + 14 + i * 36f, y + 52, 34, 30), i.ToString())) SetCount(i);
            }
            GUI.backgroundColor = oldBg;

            // rozmiar
            Row(x, y + 96, "Rozmiar glow", "x" + PSize.Value.ToString("0.00"),
                () => { PSize.Value = Mathf.Max(0.3f, PSize.Value * 0.9f); SavePrefs(); },
                () => { PSize.Value = Mathf.Min(3f, PSize.Value * 1.1f); SavePrefs(); });

            // czestotliwosc atakow
            Row(x, y + 134, "Atak co ok.", PAttack.Value.ToString("0.0") + " s",
                () => { PAttack.Value = Mathf.Min(40f, PAttack.Value * 1.25f); SavePrefs(); },
                () => { PAttack.Value = Mathf.Max(1.5f, PAttack.Value * 0.8f); SavePrefs(); });

            // glosnosc
            Row(x, y + 172, "Glosnosc", Mathf.RoundToInt(PVolume.Value * 100f) + "%",
                () => { PVolume.Value = Mathf.Max(0f, PVolume.Value - 0.1f); SavePrefs(); },
                () => { PVolume.Value = Mathf.Min(1f, PVolume.Value + 0.1f); SavePrefs(); });

            // przelaczniki
            GUI.backgroundColor = PAttackOn.Value ? Color.green : Color.red;
            if (GUI.Button(new Rect(x + 14, y + 214, 190, 30), "Atakuja: " + (PAttackOn.Value ? "TAK" : "NIE")))
            { PAttackOn.Value = !PAttackOn.Value; SavePrefs(); }
            GUI.backgroundColor = PTalk.Value ? Color.green : Color.red;
            if (GUI.Button(new Rect(x + 226, y + 214, 190, 30), "Gadaja: " + (PTalk.Value ? "TAK" : "NIE")))
            { PTalk.Value = !PTalk.Value; SavePrefs(); }
            GUI.backgroundColor = oldBg;

            // material (gdyby glowa byla niewidoczna / dziwnie wygladala)
            GUI.Label(new Rect(x + 14, y + 256, W - 28, 20), "Material: " + shaderLabel);
            if (GUI.Button(new Rect(x + 14, y + 278, W - 28, 30), "Zmien material (jesli glowa jest niewidoczna lub dziwna)"))
            {
                if (A != null && A.Ok)
                {
                    shaderLabel = A.CycleShader();
                    PShader.Value = shaderLabel;
                    foreach (var h in heads) h.ApplyMaterials();
                    SavePrefs();
                    LoggerInstance.Msg("Material glow: " + shaderLabel);
                }
            }

            GUI.Label(new Rect(x + 14, y + 318, W - 28, 20), "Strzalki lewo/prawo zmieniaja liczbe glow.");
            if (GUI.Button(new Rect(x + 14, y + 344, W - 28, 30), "Zamknij")) menuOpen = false;

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
