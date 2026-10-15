using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace GBFaces
{
    // Twarz jednej postaci: usta (z jezykiem i zebami), dwie brwi przyczepione do glowy.
    // Zbudowana z wbudowanych ksztaltow Unity (kula, szescian) - bez wlasnych siatek,
    // bo te w IL2CPP wymagaja tablic Il2Cpp.
    internal class FaceController
    {
        private const int LipSegments = 13;
        private const int BrowSegments = 7;
        private static bool dumped;
        private static Shader cachedShader;
        private static bool shaderSearched;

        public Component Actor;
        public Transform Head;
        public float Laugh;          // ile sekund jeszcze sie smieje

        private GameObject root;
        private Transform mouth, tongue, teeth;
        private Renderer mouthR, tongueR, teethR;
        private readonly Transform[] lipU = new Transform[LipSegments];
        private readonly Transform[] lipL = new Transform[LipSegments];
        private readonly Renderer[] lipLR = new Renderer[LipSegments];
        private readonly Transform[] browLT = new Transform[BrowSegments];
        private readonly Transform[] browRT = new Transform[BrowSegments];

        private float headDiameter = 0.4f;
        private Rigidbody rbL, rbR, rbChest;
        private readonly List<AudioSource> voices = new List<AudioSource>();
        private float punchHold, hurtHold;
        private Vector3 prevVel;
        private bool hasPrevVel;
        private float seed;
        private string lastState = "";
        private static int stateLogCount, hurtLogCount;

        // aktualne (wygladzone) wartosci mimiki
        private float open, smile, angry, raiseL, raiseR, wide, pucker;
        private float time;
        private bool wasKnocked;
        private bool loggedFit;

        // drobne "zycie" na postoju
        private float idleLeft, idleWait = 2f;
        private int idleKind;

        private MemberInfo stateMember;
        private readonly List<MemberInfo> angerFlags = new List<MemberInfo>();
        private readonly List<MemberInfo> strainFlags = new List<MemberInfo>();
        private readonly bool hasActorLogic;

        private FaceController(Component actor, Transform head, bool hasActorLogic)
        {
            Actor = actor;
            Head = head;
            this.hasActorLogic = hasActorLogic;
            seed = UnityEngine.Random.value * 100f;
            idleWait = 1.5f + UnityEngine.Random.value * 3f;
        }

        // ---------- Tworzenie ----------

        public static FaceController Create(Component actor, bool isActor, MelonLogger.Instance log)
        {
            Transform head = isActor ? FindHead(actor.transform) : actor.transform;
            if (head == null)
            {
                log.Warning("Postac '" + actor.name + "' - nie znaleziono kosci glowy (szukam nazwy z 'head').");
                if (!dumped) { dumped = true; Dump(actor.transform, log); }
                return null;
            }

            var fc = new FaceController(actor, head, isActor);
            try
            {
                fc.Build();
            }
            catch (Exception e)
            {
                log.Error("Nie udalo sie zbudowac twarzy: " + e);
                try { fc.Destroy(); } catch { }
                return null;
            }
            try { fc.Measure(log); }
            catch (Exception e) { log.Warning("Pomiar glowy/rak nie powiodl sie: " + e.Message); }
            if (isActor)
            {
                try { fc.Reflect(log); }
                catch (Exception e) { log.Warning("Nie udalo sie odczytac stanu postaci (twarz bedzie statyczna): " + e.Message); }
            }

            log.Msg("Dodano twarz: " + actor.name + " -> " + Path(head));
            if (!dumped)
            {
                dumped = true;
                Dump(head, log);
            }
            return fc;
        }

        private void Build()
        {
            root = new GameObject("GBFace");
            root.transform.SetParent(Head, false);
            root.layer = Head.gameObject.layer;

            var inside = new Color(0.20f, 0.015f, 0.04f);
            var black = new Color(0.02f, 0.02f, 0.02f);
            var white = new Color(0.97f, 0.96f, 0.90f);
            var pink = new Color(0.86f, 0.28f, 0.36f);
            Renderer tmp;

            // kolejnosc rysowania (sortingOrder): wnetrze < jezyk < zeby < wargi i brwi
            mouth = Prim("Mouth", PrimitiveType.Sphere, inside, 0, out mouthR);
            tongue = Prim("Tongue", PrimitiveType.Sphere, pink, 1, out tongueR);
            teeth = Prim("Teeth", PrimitiveType.Cube, white, 2, out teethR);
            for (int i = 0; i < LipSegments; i++)
            {
                lipU[i] = Prim("LipU" + i, PrimitiveType.Sphere, black, 3, out tmp);
                lipL[i] = Prim("LipL" + i, PrimitiveType.Sphere, black, 3, out lipLR[i]);
            }
            for (int i = 0; i < BrowSegments; i++)
            {
                browLT[i] = Prim("BrowL" + i, PrimitiveType.Sphere, black, 3, out tmp);
                browRT[i] = Prim("BrowR" + i, PrimitiveType.Sphere, black, 3, out tmp);
            }
        }

        private Transform Prim(string name, PrimitiveType type, Color color, int order, out Renderer rend)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;

            // zadnych kolizji - twarz ma tylko wygladac
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);

            go.transform.SetParent(root.transform, false);
            go.layer = root.layer;

            var r = go.GetComponent<Renderer>();
            rend = r;
            if (r != null)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                try { r.sortingOrder = order; } catch { }
                Colorize(r, color);
            }
            return go.transform;
        }

        private static void Colorize(Renderer r, Color c)
        {
            if (!shaderSearched)
            {
                shaderSearched = true;
                string[] names = { "Unlit/Color", "Sprites/Default", "Standard", "Legacy Shaders/Diffuse" };
                foreach (var n in names)
                {
                    var sh = Shader.Find(n);
                    if (sh != null) { cachedShader = sh; break; }
                }
                MelonLogger.Msg("[Faces] shader twarzy: " + (cachedShader != null ? cachedShader.name : "(zaden nie znaleziony - uzywam domyslnego materialu)"));
            }

            Material m;
            if (cachedShader != null)
            {
                m = new Material(cachedShader);
                m.color = c;
                r.sharedMaterial = m;
            }
            else
            {
                m = r.material;     // domyslny material ksztaltu
                m.color = c;
            }
        }

        // ---------- Pomiary: rozmiar glowy, rece (do wykrywania bicia), glos, orientacja ----------

        private static Transform FindByName(Transform start, string name)
        {
            var queue = new Queue<Transform>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                if (t.name == name) return t;
                for (int i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
            }
            return null;
        }

        private void Measure(MelonLogger.Instance log)
        {
            // rozmiar twarzy = srednica collidera glowy
            var col = Head.GetComponent<Collider>();
            if (col != null)
            {
                Vector3 size = col.bounds.size;
                float d = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                if (d > 0.05f && d < 3f) headDiameter = d;
            }

            if (!hasActorLogic) return;
            Transform top = Actor.transform;

            var chest = FindByName(top, "actor_chest_collider");
            var lh = FindByName(top, "actor_leftHand_collider");
            var rh = FindByName(top, "actor_rightHand_collider");
            if (chest != null) rbChest = chest.GetComponent<Rigidbody>();
            if (lh != null) rbL = lh.GetComponent<Rigidbody>();
            if (rh != null) rbR = rh.GetComponent<Rigidbody>();

            log.Msg("Srednica glowy: " + headDiameter.ToString("0.00") + ", rece: " + ((rbL != null && rbR != null) ? "ok" : "brak") + ", klatka: " + (rbChest != null ? "ok" : "brak"));

            FindVoices(top, log);

            if (FaceMod.AutoFwd < 0 && !FaceMod.PManual.Value)
                Calibrate(top, log);
        }

        // Zrodla dzwieku "glosu" postaci (helper_voiceBox) - gdy graja, usta sie ruszaja
        private void FindVoices(Transform top, MelonLogger.Instance log)
        {
            try
            {
                var queue = new Queue<Transform>();
                queue.Enqueue(top);
                int visited = 0;
                while (queue.Count > 0 && visited < 200)
                {
                    var t = queue.Dequeue();
                    visited++;
                    if (t.name.ToLowerInvariant().Contains("voice"))
                    {
                        var a = t.GetComponent<AudioSource>();
                        if (a != null) voices.Add(a);
                    }
                    for (int i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
                }
                log.Msg("Zrodla glosu (AudioSource): " + voices.Count);
            }
            catch (Exception e)
            {
                log.Warning("Nie udalo sie znalezc zrodel glosu: " + e.Message);
            }
        }

        // Orientacja twarzy z ukladu ciala: prawo = lewe ramie -> prawe ramie, gora = biodra -> glowa,
        // przod = prawo x gora. Potem wybieramy osie lokalne glowy najblizsze tym kierunkom.
        private void Calibrate(Transform top, MelonLogger.Instance log)
        {
            var la = FindByName(top, "actor_leftArm_collider");
            var ra = FindByName(top, "actor_rightArm_collider");
            var hips = FindByName(top, "actor_hips_collider");
            if (la == null || ra == null || hips == null)
            {
                log.Warning("Auto-orientacja: nie znaleziono kosci ramion/bioder.");
                return;
            }
            Vector3 right = ra.position - la.position;
            Vector3 upv = Head.position - hips.position;
            if (right.magnitude < 0.01f || upv.magnitude < 0.01f) return;
            right.Normalize(); upv.Normalize();
            Vector3 fwdv = Vector3.Cross(right, upv).normalized;

            int bestF = 0; float bf = -2f;
            for (int i = 0; i < 6; i++)
            {
                float d = Vector3.Dot(Head.TransformDirection(FaceMod.Axes[i]), fwdv);
                if (d > bf) { bf = d; bestF = i; }
            }
            int bestU = 0; float bu = -2f;
            for (int i = 0; i < 6; i++)
            {
                if (i / 2 == bestF / 2) continue;
                float d = Vector3.Dot(Head.TransformDirection(FaceMod.Axes[i]), upv);
                if (d > bu) { bu = d; bestU = i; }
            }
            FaceMod.AutoFwd = bestF;
            FaceMod.AutoUp = bestU;
            log.Msg("Auto-orientacja twarzy: przod=" + bestF + " (zgodnosc " + bf.ToString("0.00") + "), gora=" + bestU + " (zgodnosc " + bu.ToString("0.00") + "). Jesli twarz jest z tylu glowy, nacisnij F6.");
        }

        // Jednorazowa diagnostyka: gdzie wzgledem twarzy jest widoczna glowa i "usta" gry
        private void LogFit()
        {
            loggedFit = true;
            try
            {
                if (!hasActorLogic || root == null) return;
                Transform top = Actor.transform;
                float unit = Mathf.Max(0.0001f, headDiameter * FaceMod.PSize.Value);

                var hm = FindByName(top, "actor_head_skinnedMesh");
                if (hm != null)
                {
                    var r = hm.GetComponent<Renderer>();
                    if (r != null)
                    {
                        Bounds b = r.bounds;
                        Vector3 c = root.transform.InverseTransformPoint(b.center);
                        Vector3 e = b.extents / unit;
                        MelonLogger.Msg("[Faces] siatka glowy: srodek (x=" + c.x.ToString("0.00") + ", y=" + c.y.ToString("0.00") + ", z=" + c.z.ToString("0.00")
                            + "), polowa rozmiaru (" + e.x.ToString("0.00") + ", " + e.y.ToString("0.00") + ", " + e.z.ToString("0.00") + ") - w jednostkach twarzy");
                    }
                }
                var vb = FindByName(top, "helper_voiceBox");
                if (vb != null)
                {
                    Vector3 p = root.transform.InverseTransformPoint(vb.position);
                    MelonLogger.Msg("[Faces] helper_voiceBox: x=" + p.x.ToString("0.00") + ", y=" + p.y.ToString("0.00") + ", z=" + p.z.ToString("0.00"));
                }
            }
            catch { }
        }

        // ---------- Odczyt stanu postaci przez refleksje ----------

        private void Reflect(MelonLogger.Instance log)
        {
            const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = Actor.GetType();
            var seen = new List<string>();

            // stan postaci: najpierw "actorState", potem cokolwiek enumowego z "state" w nazwie
            foreach (var f in t.GetFields(bf))
                if (string.Equals(f.Name, "actorState", StringComparison.OrdinalIgnoreCase)) stateMember = f;
            foreach (var p in t.GetProperties(bf))
                if (stateMember == null && p.CanRead && p.GetIndexParameters().Length == 0
                    && string.Equals(p.Name, "actorState", StringComparison.OrdinalIgnoreCase)) stateMember = p;
            if (stateMember == null)
            {
                foreach (var p in t.GetProperties(bf))
                {
                    if (p.CanRead && p.GetIndexParameters().Length == 0 && p.PropertyType.IsEnum
                        && p.Name.ToLowerInvariant().Contains("state")) { stateMember = p; break; }
                }
            }
            if (stateMember == null)
            {
                foreach (var f in t.GetFields(bf))
                    if (f.FieldType.IsEnum && f.Name.ToLowerInvariant().Contains("state")) { stateMember = f; break; }
            }

            foreach (var f in t.GetFields(bf))
                if (f.FieldType == typeof(bool)) { ClassifyFlag(f, f.Name); seen.Add(f.Name); }
            foreach (var p in t.GetProperties(bf))
                if (p.PropertyType == typeof(bool) && p.CanRead && p.GetIndexParameters().Length == 0)
                { ClassifyFlag(p, p.Name); seen.Add(p.Name); }

            log.Msg("Stan postaci: " + (stateMember != null ? stateMember.Name : "(nie znaleziono)")
                + ", flagi gniewu: " + angerFlags.Count + ", flagi wysilku: " + strainFlags.Count);

            // jednorazowo: nazwy czlonkow typu bool/enum, zeby latwo dopasowac reakcje
            if (!membersLogged)
            {
                membersLogged = true;
                var enums = new List<string>();
                foreach (var p in t.GetProperties(bf))
                    if (p.PropertyType.IsEnum) enums.Add(p.Name + ":" + p.PropertyType.Name);
                log.Msg("Czlonki enum: " + string.Join(", ", enums.ToArray()));
                try
                {
                    if (stateMember != null)
                    {
                        Type st = stateMember is PropertyInfo ? ((PropertyInfo)stateMember).PropertyType : ((FieldInfo)stateMember).FieldType;
                        log.Msg("Wartosci " + st.Name + ": " + string.Join(", ", Enum.GetNames(st)));
                    }
                    var related = new List<string>();
                    foreach (var ty in t.Assembly.GetTypes())
                    {
                        string n = ty.Name.ToLowerInvariant();
                        if (n.Contains("punch") || n.Contains("grab") || n.Contains("fight") || n.Contains("attack") || n.Contains("hit") || n.Contains("knock") || n.Contains("damage"))
                            related.Add(ty.Name);
                        if (related.Count >= 40) break;
                    }
                    log.Msg("Typy zwiazane z walka: " + string.Join(", ", related.ToArray()));
                    var objs = new List<string>();
                    foreach (var p in t.GetProperties(bf))
                        if (p.PropertyType.Namespace != null && p.PropertyType.Namespace.Contains("Femur")) objs.Add(p.Name + ":" + p.PropertyType.Name);
                    if (objs.Count > 60) objs.RemoveRange(60, objs.Count - 60);
                    log.Msg("Skladniki postaci: " + string.Join(", ", objs.ToArray()));

                    // do dalszego dopracowania mimiki (chwytanie / zdrowie) - zrzut czlonkow tych klas
                    DumpMembers(t.Assembly, "Grab", log);
                    DumpMembers(t.Assembly, "StatusHandeler", log);
                }
                catch (Exception ex) { log.Warning("Dodatkowy zrzut typow nie powiodl sie: " + ex.Message); }
                if (seen.Count > 80) seen.RemoveRange(80, seen.Count - 80);
                log.Msg("Czlonki bool: " + string.Join(", ", seen.ToArray()));
            }
        }

        private static bool membersLogged;

        private static void DumpMembers(Assembly asm, string typeName, MelonLogger.Instance log)
        {
            try
            {
                Type found = null;
                foreach (var ty in asm.GetTypes())
                    if (ty.Name == typeName) { found = ty; break; }
                if (found == null) { log.Msg("Czlonki " + typeName + ": (nie znaleziono typu)"); return; }
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var names = new List<string>();
                foreach (var f in found.GetFields(bf)) names.Add(f.Name + ":" + f.FieldType.Name);
                foreach (var p in found.GetProperties(bf)) names.Add(p.Name + ":" + p.PropertyType.Name);
                if (names.Count > 70) names.RemoveRange(70, names.Count - 70);
                log.Msg("Czlonki " + typeName + ": " + string.Join(", ", names.ToArray()));
            }
            catch (Exception e) { log.Warning("Zrzut " + typeName + " nie powiodl sie: " + e.Message); }
        }

        private void ClassifyFlag(MemberInfo m, string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("punch") || n.Contains("attack") || n.Contains("fight") || n.Contains("kick"))
                angerFlags.Add(m);
            else if (n.Contains("grab") || n.Contains("hold") || n.Contains("carry") || n.Contains("climb"))
                strainFlags.Add(m);
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

        // ---------- Aktualizacja ----------

        public bool IsAlive()
        {
            return Actor != null && Head != null && root != null;
        }

        public void Destroy()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
        }

        private bool IsTalking()
        {
            for (int i = 0; i < voices.Count; i++)
            {
                try
                {
                    var a = voices[i];
                    if (a != null && a.isPlaying) return true;
                }
                catch { }
            }
            return false;
        }

        // Zwraca true w klatce, w ktorej postac wlasnie dostala nokaut
        public bool Tick(float dt)
        {
            time += dt;
            if (Laugh > 0f) Laugh -= dt;
            if (!loggedFit && time > 0.4f) LogFit();

            string state = "";
            if (hasActorLogic && stateMember != null)
            {
                var v = Read(stateMember, Actor);
                if (v != null) state = v.ToString().ToLowerInvariant();
            }

            if (state != lastState)
            {
                if (stateLogCount < 30)
                {
                    stateLogCount++;
                    MelonLogger.Msg("[Faces] stan postaci: '" + lastState + "' -> '" + state + "'");
                }
                lastState = state;
            }

            bool knocked = state.Contains("unconscious") || state.Contains("dead") || state.Contains("knock") || state == "ko";
            bool falling = state.Contains("fall") || state.Contains("ragdoll");
            bool jumping = state.Contains("jump");
            bool climbing = state.Contains("climb");
            bool swimming = state.Contains("swim");
            bool driving = state.Contains("drive");
            bool running = state.Contains("run");
            bool fighting = hasActorLogic && AnyTrue(angerFlags, Actor);

            // predkosc korpusu + wykrywanie uderzen (gwaltowna zmiana predkosci klatki piersiowej)
            float speed = 0f;
            if (rbChest != null)
            {
                Vector3 cv = rbChest.velocity;
                speed = new Vector3(cv.x, 0f, cv.z).magnitude;
                if (hasPrevVel && dt > 0.0005f && !knocked)
                {
                    float accel = (cv - prevVel).magnitude / dt;
                    if (accel > FaceMod.PHitThr.Value)
                    {
                        hurtHold = 0.4f;
                        if (hurtLogCount < 12)
                        {
                            hurtLogCount++;
                            MelonLogger.Msg("[Faces] uderzenie/wstrzas: przyspieszenie " + accel.ToString("0") + " (prog " + FaceMod.PHitThr.Value.ToString("0") + ")");
                        }
                    }
                }
                prevVel = cv;
                hasPrevVel = true;
            }

            // bicie: rece poruszaja sie szybko wzgledem klatki piersiowej
            if (rbChest != null && (rbL != null || rbR != null))
            {
                Vector3 cv2 = rbChest.velocity;
                float rel = 0f;
                if (rbL != null) rel = Mathf.Max(rel, (rbL.velocity - cv2).magnitude);
                if (rbR != null) rel = Mathf.Max(rel, (rbR.velocity - cv2).magnitude);
                if (rel > 5f) punchHold = 0.6f;
            }
            if (punchHold > 0f) { punchHold -= dt; if (!falling) fighting = true; }
            bool hurt = hurtHold > 0f;
            if (hurt) hurtHold -= dt;
            bool straining = hasActorLogic && AnyTrue(strainFlags, Actor);
            bool talking = IsTalking();

            bool plain = !(knocked || hurt || fighting || talking || Laugh > 0f || jumping || falling
                           || climbing || swimming || driving || straining || running);

            // losowe drobne reakcje na postoju (zeby twarz nie byla martwa)
            if (plain)
            {
                if (idleLeft > 0f) idleLeft -= dt;
                else
                {
                    idleWait -= dt;
                    if (idleWait <= 0f)
                    {
                        idleKind = UnityEngine.Random.Range(0, 5);
                        idleLeft = 0.7f + UnityEngine.Random.value * 0.9f;
                        idleWait = 2.5f + UnityEngine.Random.value * 4f;
                    }
                }
            }
            else idleLeft = 0f;

            float nz = Mathf.PerlinNoise(time * 9f, seed);
            float breathe = 0.5f + 0.5f * Mathf.Sin(time * 1.7f + seed);

            float tOpen = 0f, tSmile = 0.3f + 0.1f * breathe, tAngry = 0f, tRaiseL = 0f, tRaiseR = 0f, tWide = 0f, tPucker = 0f;
            float rate = 14f;

            if (knocked)
            {
                tOpen = 0.85f + 0.12f * Mathf.Sin(time * 2.2f + seed);   // szeroko otwarte, wiotkie
                tSmile = -0.2f; tAngry = -0.3f; tRaiseL = 1f; tRaiseR = 1f;
            }
            else if (hurt)
            {
                tOpen = 0.9f; tSmile = -0.8f; tAngry = -0.6f; tRaiseL = 1f; tRaiseR = 1f;   // "au!"
                rate = 28f;
            }
            else if (fighting)
            {
                tOpen = 0.2f; tSmile = -0.9f; tAngry = 1f;               // zacisniete zeby, zly
                rate = 22f;
            }
            else if (talking)
            {
                tOpen = 0.15f + 0.65f * Mathf.Abs(Mathf.Sin(time * 11f + seed + nz * 4f));   // mowi / krzyczy
                tSmile = 0.15f; tRaiseL = 0.2f + 0.4f * nz; tRaiseR = tRaiseL;
                rate = 26f;
            }
            else if (Laugh > 0f)
            {
                tOpen = 0.45f + 0.4f * Mathf.Abs(Mathf.Sin(time * 13f + seed));   // smiech
                tSmile = 1f; tWide = 1f; tRaiseL = 0.5f; tRaiseR = 0.5f;
                rate = 24f;
            }
            else if (jumping)
            {
                tOpen = 0.75f; tSmile = 0.5f; tWide = 0.6f; tRaiseL = 0.9f; tRaiseR = 0.9f;   // "hop!"
                rate = 20f;
            }
            else if (falling)
            {
                tOpen = 0.8f + 0.1f * Mathf.Sin(time * 18f); tSmile = -0.4f; tAngry = -0.3f;  // "aaa!"
                tRaiseL = 1f; tRaiseR = 1f;
                rate = 20f;
            }
            else if (climbing || straining)
            {
                tOpen = 0.12f; tSmile = -0.7f; tAngry = 0.7f;            // wysilek
            }
            else if (swimming)
            {
                tPucker = 1f; tOpen = 0.3f + 0.15f * Mathf.Abs(Mathf.Sin(time * 5f)); tRaiseL = 0.4f; tRaiseR = 0.4f;
            }
            else if (driving)
            {
                tSmile = 0.9f; tWide = 0.8f;
            }
            else if (running)
            {
                float f = Mathf.Clamp(speed / 4f, 0.4f, 1f);              // sapie w rytm biegu
                tOpen = 0.1f + 0.22f * f * Mathf.Abs(Mathf.Sin(time * (7f + speed * 1.5f) + seed));
                tSmile = 0.45f; tWide = 0.3f; tRaiseL = 0.15f; tRaiseR = 0.15f;
            }
            else if (idleLeft > 0f)
            {
                switch (idleKind)
                {
                    case 0:     // cos mruczy
                        tOpen = 0.15f + 0.4f * Mathf.Abs(Mathf.Sin(time * 9f + seed)); tSmile = 0.25f;
                        rate = 20f;
                        break;
                    case 1:     // "hmm?" - jedna brew w gore
                        tRaiseL = 1f; tSmile = 0.5f;
                        break;
                    case 2:     // szeroki usmiech
                        tSmile = 1f; tWide = 1f; tOpen = 0.18f; tRaiseL = 0.3f; tRaiseR = 0.3f;
                        break;
                    case 3:     // "ooo"
                        tPucker = 1f; tOpen = 0.3f; tRaiseL = 0.6f; tRaiseR = 0.6f;
                        break;
                    default:    // podejrzliwie
                        tAngry = 0.7f; tSmile = 0.1f; tRaiseR = 0.9f;
                        break;
                }
            }

            float k = 1f - Mathf.Exp(-rate * dt);
            open = Mathf.Lerp(open, tOpen, k);
            smile = Mathf.Lerp(smile, tSmile, k);
            angry = Mathf.Lerp(angry, tAngry, k);
            raiseL = Mathf.Lerp(raiseL, tRaiseL, k);
            raiseR = Mathf.Lerp(raiseR, tRaiseR, k);
            wide = Mathf.Lerp(wide, tWide, k);
            pucker = Mathf.Lerp(pucker, tPucker, k);

            Apply();

            bool newKo = knocked && !wasKnocked;
            wasKnocked = knocked;
            return newKo;
        }

        // Pozycja na lekko wygietej "kuli" glowy: z maleje ku brzegom (cy = wysokosc srodka kuli)
        private static Vector3 Pos(float x, float y, float push, float eps, float cy)
        {
            float dy = y - cy;
            float z = push - (x * x + dy * dy) / (2f * push) + eps;
            return new Vector3(x, y, z);
        }

        private void Apply()
        {
            int fwd = Mathf.Clamp(FaceMod.EffFwd, 0, 5);
            int up = Mathf.Clamp(FaceMod.EffUp, 0, 5);
            if (up / 2 == fwd / 2) up = (fwd + 2) % 6;

            var rt = root.transform;
            rt.localRotation = Quaternion.LookRotation(FaceMod.Axes[fwd], FaceMod.Axes[up]);
            float s = Mathf.Max(0.0001f, Head.lossyScale.x);
            rt.localScale = Vector3.one * (headDiameter * FaceMod.PSize.Value / s);
            rt.localPosition = Vector3.zero;

            float push = Mathf.Max(0.1f, FaceMod.PPush.Value);
            float fy = FaceMod.PFaceY.Value;            // przesuniecie calej twarzy w gore/dol
            float my = FaceMod.PMouthY.Value + fy;
            float by = FaceMod.PBrowY.Value + fy;
            float bx = FaceMod.PBrowX.Value;
            float cy = fy;                              // srodek krzywizny przesuwa sie razem z twarza

            // ----- wymiary ust -----
            float openH = open * 0.36f;
            float mouthW = Mathf.Clamp(0.2f + 0.06f * Mathf.Max(0f, smile) + 0.03f * open + 0.05f * wide - 0.07f * pucker, 0.1f, 0.3f);
            float curveAmp = smile * 0.075f;            // + usmiech (kaciki w gore), - skrzywione w dol
            bool showMouth = openH > 0.04f;

            // wnetrze ust (ciemna, splaszczona kula)
            if (mouthR != null) mouthR.enabled = showMouth;
            if (showMouth)
            {
                float cyMouth = my - curveAmp * 0.17f - openH * 0.5f;
                mouth.localPosition = Pos(0f, cyMouth, push, 0.004f, cy);
                mouth.localScale = new Vector3(mouthW * 1.96f, openH, 0.05f);
            }

            // zeby (biale, u gory wnetrza) - widac je tez przy zacisnietych ustach (gniew)
            bool showTeeth = showMouth && openH > 0.045f;
            if (teethR != null) teethR.enabled = showTeeth;
            if (showTeeth)
            {
                float th2 = Mathf.Min(0.06f, openH * 0.35f);
                float topY = my - curveAmp * 0.5f;
                teeth.localPosition = Pos(0f, topY - 0.012f - th2 * 0.5f, push, 0.006f, cy);
                teeth.localScale = new Vector3(mouthW * 1.1f, th2, 0.02f);
            }

            // jezyk (tylko gdy usta szeroko otwarte)
            bool showTongue = showMouth && open > 0.55f;
            if (tongueR != null) tongueR.enabled = showTongue;
            if (showTongue)
            {
                float topY = my - curveAmp * 0.5f;
                tongue.localPosition = Pos(0f, topY - openH * 0.8f, push, 0.005f, cy);
                tongue.localScale = new Vector3(mouthW * 0.7f, openH * 0.38f, 0.03f);
            }

            // ----- wargi: gorna zawsze, dolna gdy usta otwarte (ksztalt soczewki) -----
            float spacing = 2f * mouthW / (LipSegments - 1);
            bool showLower = openH > 0.035f;
            for (int i = 0; i < LipSegments; i++)
            {
                float t = (float)i / (LipSegments - 1) * 2f - 1f;          // -1..1
                float x = t * mouthW;
                float yTop = my + curveAmp * (t * t - 0.5f);
                float th = Mathf.Max(spacing * 0.95f, 0.03f + 0.02f * (1f - t * t));
                lipU[i].localPosition = Pos(x, yTop, push, 0.009f, cy);
                lipU[i].localScale = new Vector3(th, th, 0.03f);

                if (lipLR[i] != null) lipLR[i].enabled = showLower;
                if (showLower)
                {
                    float gap = openH * (1f - t * t);
                    lipL[i].localPosition = Pos(x, yTop - gap, push, 0.009f, cy);
                    lipL[i].localScale = new Vector3(th, th, 0.03f);
                }
            }

            // ----- brwi (luk z kulek, kazda brew osobno) -----
            PlaceBrow(browRT, bx, by, 1f, raiseR, push, cy);
            PlaceBrow(browLT, -bx, by, -1f, raiseL, push, cy);
        }

        private void PlaceBrow(Transform[] seg, float cx, float cyBrow, float sign, float raise, float push, float cy)
        {
            float tilt = (angry * 24f - raise * 8f) * sign * Mathf.Deg2Rad;
            float lift = raise * 0.06f - angry * 0.025f;
            float len = 0.17f;
            float half = len * 0.5f;
            float arch = Mathf.Max(0f, 0.016f + 0.02f * raise - 0.014f * angry);
            float c = Mathf.Cos(tilt), sn = Mathf.Sin(tilt);
            float spacing = len / (BrowSegments - 1);
            for (int i = 0; i < BrowSegments; i++)
            {
                float u = (float)i / (BrowSegments - 1) * 2f - 1f;         // -1..1
                float a = u * half;
                float p = arch * (1f - u * u);
                float x = cx + a * c - p * sn;
                float y = cyBrow + lift + a * sn + p * c;
                float th = Mathf.Max(spacing * 1.05f, 0.036f * (1f - 0.3f * Mathf.Abs(u)));
                seg[i].localPosition = Pos(x, y, push, 0.009f, cy);
                seg[i].localScale = new Vector3(th, th, 0.03f);
            }
        }

        // ---------- Pomocnicze ----------

        private static Transform FindHead(Transform start)
        {
            // Przeszukaj ten obiekt, a jesli nic - caly korzen
            var t = FindHeadIn(start);
            if (t == null && start.root != start) t = FindHeadIn(start.root);
            return t;
        }

        private static Transform FindHeadIn(Transform start)
        {
            Transform contains = null;
            var queue = new Queue<Transform>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                string n = t.name.ToLowerInvariant();
                if (n == "head") return t;
                if (contains == null && n.Contains("head") && !n.Contains("phone") && !n.Contains("set")) contains = t;
                for (int i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
            }
            return contains;
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        // Jednorazowy zrzut hierarchii do logu - pomaga ustawic twarz, jesli cos nie pasuje
        private static void Dump(Transform t, MelonLogger.Instance log)
        {
            try
            {
                var top = t;
                for (int i = 0; i < 2 && top.parent != null; i++) top = top.parent;
                log.Msg("--- hierarchia (zrzut) ---");
                DumpRec(top, 0, log, new int[] { 0 });
                log.Msg("--- koniec zrzutu ---");
            }
            catch { }
        }

        private static void DumpRec(Transform t, int depth, MelonLogger.Instance log, int[] count)
        {
            if (count[0]++ > 80 || depth > 6) return;
            string flags = "";
            try
            {
                if (t.GetComponent<Renderer>() != null) flags += " [Renderer]";
                if (t.GetComponent<Rigidbody>() != null) flags += " [Rigidbody]";
            }
            catch { }
            log.Msg(new string(' ', depth * 2) + t.name + flags);
            for (int i = 0; i < t.childCount; i++) DumpRec(t.GetChild(i), depth + 1, log, count);
        }
    }
}
