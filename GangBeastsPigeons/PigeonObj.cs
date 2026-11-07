using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace GBPigeons
{
    // Jeden golab "wcielony" w postac z gry: ukrywa jej siatke, rysuje golebia na jej szkielecie fizycznym,
    // animuje go jak ptaka (kiwanie glowa, dziobanie, rozgladanie, machanie skrzydlami, gruchanie) i podmienia glos postaci.
    internal class PigeonObj
    {
        private readonly PigeonsMod M;
        private readonly PigeonAssets A;
        public Component Actor;
        public Transform Top;
        public bool Dead;

        private Transform hips, chest, head, lArm, rArm, lHand, rHand;
        private Rigidbody rbChest;
        private readonly List<Collider> cols = new List<Collider>();

        // model golebia
        private GameObject root;
        private Transform rootT, modelT;
        private Transform[] bones;
        private SkinnedMeshRenderer smr;
        private AudioSource cooSrc, sfxSrc;
        private string variantKey = "base";
        private float vHue, vSat = 1f, vVal = 1f;

        // indeksy kosci
        private int iThorax, iNeck, iFace, iTail1;
        private int[] wingSide, legKind, legSide;        // wingSide: -1 lewe, 1 prawe; legKind: 1 udo, 2 lydka
        private PClip cIdle, cFlap, cFold, cTuck;

        // oryginalna postac
        private readonly List<Renderer> origR = new List<Renderer>();
        private readonly List<Renderer> hidden = new List<Renderer>();
        private readonly List<AudioSource> voices = new List<AudioSource>();
        private readonly List<bool> voiceWas = new List<bool>();
        private readonly List<bool> voiceMutedByUs = new List<bool>();
        private float hideTimer, rescanTimer;

        // stan z gry (odczyt przez refleksje, tak jak w modzie Faces)
        private MemberInfo stateMember;
        private readonly List<MemberInfo> angerFlags = new List<MemberInfo>();
        private readonly List<MemberInfo> strainFlags = new List<MemberInfo>();
        private string lastState = "";
        private static int stateLogCount, dumpCount;
        private static bool membersLogged;

        private bool knocked, falling, jumping, climbing, swimming, fighting, straining, hurt, airborne;
        private float punchHold, hurtHold;
        private Vector3 prevCv; private bool hasPrevCv;
        private Vector3 lastHips, vel; private bool hasLastHips;
        private float speed;

        // uklad ciala
        private Vector3 upv = Vector3.up, fwdv = Vector3.forward, rightv = Vector3.right, lastRight = Vector3.right;
        private Quaternion frameRot = Quaternion.identity;
        private bool hasFrame;
        private float charH = 1.8f, footDrop = 0.9f, measureTimer;
        private float k = 5f;

        // animacja
        private float time, seed;
        private float wOpenL, wOpenR, wFlap, wTuck, walk, phase, flapT;
        private float idleTime, nextPeckAt, peckT = -1f; private int peckN;
        private float lookTimer, lookTarget, neckYaw;
        private float hurtPuff, bobZ, leanDeg;
        private float flapSfxAt, nextCoo, lastVoiceAct, flipTimer;
        private bool wasFighting, wasHurt, wasJumping, wasKnocked, wasFlapping;
        private float basePitch = 1f;

        // aktualnie grane gruchanie (do synchronizacji z szyja)
        private PSound cooSnd; private float cooStart, cooUntil;
        private readonly Dictionary<string, int> lastPick = new Dictionary<string, int>();

        private PigeonObj(PigeonsMod mod, PigeonAssets assets, Component actor)
        {
            M = mod; A = assets; Actor = actor; Top = actor.transform;
            seed = UnityEngine.Random.value * 100f;
            nextPeckAt = 2.5f + UnityEngine.Random.value * 4f;
            lookTimer = 0.5f + UnityEngine.Random.value * 2f;
            basePitch = 0.9f + UnityEngine.Random.value * 0.22f;
        }

        // ---------- Tworzenie ----------

        public static PigeonObj Create(PigeonsMod mod, PigeonAssets assets, Component actor)
        {
            Transform top = actor.transform;
            Transform hips = Il2.FindBone(top, "actor_hips_collider");
            if (hips == null)
            {
                MelonLogger.Warning("[Pigeons] postac '" + actor.name + "' bez kosci actor_hips_collider - pomijam.");
                return null;
            }
            var p = new PigeonObj(mod, assets, actor);
            p.hips = hips;
            p.chest = Il2.FindBone(top, "actor_chest_collider") ?? hips;
            p.head = Il2.FindBone(top, "actor_head_collider");
            p.lArm = Il2.FindBone(top, "actor_leftArm_collider");
            p.rArm = Il2.FindBone(top, "actor_rightArm_collider");
            p.lHand = Il2.FindBone(top, "actor_leftHand_collider");
            p.rHand = Il2.FindBone(top, "actor_rightHand_collider");
            try { p.rbChest = p.chest.GetComponent<Rigidbody>(); } catch { }
            try { Il2.CollectColliders(top, p.cols); } catch { }

            try { p.PickColor(); } catch (Exception e) { MelonLogger.Warning("[Pigeons] kolor gracza: " + e.Message); }
            try { p.Build(); }
            catch (Exception e)
            {
                MelonLogger.Error("[Pigeons] nie udalo sie zbudowac golebia: " + e);
                try { p.Destroy(); } catch { }
                return null;
            }
            try { p.Reflect(); } catch (Exception e) { MelonLogger.Warning("[Pigeons] stan postaci niedostepny (golab bedzie reagowal tylko na ruch): " + e.Message); }
            try { p.FindVoices(); } catch (Exception e) { MelonLogger.Warning("[Pigeons] zrodla glosu: " + e.Message); }
            try { p.ScanRenderers(); } catch (Exception e) { MelonLogger.Warning("[Pigeons] renderery postaci: " + e.Message); }
            p.MeasureBody(true);
            p.nextCoo = Time.time + UnityEngine.Random.Range(1.5f, 5f);

            MelonLogger.Msg("[Pigeons] golab dla '" + actor.name + "': wysokosc postaci " + p.charH.ToString("0.00") + ", biodra nad ziemia " + p.footDrop.ToString("0.00")
                + ", rece: " + ((p.lHand != null && p.rHand != null) ? "ok" : "brak") + ", glosy: " + p.voices.Count + ", renderery: " + p.origR.Count
                + ", wariant koloru: " + p.variantKey);
            if (dumpCount++ == 0) p.DumpHierarchy();
            return p;
        }

        // Kolor gracza -> wariant koloru ciala golebia (zeby gracze dalej sie roznili)
        private void PickColor()
        {
            variantKey = "base";
            if (!PigeonsMod.PTint.Value) return;
            Renderer br = BodyColor.FindBodyRenderer(Top);
            if (br == null) return;
            Color c; string prop;
            if (!BodyColor.TryRead(br, out c, out prop)) return;
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            int hb = Mathf.Clamp((int)(h * 24f), 0, 23);
            if (s < 0.12f)
            {
                vHue = 0.6f; vSat = 0.12f; vVal = Mathf.Clamp(v / 0.85f, 0.75f, 1.15f);
                variantKey = "gray" + Mathf.RoundToInt(vVal * 10f);
            }
            else
            {
                vHue = (hb + 0.5f) / 24f;
                vSat = Mathf.Clamp(s / 0.4f, 0.6f, 1.7f);
                vVal = Mathf.Clamp(v / 0.85f, 0.75f, 1.15f);
                variantKey = "h" + hb + "s" + Mathf.RoundToInt(vSat * 4f) + "v" + Mathf.RoundToInt(vVal * 8f);
            }
            MelonLogger.Msg("[Pigeons] kolor gracza " + c.r.ToString("0.00") + "," + c.g.ToString("0.00") + "," + c.b.ToString("0.00") + " (" + prop + ") -> wariant " + variantKey);
        }

        private void Build()
        {
            cIdle = A.Clips["idle"]; cFlap = A.Clips["flap"]; cFold = A.Clips["fold"]; cTuck = A.Clips["tuck"];
            iThorax = A.Bone("thorax-bone"); iNeck = A.Bone("neck-bone"); iFace = A.Bone("face-bone"); iTail1 = A.Bone("tail-bone-1");
            if (iThorax < 0 || iNeck < 0 || iFace < 0) throw new Exception("w modelu brakuje kosci thorax/neck/face");
            wingSide = new int[A.NB]; legKind = new int[A.NB]; legSide = new int[A.NB];
            for (int i = 0; i < A.NB; i++)
            {
                string n = A.BoneNames[i];
                int side = n.StartsWith("left") ? -1 : (n.StartsWith("right") ? 1 : 0);
                if (n.Contains("wing")) wingSide[i] = side;
                else if (n.Contains("upper-leg")) { legKind[i] = 1; legSide[i] = side; }
                else if (n.Contains("lower-leg")) { legKind[i] = 2; legSide[i] = side; }
            }

            root = new GameObject("GBPigeon");
            rootT = root.transform;
            var model = new GameObject("GBPigeonModel");
            modelT = model.transform;
            modelT.SetParent(rootT, false);

            bones = new Transform[A.NB];
            for (int i = 0; i < A.NB; i++)
            {
                var g = new GameObject(A.BoneNames[i]);
                bones[i] = g.transform;
            }
            for (int i = 0; i < A.NB; i++)
            {
                bones[i].SetParent(A.BoneParent[i] < 0 ? modelT : bones[A.BoneParent[i]], false);
                bones[i].localPosition = A.RestPos[i];
                bones[i].localRotation = A.RestRot[i];
                bones[i].localScale = Vector3.one;
            }

            smr = model.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = A.Mesh;
            if (!Il2.SetArr(smr, "bones", bones)) throw new Exception("SkinnedMeshRenderer.bones");
            smr.rootBone = modelT;
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = PigeonsMod.PShadow.Value ? ShadowCastingMode.On : ShadowCastingMode.Off;
            smr.receiveShadows = false;
            ApplyMaterial();

            cooSrc = root.AddComponent<AudioSource>();
            cooSrc.playOnAwake = false; cooSrc.loop = false; cooSrc.spatialBlend = 0f;
            sfxSrc = root.AddComponent<AudioSource>();
            sfxSrc.playOnAwake = false; sfxSrc.loop = false; sfxSrc.spatialBlend = 0f;
        }

        public void Retint()
        {
            try { vHue = 0f; vSat = 1f; vVal = 1f; PickColor(); } catch (Exception e) { MelonLogger.Warning("[Pigeons] kolor gracza: " + e.Message); }
            ApplyMaterial();
        }

        public void TestCoo()
        {
            Say("coo", 1f);
        }

        public void ApplyMaterial()
        {
            try
            {
                if (smr == null) return;
                smr.sharedMaterial = A.GetMaterial(variantKey, vHue, vSat, vVal);
                smr.shadowCastingMode = PigeonsMod.PShadow.Value ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            catch (Exception e) { MelonLogger.Warning("[Pigeons] material golebia: " + e.Message); }
        }

        // ---------- Dane postaci ----------

        private void Reflect()
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = Actor.GetType();
            foreach (var f in t.GetFields(bf))
                if (string.Equals(f.Name, "actorState", StringComparison.OrdinalIgnoreCase)) stateMember = f;
            foreach (var p in t.GetProperties(bf))
                if (stateMember == null && p.CanRead && p.GetIndexParameters().Length == 0
                    && string.Equals(p.Name, "actorState", StringComparison.OrdinalIgnoreCase)) stateMember = p;
            if (stateMember == null)
                foreach (var p in t.GetProperties(bf))
                    if (p.CanRead && p.GetIndexParameters().Length == 0 && p.PropertyType.IsEnum && p.Name.ToLowerInvariant().Contains("state")) { stateMember = p; break; }
            if (stateMember == null)
                foreach (var f in t.GetFields(bf))
                    if (f.FieldType.IsEnum && f.Name.ToLowerInvariant().Contains("state")) { stateMember = f; break; }

            foreach (var f in t.GetFields(bf)) if (f.FieldType == typeof(bool)) ClassifyFlag(f, f.Name);
            foreach (var p in t.GetProperties(bf))
                if (p.PropertyType == typeof(bool) && p.CanRead && p.GetIndexParameters().Length == 0) ClassifyFlag(p, p.Name);

            if (!membersLogged)
            {
                membersLogged = true;
                MelonLogger.Msg("[Pigeons] stan postaci: " + (stateMember != null ? stateMember.Name : "(nie znaleziono)")
                    + ", flagi walki: " + angerFlags.Count + ", flagi chwytania: " + strainFlags.Count);
                try
                {
                    if (stateMember != null)
                    {
                        Type st = stateMember is PropertyInfo ? ((PropertyInfo)stateMember).PropertyType : ((FieldInfo)stateMember).FieldType;
                        MelonLogger.Msg("[Pigeons] wartosci " + st.Name + ": " + string.Join(", ", Enum.GetNames(st)));
                    }
                }
                catch { }
            }
        }

        private void ClassifyFlag(MemberInfo m, string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("punch") || n.Contains("attack") || n.Contains("fight") || n.Contains("kick")) angerFlags.Add(m);
            else if (n.Contains("grab") || n.Contains("hold") || n.Contains("carry") || n.Contains("climb")) strainFlags.Add(m);
        }

        private static object Read(MemberInfo m, object o)
        {
            try
            {
                var f = m as FieldInfo;
                if (f != null) return f.GetValue(o);
                var p = m as PropertyInfo;
                if (p != null) return p.GetValue(o, null);
            }
            catch { }
            return null;
        }

        private static bool AnyTrue(List<MemberInfo> list, object o)
        {
            foreach (var m in list)
            {
                var v = Read(m, o);
                if (v is bool && (bool)v) return true;
            }
            return false;
        }

        private void FindVoices()
        {
            var q = new Queue<Transform>();
            q.Enqueue(Top);
            int visited = 0;
            while (q.Count > 0 && visited++ < 250)
            {
                var t = q.Dequeue();
                if (t.name.ToLowerInvariant().Contains("voice"))
                {
                    AudioSource a = null;
                    try { a = t.GetComponent<AudioSource>(); } catch { }
                    if (a != null && !voices.Contains(a)) { voices.Add(a); voiceWas.Add(false); voiceMutedByUs.Add(false); }
                }
                for (int i = 0; i < t.childCount; i++) q.Enqueue(t.GetChild(i));
            }
        }

        private void ScanRenderers()
        {
            origR.Clear();
            var q = new Queue<Transform>();
            q.Enqueue(Top);
            int guard = 0;
            while (q.Count > 0 && guard++ < 800)
            {
                var t = q.Dequeue();
                for (int i = 0; i < t.childCount; i++) q.Enqueue(t.GetChild(i));
                if (t.name == "GBPigeon") continue;
                Renderer r = null;
                try { r = t.GetComponent<SkinnedMeshRenderer>(); } catch { }
                if (r == null) { try { r = t.GetComponent<MeshRenderer>(); } catch { } }
                if (r != null) origR.Add(r);
            }
        }

        private void DumpHierarchy()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                var q = new Queue<Transform>();
                q.Enqueue(Top);
                int n = 0;
                while (q.Count > 0 && n < 140)
                {
                    var t = q.Dequeue(); n++;
                    string flags = "";
                    try { if (t.GetComponent<Renderer>() != null) flags += "R"; } catch { }
                    try { if (t.GetComponent<Collider>() != null) flags += "C"; } catch { }
                    try { if (t.GetComponent<AudioSource>() != null) flags += "A"; } catch { }
                    sb.Append(t.name).Append(flags.Length > 0 ? "[" + flags + "]" : "").Append(' ');
                    for (int i = 0; i < t.childCount; i++) q.Enqueue(t.GetChild(i));
                }
                MelonLogger.Msg("[Pigeons] hierarchia postaci (R=renderer, C=collider, A=audio): " + sb);
            }
            catch { }
        }

        // ---------- Pomiar ciala (wysokosc, odleglosc bioder od ziemi) ----------

        private void MeasureBody(bool force)
        {
            try
            {
                float minY = float.MaxValue, maxY = float.MinValue;
                int n = 0;
                foreach (var c in cols)
                {
                    if (c == null || c.isTrigger) continue;
                    Bounds b = c.bounds;
                    if (b.min.y < minY) minY = b.min.y;
                    if (b.max.y > maxY) maxY = b.max.y;
                    n++;
                }
                float hipsY = hips.position.y;
                float height = maxY - minY;
                float fd = hipsY - minY;
                bool upright = upv.y > 0.9f || force;
                if (n > 0 && upright && height > 0.8f && height < 4f && fd > 0.12f * height && fd < 0.95f * height)
                {
                    if (force && !measured) { charH = height; footDrop = fd; }
                    else { charH = Mathf.Lerp(charH, height, 0.2f); footDrop = Mathf.Lerp(footDrop, fd, 0.2f); }
                    measured = true;
                }
                else if (!measured && head != null)
                {
                    float h2 = (head.position - hips.position).magnitude * 2.4f;
                    if (h2 > 0.6f && h2 < 6f) { charH = h2; footDrop = h2 * 0.5f; }
                }
            }
            catch { }
        }
        private bool measured;

        // ---------- Aktualizacja ----------

        public void Tick(float dt)
        {
            if (Dead) return;
            if (Actor == null || hips == null || root == null) { Dead = true; return; }
            bool active = true;
            try { active = Actor.gameObject.activeInHierarchy; } catch { }
            if (root.activeSelf != active) root.SetActive(active);
            if (!active) return;

            dt = Mathf.Clamp(dt, 0.0005f, 0.1f);
            time += dt;

            ComputeFrame(dt);
            ReadState(dt);
            HideOriginals(dt);
            UpdateWeights(dt);
            Place(dt);
            Animate(dt);
            UpdateSounds(dt);
        }

        // Uklad ciala z kosci: gora = biodra->glowa, prawo = lewe ramie->prawe ramie, przod = prawo x gora (tak samo jak w modzie Faces)
        private void ComputeFrame(float dt)
        {
            Vector3 hp = hips.position;
            Vector3 top = head != null ? head.position : chest.position;
            Vector3 u = top - hp;
            if (u.sqrMagnitude < 1e-6f) u = upv;
            u.Normalize();

            Vector3 r;
            if (lArm != null && rArm != null) r = rArm.position - lArm.position;
            else r = hips.right;
            r -= u * Vector3.Dot(r, u);
            if (r.sqrMagnitude < 1e-4f) r = lastRight - u * Vector3.Dot(lastRight, u);
            if (r.sqrMagnitude < 1e-6f) r = Vector3.right;
            r.Normalize();
            lastRight = r;

            Vector3 f = Vector3.Cross(r, u).normalized;
            if (PigeonsMod.PFlip.Value) f = -f;

            Quaternion target = Quaternion.LookRotation(f, u);
            if (!hasFrame) { frameRot = target; hasFrame = true; }
            else frameRot = Quaternion.Slerp(frameRot, target, 1f - Mathf.Exp(-dt * 32f));
            upv = u; fwdv = f; rightv = Vector3.Cross(u, f).normalized * -1f;
            rightv = Vector3.Cross(u, f); // w Unity: prawo = gora x przod
            rightv.Normalize();

            // predkosc korpusu z przesuniecia bioder (wygladzona)
            if (hasLastHips)
            {
                Vector3 v = (hp - lastHips) / dt;
                if (v.magnitude > 60f) v = Vector3.zero;      // teleport / respawn
                vel = Vector3.Lerp(vel, v, 1f - Mathf.Exp(-dt * 12f));
            }
            lastHips = hp; hasLastHips = true;
            speed = new Vector3(vel.x, 0f, vel.z).magnitude;

            measureTimer -= dt;
            if (measureTimer <= 0f)
            {
                measureTimer = 0.5f;
                if (!airborne && !knocked && speed < 6f) MeasureBody(false);
            }
        }

        private void ReadState(float dt)
        {
            string state = "";
            if (stateMember != null)
            {
                var v = Read(stateMember, Actor);
                if (v != null) state = v.ToString().ToLowerInvariant();
            }
            if (state != lastState)
            {
                if (stateLogCount < 30) { stateLogCount++; MelonLogger.Msg("[Pigeons] stan postaci: '" + lastState + "' -> '" + state + "'"); }
                lastState = state;
            }
            knocked = state.Contains("unconscious") || state.Contains("dead") || state.Contains("knock") || state == "ko";
            falling = state.Contains("fall") || state.Contains("ragdoll");
            jumping = state.Contains("jump");
            climbing = state.Contains("climb");
            swimming = state.Contains("swim");
            fighting = AnyTrue(angerFlags, Actor);
            straining = climbing || AnyTrue(strainFlags, Actor);

            // uderzenie = gwaltowna zmiana predkosci klatki piersiowej
            if (rbChest != null)
            {
                Vector3 cv = rbChest.velocity;
                if (hasPrevCv && !knocked && (cv - prevCv).magnitude / dt > PigeonsMod.HitThreshold) hurtHold = 0.45f;
                prevCv = cv; hasPrevCv = true;
                if (lHand != null || rHand != null)
                {
                    var l = lHand != null ? lHand.GetComponent<Rigidbody>() : null;
                    var r = rHand != null ? rHand.GetComponent<Rigidbody>() : null;
                    float rel = 0f;
                    if (l != null) rel = Mathf.Max(rel, (l.velocity - cv).magnitude);
                    if (r != null) rel = Mathf.Max(rel, (r.velocity - cv).magnitude);
                    if (rel > 5.5f) punchHold = 0.5f;
                }
            }
            if (punchHold > 0f) { punchHold -= dt; if (!falling) fighting = true; }
            hurt = hurtHold > 0f;
            if (hurt) hurtHold -= dt;
            airborne = jumping || falling || Mathf.Abs(vel.y) > 4.5f;
        }

        // Ukrywa oryginalna siatke postaci (gra czasem ja wlacza z powrotem - sprawdzamy co chwile) i wycisza jej glos
        private void HideOriginals(float dt)
        {
            hideTimer -= dt; rescanTimer -= dt;
            if (rescanTimer <= 0f)
            {
                rescanTimer = 2.5f;
                try { ScanRenderers(); FindVoices(); } catch { }
            }
            if (hideTimer > 0f) return;
            hideTimer = 0.2f;
            for (int i = 0; i < origR.Count; i++)
            {
                var r = origR[i];
                if (r == null) continue;
                try
                {
                    if (r.enabled) { r.enabled = false; if (!hidden.Contains(r)) hidden.Add(r); }
                }
                catch { }
            }
            bool replace = PigeonsMod.PReplaceVoice.Value;
            for (int i = 0; i < voices.Count; i++)
            {
                var a = voices[i];
                if (a == null) continue;
                try
                {
                    if (replace && !a.mute) { a.mute = true; voiceMutedByUs[i] = true; }
                    else if (!replace && voiceMutedByUs[i] && a.mute) { a.mute = false; voiceMutedByUs[i] = false; }
                }
                catch { }
            }
        }

        // ---------- Wagi animacji ----------

        private void UpdateWeights(float dt)
        {
            float tOpenL = 0f, tOpenR = 0f, tFlap = 0f, tTuck = 0f;

            if (PigeonsMod.PWingsArms.Value && !knocked)
            {
                tOpenL = ArmOpen(lHand);
                tOpenR = ArmOpen(rHand);
            }

            if (knocked) { tOpenL = tOpenR = 1f; }
            else if (airborne || swimming) { tOpenL = tOpenR = 1f; tFlap = 1f; tTuck = airborne ? 1f : 0f; }
            else if (hurt) { tOpenL = tOpenR = 1f; tFlap = 1f; }
            else if (fighting) { tOpenL = Mathf.Max(tOpenL, 0.85f); tOpenR = Mathf.Max(tOpenR, 0.85f); tFlap = 0.8f; }
            else if (straining) { tOpenL = Mathf.Max(tOpenL, 0.9f); tOpenR = Mathf.Max(tOpenR, 0.9f); tFlap = 0.55f; }

            float rate = 1f - Mathf.Exp(-dt * 14f);
            wOpenL = Mathf.Lerp(wOpenL, tOpenL, rate);
            wOpenR = Mathf.Lerp(wOpenR, tOpenR, rate);
            wFlap = Mathf.Lerp(wFlap, tFlap, 1f - Mathf.Exp(-dt * 10f));
            wTuck = Mathf.Lerp(wTuck, tTuck, 1f - Mathf.Exp(-dt * 12f));

            float tWalk = (!airborne && !knocked) ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1.8f, speed)) : 0f;
            walk = Mathf.Lerp(walk, tWalk, 1f - Mathf.Exp(-dt * 10f));
            float stepHz = Mathf.Clamp(speed / Mathf.Max(0.5f, charH * 0.55f), 0f, 5.5f);
            phase += dt * stepHz * Mathf.PI * 2f;
            if (phase > 1000f) phase -= 1000f * 1f;
            flapT += dt * Mathf.Lerp(2.5f, 6.5f, wFlap) * (0.35f + 0.65f * wFlap);

            if (hurtHold > 0.3f) hurtPuff = 1f;
            hurtPuff = Mathf.Max(0f, hurtPuff - dt * 3f);
        }

        // otwarcie skrzydla zalezne od polozenia reki postaci (reka w gorze / na bok / do przodu = skrzydlo rozlozone)
        private float ArmOpen(Transform hand)
        {
            if (hand == null) return 0f;
            Vector3 h = hand.position - chest.position;
            float H = Mathf.Max(0.5f, charH);
            float u = Vector3.Dot(h, upv) / H;
            float lat = Mathf.Abs(Vector3.Dot(h, rightv)) / H;
            float fw = Vector3.Dot(h, fwdv) / H;
            float a1 = Mathf.Clamp01((u + 0.20f) / 0.40f);
            float a2 = Mathf.Clamp01((lat - 0.14f) / 0.18f);
            float a3 = Mathf.Clamp01((fw - 0.14f) / 0.25f) * 0.8f;
            return Mathf.Max(a1, Mathf.Max(a2, a3));
        }

        // ---------- Polozenie ----------

        private void Place(float dt)
        {
            k = Mathf.Max(0.05f, charH * PigeonsMod.PSize.Value) / Mathf.Max(0.05f, A.MaxY - A.MinY);
            float pivotUp = footDrop - (A.Pivot.y - A.MinY) * k + PigeonsMod.PHeight.Value * charH;

            float tLean = Mathf.Clamp(speed * 2.4f, 0f, 15f) * walk;
            leanDeg = Mathf.Lerp(leanDeg, tLean, 1f - Mathf.Exp(-dt * 6f));

            rootT.position = hips.position + upv * pivotUp;
            rootT.rotation = frameRot * Quaternion.AngleAxis(leanDeg, Vector3.right);
            modelT.localScale = new Vector3(k, k, k);
            modelT.localPosition = -A.Pivot * k;
        }

        // ---------- Animacja szkieletu ----------

        private void Animate(float dt)
        {
            bool canIdle = !knocked && !airborne && !fighting && !hurt && !straining && !swimming && speed < 0.5f;

            // dziobanie
            if (canIdle) idleTime += dt; else { idleTime = 0f; peckT = -1f; }
            if (peckT < 0f && canIdle && idleTime > nextPeckAt)
            {
                peckT = 0f; peckN = UnityEngine.Random.Range(2, 5);
                nextPeckAt = idleTime + UnityEngine.Random.Range(5f, 11f);
            }
            float peck = 0f;
            if (peckT >= 0f)
            {
                const float cyc = 0.42f;
                peckT += dt;
                if (peckT > peckN * cyc) peckT = -1f;
                else peck = Mathf.Pow(Mathf.Sin(Mathf.PI * ((peckT % cyc) / cyc)), 0.7f);
            }

            // rozgladanie sie (szybkie "zatrzymane" ruchy glowy)
            lookTimer -= dt;
            if (lookTimer <= 0f)
            {
                lookTimer = UnityEngine.Random.Range(0.7f, 3.0f);
                lookTarget = UnityEngine.Random.value < 0.3f ? 0f : UnityEngine.Random.Range(-50f, 50f);
            }
            neckYaw = Mathf.Lerp(neckYaw, lookTarget * (1f - 0.85f * walk), 1f - Mathf.Exp(-dt * 16f));

            // kiwanie glowa przy chodzie: szybkie wysuniecie do przodu, potem powolny powrot (glowa "stoi" w miejscu wzgledem swiata)
            float p01 = (phase / (Mathf.PI * 2f)); p01 -= Mathf.Floor(p01);
            float bob = p01 < 0.25f ? Mathf.Lerp(-1f, 1f, Mathf.SmoothStep(0f, 1f, p01 / 0.25f)) : Mathf.Lerp(1f, -1f, (p01 - 0.25f) / 0.75f);
            bobZ = Mathf.Lerp(bobZ, bob * 0.030f * walk, 1f - Mathf.Exp(-dt * 30f));

            // gruchanie: nadymanie szyi i podnoszenie glowy w rytm dzwieku
            float cooEnv = CooEnv();

            float breathe = 0.012f * Mathf.Sin(time * 2.1f + seed);

            for (int b = 0; b < A.NB; b++)
            {
                Vector3 p; Quaternion q;
                A.Sample(cIdle, time * 0.9f, b, out p, out q);
                int ws = wingSide[b];
                bool bodyBone = b == iThorax || b == iTail1;

                if (wFlap > 0.001f && (ws != 0 || bodyBone))
                {
                    Vector3 fp; Quaternion fq;
                    A.Sample(cFlap, flapT, b, out fp, out fq);
                    p = Vector3.Lerp(p, fp, wFlap);
                    q = Quaternion.Slerp(q, fq, wFlap);
                }
                if (ws != 0)
                {
                    Vector3 gp; Quaternion gq;
                    A.Sample(cFold, 0f, b, out gp, out gq);
                    float o = ws > 0 ? wOpenR : wOpenL;
                    p = Vector3.Lerp(gp, p, o);
                    q = Quaternion.Slerp(gq, q, o);
                }
                int lk = legKind[b];
                if (lk != 0)
                {
                    if (wTuck > 0.001f)
                    {
                        Vector3 tp; Quaternion tq;
                        A.Sample(cTuck, 99f, b, out tp, out tq);
                        p = Vector3.Lerp(p, tp, wTuck);
                        q = Quaternion.Slerp(q, tq, wTuck);
                    }
                    float wk = walk * (1f - wTuck);
                    if (wk > 0.01f)
                    {
                        float ph = phase + (legSide[b] > 0 ? Mathf.PI : 0f);
                        if (lk == 1) q = Quaternion.AngleAxis(-34f * Mathf.Sin(ph) * wk, A.AxLat[b]) * q;
                        else q = Quaternion.AngleAxis(42f * Mathf.Max(0f, Mathf.Cos(ph)) * wk, A.AxLat[b]) * q;
                    }
                }

                Vector3 sc = Vector3.one;
                if (b == iThorax)
                {
                    q = Quaternion.AngleAxis(10f * peck, A.AxLat[b]) * q;
                    float s = 1f + breathe + 0.14f * hurtPuff;
                    sc = new Vector3(s, s, s);
                }
                else if (b == iNeck)
                {
                    float pitch = 58f * peck - 14f * cooEnv + 4f * Mathf.Sin(time * 1.3f + seed);
                    q = Quaternion.AngleAxis(pitch, A.AxLat[b]) * Quaternion.AngleAxis(neckYaw, A.AxUp[b]) * q;
                    p += A.AxFwd[b] * bobZ;
                    float s = 1f + 0.24f * cooEnv + 0.1f * hurtPuff;
                    sc = new Vector3(s, s, s);
                }
                else if (b == iFace)
                {
                    float pitch = 24f * peck - 6f * cooEnv - 0.25f * 58f * peck * 0f;
                    q = Quaternion.AngleAxis(pitch, A.AxLat[b]) * Quaternion.AngleAxis(-0.25f * neckYaw, A.AxUp[b]) * q;
                }
                else if (b == iTail1)
                {
                    q = Quaternion.AngleAxis(-8f * cooEnv - 5f * hurtPuff, A.AxLat[b]) * q;
                }

                bones[b].localPosition = p;
                bones[b].localRotation = q;
                bones[b].localScale = sc;
            }
        }

        // ---------- Dzwieki ----------

        private float CooEnv()
        {
            if (cooSnd == null || Time.time >= cooUntil) return 0f;
            int idx = (int)((Time.time - cooStart) * 50f);
            if (idx < 0 || idx >= cooSnd.Env.Length) return 0f;
            return cooSnd.Env[idx];
        }

        private PSound Pick(string cat)
        {
            List<PSound> list;
            if (!A.Sounds.TryGetValue(cat, out list) || list.Count == 0) return null;
            int i = UnityEngine.Random.Range(0, list.Count);
            int last;
            if (list.Count > 1 && lastPick.TryGetValue(cat, out last) && last == i) i = (i + 1) % list.Count;
            lastPick[cat] = i;
            return list[i];
        }

        private void Say(string cat, float vol)
        {
            if (cooSrc == null || PigeonsMod.PVolume.Value <= 0.001f) return;
            var s = Pick(cat);
            if (s == null) return;
            try
            {
                cooSrc.pitch = basePitch * UnityEngine.Random.Range(0.96f, 1.05f);
                cooSrc.PlayOneShot(s.Clip, Mathf.Clamp01(vol * M.VolumeScale()));
                cooSnd = s; cooStart = Time.time; cooUntil = Time.time + s.Dur / Mathf.Max(0.5f, cooSrc.pitch);
            }
            catch (Exception e) { if (!sayErr) { sayErr = true; MelonLogger.Warning("[Pigeons] odtwarzanie dzwieku: " + e.Message); } }
        }
        private bool sayErr;

        private void Flap(float vol)
        {
            if (sfxSrc == null || PigeonsMod.PVolume.Value <= 0.001f) return;
            var s = Pick("flap");
            if (s == null) return;
            try
            {
                sfxSrc.pitch = UnityEngine.Random.Range(0.95f, 1.08f);
                sfxSrc.PlayOneShot(s.Clip, Mathf.Clamp01(vol * M.VolumeScale()));
            }
            catch { }
        }

        private void UpdateSounds(float dt)
        {
            float now = Time.time;

            // glos postaci z gry -> dzwiek golebia (oryginalny jest wyciszony)
            bool voiceStart = false;
            for (int i = 0; i < voices.Count; i++)
            {
                bool playing = false;
                try { var a = voices[i]; playing = a != null && a.isPlaying; } catch { }
                if (playing && !voiceWas[i]) voiceStart = true;
                voiceWas[i] = playing;
            }
            if (voiceStart && PigeonsMod.PReplaceVoice.Value && now - lastVoiceAct > 0.25f)
            {
                lastVoiceAct = now;
                if (knocked) Say("sleep", 0.9f);
                else if (hurt || falling || airborne) Say("hurt", 1f);
                else if (fighting) Say("angry", 1f);
                else Say(UnityEngine.Random.value < 0.25f ? "laugh" : "coo", 0.95f);
            }

            // zdarzenia
            if (fighting && !wasFighting && now - lastVoiceAct > 0.5f && UnityEngine.Random.value < 0.65f) { lastVoiceAct = now; Say("angry", 1f); }
            if (hurt && !wasHurt && now - lastVoiceAct > 0.3f) { lastVoiceAct = now; Say("hurt", 1f); }
            if (jumping && !wasJumping) { Flap(0.8f); if (now - lastVoiceAct > 0.4f) { lastVoiceAct = now; Say("coo", 0.8f); } }
            if (knocked && !wasKnocked) { lastVoiceAct = now; Say("sleep", 0.9f); }
            wasFighting = fighting; wasHurt = hurt; wasJumping = jumping; wasKnocked = knocked;

            // machanie skrzydlami - dzwiek co ~0.85 s (dlugosc nagrania), dopoki macha
            bool flapping = wFlap > 0.5f;
            if (flapping && now >= flapSfxAt) { Flap(0.85f); flapSfxAt = now + 0.9f; }
            wasFlapping = flapping;

            // losowe gruchanie
            if (now >= nextCoo)
            {
                float every = Mathf.Max(2f, PigeonsMod.PCooEvery.Value);
                nextCoo = now + every * UnityEngine.Random.Range(0.5f, 1.5f);
                if (!knocked && !fighting && !hurt && !airborne && now - lastVoiceAct > 1.2f)
                {
                    lastVoiceAct = now;
                    Say("coo", 0.9f);
                }
            }

            // automatyczna kalibracja kierunku "przodu" na podstawie ruchu (gdy golab idzie tylem, odwracamy)
            flipTimer -= dt;
            if (flipTimer <= 0f)
            {
                flipTimer = 0.2f;
                if (!knocked && !airborne && !falling && speed > 2.2f && upv.y > 0.7f)
                {
                    Vector3 fh = new Vector3(fwdv.x, 0f, fwdv.z);
                    Vector3 vh = new Vector3(vel.x, 0f, vel.z);
                    if (fh.sqrMagnitude > 0.01f && vh.sqrMagnitude > 0.01f)
                    {
                        float d = Vector3.Dot(fh.normalized, vh.normalized);
                        M.Vote(d > 0.35f ? 1 : (d < -0.35f ? -1 : 0));
                    }
                }
            }
        }

        // ---------- Sprzatanie ----------

        // przywraca oryginalna postac (wlacza siatke, odcisza glos)
        public void Restore()
        {
            foreach (var r in hidden)
            {
                try { if (r != null) r.enabled = true; } catch { }
            }
            hidden.Clear();
            for (int i = 0; i < voices.Count; i++)
            {
                try { if (voices[i] != null && voiceMutedByUs[i]) voices[i].mute = false; } catch { }
                voiceMutedByUs[i] = false;
            }
        }

        public void Destroy()
        {
            try { Restore(); } catch { }
            try { if (root != null) UnityEngine.Object.Destroy(root); } catch { }
            root = null;
            Dead = true;
        }
    }
}
