using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(GangBeastsJiggle.JiggleMod), "Gang Beasts Jiggle", "1.1.0", "xksunix")]

namespace GangBeastsJiggle
{
    public class JiggleMod : MelonMod
    {
        internal static MelonPreferences_Entry<bool> Enabled;
        internal static MelonPreferences_Entry<string> IncludePatterns;
        internal static MelonPreferences_Entry<string> ExcludePatterns;
        internal static MelonPreferences_Entry<string> Mode;
        internal static MelonPreferences_Entry<float> Stiffness;
        internal static MelonPreferences_Entry<float> Damping;
        internal static MelonPreferences_Entry<float> Amplitude;
        internal static MelonPreferences_Entry<float> Reaction;
        internal static MelonPreferences_Entry<float> MaxOffset;
        internal static MelonPreferences_Entry<bool> DumpBones;

        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly HashSet<string> _dumped = new HashSet<string>();
        private float _nextScan;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("GangBeastsJiggle", "Gang Beasts Jiggle");
            Enabled = cat.CreateEntry("Enabled", true, "Wlacza / wylacza moda");
            IncludePatterns = cat.CreateEntry("BonePatterns", "butt,hip,pelvis,ass",
                "Fragmenty nazw kosci (po przecinku), ktore maja sie trzasc");
            ExcludePatterns = cat.CreateEntry("ExcludePatterns", "hand,arm,leg,foot,head,finger",
                "Kosci z tymi fragmentami w nazwie sa pomijane");
            Mode = cat.CreateEntry("Mode", "Scale", "Scale, Position albo Both");
            Stiffness = cat.CreateEntry("Stiffness", 90f, "Sztywnosc sprezyny");
            Damping = cat.CreateEntry("Damping", 5f, "Tlumienie");
            Amplitude = cat.CreateEntry("Amplitude", 0.25f, "Sila efektu");
            Reaction = cat.CreateEntry("Reaction", 0.6f, "Reakcja na ruch postaci");
            MaxOffset = cat.CreateEntry("MaxOffset", 1.0f, "Maksymalne wychylenie");
            DumpBones = cat.CreateEntry("DumpBones", true, "Zapisuje liste kosci postaci do logu");
            LoggerInstance.Msg("Gang Beasts Jiggle zaladowany.");
        }

        public override void OnUpdate()
        {
            if (!Enabled.Value || Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 2f;
            try { Scan(); }
            catch (Exception e) { LoggerInstance.Error("Scan error: " + e); }
        }

        private static string[] Split(string s)
        {
            return s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool Matches(string name, string[] pats)
        {
            string n = name.ToLowerInvariant();
            for (int i = 0; i < pats.Length; i++)
                if (n.Contains(pats[i].Trim().ToLowerInvariant())) return true;
            return false;
        }

        private void Scan()
        {
            string[] inc = Split(IncludePatterns.Value);
            string[] exc = Split(ExcludePatterns.Value);
            SkinnedMeshRenderer[] meshes = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer smr in meshes)
            {
                Transform[] bones = smr.bones;
                if (bones == null || bones.Length < 3) continue;

                if (DumpBones.Value)
                {
                    string key = smr.name + ":" + bones.Length;
                    if (_dumped.Add(key))
                    {
                        var names = new List<string>();
                        foreach (Transform b in bones) if (b != null) names.Add(b.name);
                        LoggerInstance.Msg("Mesh '" + smr.name + "' kosci (" + bones.Length + "): " + string.Join(", ", names.ToArray()));
                    }
                }

                foreach (Transform bone in bones)
                {
                    if (bone == null) continue;
                    if (!Matches(bone.name, inc) || Matches(bone.name, exc)) continue;
                    if (!_seen.Add(bone.GetInstanceID())) continue;
                    bone.gameObject.AddComponent<JiggleBone>();
                    LoggerInstance.Msg("Jiggle dodany do kosci: " + bone.name + " (mesh " + smr.name + ")");
                }
            }
        }
    }

    /// <summary>Sprezyna na kosci - efekt tylko wizualny, nie rusza fizyki.</summary>
    public class JiggleBone : MonoBehaviour
    {
        private Vector3 _lastPos, _lastVel, _offset, _springVel, _origScale, _origLocalPos;
        private bool _applied, _init;

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
                if (_applied)
                {
                    transform.localScale = _origScale;
                    transform.localPosition = _origLocalPos;
                    _applied = false;
                }
            }
        }

        private void LateUpdate()
        {
            if (!JiggleMod.Enabled.Value) return;
            float rawDt = Time.deltaTime;
            if (rawDt <= 0f) return;
            float dt = Mathf.Min(rawDt, 0.05f);

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
            float max = JiggleMod.MaxOffset.Value;
            if (_offset.magnitude > max) _offset = _offset.normalized * max;

            if (_applied) return;
            _origScale = transform.localScale;
            _origLocalPos = transform.localPosition;

            string mode = JiggleMod.Mode.Value;
            float amp = JiggleMod.Amplitude.Value;
            if (mode == "Scale" || mode == "Both")
            {
                Vector3 l = transform.InverseTransformDirection(_offset);
                transform.localScale = new Vector3(
                    _origScale.x * (1f + Mathf.Abs(l.x) * amp),
                    _origScale.y * (1f + Mathf.Abs(l.y) * amp),
                    _origScale.z * (1f + Mathf.Abs(l.z) * amp));
            }
            if (mode == "Position" || mode == "Both")
                transform.position = transform.position + _offset * amp;
            _applied = true;
        }

        private void OnDisable()
        {
            if (_applied)
            {
                transform.localScale = _origScale;
                transform.localPosition = _origLocalPos;
                _applied = false;
            }
        }
    }
}
