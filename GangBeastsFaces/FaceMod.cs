using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(GBFaces.FaceMod), "Gang Beasts Faces", "1.0.0", "Claude")]

namespace GBFaces
{
    public class FaceMod : MelonMod
    {
        // Ustawienia (zapisują się w UserData/MelonPreferences.cfg)
        internal static MelonPreferences_Entry<int> PFwd;      // oś "przodu" twarzy w układzie głowy (0..5 = +X,-X,+Y,-Y,+Z,-Z)
        internal static MelonPreferences_Entry<int> PUp;       // oś "góry" twarzy
        internal static MelonPreferences_Entry<float> PSize;   // wielkość twarzy
        internal static MelonPreferences_Entry<float> PPush;   // jak daleko od środka głowy (promień)
        internal static MelonPreferences_Entry<float> PMouthY; // wysokość ust
        internal static MelonPreferences_Entry<float> PBrowY;  // wysokość brwi
        internal static MelonPreferences_Entry<float> PBrowX;  // odstęp brwi od środka

        internal static readonly Vector3[] Axes =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };

        private readonly Dictionary<int, FaceController> faces = new Dictionary<int, FaceController>();
        private Type actorType;
        private bool actorTypeSearched;
        private bool loggedNoActor;
        private float scanTimer;
        private static MethodInfo getKeyDown;

        public override void OnApplicationStart()
        {
            var cat = MelonPreferences.CreateCategory("GBFaces", "Gang Beasts Faces");
            PFwd = cat.CreateEntry("ForwardAxis", 4);
            PUp = cat.CreateEntry("UpAxis", 2);
            PSize = cat.CreateEntry("Size", 0.4f);
            PPush = cat.CreateEntry("Push", 0.5f);
            PMouthY = cat.CreateEntry("MouthY", -0.2f);
            PBrowY = cat.CreateEntry("BrowY", 0.12f);
            PBrowX = cat.CreateEntry("BrowX", 0.17f);
            LoggerInstance.Msg("Gang Beasts Faces zaladowany. Klawisze: F6/F7 obrot twarzy, F8/F9 rozmiar, PgUp/PgDn usta, Ins/Del brwi, Home/End glebokosc, F10 reset.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            faces.Clear();
            scanTimer = 0f;
        }

        public override void OnUpdate()
        {
            HandleKeys();

            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = 1f;
                Scan();
            }

            List<int> dead = null;
            List<FaceController> justKnocked = null;
            foreach (var kv in faces)
            {
                var f = kv.Value;
                if (!f.IsAlive())
                {
                    if (dead == null) dead = new List<int>();
                    dead.Add(kv.Key);
                    continue;
                }
                bool newKo = f.Tick(Time.deltaTime);
                if (newKo)
                {
                    if (justKnocked == null) justKnocked = new List<FaceController>();
                    justKnocked.Add(f);
                }
            }
            if (dead != null)
                foreach (var k in dead)
                {
                    faces[k].Destroy();
                    faces.Remove(k);
                }

            // Gdy ktos dostaje nokaut, reszta sie smieje
            if (justKnocked != null)
            {
                foreach (var f in faces.Values)
                {
                    if (justKnocked.Contains(f)) continue;
                    if (UnityEngine.Random.value < 0.7f)
                        f.Laugh = 2f + UnityEngine.Random.value * 1.5f;
                }
            }
        }

        private void Scan()
        {
            try
            {
                if (!actorTypeSearched)
                {
                    actorTypeSearched = true;
                    actorType = FindActorType();
                    if (actorType != null)
                        LoggerInstance.Msg("Znaleziono typ postaci: " + actorType.FullName);
                }

                UnityEngine.Object[] found;
                if (actorType != null)
                {
                    found = UnityEngine.Object.FindObjectsOfType(actorType);
                }
                else
                {
                    if (!loggedNoActor)
                    {
                        loggedNoActor = true;
                        LoggerInstance.Warning("Nie znaleziono klasy 'Actor' - uzywam obiektow o nazwie 'Head'. Mimika bedzie ograniczona.");
                    }
                    found = UnityEngine.Object.FindObjectsOfType(typeof(Transform));
                }

                foreach (var o in found)
                {
                    var comp = o as Component;
                    if (comp == null) continue;
                    if (actorType == null && !string.Equals(comp.name, "Head", StringComparison.OrdinalIgnoreCase)) continue;

                    int id = comp.GetInstanceID();
                    if (faces.ContainsKey(id)) continue;

                    var fc = FaceController.Create(comp, actorType != null, LoggerInstance);
                    if (fc != null) faces[id] = fc;
                }
            }
            catch (Exception e)
            {
                LoggerInstance.Error("Blad skanowania postaci: " + e);
            }
        }

