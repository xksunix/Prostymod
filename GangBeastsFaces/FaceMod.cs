using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(GBFaces.FaceMod), "Gang Beasts Faces", "1.3.0", "Claude")]

namespace GBFaces
{
    // Wersja pod IL2CPP: wszystkie wywolania, ktore w IL2CPP maja inny podpis niz w zwyklym Unity
    // (tablice, listy, Type), idą przez refleksję w czasie dzialania.
    public class FaceMod : MelonMod
    {
        internal static MelonPreferences_Entry<bool> PManual;  // true = osie ustawione recznie (F6/F7), false = automatyczna orientacja
        internal static MelonPreferences_Entry<int> PFwd;      // oś "przodu" twarzy w układzie głowy (0..5 = +X,-X,+Y,-Y,+Z,-Z)
        internal static MelonPreferences_Entry<int> PUp;       // oś "góry" twarzy
        internal static MelonPreferences_Entry<float> PSize;   // mnoznik wielkosci twarzy (1.0 = tyle co glowa)
        internal static MelonPreferences_Entry<float> PPush;   // jak daleko od środka głowy (promień)
        internal static MelonPreferences_Entry<float> PMouthY; // wysokość ust
        internal static MelonPreferences_Entry<float> PBrowY;  // wysokość brwi
        internal static MelonPreferences_Entry<float> PBrowX;  // odstęp brwi od środka
        internal static MelonPreferences_Entry<float> PFaceY;  // przesunięcie CAŁEJ twarzy w górę/dół (dodatnie = wyżej)
        internal static MelonPreferences_Entry<float> PHitThr; // próg przyspieszenia, od którego postać robi minę "au!"

        internal static readonly Vector3[] Axes =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };

        // Orientacja wyliczona automatycznie z pozycji kosci (ustawiana przez FaceController)
        internal static int AutoFwd = -1, AutoUp = -1;
        internal static int EffFwd { get { return (PManual.Value || AutoFwd < 0) ? PFwd.Value : AutoFwd; } }
        internal static int EffUp { get { return (PManual.Value || AutoUp < 0) ? PUp.Value : AutoUp; } }

        private readonly Dictionary<int, FaceController> faces = new Dictionary<int, FaceController>();
        private Type actorType;
        private bool actorTypeSearched;
        private bool loggedNoActor;
        private float scanTimer;
        private static MethodInfo getKeyDown;
        private static MethodInfo findObjectsMethod;
        private static bool findObjectsResolved;
        private static bool findObjectsNeedsBool;

        public override void OnApplicationStart()
        {
            var cat = MelonPreferences.CreateCategory("GBFaces2", "Gang Beasts Faces");
            PManual = cat.CreateEntry("ManualOrientation", false);
            PFwd = cat.CreateEntry("ForwardAxis", 4);
            PUp = cat.CreateEntry("UpAxis", 2);
            PSize = cat.CreateEntry("SizeMultiplier", 1.0f);
            PPush = cat.CreateEntry("Push", 0.52f);
            PMouthY = cat.CreateEntry("MouthY", -0.2f);
            PBrowY = cat.CreateEntry("BrowY", 0.12f);
            PBrowX = cat.CreateEntry("BrowX", 0.17f);
            PFaceY = cat.CreateEntry("FaceY", 0.12f);
            PHitThr = cat.CreateEntry("HitThreshold", 160f);
            LoggerInstance.Msg("Gang Beasts Faces 1.3 (IL2CPP) zaladowany. Klawisze: F6/F7 obrot twarzy, F8/F9 rozmiar, Num+/Num- cala twarz gora/dol, PgUp/PgDn usta, Ins/Del brwi, Home/End glebokosc, F10 reset, F11 przekalibruj.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            foreach (var f in faces.Values)
            {
                try { f.Destroy(); } catch { }
            }
            faces.Clear();
            scanTimer = 0f;
        }

        public override void OnUpdate()
        {
            try
            {
                HandleKeys();

                scanTimer -= Time.deltaTime;
                if (scanTimer <= 0f)
                {
                    scanTimer = actorType != null ? 1f : 3f;
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
                        try { faces[k].Destroy(); } catch { }
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
            catch (Exception e)
            {
                if (!errorLogged)
                {
                    errorLogged = true;
                    LoggerInstance.Error("Blad w OnUpdate (kolejne takie same pomijam): " + e);
                }
            }
        }

        private bool errorLogged;
        private bool scanErrorLogged;

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

                List<UnityEngine.Object> found;
                if (actorType != null)
                {
                    found = FindAll(actorType);
                }
                else
                {
                    if (!loggedNoActor)
                    {
                        loggedNoActor = true;
                        LoggerInstance.Warning("Nie znaleziono klasy 'Actor' - szukam obiektow o nazwie 'Head'. Mimika bedzie ograniczona.");
                    }
                    found = FindAll(typeof(Transform));
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
                if (!scanErrorLogged)
                {
                    scanErrorLogged = true;
                    LoggerInstance.Error("Blad skanowania postaci (kolejne takie same pomijam): " + e);
                }
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
                    if (t == null) continue;
                    if (t.Name != "Actor") continue;
                    bool isComponent;
                    try { isComponent = typeof(Component).IsAssignableFrom(t); } catch { continue; }
                    if (!isComponent) continue;
                    if (n.Contains("Assembly-CSharp")) return t;
                    if (best == null) best = t;
                }
            }
            return best;
        }

        // ---------- Sterowanie ustawieniami w grze ----------

        private static bool Key(KeyCode k)
        {
            try
            {
                if (getKeyDown == null)
                {
                    Type t = null;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (!asm.GetName().Name.StartsWith("UnityEngine")) continue;
                        t = asm.GetType("UnityEngine.Input", false);
                        if (t != null) break;
                    }
                    if (t != null) getKeyDown = t.GetMethod("GetKeyDown", new[] { typeof(KeyCode) });
                    if (getKeyDown == null) return false;
                }
                return (bool)getKeyDown.Invoke(null, new object[] { k });
            }
            catch { return false; }
        }

