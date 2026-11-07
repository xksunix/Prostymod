using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(GBPigeons.PigeonsMod), "Gang Beasts Pigeons", "1.0.0", "Claude")]

namespace GBPigeons
{
    public class PigeonsMod : MelonMod
    {
        internal static MelonPreferences_Entry<bool> PEnabled, PReplaceVoice, PWingsArms, PTint, PShadow, PFlip, PFlipChecked;
        internal static MelonPreferences_Entry<float> PSize, PHeight, PVolume, PCooEvery;
        internal static MelonPreferences_Entry<string> PShader;

        // prog gwaltownej zmiany predkosci klatki piersiowej (m/s^2), od ktorego uznajemy uderzenie
        internal static float HitThreshold = 160f;

        private PigeonAssets A;
        private bool assetsTried;
        private readonly Dictionary<int, PigeonObj> pigeons = new Dictionary<int, PigeonObj>();
        private readonly Dictionary<int, float> retryAt = new Dictionary<int, float>();
        private readonly Dictionary<int, int> failCount = new Dictionary<int, int>();

        private Type actorType;
        private bool actorTypeSearched;
        private float scanTimer;
        private float sceneTime;
        private bool errLogged, guiErrLogged, lateErrLogged;

        private bool menuOpen;
        private float lastMenuToggle = -10f, lastEnableToggle = -10f;
        private string shaderLabel = "";
        private int voteFor, voteAgainst;

        // ---------- Start ----------

