using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace GBHeads
{
    // Jedna latajaca glowa: model ze szczeka i powiekami (kosci), jezyk, zeby, dzwiek i zachowanie.
    internal class HeadObj
    {
        private enum St { Fly, Dive, Grab, Chew }

        private readonly HeadsMod M;
        private readonly HeadAssets A;

        public GameObject Root;
        public Vector2 Vp = new Vector2(0.5f, 0.75f);     // docelowy punkt na ekranie (viewport)
        public int SpeakingClip = -1;

        private Transform rootT, modelT, jawT, lidAT, lidBT, teethT, tongueT, tipT, capAT, capBT;
        private Renderer mainR, darkR, teethR, tongueR, tipR, capAR, capBR;
        private Rigidbody rb;
        private SphereCollider col;
        private readonly List<Collider> vCols = new List<Collider>();
        private int baseLayer = -1;
        private AudioSource audio;
        private Vector3 eyeA, eyeB, mouthLocal;

        private Vector3 pos, vel;
        private Quaternion rot = Quaternion.identity;
        private float bobPhase, nextGoal;

        private St st = St.Fly;
        private float stT;
        private float nextAttack, nextTalk, nextBlink, blinkT = -1f;
        private float jaw;
        private int lastClip = -1;
        private float speakStart, speakEnd;
        private float[] speakEnv;

        private ActorInfo victim;
        private readonly List<Rigidbody> vRbs = new List<Rigidbody>();
        private float tongueLen;
        private bool bitePlayed;
        private bool errLogged;

        private HeadObj(HeadsMod mod, HeadAssets assets) { M = mod; A = assets; }

        // ---------- Tworzenie ----------

        public static HeadObj Create(HeadsMod mod, HeadAssets assets, Vector2 vp)
        {
            var h = new HeadObj(mod, assets);
            try { h.Build(vp); }
            catch (Exception e)
            {
                MelonLogger.Error("[Heads] Nie udalo sie zbudowac glowy: " + e);
                h.Destroy();
                return null;
            }
            return h;
        }

        private void Build(Vector2 vp)
        {
            float[] lm = A.LM;
            eyeA = new Vector3(lm[2], lm[3], lm[4]);
            eyeB = new Vector3(lm[5], lm[6], lm[7]);
            mouthLocal = new Vector3(lm[9], lm[10], lm[11]);

            Root = new GameObject("GBHead");
            rootT = Root.transform;

            var model = new GameObject("Model");
            modelT = model.transform;
            modelT.SetParent(rootT, false);

            var jawGo = new GameObject("Jaw");
            jawT = jawGo.transform;
            jawT.SetParent(modelT, false);
            jawT.localPosition = new Vector3(0f, lm[0], lm[1]);

            var la = new GameObject("LidA");
            lidAT = la.transform; lidAT.SetParent(modelT, false); lidAT.localPosition = eyeA;
            var lb = new GameObject("LidB");
            lidBT = lb.transform; lidBT.SetParent(modelT, false); lidBT.localPosition = eyeB;

            try { BuildSkinned(model); }
            catch (Exception e)
            {
                MelonLogger.Warning("[Heads] skinning nie zadzialal, glowa bedzie statyczna (bez ruchu szczeki/powiek): " + e.Message);
                BuildStatic(model);
            }

            // zeby (gorne) - widoczne tylko przy otwartych ustach
            teethR = Prim("Teeth", modelT, A.TeethMat, out teethT);
            teethT.localPosition = new Vector3(mouthLocal.x, mouthLocal.y - 0.022f, mouthLocal.z - 0.012f);
            teethT.localScale = new Vector3(0.20f, 0.04f, 0.07f);
            teethR.enabled = false;

            // powieki: skorzane nakladki, ktore zjezdzaja na oczy przy mruganiu
            capAR = Prim("LidCapA", modelT, A.LidMat, out capAT);
            capBR = Prim("LidCapB", modelT, A.LidMat, out capBT);
            capAT.localPosition = eyeA; capBT.localPosition = eyeB;
            capAR.enabled = false; capBR.enabled = false;

            // jezyk (do lapania ofiary)
            tongueR = Prim("Tongue", rootT, A.TongueMat, out tongueT);
            tipR = Prim("TongueTip", rootT, A.TongueMat, out tipT);
            tongueR.enabled = false; tipR.enabled = false;

            audio = Root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 0f;

            bobPhase = UnityEngine.Random.value * 10f;
            Vp = vp;
            nextGoal = Time.time + UnityEngine.Random.Range(2f, 4f);
            nextAttack = Time.time + UnityEngine.Random.Range(3f, 9f);
            nextTalk = Time.time + UnityEngine.Random.Range(1.5f, 6f);
            nextBlink = Time.time + UnityEngine.Random.Range(0.5f, 3f);

            // start: nad gornym brzegiem ekranu, zleci w dol
            pos = M.ViewportToWorld(new Vector2(vp.x, 1.35f));
            rootT.position = pos;
            Camera cam = M.Cam;
            if (cam != null) rot = Quaternion.LookRotation((cam.transform.position - pos).normalized, cam.transform.up);
            rootT.rotation = rot;

            // prawdziwy obiekt fizyczny: collider + rigidbody (bez grawitacji, sterowany predkoscia)
            try
            {
                col = Root.AddComponent<SphereCollider>();
                col.radius = 0.42f * M.HeadWorld;
                rb = Root.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.mass = 6f;
                rb.drag = 0f;
                rb.angularDrag = 0f;
                rb.freezeRotation = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.velocity = Vector3.zero;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Heads] fizyka glowy nie zadzialala (glowa bez kolizji): " + e.Message);
                rb = null;
            }
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
            var smr2 = dg.AddComponent<SkinnedMeshRenderer>();
            smr2.sharedMesh = A.Dark;
            if (!Il2.SetArr(smr2, "bones", bones)) throw new Exception("SkinnedMeshRenderer.bones (usta)");
            smr2.rootBone = modelT;
            smr2.updateWhenOffscreen = true;
            smr2.sharedMaterial = A.HeadMat;
            smr2.shadowCastingMode = ShadowCastingMode.Off;
            smr2.receiveShadows = false;
            smr2.enabled = false;
            darkR = smr2;
        }

        private void BuildStatic(GameObject model)
        {
            var old = model.GetComponent<SkinnedMeshRenderer>();
            if (old != null) UnityEngine.Object.Destroy(old);
            var mf = model.AddComponent<MeshFilter>();
            mf.sharedMesh = A.Main;
            var mr = model.AddComponent<MeshRenderer>();
            mr.sharedMaterial = A.HeadMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mainR = mr;
            darkR = null;
        }

        private static Renderer Prim(string name, Transform parent, Material mat, out Transform t)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
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
                if (tongueR != null) tongueR.sharedMaterial = A.TongueMat;
                if (tipR != null) tipR.sharedMaterial = A.TongueMat;
                if (capAR != null) capAR.sharedMaterial = A.LidMat;
                if (capBR != null) capBR.sharedMaterial = A.LidMat;
            }
            catch { }
        }

        public void Destroy()
        {
            RestoreCollisions();
            try { if (Root != null) UnityEngine.Object.Destroy(Root); } catch { }
            Root = null;
        }

        public bool IsClipPlaying(int clip)
        {
            return SpeakingClip == clip && Time.time < speakEnd;
        }

        // ---------- Aktualizacja ----------

        public void Update(float dt)
        {
            if (Root == null) return;
            try { Tick(dt); }
            catch (Exception e)
            {
                if (!errLogged) { errLogged = true; MelonLogger.Error("[Heads] blad w glowie (kolejne takie same pomijam): " + e); }
            }
        }

        private bool VictimValid()
        {
            return victim != null && victim.Chest != null && victim.Comp != null;
        }

        private void Tick(float dt)
        {
            Camera cam = M.Cam;
            if (cam == null) return;
            dt = Mathf.Min(dt, 0.05f);
            float t = Time.time;

            float hw = M.HeadWorld;
            float ms = hw / Mathf.Max(0.1f, A.LM[14]);
            modelT.localScale = Vector3.one * ms;
            Vector3 mouthOff = mouthLocal * ms;

            if (col != null) col.radius = 0.42f * hw;
            if (rb != null)
            {
                pos = rootT.position;
                vel = rb.velocity;
                int al = M.ActorLayer;
                if (al >= 0 && al != baseLayer) { baseLayer = al; Root.layer = al; }
            }

            Transform ct = cam.transform;
            Vector3 camPos = ct.position, camUp = ct.up, camRight = ct.right;

            if (st != St.Fly && !VictimValid()) AbortAttack();
            stT += dt;

            // ----- docelowa orientacja: twarza do kamery, podczas ataku czesciowo do ofiary -----
            Vector3 toCam = camPos - pos;
            if (toCam.sqrMagnitude < 1e-4f) toCam = -ct.forward;
            toCam.Normalize();
            Vector3 fwd = toCam;
            Vector3 vPos = Vector3.zero;
            if (st != St.Fly)
            {
                vPos = victim.Chest.position;
                Vector3 toV = vPos - pos;
                if (toV.sqrMagnitude > 1e-4f) fwd = Vector3.Slerp(toCam, toV.normalized, 0.6f);
            }
            else
            {
                float sway = Mathf.Sin(t * 0.8f + bobPhase) * 18f;
                fwd = Quaternion.AngleAxis(sway, camUp) * toCam;
            }
            float roll = Mathf.Clamp(-Vector3.Dot(vel, camRight) * 10f / Mathf.Max(0.5f, hw), -22f, 22f);
            Quaternion look = Quaternion.LookRotation(fwd, camUp) * Quaternion.Euler(0f, 0f, roll);
            rot = Quaternion.Slerp(rot, look, 1f - Mathf.Exp(-9f * dt));

            // ----- ruch (sprezyna tlumiona) -----
            Vector3 goal;
            float omega;
            if (st == St.Fly)
            {
                if (t >= nextGoal)
                {
                    Vp = M.PickViewport(this);
                    nextGoal = t + UnityEngine.Random.Range(2.5f, 5.5f);
                }
                goal = M.ViewportToWorld(Vp);
                omega = 2.0f;
            }
            else
            {
                goal = vPos - (look * mouthOff);              // usta maja trafic w ofiare
                omega = st == St.Dive ? 5.5f : (st == St.Grab ? 4.0f : 6.0f);
            }
            Vector3 acc = (goal - pos) * (omega * omega) - vel * (2f * omega);
            vel += acc * dt;
            float maxV = (st == St.Fly ? 9f : 14f) * Mathf.Max(0.5f, hw);
            if (vel.magnitude > maxV) vel = vel.normalized * maxV;
            if (rb != null) rb.velocity = vel;
            else pos += vel * dt;

            // ----- maszyna stanow ataku -----
            Vector3 mouthW = pos + rot * mouthOff;
            if (st == St.Fly)
            {
                if (HeadsMod.PAttackOn.Value && t >= nextAttack) TryStartAttack();
            }
            else if (st == St.Dive)
            {
                float dist = (vPos - mouthW).magnitude;
                if (dist < hw * 0.95f) { st = St.Grab; stT = 0f; tongueLen = 0f; GatherBodies(); }
                else if (stT > 3.2f) AbortAttack();
            }
            else if (st == St.Grab)
            {
                float dist = (vPos - mouthW).magnitude;
                tongueLen = Mathf.Lerp(tongueLen, dist, 1f - Mathf.Exp(-18f * dt));
                PullVictim(mouthW, dt);
                if (dist < hw * 0.30f || stT > 1.1f) { st = St.Chew; stT = 0f; bitePlayed = false; }
            }
            else if (st == St.Chew)
            {
                if (!bitePlayed) { bitePlayed = true; PlayBite(); }
                tongueLen = Mathf.Lerp(tongueLen, 0f, 1f - Mathf.Exp(-14f * dt));
                HoldVictim(mouthW);
                if (stT > 1.4f) { Launch(ct); EndAttack(); }
            }

            // ----- ustawienie obiektu -----
            if (rb == null) rootT.position = pos;
            modelT.localPosition = Vector3.up * (Mathf.Sin(t * 1.6f + bobPhase) * 0.02f);
            rootT.rotation = rot;

            // ----- rozmowa -----
            bool speaking = t < speakEnd && speakEnv != null;
            if (!speaking) SpeakingClip = -1;
            if (HeadsMod.PTalk.Value && t >= nextTalk)
            {
                if (speaking) nextTalk = speakEnd + 0.5f;
                else if (st != St.Chew) StartTalk();
                else nextTalk = t + 1f;
            }

            // ----- szczeka -----
            float jt = 0f;
            if (st == St.Dive) jt = 20f + 4f * Mathf.Sin(t * 18f);
            else if (st == St.Grab) jt = 25f;
            else if (st == St.Chew) jt = 3f + 12f * Mathf.Abs(Mathf.Sin(stT * Mathf.PI * 3.4f));
            else if (speaking) jt = Mathf.Pow(EnvNow(t), 0.85f) * 15f;
            float rate = jt > jaw ? 24f : 15f;
            jaw = Mathf.Lerp(jaw, jt, 1f - Mathf.Exp(-rate * dt));
            jawT.localRotation = Quaternion.Euler(jaw, 0f, 0f);
            if (darkR != null) darkR.enabled = jaw > 1.2f;
            if (teethR != null) teethR.enabled = jaw > 3f && darkR != null;

            // ----- mruganie -----
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
            float capH = 0.115f * blink;
            bool capOn = blink > 0.03f;
            if (capAR != null)
            {
                capAR.enabled = capOn; capBR.enabled = capOn;
                capAT.localScale = new Vector3(0.27f, capH, 0.09f);
                capBT.localScale = new Vector3(0.27f, capH, 0.09f);
            }

            // ----- jezyk -----
            if (tongueLen > 0.02f && st != St.Fly && VictimValid())
            {
                Vector3 mw = rootT.position + rootT.rotation * mouthOff;
                Vector3 dir = victim.Chest.position - mw;
                float dl = dir.magnitude;
                if (dl > 0.01f)
                {
                    dir /= dl;
                    float len = Mathf.Min(tongueLen, dl);
                    float th = hw * 0.07f;
                    tongueT.position = mw + dir * (len * 0.5f);
                    tongueT.rotation = Quaternion.LookRotation(dir);
                    tongueT.localScale = new Vector3(th, th * 0.6f, len);
                    tipT.position = mw + dir * len;
                    tipT.localScale = Vector3.one * (th * 1.5f);
                    tongueR.enabled = true; tipR.enabled = true;
                }
            }
            else
            {
                if (tongueR.enabled) { tongueR.enabled = false; tipR.enabled = false; }
            }
        }

        // ---------- Atak ----------

        private void TryStartAttack()
        {
            ActorInfo v = M.PickVictim();
            if (v == null) { nextAttack = Time.time + 2f; return; }
            victim = v;
            st = St.Dive;
            stT = 0f;
            tongueLen = 0f;
            vRbs.Clear();
            vCols.Clear();
            try
            {
                if (col != null && v.Top != null)
                {
                    Il2.CollectColliders(v.Top, vCols);
                    for (int i = 0; i < vCols.Count; i++)
                        if (vCols[i] != null) Physics.IgnoreCollision(col, vCols[i], true);
                }
            }
            catch { }
        }

        private void RestoreCollisions()
        {
            try
            {
                if (col != null)
                    for (int i = 0; i < vCols.Count; i++)
                        if (vCols[i] != null) Physics.IgnoreCollision(col, vCols[i], false);
            }
            catch { }
            vCols.Clear();
        }

        private void GatherBodies()
        {
            vRbs.Clear();
            try { if (victim != null && victim.Top != null) Il2.CollectBodies(victim.Top, vRbs); }
            catch { }
        }

        private void PullVictim(Vector3 mouthW, float dt)
        {
            for (int i = 0; i < vRbs.Count; i++)
            {
                Rigidbody rb = vRbs[i];
                if (rb == null) continue;
                Vector3 d = mouthW - rb.position;
                float dl = d.magnitude;
                if (dl < 0.05f) continue;
                Vector3 want = d / dl * Mathf.Clamp(dl * 4f, 3f, 16f);
                rb.velocity = Vector3.Lerp(rb.velocity, want, 1f - Mathf.Exp(-12f * dt));
            }
        }

        private void HoldVictim(Vector3 mouthW)
        {
            for (int i = 0; i < vRbs.Count; i++)
            {
                Rigidbody rb = vRbs[i];
                if (rb == null) continue;
                Vector3 want = (mouthW - rb.position) * 9f;
                if (want.magnitude > 14f) want = want.normalized * 14f;
                rb.velocity = want + UnityEngine.Random.insideUnitSphere * 2f;
            }
        }

        private void Launch(Transform ct)
        {
            Vector3 dir = (-ct.forward * 0.2f + ct.up * 0.7f + ct.right * UnityEngine.Random.Range(-1f, 1f)).normalized;
            for (int i = 0; i < vRbs.Count; i++)
            {
                Rigidbody rb = vRbs[i];
                if (rb == null) continue;
                rb.velocity = dir * UnityEngine.Random.Range(11f, 17f) + UnityEngine.Random.insideUnitSphere * 3f;
                rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 8f;
            }
        }

        private void AbortAttack() { EndAttack(); }

        private void EndAttack()
        {
            RestoreCollisions();
            st = St.Fly;
            stT = 0f;
            victim = null;
            vRbs.Clear();
            tongueLen = 0f;
            nextAttack = Time.time + UnityEngine.Random.Range(0.6f, 1.4f) * Mathf.Max(1f, HeadsMod.PAttack.Value);
        }

        // ---------- Dzwiek ----------

        private float EnvNow(float t)
        {
            if (speakEnv == null || speakEnv.Length == 0) return 0f;
            int i = (int)((t - speakStart) * 50f);
            return speakEnv[Mathf.Clamp(i, 0, speakEnv.Length - 1)];
        }

        private void StartTalk()
        {
            var list = A.Talk;
            if (list.Count == 0 || audio == null) { nextTalk = Time.time + 8f; return; }
            int pick = -1;
            for (int tries = 0; tries < 12; tries++)
            {
                int c = list[UnityEngine.Random.Range(0, list.Count)];
                if (c == lastClip && list.Count > 1) continue;
                if (tries < 10 && M.IsClipPlaying(c)) continue;
                pick = c;
                break;
            }
            if (pick < 0) pick = list[UnityEngine.Random.Range(0, list.Count)];
            PlayClip(pick);
            nextTalk = speakEnd + UnityEngine.Random.Range(2f, 8f);
        }

        private void PlayClip(int c)
        {
            if (A.Clips[c] == null) return;
            audio.Stop();
            audio.clip = A.Clips[c];
            audio.volume = M.VolumeFor();
            audio.Play();
            speakStart = Time.time;
            speakEnd = speakStart + A.Dur[c];
            speakEnv = A.Env[c];
            SpeakingClip = c;
            lastClip = c;
        }

        private void PlayBite()
        {
            if (A.Bite < 0 || audio == null || A.Clips[A.Bite] == null) return;
            audio.Stop();
            audio.clip = A.Clips[A.Bite];
            audio.volume = Mathf.Clamp01(M.VolumeFor() * 1.2f);
            audio.Play();
            speakEnd = 0f;
            SpeakingClip = -1;
        }
    }
}
