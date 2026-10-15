using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(GangBeastsHunch.HunchMod), "Gang Beasts Hunch", "1.0.0", "Prostymod")]

namespace GangBeastsHunch
{
    public class HunchMod : MelonMod
    {
        internal static MelonPreferences_Entry<bool> Enabled;
        internal static MelonPreferences_Entry<float> ForwardSign;
        internal static MelonPreferences_Entry<float> HunchAngle;
        internal static MelonPreferences_Entry<float> ButtBack;
        internal static MelonPreferences_Entry<float> ButtScale;
        internal static MelonPreferences_Entry<float> ArmScale;

        internal static HunchMod Instance;

        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly HashSet<string> _dumped = new HashSet<string>();
        private float _nextScan;
        private string _logPath;
        private int _errors;
        private string _toast = "";
        private float _toastUntil;
        private bool _keysBroken;
        private readonly bool[] _prev = new bool[6];

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public override void OnInitializeMelon()
        {
            Instance = this;
            var cat = MelonPreferences.CreateCategory("GangBeastsHunch", "Gang Beasts Hunch");
            Enabled = cat.CreateEntry("Enabled", true, "Wlacza / wylacza moda");
            ForwardSign = cat.CreateEntry("ForwardSign", 1f, "1 lub -1: odwraca kierunek garbu, jesli jest w zla strone");
            HunchAngle = cat.CreateEntry("HunchAngle", 35f, "Kat pochylenia tulowia z glowa do przodu (stopnie)");
            ButtBack = cat.CreateEntry("ButtBack", 0.2f, "O ile tylek jest odsuniety do tylu");
            ButtScale = cat.CreateEntry("ButtScale", 1.25f, "Rozmiar tylka (1 = bez zmian)");
            ArmScale = cat.CreateEntry("ArmScale", 1.6f, "Dlugosc rak (1 = bez zmian)");
            try
            {
                _logPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "GangBeastsHunch_log.txt");
                File.WriteAllText(_logPath, "Gang Beasts Hunch 1.0.0\r\n");
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

        private void Toast(string msg)
        {
            _toast = msg;
            _toastUntil = Time.unscaledTime + 2.5f;
            Log(msg);
        }

        private static float Round(float v) { return Mathf.Round(v * 100f) / 100f; }

        private bool Pressed(int idx, int vk)
        {
            bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
            bool hit = down && !_prev[idx];
            _prev[idx] = down;
            return hit;
        }

        // F6 wl/wyl, F7 odwroc, F8/F9 kat -/+, F10/F11 rece -/+
        private void HandleKeys()
        {
            if (_keysBroken) return;
            try
            {
                if (Pressed(0, 0x75))
                {
                    Enabled.Value = !Enabled.Value;
                    Toast("Garb: " + (Enabled.Value ? "WLACZONY" : "WYLACZONY"));
                }
                if (Pressed(1, 0x76))
                {
                    ForwardSign.Value = ForwardSign.Value >= 0f ? -1f : 1f;
                    Toast("Kierunek garbu: " + ForwardSign.Value);
                }
                if (Pressed(2, 0x77))
                {
                    HunchAngle.Value = Round(Mathf.Max(0f, HunchAngle.Value - 5f));
                    Toast("Kat garbu: " + HunchAngle.Value);
                }
                if (Pressed(3, 0x78))
                {
                    HunchAngle.Value = Round(Mathf.Min(70f, HunchAngle.Value + 5f));
                    Toast("Kat garbu: " + HunchAngle.Value);
                }
                if (Pressed(4, 0x79))
                {
                    ArmScale.Value = Round(Mathf.Max(1f, ArmScale.Value - 0.1f));
                    Toast("Dlugosc rak: " + ArmScale.Value);
                }
                if (Pressed(5, 0x7A))
                {
                    ArmScale.Value = Round(Mathf.Min(3f, ArmScale.Value + 0.1f));
                    Toast("Dlugosc rak: " + ArmScale.Value);
                }
            }
            catch (Exception e)
            {
                _keysBroken = true;
                Log("Klawisze niedostepne: " + e.Message);
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
                GUI.Label(new Rect(20f, 20f, 700f, 40f), "[Garb] " + _toast, style);
            }
            catch { }
        }

        public override void OnUpdate()
        {
            HandleKeys();
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 2f;
            try { Scan(); }
            catch (Exception e) { Fail("Scan", e); }
        }

        private void Scan()
        {
            SkinnedMeshRenderer[] meshes = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer smr in meshes)
            {
                Transform[] bones = smr.bones;
                if (bones == null || bones.Length < 8) continue;

                string key = smr.name + ":" + bones.Length;
                if (_dumped.Add(key))
                {
                    var names = new List<string>();
                    foreach (Transform b in bones) if (b != null) names.Add(b.name);
                    Log("Mesh '" + smr.name + "' kosci (" + bones.Length + "): " + string.Join(", ", names.ToArray()));
                }

                Rig rig = Rig.Build(bones);
                if (rig == null || !_seen.Add(rig.Hips.GetInstanceID())) continue;
                try
                {
                    HunchBody body = rig.Hips.gameObject.AddComponent<HunchBody>();
                    body.Rig = rig;
                    Log("Garb dodany: biodro '" + rig.Hips.name + "', tulow '" + rig.Spine.name + "', rece: " + rig.ArmChains.Count
                        + ", uda: " + (rig.LeftThigh != null) + "/" + (rig.RightThigh != null));
                }
                catch (Exception e) { Fail("AddComponent", e); }
            }
        }
    }

