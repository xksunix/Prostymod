using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(GangBeastsJiggle.JiggleMod), "Gang Beasts Jiggle", "3.0.0", "xksunix")]

namespace GangBeastsJiggle
{
    public class JiggleMod : MelonMod
    {
        internal static MelonPreferences_Entry<bool> Enabled;
        internal static MelonPreferences_Entry<string> ForceBone;
        internal static MelonPreferences_Entry<string> IncludePatterns;
        internal static MelonPreferences_Entry<string> ExcludePatterns;
        internal static MelonPreferences_Entry<float> BaseScale;
        internal static MelonPreferences_Entry<float> JiggleScale;
        internal static MelonPreferences_Entry<float> PositionJiggle;
        internal static MelonPreferences_Entry<float> Stiffness;
        internal static MelonPreferences_Entry<float> Damping;
        internal static MelonPreferences_Entry<float> Reaction;
        internal static MelonPreferences_Entry<float> MaxOffset;
        internal static MelonPreferences_Entry<float> CheekBack;
        internal static MelonPreferences_Entry<float> CheekUp;

        internal static JiggleMod Instance;
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly HashSet<string> _dumped = new HashSet<string>();
        private float _nextScan;
        private string _logPath;
        private bool _diag;
        private int _errors;

        public override void OnInitializeMelon()
        {
            Instance = this;
            var cat = MelonPreferences.CreateCategory("GangBeastsJiggleV3", "Gang Beasts Jiggle");
            Enabled = cat.CreateEntry("Enabled", true, "Wlacza / wylacza moda");
            ForceBone = cat.CreateEntry("ForceBone", "",
                "Opcjonalnie: dokladna nazwa (lub fragment) kosci, ktora ma byc 'pupa'. Puste = auto");
            IncludePatterns = cat.CreateEntry("BonePatterns", "butt,ass,bum,pelvis,hips,hip",
                "Kolejnosc szukania kosci wg nazwy (pierwszy pasujacy wzorzec wygrywa)");
            ExcludePatterns = cat.CreateEntry("ExcludePatterns",
                "leg,thigh,calf,knee,foot,toe,arm,hand,finger,head,neck,eye,hat,shoulder",
                "Pomijane fragmenty nazw");
            BaseScale = cat.CreateEntry("BaseScale", 1.7f, "Staly rozmiar pupy (1 = bez zmian, wiecej = wieksza)");
            JiggleScale = cat.CreateEntry("JiggleScale", 0.5f, "Jak mocno pupa pulsuje podczas drgan");
            PositionJiggle = cat.CreateEntry("PositionJiggle", 0.3f, "Jak mocno pupa sie przesuwa (0 = wylaczone)");
            Stiffness = cat.CreateEntry("Stiffness", 70f, "Sztywnosc sprezyny");
            Damping = cat.CreateEntry("Damping", 4f, "Tlumienie");
            Reaction = cat.CreateEntry("Reaction", 1.0f, "Reakcja na ruch postaci");
            MaxOffset = cat.CreateEntry("MaxOffset", 1.0f, "Maksymalne wychylenie sprezyny");
            CheekBack = cat.CreateEntry("CheekBack", 0.5f, "Jak daleko za biodrami sa posladki (w jednostkach glowy)");
            CheekUp = cat.CreateEntry("CheekUp", -0.1f, "Wysokosc posladkow wzgledem bioder (w jednostkach glowy)");
            try
            {
                _logPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "GangBeastsJiggle_log.txt");
                File.WriteAllText(_logPath, "Gang Beasts Jiggle 3.0.0\r\n");
            }
            catch { _logPath = null; }
            Log("Zaladowany. Log: " + _logPath);
            try { PoopFeature.Init(LoggerInstance); }
            catch (Exception e) { Fail("PoopInit", e); }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            try { PoopFeature.SceneLoaded(); } catch { }
        }

        internal void Log(string msg)
        {
            LoggerInstance.Msg(msg);
            if (_logPath == null) return;
            try { File.AppendAllText(_logPath, msg + "\r\n"); } catch { }
        }

