using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(GangBeastsJiggle.JiggleMod), "Gang Beasts Jiggle", "2.1.0", "xksunix")]

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
            try
            {
                _logPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "GangBeastsJiggle_log.txt");
                File.WriteAllText(_logPath, "Gang Beasts Jiggle 2.1.0\r\n");
            }
            catch { _logPath = null; }
            Log("Zaladowany. Log: " + _logPath);
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
                if (Input.GetKeyDown(KeyCode.F6))
                {
                    Enabled.Value = !Enabled.Value;
                    Toast("Jiggle: " + (Enabled.Value ? "WLACZONY" : "WYLACZONY"));
                }
                if (Input.GetKeyDown(KeyCode.F7))
                {
                    BaseScale.Value = Round(Mathf.Max(1f, BaseScale.Value - 0.1f));
                    Toast("Rozmiar (BaseScale): " + BaseScale.Value);
                }
                if (Input.GetKeyDown(KeyCode.F8))
                {
                    BaseScale.Value = Round(Mathf.Min(4f, BaseScale.Value + 0.1f));
                    Toast("Rozmiar (BaseScale): " + BaseScale.Value);
                }
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    JiggleScale.Value = Round(Mathf.Max(0f, JiggleScale.Value - 0.1f));
                    PositionJiggle.Value = Round(Mathf.Max(0f, PositionJiggle.Value - 0.1f));
                    Toast("Sila drgan: " + JiggleScale.Value);
                }
                if (Input.GetKeyDown(KeyCode.F10))
                {
                    JiggleScale.Value = Round(Mathf.Min(3f, JiggleScale.Value + 0.1f));
                    PositionJiggle.Value = Round(Mathf.Min(2f, PositionJiggle.Value + 0.1f));
                    Toast("Sila drgan: " + JiggleScale.Value);
                }
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
            HandleKeys();
            if (!Enabled.Value || Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 2f;
            try { Scan(); }
            catch (Exception e) { Fail("Scan", e); }
        }

        private static string[] Split(string s)
        {
            return s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool Has(string name, string pat)
        {
            return name.IndexOf(pat.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool Excluded(string name, string[] exc)
        {
            for (int i = 0; i < exc.Length; i++) if (Has(name, exc[i])) return true;
            return false;
        }

        // Wybiera JEDNA kosc "pupy" dla szkieletu.
        private Transform PickBone(Transform[] bones, string[] inc, string[] exc, out string how)
        {
            how = "";
            string force = ForceBone.Value;
            if (!string.IsNullOrEmpty(force) && force.Trim().Length > 0)
            {
                foreach (Transform b in bones)
                    if (b != null && Has(b.name, force)) { how = "ForceBone"; return b; }
            }

            for (int p = 0; p < inc.Length; p++)
            {
                foreach (Transform b in bones)
                {
                    if (b == null || Excluded(b.name, exc)) continue;
                    if (Has(b.name, inc[p])) { how = "nazwa '" + inc[p].Trim() + "'"; return b; }
                }
            }

            if (bones.Length < 8) return null;

            // zapas: miednica = pierwsza kosc, w ktorej szkielet sie rozgalezia (kregoslup + nogi)
            var set = new HashSet<Transform>();
            foreach (Transform b in bones) if (b != null) set.Add(b);

            Transform root = null; int bestCount = -1;
            foreach (Transform b in bones)
            {
                if (b == null) continue;
                if (b.parent != null && set.Contains(b.parent)) continue;
                int cnt = CountDesc(b, set);
                if (cnt > bestCount) { bestCount = cnt; root = b; }
            }
            if (root == null) return null;

            Transform cur = root;
            for (int guard = 0; guard < 20; guard++)
            {
                var kids = new List<Transform>();
                for (int i = 0; i < cur.childCount; i++)
                {
                    Transform c = cur.GetChild(i);
                    if (set.Contains(c) && CountDesc(c, set) >= 0) kids.Add(c);
                }
                if (kids.Count >= 2 || kids.Count == 0) break;
                cur = kids[0];
            }
            how = "rozgalezienie szkieletu";
            return cur;
        }

        private static int CountDesc(Transform t, HashSet<Transform> set)
        {
            int n = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (set.Contains(c)) n += 1 + CountDesc(c, set);
            }
            return n;
        }

        private void Scan()
        {
            string[] inc = Split(IncludePatterns.Value);
            string[] exc = Split(ExcludePatterns.Value);
            SkinnedMeshRenderer[] meshes = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();

            if (!_diag && meshes.Length > 0)
            {
                _diag = true;
                Log("DIAG: SkinnedMeshRenderer w scenie = " + meshes.Length);
            }

            foreach (SkinnedMeshRenderer smr in meshes)
            {
                Transform[] bones = smr.bones;
                if (bones == null || bones.Length < 3) continue;

                string key = smr.name + ":" + bones.Length;
                if (_dumped.Add(key))
                {
                    var names = new List<string>();
                    foreach (Transform b in bones) if (b != null) names.Add(b.name);
                    Log("Mesh '" + smr.name + "' kosci (" + bones.Length + "): " + string.Join(", ", names.ToArray()));
                }

                string how;
                Transform pick = PickBone(bones, inc, exc, out how);
                if (pick == null || !_seen.Add(pick.GetInstanceID())) continue;
                try
                {
                    pick.gameObject.AddComponent<JiggleBone>();
                    Log("Jiggle dodany do kosci '" + pick.name + "' (" + how + ", mesh " + smr.name + ", dzieci: " + pick.childCount + ")");
                }
                catch (Exception e) { Fail("AddComponent", e); }
            }
        }
    }

    /// <summary>
    /// Powieksza kosc (pupa) i wprawia ja w sprezyste drgania. Dzieci kosci (nogi, kregoslup) sa
    /// kompensowane, wiec rosnie tylko okolica miednicy. Efekt trwa tylko do konca klatki.
    /// </summary>
    public class JiggleBone : MonoBehaviour
    {
        private Vector3 _lastPos, _lastVel, _offset, _springVel;
        private Vector3 _origLocalScale, _origLocalPos;
        private Transform[] _kids = new Transform[0];
        private Vector3[] _kidWorldPos = new Vector3[0];
        private Vector3[] _kidLocalPos = new Vector3[0];
        private Vector3[] _kidLocalScale = new Vector3[0];
        private bool _applied, _init, _dead;

        private void OnEnable()
        {
            StartCoroutine(RestoreLoop());
        }

        private IEnumerator RestoreLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                Restore();
            }
        }

        private void Restore()
        {
            if (!_applied) return;
            _applied = false;
            transform.localScale = _origLocalScale;
            transform.localPosition = _origLocalPos;
            for (int i = 0; i < _kids.Length; i++)
            {
                if (_kids[i] == null) continue;
                _kids[i].localPosition = _kidLocalPos[i];
                _kids[i].localScale = _kidLocalScale[i];
            }
        }

        private void LateUpdate()
        {
            if (_dead || !JiggleMod.Enabled.Value) return;
            try { Step(); }
            catch (Exception e)
            {
                _dead = true;
                Restore();
                JiggleMod.Instance.Fail("JiggleBone", e);
            }
        }

        private void Step()
        {
            float rawDt = Time.deltaTime;
            if (rawDt <= 0f) return;
            float dt = Mathf.Min(rawDt, 0.05f);

            Restore(); // gdyby koniec klatki nie zdazyl

            Vector3 pos = transform.position;
            if (!_init) { _lastPos = pos; _lastVel = Vector3.zero; _init = true; }
            Vector3 vel = (pos - _lastPos) / rawDt;
            Vector3 acc = (vel - _lastVel) / rawDt;
            _lastPos = pos; _lastVel = vel;

            float k = JiggleMod.Stiffness.Value;
            float c = JiggleMod.Damping.Value;
            Vector3 force = -acc * 0.01f * JiggleMod.Reaction.Value - k * _offset - c * _springVel;
            _springVel += force * dt;
            _offset += _springVel * dt;
            float max = Mathf.Max(0.01f, JiggleMod.MaxOffset.Value);
            if (_offset.magnitude > max) _offset = _offset.normalized * max;
            if (float.IsNaN(_offset.x) || float.IsNaN(_offset.y) || float.IsNaN(_offset.z))
            {
                _offset = Vector3.zero; _springVel = Vector3.zero; return;
            }

            // zapamietaj stan dzieci
            int n = transform.childCount;
            if (_kids.Length != n)
            {
                _kids = new Transform[n];
                _kidWorldPos = new Vector3[n];
                _kidLocalPos = new Vector3[n];
                _kidLocalScale = new Vector3[n];
            }
            for (int i = 0; i < n; i++)
            {
                Transform ch = transform.GetChild(i);
                _kids[i] = ch;
                _kidWorldPos[i] = ch.position;
                _kidLocalPos[i] = ch.localPosition;
                _kidLocalScale[i] = ch.localScale;
            }
            _origLocalScale = transform.localScale;
            _origLocalPos = transform.localPosition;

            float s = Mathf.Max(0.1f, JiggleMod.BaseScale.Value + JiggleMod.JiggleScale.Value * _offset.magnitude);
            transform.localScale = _origLocalScale * s;
            transform.position = pos + _offset * JiggleMod.PositionJiggle.Value;

            for (int i = 0; i < _kids.Length; i++)
            {
                if (_kids[i] == null) continue;
                _kids[i].position = _kidWorldPos[i];
                _kids[i].localScale = _kidLocalScale[i] / s;
            }
            _applied = true;
        }

        private void OnDisable()
        {
            Restore();
        }
    }
}
