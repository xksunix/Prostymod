using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace GangBeastsJiggle
{
    [BepInPlugin("xksunix.gangbeasts.jiggle", "Gang Beasts Jiggle", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> IncludePatterns;
        internal static ConfigEntry<string> ExcludePatterns;
        internal static ConfigEntry<float> Stiffness;
        internal static ConfigEntry<float> Damping;
        internal static ConfigEntry<float> Amplitude;
        internal static ConfigEntry<float> Reaction;
        internal static ConfigEntry<float> MaxOffset;
        internal static ConfigEntry<string> Mode;
        internal static ConfigEntry<bool> DumpBones;

        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly HashSet<string> _dumped = new HashSet<string>();
        private float _nextScan;

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("General", "Enabled", true, "Wlacza / wylacza moda.");
            IncludePatterns = Config.Bind("General", "BonePatterns", "butt,hip,pelvis,ass",
                "Fragmenty nazw kosci (po przecinku, bez rozrozniania wielkosci liter), ktore maja sie trzasc.");
            ExcludePatterns = Config.Bind("General", "ExcludePatterns", "hand,arm,leg,foot,head,finger",
                "Kosci z tymi fragmentami w nazwie sa pomijane.");
            Mode = Config.Bind("Jiggle", "Mode", "Scale",
                "Scale = ugniatanie/rozciaganie kosci, Position = przesuwanie kosci, Both = jedno i drugie.");
            Stiffness = Config.Bind("Jiggle", "Stiffness", 90f, "Sztywnosc sprezyny (wiecej = szybsze drgania).");
            Damping = Config.Bind("Jiggle", "Damping", 5f, "Tlumienie (wiecej = szybciej sie uspokaja).");
            Amplitude = Config.Bind("Jiggle", "Amplitude", 0.25f, "Sila efektu (skala / przesuniecie).");
            Reaction = Config.Bind("Jiggle", "Reaction", 0.6f, "Jak mocno ruch postaci wprawia kosc w drgania.");
            MaxOffset = Config.Bind("Jiggle", "MaxOffset", 1.0f, "Maksymalne wychylenie sprezyny (ogranicza przesadne efekty).");
            DumpBones = Config.Bind("Debug", "DumpBones", true,
                "Zapisuje do logu liste kosci kazdej postaci - przydatne do dopasowania BonePatterns.");
            Logger.LogInfo("Gang Beasts Jiggle zaladowany.");
        }

        private void Update()
        {
            if (!Enabled.Value || Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 2f;
            try { Scan(); }
            catch (Exception e) { Logger.LogError("Scan error: " + e); }
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
            var meshes = FindObjectsOfType<SkinnedMeshRenderer>();
            foreach (var smr in meshes)
            {
                Transform[] bones = smr.bones;
                if (bones == null || bones.Length < 3) continue;

                if (DumpBones.Value)
                {
                    string key = smr.name + ":" + bones.Length;
                    if (_dumped.Add(key))
                    {
                        var names = new List<string>();
                        foreach (var b in bones) if (b != null) names.Add(b.name);
                        Logger.LogInfo("Mesh '" + smr.name + "' kosci (" + bones.Length + "): " + string.Join(", ", names.ToArray()));
                    }
                }

                foreach (var bone in bones)
                {
                    if (bone == null) continue;
                    if (!Matches(bone.name, inc) || Matches(bone.name, exc)) continue;
                    if (!_seen.Add(bone.GetInstanceID())) continue;
                    bone.gameObject.AddComponent<JiggleBone>();
                    Logger.LogInfo("Jiggle dodany do kosci: " + bone.name + " (mesh " + smr.name + ")");
                }
            }
        }
    }

    /// <summary>Sprezyna na kosci: ruch kosci wprawia ja w drgania, efekt jest tylko wizualny.</summary>
    public class JiggleBone : MonoBehaviour
    {
        private Vector3 _lastPos;
        private Vector3 _lastVel;
        private Vector3 _offset;      // wychylenie sprezyny (przestrzen swiata)
        private Vector3 _springVel;
        private Vector3 _origScale;
        private Vector3 _origLocalPos;
        private bool _applied;
        private bool _init;

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
            if (!Plugin.Enabled.Value) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (dt > 0.05f) dt = 0.05f;

            Vector3 pos = transform.position;
            if (!_init)
            {
                _lastPos = pos; _lastVel = Vector3.zero; _init = true;
            }

            // przyspieszenie kosci -> sila bezwladnosci na sprezyne
            Vector3 vel = (pos - _lastPos) / Time.deltaTime;
            Vector3 acc = (vel - _lastVel) / Time.deltaTime;
            _lastPos = pos; _lastVel = vel;

            float k = Plugin.Stiffness.Value;
            float c = Plugin.Damping.Value;
            Vector3 force = -acc * 0.01f * Plugin.Reaction.Value - k * _offset - c * _springVel;
            _springVel += force * dt;
            _offset += _springVel * dt;
            if (_offset.magnitude > Plugin.MaxOffset.Value)
                _offset = _offset.normalized * Plugin.MaxOffset.Value;

            if (_applied) return; // zabezpieczenie przed podwojnym nalozeniem w jednej klatce
            _origScale = transform.localScale;
            _origLocalPos = transform.localPosition;

            string mode = Plugin.Mode.Value;
            float amp = Plugin.Amplitude.Value;
            bool scale = mode == "Scale" || mode == "Both";
            bool position = mode == "Position" || mode == "Both";

            if (scale)
            {
                Vector3 l = transform.InverseTransformDirection(_offset);
                transform.localScale = new Vector3(
                    _origScale.x * (1f + Mathf.Abs(l.x) * amp),
                    _origScale.y * (1f + Mathf.Abs(l.y) * amp),
                    _origScale.z * (1f + Mathf.Abs(l.z) * amp));
            }
            if (position)
            {
                transform.position = transform.position + _offset * amp;
            }
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