        internal void Fail(string where, Exception e)
        {
            if (_errors++ < 10) Log("BLAD (" + where + "): " + e);
        }

        private bool _keysBroken;
        private string _toast = "";
        private float _toastUntil;

        private void Toast(string msg)
        {
            _toast = msg;
            _toastUntil = Time.unscaledTime + 2.5f;
            Log(msg);
        }

        private static float Round(float v) { return Mathf.Round(v * 100f) / 100f; }

        // Klawisze w grze: F6 wl/wyl, F7/F8 rozmiar -/+, F9/F10 sila drgan -/+
        private void HandleKeys()
        {
            if (_keysBroken) return;
            try
            {
                if (PoopKeys.Down(KeyCode.F6))
                {
                    Enabled.Value = !Enabled.Value;
                    Toast("Jiggle: " + (Enabled.Value ? "WLACZONY" : "WYLACZONY"));
                }
                if (PoopKeys.Down(KeyCode.F7))
                {
                    BaseScale.Value = Round(Mathf.Max(0.3f, BaseScale.Value - 0.1f));
                    Toast("Rozmiar (BaseScale): " + BaseScale.Value);
                }
                if (PoopKeys.Down(KeyCode.F8))
                {
                    BaseScale.Value = Round(Mathf.Min(4f, BaseScale.Value + 0.1f));
                    Toast("Rozmiar (BaseScale): " + BaseScale.Value);
                }
                if (PoopKeys.Down(KeyCode.F9))
                {
                    JiggleScale.Value = Round(Mathf.Max(0f, JiggleScale.Value - 0.1f));
                    PositionJiggle.Value = Round(Mathf.Max(0f, PositionJiggle.Value - 0.1f));
                    Toast("Sila drgan: " + JiggleScale.Value);
                }
                if (PoopKeys.Down(KeyCode.F10))
                {
                    JiggleScale.Value = Round(Mathf.Min(3f, JiggleScale.Value + 0.1f));
                    PositionJiggle.Value = Round(Mathf.Min(2f, PositionJiggle.Value + 0.1f));
                    Toast("Sila drgan: " + JiggleScale.Value);
                }
                if (PoopKeys.Down(KeyCode.PageUp)) { CheekUp.Value = Round(CheekUp.Value + 0.05f); Toast("Pupa wyzej: " + CheekUp.Value); }
                if (PoopKeys.Down(KeyCode.PageDown)) { CheekUp.Value = Round(CheekUp.Value - 0.05f); Toast("Pupa nizej: " + CheekUp.Value); }
                if (PoopKeys.Down(KeyCode.Home)) { CheekBack.Value = Round(CheekBack.Value + 0.04f); Toast("Pupa dalej od ciala: " + CheekBack.Value); }
                if (PoopKeys.Down(KeyCode.End)) { CheekBack.Value = Round(CheekBack.Value - 0.04f); Toast("Pupa blizej ciala: " + CheekBack.Value); }
            }
            catch (Exception e)
            {
                _keysBroken = true;
                Log("Klawisze niedostepne (gra nie uzywa starego Input): " + e.Message);
            }
        }

        public override void OnApplicationQuit()
        {
            try { MelonPreferences.Save(); } catch { }
        }

        public override void OnGUI()
        {
            if (Time.unscaledTime > _toastUntil || _toast.Length == 0) return;
            try
            {
                var style = new GUIStyle(GUI.skin.label);
                style.fontSize = 22;
                style.fontStyle = FontStyle.Bold;
                style.normal.textColor = Color.white;
                GUI.Label(new Rect(20f, 20f, 700f, 40f), "[Jiggle] " + _toast, style);
            }
            catch { }
        }

        public override void OnUpdate()
        {
            try { PoopFeature.Tick(); }
            catch (Exception e) { Fail("Tick", e); }
            HandleKeys();
        }

        public override void OnLateUpdate()
        {
            PoopFeature.LateTick();
        }
    }
}