    /// <summary>Znalezione kosci jednej postaci.</summary>
    public class Rig
    {
        public Transform Hips, Spine, LeftThigh, RightThigh;
        public List<Transform[]> ArmChains = new List<Transform[]>();

        private static bool Has(string n, string p) { return n.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0; }

        private static bool IsLeg(string n)
        {
            return Has(n, "thigh") || Has(n, "upleg") || Has(n, "leg") || Has(n, "calf") || Has(n, "foot");
        }

        private static int CountDesc(Transform t, HashSet<Transform> set)
        {
            int c = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform ch = t.GetChild(i);
                if (set.Contains(ch)) c += 1 + CountDesc(ch, set);
            }
            return c;
        }

        public static Rig Build(Transform[] bones)
        {
            var set = new HashSet<Transform>();
            foreach (Transform b in bones) if (b != null) set.Add(b);

            var r = new Rig();
            var upperArms = new List<Transform>();
            var shoulders = new List<Transform>();
            foreach (Transform b in bones)
            {
                if (b == null) continue;
                string n = b.name;
                if (r.Hips == null && (Has(n, "hips") || Has(n, "pelvis"))) { r.Hips = b; continue; }
                if (Has(n, "thigh") || Has(n, "upleg"))
                {
                    if (Has(n, "left")) r.LeftThigh = b;
                    else if (Has(n, "right")) r.RightThigh = b;
                    else if (r.LeftThigh == null) r.LeftThigh = b;
                    else if (r.RightThigh == null) r.RightThigh = b;
                    continue;
                }
                if (Has(n, "fore") || Has(n, "lower") || Has(n, "hand") || Has(n, "finger")) continue;
                if (Has(n, "arm")) upperArms.Add(b);
                else if (Has(n, "shoulder")) shoulders.Add(b);
            }
            if (r.Hips == null) return null;

            // tulow = dziecko biodra z najwieksza liczba potomkow (nie noga)
            int best = -1;
            for (int i = 0; i < r.Hips.childCount; i++)
            {
                Transform ch = r.Hips.GetChild(i);
                if (!set.Contains(ch) || IsLeg(ch.name)) continue;
                int cnt = CountDesc(ch, set);
                if (cnt > best) { best = cnt; r.Spine = ch; }
            }
            if (r.Spine == null) return null;

            List<Transform> arms = upperArms.Count > 0 ? upperArms : shoulders;
            foreach (Transform a in arms)
            {
                var chain = new List<Transform>();
                Transform cur = a;
                for (int d = 0; d < 2; d++)
                {
                    Transform next = null;
                    for (int i = 0; i < cur.childCount; i++)
                    {
                        Transform ch = cur.GetChild(i);
                        if (set.Contains(ch) && !Has(ch.name, "finger") && !Has(ch.name, "thumb")) { next = ch; break; }
                    }
                    if (next == null) break;
                    chain.Add(next);
                    cur = next;
                }
                if (chain.Count > 0) r.ArmChains.Add(chain.ToArray());
            }
            return r;
        }
    }

    /// <summary>
    /// Co klatke (LateUpdate) odsuwa biodro do tylu, pochyla tulow do przodu i wydluza rece.
    /// Na koniec klatki wszystko wraca do oryginalnych wartosci, wiec fizyka gry sie nie zmienia.
    /// </summary>
    public class HunchBody : MonoBehaviour
    {
        public Rig Rig;

        private class Saved
        {
            public Transform T;
            public Vector3 Pos, Scale;
            public Quaternion Rot;
        }

        private readonly List<Saved> _saved = new List<Saved>();
        private bool _applied, _dead;

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

        private void Save(Transform t)
        {
            if (t == null) return;
            _saved.Add(new Saved { T = t, Pos = t.localPosition, Scale = t.localScale, Rot = t.localRotation });
        }

        private void Restore()
        {
            if (!_applied) return;
            _applied = false;
            for (int i = _saved.Count - 1; i >= 0; i--)
            {
                Saved s = _saved[i];
                if (s.T == null) continue;
                s.T.localPosition = s.Pos;
                s.T.localScale = s.Scale;
                s.T.localRotation = s.Rot;
            }
            _saved.Clear();
        }

        private void LateUpdate()
        {
            if (_dead || !HunchMod.Enabled.Value) return;
            try { Step(); }
            catch (Exception e)
            {
                _dead = true;
                Restore();
                HunchMod.Instance.Fail("HunchBody", e);
            }
        }

        // Przod postaci z ulozenia ud (osie kosci w tej grze sa dowolne), awaryjnie z osi biodra
        private Vector3 Facing()
        {
            if (Rig.LeftThigh != null && Rig.RightThigh != null)
            {
                Vector3 right = Rig.RightThigh.position - Rig.LeftThigh.position;
                right.y = 0f;
                if (right.sqrMagnitude > 0.0001f)
                    return Vector3.Cross(right.normalized, Vector3.up);
            }
            return Rig.Hips.forward;
        }

        private void Step()
        {
            if (Time.deltaTime <= 0f) return;
            Restore(); // gdyby koniec klatki nie zdazyl

            Transform hips = Rig.Hips;
            Transform spine = Rig.Spine;
            if (hips == null || spine == null) { _dead = true; return; }

            Vector3 fwd = Facing() * (HunchMod.ForwardSign.Value >= 0f ? 1f : -1f);
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return;
            fwd.Normalize();
            Vector3 axis = Vector3.Cross(Vector3.up, fwd);

            // zapamietaj stan
            _saved.Clear();
            Save(hips);
            int n = hips.childCount;
            var kidWorld = new Vector3[n];
            var kidScale = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Transform ch = hips.GetChild(i);
                Save(ch);
                kidWorld[i] = ch.position;
                kidScale[i] = ch.localScale;
            }
            foreach (Transform[] chain in Rig.ArmChains)
                foreach (Transform t in chain) Save(t);
            _applied = true;

            // 1) tylek: odsun do tylu i powieksz, dzieci (nogi, tulow) zostaja na miejscu
            float s = Mathf.Max(0.1f, HunchMod.ButtScale.Value);
            hips.position = hips.position - fwd * Mathf.Max(0f, HunchMod.ButtBack.Value);
            hips.localScale = hips.localScale * s;
            for (int i = 0; i < n; i++)
            {
                Transform ch = hips.GetChild(i);
                ch.position = kidWorld[i];
                ch.localScale = kidScale[i] / s;
            }

            // 2) tulow z glowa do przodu
            float angle = Mathf.Clamp(HunchMod.HunchAngle.Value, 0f, 70f);
            spine.rotation = Quaternion.AngleAxis(angle, axis) * spine.rotation;

            // 3) dluzsze rece (wizualnie): przedramie i dlon dalej od siebie
            float arm = Mathf.Clamp(HunchMod.ArmScale.Value, 1f, 3f);
            if (arm > 1.001f)
            {
                foreach (Transform[] chain in Rig.ArmChains)
                    foreach (Transform t in chain)
                        if (t != null) t.localPosition = t.localPosition * arm;
            }
        }

        private void OnDisable()
        {
            Restore();
        }
    }
}