        public override void OnApplicationStart()
        {
            var cat = MelonPreferences.CreateCategory("GBPigeons", "Gang Beasts Pigeons");
            PEnabled = cat.CreateEntry("Enabled", true);
            PSize = cat.CreateEntry("PigeonSize", 1.0f);
            PHeight = cat.CreateEntry("HeightOffset", 0f);
            PVolume = cat.CreateEntry("Volume", 0.8f);
            PCooEvery = cat.CreateEntry("CooEverySeconds", 6f);
            PReplaceVoice = cat.CreateEntry("ReplaceCharacterVoices", true);
            PWingsArms = cat.CreateEntry("ArmsControlWings", true);
            PTint = cat.CreateEntry("TintByPlayerColor", true);
            PShadow = cat.CreateEntry("CastShadow", true);
            PFlip = cat.CreateEntry("FlipForward", false);
            PFlipChecked = cat.CreateEntry("ForwardAutoChecked", false);
            PShader = cat.CreateEntry("ShaderName", "");
            LoggerInstance.Msg("Gang Beasts Pigeons 1.0 zaladowany. P (lub F8) = menu, F9 = wlacz/wylacz golebie.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            DestroyAll();
            retryAt.Clear();
            failCount.Clear();
            scanTimer = 0f;
            sceneTime = Time.time;
            LoggerInstance.Msg("Scena: " + sceneName);
        }

        public override void OnApplicationQuit()
        {
            DestroyAll();
        }

        private void DestroyAll()
        {
            foreach (var kv in pigeons) { try { kv.Value.Destroy(); } catch { } }
            pigeons.Clear();
        }

        // ---------- Petla ----------

        public override void OnUpdate()
        {
            try
            {
                if (Inp.KeyDown(KeyCode.P) || Inp.KeyDown(KeyCode.F8)) ToggleMenu();
                if (Inp.KeyDown(KeyCode.F9)) ToggleEnabled();
                if (menuOpen)
                {
                    try { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } catch { }
                    if (Inp.KeyDown(KeyCode.Escape)) menuOpen = false;
                }

                if (A == null)
                {
                    // silnik musi juz dzialac i sceny byc zaladowane, zeby zebrac shadery z gry
                    if (!assetsTried && Time.frameCount > 90) LoadAssets();
                    return;
                }
                if (!A.Ok) return;

                if (!PEnabled.Value)
                {
                    if (pigeons.Count > 0) DestroyAll();
                    return;
                }

                scanTimer -= Time.unscaledDeltaTime;
                if (scanTimer <= 0f)
                {
                    scanTimer = 1.2f;
                    ScanActors();
                }
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

        // golebie sa ustawiane po fizyce i animacjach gry, zeby nie "drzaly"
        public override void OnLateUpdate()
        {
            try
            {
                if (A == null || !A.Ok || pigeons.Count == 0) return;
                float dt = Time.deltaTime;
                if (dt <= 0f) return;
                List<int> dead = null;
                foreach (var kv in pigeons)
                {
                    var p = kv.Value;
                    try { p.Tick(dt); }
                    catch (Exception e)
                    {
                        if (!lateErrLogged) { lateErrLogged = true; LoggerInstance.Error("Blad animacji golebia (kolejne takie same pomijam): " + e); }
                    }
                    if (p.Dead) { if (dead == null) dead = new List<int>(); dead.Add(kv.Key); }
                }
                if (dead != null)
                    foreach (int id in dead)
                    {
                        try { pigeons[id].Destroy(); } catch { }
                        pigeons.Remove(id);
                    }
            }
            catch (Exception e)
            {
                if (!lateErrLogged) { lateErrLogged = true; LoggerInstance.Error("Blad w OnLateUpdate: " + e); }
            }
        }

        private void LoadAssets()
        {
            assetsTried = true;
            try
            {
                A = PigeonAssets.Load(PShader.Value);
                shaderLabel = A.CurrentShaderName;
                LoggerInstance.Msg(A.Ok ? "Zasoby golebia gotowe." : "Zasoby golebia NIE zaladowaly sie - sprawdz log powyzej.");
            }
            catch (Exception e)
            {
                A = new PigeonAssets();
                LoggerInstance.Error("Blad ladowania zasobow: " + e);
            }
        }

        internal float VolumeScale()
        {
            return Mathf.Clamp01(PVolume.Value) * Mathf.Clamp(2.2f / Mathf.Sqrt(Mathf.Max(1, pigeons.Count)), 0.35f, 1f);
        }

        // Glosowanie nad kierunkiem "przodu": gdy golab konsekwentnie idzie tylem, odwracamy go (tylko raz, potem zapamietane)
        internal void Vote(int v)
        {
            if (PFlipChecked.Value) return;
            if (v > 0) voteFor++;
            else if (v < 0) voteAgainst++;
            int total = voteFor + voteAgainst;
            if (total < 30) return;
            if (voteAgainst >= 2 * voteFor)
            {
                PFlip.Value = !PFlip.Value;
                PFlipChecked.Value = true;
                SavePrefs();
                LoggerInstance.Msg("Golebie szly tylem (" + voteAgainst + " vs " + voteFor + ") - odwracam przod. Mozna to zmienic w menu.");
            }
            else if (voteFor >= 2 * voteAgainst)
            {
                PFlipChecked.Value = true;
                SavePrefs();
                LoggerInstance.Msg("Kierunek przodu golebi potwierdzony (" + voteFor + " vs " + voteAgainst + ").");
            }
            else { voteFor = voteFor / 2; voteAgainst = voteAgainst / 2; }
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
                        : "Nie znaleziono klasy 'Actor' - nie ma kogo zamieniac w golebie.");
                }
                if (actorType == null) return;

                var alive = new HashSet<int>();
                float now = Time.time;
                foreach (var o in Il2.FindAll(actorType))
                {
                    var c = o as Component;
                    if (c == null) continue;
                    int id = c.GetInstanceID();
                    alive.Add(id);
                    if (pigeons.ContainsKey(id)) continue;
                    if (now - sceneTime < 0.8f) continue;
                    float at;
                    if (retryAt.TryGetValue(id, out at) && now < at) continue;
                    int fails;
                    failCount.TryGetValue(id, out fails);
                    if (fails >= 3) continue;

                    PigeonObj p = null;
                    try { p = PigeonObj.Create(this, A, c); }
                    catch (Exception e) { LoggerInstance.Error("Blad tworzenia golebia: " + e); }
                    if (p == null)
                    {
                        failCount[id] = fails + 1;
                        retryAt[id] = now + 6f;
                    }
                    else pigeons[id] = p;
                }

                var gone = new List<int>();
                foreach (var kv in pigeons)
                    if (!alive.Contains(kv.Key) || kv.Value.Actor == null) gone.Add(kv.Key);
                foreach (int id in gone)
                {
                    try { pigeons[id].Destroy(); } catch { }
                    pigeons.Remove(id);
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

        // ---------- Menu (P / F8) ----------

        private void ToggleMenu()
        {
            if (Time.unscaledTime - lastMenuToggle < 0.25f) return;     // trzy drogi wykrywania klawisza - bez podwojnego przelaczania
            lastMenuToggle = Time.unscaledTime;
            menuOpen = !menuOpen;
            LoggerInstance.Msg("Menu golebi: " + (menuOpen ? "otwarte" : "zamkniete"));
        }

        private void ToggleEnabled()
        {
            if (Time.unscaledTime - lastEnableToggle < 0.25f) return;
            lastEnableToggle = Time.unscaledTime;
            SetEnabled(!PEnabled.Value);
        }

        private void SetEnabled(bool on)
        {
            PEnabled.Value = on;
            SavePrefs();
            if (!on) DestroyAll();
            else { scanTimer = 0f; retryAt.Clear(); failCount.Clear(); }
            LoggerInstance.Msg("Golebie: " + (on ? "WLACZONE" : "WYLACZONE"));
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
                if (e != null && e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.P || e.keyCode == KeyCode.F8) ToggleMenu();
                    else if (e.keyCode == KeyCode.F9) ToggleEnabled();
                }
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
            const float W = 450f, H = 470f;
            float x = (vw - W) * 0.5f, y = 20f;

            GUI.Box(new Rect(x, y, W, H), "GOLEBIE   (P / F8 / Esc = zamknij)");

            GUI.backgroundColor = PEnabled.Value ? Color.green : Color.red;
            if (GUI.Button(new Rect(x + 14, y + 28, W - 28, 32), "Golebie: " + (PEnabled.Value ? "WLACZONE  (F9)" : "WYLACZONE  (F9)")))
                SetEnabled(!PEnabled.Value);
            GUI.backgroundColor = oldBg;

            Row(x, y + 70, "Rozmiar golebia", "x" + PSize.Value.ToString("0.00"),
                () => { PSize.Value = Mathf.Max(0.3f, PSize.Value * 0.92f); SavePrefs(); },
                () => { PSize.Value = Mathf.Min(3f, PSize.Value * 1.08f); SavePrefs(); });

            Row(x, y + 106, "Wysokosc nad ziemia", (PHeight.Value >= 0 ? "+" : "") + PHeight.Value.ToString("0.00"),
                () => { PHeight.Value = Mathf.Max(-0.5f, PHeight.Value - 0.02f); SavePrefs(); },
                () => { PHeight.Value = Mathf.Min(0.5f, PHeight.Value + 0.02f); SavePrefs(); });

            Row(x, y + 142, "Glosnosc", Mathf.RoundToInt(PVolume.Value * 100f) + "%",
                () => { PVolume.Value = Mathf.Max(0f, PVolume.Value - 0.1f); SavePrefs(); },
                () => { PVolume.Value = Mathf.Min(1f, PVolume.Value + 0.1f); SavePrefs(); });

            Row(x, y + 178, "Gruchanie co ok.", PCooEvery.Value.ToString("0") + " s",
                () => { PCooEvery.Value = Mathf.Min(60f, PCooEvery.Value + 1f); SavePrefs(); },
                () => { PCooEvery.Value = Mathf.Max(2f, PCooEvery.Value - 1f); SavePrefs(); });

            // przelaczniki
            Toggle(x + 14, y + 220, 202, "Glosy postaci -> golab", PReplaceVoice, null);
            Toggle(x + 232, y + 220, 202, "Rece = skrzydla", PWingsArms, null);
            Toggle(x + 14, y + 256, 202, "Kolor wg gracza", PTint, () => { foreach (var kv in pigeons) kv.Value.Retint(); });
            Toggle(x + 232, y + 256, 202, "Cien golebia", PShadow, () => { foreach (var kv in pigeons) kv.Value.ApplyMaterial(); });

            if (GUI.Button(new Rect(x + 14, y + 296, 202, 30), "Odwroc przod (gdy tylem)"))
            {
                PFlip.Value = !PFlip.Value;
                PFlipChecked.Value = true;
                SavePrefs();
            }
            if (GUI.Button(new Rect(x + 232, y + 296, 202, 30), "Zagruchaj!"))
                foreach (var kv in pigeons) kv.Value.TestCoo();

            // material (gdyby golab byl niewidoczny / dziwnie wygladal)
            GUI.Label(new Rect(x + 14, y + 336, W - 28, 20), "Material: " + shaderLabel);
            if (GUI.Button(new Rect(x + 14, y + 358, W - 28, 30), "Zmien material (jesli golab jest niewidoczny lub dziwny)"))
            {
                if (A != null && A.Ok)
                {
                    shaderLabel = A.CycleShader();
                    PShader.Value = shaderLabel;
                    foreach (var kv in pigeons) kv.Value.ApplyMaterial();
                    SavePrefs();
                    LoggerInstance.Msg("Material golebi: " + shaderLabel);
                }
            }

            GUI.Label(new Rect(x + 14, y + 396, W - 28, 20), "Wlasne dzwieki: Mods/GangBeastsPigeons/coo1.wav, hurt1.wav ...");
            GUI.Label(new Rect(x + 14, y + 414, W - 28, 20), "Golebi na scenie: " + pigeons.Count);
            if (GUI.Button(new Rect(x + 14, y + 436, W - 28, 28), "Zamknij")) menuOpen = false;

            GUI.matrix = oldM;
            GUI.backgroundColor = oldBg;
        }

        private static void Toggle(float x, float y, float w, string label, MelonPreferences_Entry<bool> e, Action after)
        {
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = e.Value ? Color.green : Color.red;
            if (GUI.Button(new Rect(x, y, w, 30), label + ": " + (e.Value ? "TAK" : "NIE")))
            {
                e.Value = !e.Value;
                SavePrefs();
                if (after != null) after();
            }
            GUI.backgroundColor = old;
        }

        private static void Row(float x, float y, string label, string value, Action minus, Action plus)
        {
            GUI.Label(new Rect(x + 14, y + 4, 160, 22), label);
            if (GUI.Button(new Rect(x + 180, y, 34, 28), "-")) minus();
            GUI.Label(new Rect(x + 222, y + 4, 90, 22), value);
            if (GUI.Button(new Rect(x + 316, y, 34, 28), "+")) plus();
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
            { KeyCode.P, "pKey" },
            { KeyCode.F8, "f8Key" },
            { KeyCode.F9, "f9Key" },
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