        // Przejscie w tryb reczny: zaczynamy od tego, co wyliczyla automatyka
        private static void EnterManual()
        {
            if (PManual.Value) return;
            int f = EffFwd, u = EffUp;
            PFwd.Value = f;
            PUp.Value = u;
            PManual.Value = true;
        }

        private void HandleKeys()
        {
            bool changed = false;

            if (Key(KeyCode.F6))
            {
                EnterManual();
                PFwd.Value = (PFwd.Value + 1) % 6;
                if (PUp.Value / 2 == PFwd.Value / 2) PUp.Value = NextUp(PFwd.Value, PUp.Value);
                changed = true;
            }
            if (Key(KeyCode.F7)) { EnterManual(); PUp.Value = NextUp(PFwd.Value, PUp.Value); changed = true; }
            if (Key(KeyCode.F8)) { PSize.Value = Mathf.Max(0.1f, PSize.Value * 0.9f); changed = true; }
            if (Key(KeyCode.F9)) { PSize.Value = Mathf.Min(5f, PSize.Value * 1.1f); changed = true; }
            if (Key(KeyCode.KeypadPlus)) { PFaceY.Value += 0.02f; changed = true; }
            if (Key(KeyCode.KeypadMinus)) { PFaceY.Value -= 0.02f; changed = true; }
            if (Key(KeyCode.PageUp)) { PMouthY.Value += 0.02f; changed = true; }
            if (Key(KeyCode.PageDown)) { PMouthY.Value -= 0.02f; changed = true; }
            if (Key(KeyCode.Insert)) { PBrowY.Value += 0.02f; changed = true; }
            if (Key(KeyCode.Delete)) { PBrowY.Value -= 0.02f; changed = true; }
            if (Key(KeyCode.Home)) { PPush.Value += 0.02f; changed = true; }
            if (Key(KeyCode.End)) { PPush.Value = Mathf.Max(0.1f, PPush.Value - 0.02f); changed = true; }
            if (Key(KeyCode.F11))
            {
                // ponowna automatyczna kalibracja (postac powinna stac prosto)
                PManual.Value = false;
                AutoFwd = -1; AutoUp = -1;
                LoggerInstance.Msg("Kalibracja orientacji twarzy od nowa.");
                changed = true;
            }
            if (Key(KeyCode.F10))
            {
                PManual.Value = false; AutoFwd = -1; AutoUp = -1;
                PFwd.Value = 4; PUp.Value = 2; PSize.Value = 1.0f; PPush.Value = 0.52f;
                PMouthY.Value = -0.2f; PBrowY.Value = 0.12f; PBrowX.Value = 0.17f;
                PFaceY.Value = 0.12f;
                changed = true;
            }

            if (changed)
            {
                LoggerInstance.Msg("Twarz: " + (PManual.Value ? "reczna" : "auto") + " przod=" + EffFwd + " gora=" + EffUp
                    + " rozmiar x" + PSize.Value.ToString("0.00")
                    + " wysokosc=" + PFaceY.Value.ToString("0.00")
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
