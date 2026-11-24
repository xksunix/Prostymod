using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace GBSwap
{
    // Glowa-model przyczepiona do kosci glowy postaci: mruga, a przy glosie postaci (smiech, krzyk) otwiera usta.
    internal class SwapHead
    {
        private readonly HeadAssets A;
        private readonly SwapMod M;
        public GameObject Root;
        private Transform rootT, modelT, jawT, lidAT, lidBT, capAT, capBT;
        private Renderer mainR, darkR, capAR, capBR, teethR;
        private Transform teethT;
        private Transform head, hips, la, ra;
        private Renderer origHead;
        private readonly List<AudioSource> voices = new List<AudioSource>();
        private float headDiameter = 0.5f;
        private float born, nextBlink, blinkT = -1f, jaw, nextHide;
        private bool calibrated, errLogged;
        private Vector3 eyeA, eyeB;
        public bool Visible = true;

        public SwapHead(SwapMod mod, HeadAssets assets) { M = mod; A = assets; }

        public static SwapHead Create(SwapMod mod, HeadAssets assets, ActorRec a)
        {
            var h = new SwapHead(mod, assets);
            try { h.Build(a); }
            catch (Exception e)
            {
                MelonLogger.Error("[Swap] nie udalo sie zbudowac glowy: " + e);
                h.Destroy();
                return null;
            }
            return h;
        }

        private void Build(ActorRec a)
        {
            head = a.Head; hips = a.Hips; la = a.LA; ra = a.RA;
            origHead = a.OrigHead;
            float[] lm = A.LM;
            eyeA = new Vector3(lm[2], lm[3], lm[4]);
            eyeB = new Vector3(lm[5], lm[6], lm[7]);

            var col = head.GetComponent<Collider>();
            if (col != null)
            {
                Vector3 s = col.bounds.size;
                float d = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                if (d > 0.05f && d < 3f) headDiameter = d;
            }

            Root = new GameObject("GBSwapHead");
            rootT = Root.transform;
            rootT.SetParent(head, false);

            var model = new GameObject("Model");
            modelT = model.transform;
            modelT.SetParent(rootT, false);
            var jawGo = new GameObject("Jaw");
            jawT = jawGo.transform; jawT.SetParent(modelT, false);
            jawT.localPosition = new Vector3(0f, lm[0], lm[1]);
            var l1 = new GameObject("LidA"); lidAT = l1.transform; lidAT.SetParent(modelT, false); lidAT.localPosition = eyeA;
            var l2 = new GameObject("LidB"); lidBT = l2.transform; lidBT.SetParent(modelT, false); lidBT.localPosition = eyeB;

            try { BuildSkinned(model); }
            catch (Exception e)
            {
                MelonLogger.Warning("[Swap] skinning nie zadzialal, glowa statyczna: " + e.Message);
                BuildStatic(model);
            }

            teethR = Prim(PrimitiveType.Sphere, "Teeth", modelT, A.TeethMat, out teethT);
            teethT.localPosition = new Vector3(lm[9], lm[10] - 0.022f, lm[11] - 0.012f);
            teethT.localScale = new Vector3(0.20f, 0.04f, 0.07f);
            teethR.enabled = false;

            capAR = Prim(PrimitiveType.Sphere, "LidCapA", modelT, A.LidMat, out capAT);
            capBR = Prim(PrimitiveType.Sphere, "LidCapB", modelT, A.LidMat, out capBT);
            capAT.localPosition = eyeA; capBT.localPosition = eyeB;
            capAR.enabled = false; capBR.enabled = false;

            FindVoices(a.Top);
            born = Time.time;
            nextBlink = Time.time + UnityEngine.Random.Range(0.5f, 3f);
            Root.SetActive(false);               // pokazemy po kalibracji orientacji
        }

        private void FindVoices(Transform top)
        {
            try
            {
                var q = new Queue<Transform>();
                q.Enqueue(top);
                int n = 0;
                while (q.Count > 0 && n++ < 300)
                {
                    var t = q.Dequeue();
                    if (t.name.ToLowerInvariant().Contains("voice"))
                    {
                        var s = t.GetComponent<AudioSource>();
                        if (s != null) voices.Add(s);
                    }
                    for (int i = 0; i < t.childCount; i++) q.Enqueue(t.GetChild(i));
                }
            }
            catch { }
        }

        private void BuildSkinned(GameObject model)
        {
            var bones = new Transform[] { modelT, jawT, lidAT, lidBT };
            var smr = model.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = A.Main;
            if (!Il2.SetArr(smr, "bones", bones)) throw new Exception("SkinnedMeshRenderer.bones");
            smr.rootBone = modelT;
            smr.updateWhenOffscreen = true;
            smr.sharedMaterial = A.HeadMat;
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.receiveShadows = false;
            mainR = smr;

            var dg = new GameObject("MouthInside");
            dg.transform.SetParent(modelT, false);
            var s2 = dg.AddComponent<SkinnedMeshRenderer>();
            s2.sharedMesh = A.Dark;
            if (!Il2.SetArr(s2, "bones", bones)) throw new Exception("SkinnedMeshRenderer.bones (usta)");
            s2.rootBone = modelT;
            s2.updateWhenOffscreen = true;
            s2.sharedMaterial = A.HeadMat;
            s2.shadowCastingMode = ShadowCastingMode.Off;
            s2.receiveShadows = false;
            s2.enabled = false;
            darkR = s2;
        }

        private void BuildStatic(GameObject model)
        {
            var old = model.GetComponent<SkinnedMeshRenderer>();
            if (old != null) UnityEngine.Object.Destroy(old);
            model.AddComponent<MeshFilter>().sharedMesh = A.Main;
            var mr = model.AddComponent<MeshRenderer>();
            mr.sharedMaterial = A.HeadMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mainR = mr;
            darkR = null;
        }

        private static Renderer Prim(PrimitiveType pt, string name, Transform parent, Material mat, out Transform t)
        {
            var go = GameObject.CreatePrimitive(pt);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            t = go.transform;
            t.SetParent(parent, false);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        public void ApplyMaterials()
        {
            try
            {
                if (mainR != null) mainR.sharedMaterial = A.HeadMat;
                if (darkR != null) darkR.sharedMaterial = A.HeadMat;
                if (teethR != null) teethR.sharedMaterial = A.TeethMat;
                if (capAR != null) capAR.sharedMaterial = A.LidMat;
                if (capBR != null) capBR.sharedMaterial = A.LidMat;
            }
            catch { }
        }

        public void Destroy()
        {
            SetOriginalVisible(true);
            try { if (Root != null) UnityEngine.Object.Destroy(Root); } catch { }
            Root = null;
        }

        private void SetOriginalVisible(bool v)
        {
            try { if (origHead != null && origHead.enabled != v) origHead.enabled = v; } catch { }
        }

        // Orientacja z ukladu ciala: prawo = lewe ramie -> prawe ramie, gora = biodra -> glowa, przod = prawo x gora.
        private void Calibrate()
        {
            if (la == null || ra == null || hips == null || head == null) return;
            Vector3 right = ra.position - la.position;
            Vector3 up = head.position - hips.position;
            if (right.sqrMagnitude < 1e-4f || up.sqrMagnitude < 1e-4f) return;
            right.Normalize(); up.Normalize();
            Vector3 fwd = Vector3.Cross(right, up);
            if (fwd.sqrMagnitude < 1e-4f) return;
            Quaternion look = Quaternion.LookRotation(fwd.normalized, up);
            localRot = Quaternion.Inverse(head.rotation) * look;
        }

        private Quaternion localRot = Quaternion.identity;

        public void Update(float dt)
        {
            if (Root == null) return;
            try { Tick(dt); }
            catch (Exception e)
            {
                if (!errLogged) { errLogged = true; MelonLogger.Error("[Swap] blad w glowie (kolejne takie same pomijam): " + e); }
            }
        }

        private void Tick(float dt)
        {
            if (head == null) { Destroy(); return; }
            bool on = SwapMod.PEnabled.Value;
            if (!on)
            {
                if (Root.activeSelf) Root.SetActive(false);
                SetOriginalVisible(true);
                return;
            }

            float t = Time.time;
            // kalibracja przez pierwsze 2 s (poza t-pose / pozycja startowa), potem stala
            if (!calibrated)
            {
                Calibrate();
                if (t - born > 2f) calibrated = true;
                if (t - born < 0.3f) return;
            }
            if (!Root.activeSelf) Root.SetActive(true);

            // rozmiar i polozenie
            float lossy = Mathf.Max(0.0001f, head.lossyScale.x);
            float hw = headDiameter * Mathf.Clamp(SwapMod.PSize.Value, 0.3f, 4f);
            float ms = hw / Mathf.Max(0.1f, A.LM[14]);
            rootT.localScale = Vector3.one * (1f / lossy);
            modelT.localScale = Vector3.one * ms;
            rootT.localPosition = Vector3.zero;
            rootT.localRotation = localRot * Quaternion.Euler(0f, SwapMod.PYaw.Value, 0f);

            if (t >= nextHide)
            {
                nextHide = t + 0.4f;
                SetOriginalVisible(false);          // gra moze wlaczyc oryginalna glowe z powrotem
            }

            // usta: gdy postac "mowi" (smiech, krzyk, bol)
            bool speaking = false;
            for (int i = 0; i < voices.Count; i++)
            {
                var s = voices[i];
                try { if (s != null && s.isPlaying) { speaking = true; break; } } catch { }
            }
            float jt = speaking ? 10f + 8f * Mathf.Abs(Mathf.Sin(t * 11f + born)) : 0f;
            jaw = Mathf.Lerp(jaw, jt, 1f - Mathf.Exp(-18f * dt));
            jawT.localRotation = Quaternion.Euler(jaw, 0f, 0f);
            if (darkR != null) darkR.enabled = jaw > 1.2f;
            if (teethR != null) teethR.enabled = jaw > 3f && darkR != null;

            // mruganie (nakladki-powieki)
            float blink = 0f;
            if (blinkT < 0f)
            {
                if (t >= nextBlink) blinkT = 0f;
            }
            else
            {
                blinkT += dt;
                float k = blinkT / 0.2f;
                blink = k < 0.4f ? k / 0.4f : 1f - (k - 0.4f) / 0.6f;
                if (blinkT >= 0.2f)
                {
                    blinkT = -1f; blink = 0f;
                    nextBlink = t + (UnityEngine.Random.value < 0.2f ? 0.25f : UnityEngine.Random.Range(1.5f, 5f));
                }
            }
            blink = Mathf.Clamp01(blink);
            bool capOn = blink > 0.03f;
            capAR.enabled = capOn; capBR.enabled = capOn;
            Vector3 cs = new Vector3(0.27f, 0.115f * blink, 0.09f);
            capAT.localScale = cs; capBT.localScale = cs;
        }
    }
}