        private static Type FindActorType()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = asm.GetName().Name;
                if (n.StartsWith("UnityEngine") || n.StartsWith("System") || n.StartsWith("mscorlib")
                    || n.StartsWith("MelonLoader") || n.StartsWith("Mono.") || n.StartsWith("0Harmony"))
                    continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (t.Name == "Actor" && typeof(Component).IsAssignableFrom(t)) return t;
                }
            }
            return null;
        }

        // ---------- Sterowanie ustawieniami w grze ----------

        private static bool Key(KeyCode k)
        {
            try
            {
                if (getKeyDown == null)
                {
                    var t = Type.GetType("UnityEngine.Input, UnityEngine.InputLegacyModule")
                            ?? Type.GetType("UnityEngine.Input, UnityEngine.CoreModule")
                            ?? Type.GetType("UnityEngine.Input, UnityEngine");
                    if (t != null) getKeyDown = t.GetMethod("GetKeyDown", new[] { typeof(KeyCode) });
                }
                return getKeyDown != null && (bool)getKeyDown.Invoke(null, new object[] { k });
            }
            catch { return false; }
        }

        private void HandleKeys()
        {
            bool changed = false;

            if (Key(KeyCode.F6)) { PFwd.Value = (PFwd.Value + 1) % 6; if (PUp.Value / 2 == PFwd.Value / 2) PUp.Value = NextUp(PFwd.Value, PUp.Value); changed = true; }
            if (Key(KeyCode.F7)) { PUp.Value = NextUp(PFwd.Value, PUp.Value); changed = true; }
            if (Key(KeyCode.F8)) { PSize.Value = Mathf.Max(0.05f, PSize.Value * 0.9f); changed = true; }
            if (Key(KeyCode.F9)) { PSize.Value = Mathf.Min(3f, PSize.Value * 1.1f); changed = true; }
            if (Key(KeyCode.PageUp)) { PMouthY.Value += 0.02f; changed = true; }
            if (Key(KeyCode.PageDown)) { PMouthY.Value -= 0.02f; changed = true; }
            if (Key(KeyCode.Insert)) { PBrowY.Value += 0.02f; changed = true; }
            if (Key(KeyCode.Delete)) { PBrowY.Value -= 0.02f; changed = true; }
            if (Key(KeyCode.Home)) { PPush.Value += 0.02f; changed = true; }
            if (Key(KeyCode.End)) { PPush.Value = Mathf.Max(0.1f, PPush.Value - 0.02f); changed = true; }
            if (Key(KeyCode.F10))
            {
                PFwd.Value = 4; PUp.Value = 2; PSize.Value = 0.4f; PPush.Value = 0.5f;
                PMouthY.Value = -0.2f; PBrowY.Value = 0.12f; PBrowX.Value = 0.17f;
                changed = true;
            }

            if (changed)
            {
                LoggerInstance.Msg("Twarz: przod=" + PFwd.Value + " gora=" + PUp.Value + " rozmiar=" + PSize.Value.ToString("0.00")
                    + " usta=" + PMouthY.Value.ToString("0.00") + " brwi=" + PBrowY.Value.ToString("0.00")
                    + " glebokosc=" + PPush.Value.ToString("0.00"));
                try { MelonPreferences.Save(); } catch { }
            }
        }

        // Nastepna os "gory", pomijajac os rownolegla do "przodu"
        private static int NextUp(int fwd, int up)
        {
            for (int i = 1; i <= 6; i++)
            {
                int c = (up + i) % 6;
                if (c / 2 != fwd / 2) return c;
            }
            return up;
        }
    }
}
