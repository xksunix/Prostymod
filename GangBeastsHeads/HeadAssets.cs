using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;

namespace GBHeads
{
    // Wspolne zasoby wszystkich glow: siatki, tekstura, material, dzwieki. Tworzone raz.
    internal class HeadAssets
    {
        public const int Rate = 32000;

        public bool Ok;
        public float[] LM;                       // punkty orientacyjne modelu (szczeka, oczy, usta, wysokosc)
        public Mesh Main, Dark;
        public Texture2D Tex;
        public bool TexOk;
        public AudioClip[] Clips = new AudioClip[0];
        public float[][] Env = new float[0][];
        public float[] Dur = new float[0];
        public int Bite = -1;
        public readonly List<int> Talk = new List<int>();

        public Material HeadMat, TeethMat, TongueMat, LidMat;
        public Color LidColor = new Color(0.85f, 0.65f, 0.55f);
        private Vector3[] _pos; private Vector2[] _uv;
        public readonly List<string> ShaderNames = new List<string>();
        public int ShaderSel;

        // ---------- Wczytanie ----------

        public static HeadAssets Load(string preferredShader)
        {
            var A = new HeadAssets();
            var asm = typeof(HeadAssets).Assembly;
            MelonLogger.Msg("[Heads] zasoby w dll: " + string.Join(", ", asm.GetManifestResourceNames()));

            byte[] bin = ReadRes(asm, "head.bin");
            if (bin == null) { MelonLogger.Error("[Heads] brak zasobu head.bin w dll."); return A; }
            try { A.ParseHead(bin); }
            catch (Exception e) { MelonLogger.Error("[Heads] blad budowy modelu glowy: " + e); return A; }

            try { A.LoadClips(asm); }
            catch (Exception e) { MelonLogger.Warning("[Heads] blad dzwiekow (mod dziala bez dzwieku): " + e.Message); }

            try { A.BuildShaderList(preferredShader); A.BuildMaterials(); }
            catch (Exception e) { MelonLogger.Error("[Heads] blad materialow: " + e); return A; }

            A.Ok = A.Main != null && A.HeadMat != null;
            return A;
        }

        private static byte[] ReadRes(Assembly asm, string name)
        {
            using (var s = asm.GetManifestResourceStream(name))
            {
                if (s == null) return null;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static float[] ReadFloats(BinaryReader br, int count)
        {
            byte[] b = br.ReadBytes(count * 4);
            var f = new float[count];
            Buffer.BlockCopy(b, 0, f, 0, b.Length);
            return f;
        }

        private static Vector3[] ReadV3(BinaryReader br, int n)
        {
            float[] f = ReadFloats(br, n * 3);
            var v = new Vector3[n];
            for (int i = 0; i < n; i++) v[i] = new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
            return v;
        }

        private static Vector2[] ReadV2(BinaryReader br, int n)
        {
            float[] f = ReadFloats(br, n * 2);
            var v = new Vector2[n];
            for (int i = 0; i < n; i++) v[i] = new Vector2(f[i * 2], f[i * 2 + 1]);
            return v;
        }

        private static int[] ReadIdx(BinaryReader br, int n)
        {
            byte[] b = br.ReadBytes(n * 2);
            var t = new int[n];
            for (int i = 0; i < n; i++) t[i] = BitConverter.ToUInt16(b, i * 2);
            return t;
        }

        private void ParseHead(byte[] bin)
        {
            var br = new BinaryReader(new MemoryStream(bin));
            string magic = new string(br.ReadChars(4));
            if (magic != "GBH2") throw new Exception("zly naglowek head.bin: " + magic);
            int nv = br.ReadInt32(), ni = br.ReadInt32(), dnv = br.ReadInt32(), dni = br.ReadInt32();
            int tw = br.ReadInt32(), th = br.ReadInt32(), nlm = br.ReadInt32();
            LM = ReadFloats(br, nlm);

            Vector3[] pos = ReadV3(br, nv), nrm = ReadV3(br, nv);
            Vector2[] uv = ReadV2(br, nv);
            byte[] w = br.ReadBytes(nv * 3);
            int[] tris = ReadIdx(br, ni);

            Vector3[] dpos = ReadV3(br, dnv), dnrm = ReadV3(br, dnv);
            Vector2[] duv = ReadV2(br, dnv);
            byte[] dw = br.ReadBytes(dnv * 3);
            int[] dtris = ReadIdx(br, dni);

            int pngLen = br.ReadInt32();
            byte[] png = br.ReadBytes(pngLen);

            // pozycje kosci (przestrzen modelu): korzen, szczeka, powieka A, powieka B
            Vector3[] bonePos =
            {
                Vector3.zero,
                new Vector3(0f, LM[0], LM[1]),
                new Vector3(LM[2], LM[3], LM[4]),
                new Vector3(LM[5], LM[6], LM[7])
            };

            _pos = pos; _uv = uv;
            Main = MakeMesh("GBHeadMain", pos, nrm, uv, w, tris, bonePos);
            Dark = MakeMesh("GBHeadMouth", dpos, dnrm, duv, dw, dtris, bonePos);

            try { BuildTexture(png); }
            catch (Exception e)
            {
                TexOk = false;
                MelonLogger.Error("[Heads] tekstura glowy nie zaladowana (glowa bedzie jednolita): " + e.Message);
            }
            MelonLogger.Msg("[Heads] model: " + nv + " wierzcholkow, " + (ni / 3) + " trojkatow, tekstura " + tw + "x" + th + " (" + (TexOk ? "ok" : "BLAD") + ")");
        }

        private static Mesh MakeMesh(string name, Vector3[] v, Vector3[] n, Vector2[] uv, byte[] w, int[] tris, Vector3[] bonePos)
        {
            var m = new Mesh();
            m.name = name;
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (!Il2.SetArr(m, "vertices", v)) throw new Exception("Mesh.vertices");
            Il2.SetArr(m, "normals", n);
            Il2.SetArr(m, "uv", uv);
            // siatka dwustronna: doklejamy odwrocone trojkaty (zadnych dziur niezaleznie od cullingu shadera)
            var both = new int[tris.Length * 2];
            Array.Copy(tris, both, tris.Length);
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                both[tris.Length + i] = tris[i];
                both[tris.Length + i + 1] = tris[i + 2];
                both[tris.Length + i + 2] = tris[i + 1];
            }
            if (!Il2.SetArr(m, "triangles", both)) throw new Exception("Mesh.triangles");

            var white = new Color32[v.Length];
            for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
            Il2.SetArr(m, "colors32", white);

            try
            {
                Il2.SetArr(m, "boneWeights", MakeBoneWeights(w, v.Length));
                var bind = new Matrix4x4[bonePos.Length];
                for (int i = 0; i < bind.Length; i++)
                    bind[i] = Matrix4x4.TRS(-bonePos[i], Quaternion.identity, Vector3.one);
                Il2.SetArr(m, "bindposes", bind);
            }
            catch (Exception e) { MelonLogger.Warning("[Heads] wagi kosci: " + e.Message); }

            m.RecalculateBounds();
            m.bounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f));
            return m;
        }

        // wagi: kosc 0 = glowa (reszta), 1 = szczeka, 2 = powieka A, 3 = powieka B
        private static Array MakeBoneWeights(byte[] w, int n)
        {
            Type bwT = typeof(BoneWeight);
            Array arr = Array.CreateInstance(bwT, n);
            object def = Activator.CreateInstance(bwT);
            Il2.SetMember(def, "boneIndex0", 0); Il2.SetMember(def, "weight0", 1f);
            Il2.SetMember(def, "boneIndex1", 0); Il2.SetMember(def, "weight1", 0f);
            Il2.SetMember(def, "boneIndex2", 0); Il2.SetMember(def, "weight2", 0f);
            Il2.SetMember(def, "boneIndex3", 0); Il2.SetMember(def, "weight3", 0f);

            var idx = new int[4];
            var wt = new float[4];
            for (int i = 0; i < n; i++)
            {
                float j = w[i * 3] / 255f, a = 0f, b = 0f;      // powieki to osobne nakladki, siatka rusza sie tylko szczeka
                if (j < 0.004f && a < 0.004f && b < 0.004f) { arr.SetValue(def, i); continue; }
                float s = j + a + b;
                if (s > 1f) { j /= s; a /= s; b /= s; s = 1f; }
                idx[0] = 0; wt[0] = 1f - s;
                idx[1] = 1; wt[1] = j;
                idx[2] = 2; wt[2] = a;
                idx[3] = 3; wt[3] = b;
                // sortowanie malejaco po wadze (proste, 4 elementy)
                for (int p = 0; p < 3; p++)
                    for (int q = p + 1; q < 4; q++)
                        if (wt[q] > wt[p])
                        {
                            float tw = wt[p]; wt[p] = wt[q]; wt[q] = tw;
                            int ti = idx[p]; idx[p] = idx[q]; idx[q] = ti;
                        }
                object bw = Activator.CreateInstance(bwT);
                for (int k = 0; k < 4; k++)
                {
                    Il2.SetMember(bw, "boneIndex" + k, wt[k] > 0f ? idx[k] : 0);
                    Il2.SetMember(bw, "weight" + k, wt[k]);
                }
                arr.SetValue(bw, i);
            }
            return arr;
        }

        // ---------- Tekstura ----------

        private void BuildTexture(byte[] png)
        {
            int w, h;
            byte[] rgba = Png.DecodeRgba(png, out w, out h);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.name = "GBHeadTex";
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

            bool loaded = false;
            foreach (var m in typeof(Texture2D).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "LoadRawTextureData" || m.IsGenericMethodDefinition) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || ps[0].ParameterType == typeof(IntPtr)) continue;
                object arg = Il2.Wrap(ps[0].ParameterType, rgba);
                if (arg == null) continue;
                try { m.Invoke(tex, new object[] { arg }); loaded = true; break; }
                catch (Exception e) { MelonLogger.Warning("[Heads] LoadRawTextureData(tablica): " + (e.InnerException ?? e).Message); }
            }
            if (!loaded)
            {
                foreach (var m in typeof(Texture2D).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "LoadRawTextureData" || m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 2 || ps[0].ParameterType != typeof(IntPtr)) continue;
                    GCHandle gh = GCHandle.Alloc(rgba, GCHandleType.Pinned);
                    try
                    {
                        m.Invoke(tex, new object[] { gh.AddrOfPinnedObject(), rgba.Length });
                        loaded = true;
                        break;
                    }
                    catch (Exception e) { MelonLogger.Warning("[Heads] LoadRawTextureData(wskaznik): " + (e.InnerException ?? e).Message); }
                    finally { gh.Free(); }
                }
            }
            if (!loaded) throw new Exception("zadna wersja LoadRawTextureData nie zadzialala");

            tex.Apply(false, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Tex = tex;
            TexOk = true;
            try { LidColor = SampleSkin(rgba, w, h); }
            catch (Exception e) { MelonLogger.Warning("[Heads] kolor powiek: " + e.Message); }
        }

        // kolor skory nad oczami (do powiek)
        private Color SampleSkin(byte[] rgba, int w, int h)
        {
            float r = 0, g = 0, b = 0; int n = 0;
            for (int e = 0; e < 2; e++)
            {
                int o = e == 0 ? 2 : 5;
                Vector3 p = new Vector3(LM[o], LM[o + 1] + 0.05f, LM[o + 2]);
                int best = -1; float bd = 1e9f;
                for (int i = 0; i < _pos.Length; i++)
                {
                    float d = (_pos[i] - p).sqrMagnitude;
                    if (d < bd) { bd = d; best = i; }
                }
                if (best < 0) continue;
                int cx = (int)(_uv[best].x * w), cy = (int)(_uv[best].y * h);
                for (int dy = -3; dy <= 3; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int x = Mathf.Clamp(cx + dx, 0, w - 1), y = Mathf.Clamp(cy + dy, 0, h - 1);
                        int k = (y * w + x) * 4;
                        r += rgba[k]; g += rgba[k + 1]; b += rgba[k + 2]; n++;
                    }
            }
            if (n == 0) return LidColor;
            return new Color(r / n / 255f * 0.96f, g / n / 255f * 0.96f, b / n / 255f * 0.96f, 1f);
        }

        // ---------- Dzwieki ----------

        private void LoadClips(Assembly asm)
        {
            var clips = new List<AudioClip>();
            var envs = new List<float[]>();
            var durs = new List<float>();
            var names = new List<string>();
            for (int i = 0; i < 32; i++) names.Add("clip" + i);
            names.Add("bite");

            foreach (string name in names)
            {
                byte[] raw = ReadRes(asm, name + ".pcm");
                if (raw == null) continue;
                int n = raw.Length / 2;
                var data = new float[n];
                for (int i = 0; i < n; i++) data[i] = BitConverter.ToInt16(raw, i * 2) / 32768f;

                AudioClip clip = MakeClip("gbheads_" + name, data);
                // obwiednia glosnosci co 20 ms (do ruchu ust)
                int hop = Rate / 50;
                int cnt = Math.Max(1, n / hop);
                var env = new float[cnt];
                for (int k = 0; k < cnt; k++)
                {
                    double s = 0; int c = 0;
                    for (int i = k * hop; i < Math.Min(n, (k + 1) * hop); i++) { s += data[i] * data[i]; c++; }
                    env[k] = c > 0 ? (float)Math.Sqrt(s / c) : 0f;
                }
                var sorted = (float[])env.Clone();
                Array.Sort(sorted);
                float p90 = Math.Max(0.02f, sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.9f))]);
                for (int k = 0; k < cnt; k++) env[k] = Mathf.Clamp01(env[k] / p90);

                if (clip == null) { MelonLogger.Warning("[Heads] nie udalo sie zrobic klipu " + name); continue; }
                clips.Add(clip); envs.Add(env); durs.Add(n / (float)Rate);
                if (name == "bite") Bite = clips.Count - 1;
                else Talk.Add(clips.Count - 1);
            }
            Clips = clips.ToArray();
            Env = envs.ToArray();
            Dur = durs.ToArray();
            MelonLogger.Msg("[Heads] dzwieki: " + Talk.Count + " klipow gadania, gryzienie: " + (Bite >= 0 ? "tak" : "brak"));
        }

        private static AudioClip MakeClip(string name, float[] data)
        {
            try
            {
                AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
                clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                foreach (var m in typeof(AudioClip).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "SetData") continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 2 || ps[1].ParameterType != typeof(int)) continue;
                    object arg = Il2.Wrap(ps[0].ParameterType, data);
                    if (arg == null) continue;
                    m.Invoke(clip, new object[] { arg, 0 });
                    return clip;
                }
            }
            catch (Exception e) { MelonLogger.Warning("[Heads] dzwiek " + name + ": " + (e.InnerException ?? e).Message); }
            return null;
        }

        // ---------- Shadery i materialy ----------

        private static bool Skip(string n)
        {
            string l = n.ToLowerInvariant();
            return l.StartsWith("hidden") || l.Contains("particle") || l.Contains("skybox") || l.Contains("terrain") || l.Contains("text")
                || l.StartsWith("ui/") || l.Contains("gizmo") || l.Contains("outline") || l.Contains("shadow") || l.Contains("error")
                || l.Contains("decal") || l.Contains("water") || l.Contains("sprite") || l.Contains("blit") || l.Contains("depth");
        }

        private void BuildShaderList(string preferred)
        {
            ShaderNames.Clear();
            string[] order =
            {
                "Universal Render Pipeline/Unlit", "Unlit/Texture", "Sprites/Default",
                "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Lit", "Mobile/Diffuse"
            };
            foreach (string n in order)
            {
                Shader s = null;
                try { s = Shader.Find(n); } catch { }
                MelonLogger.Msg("[Heads] shader '" + n + "': " + (s != null ? "jest" : "brak"));
                if (s != null) ShaderNames.Add(n);
            }

            // shadery z obiektow w scenie (gra na pewno je ma w buildzie)
            try
            {
                var seen = new List<string>();
                int cnt = 0;
                foreach (var o in Il2.FindAll(typeof(Renderer)))
                {
                    if (cnt++ > 400) break;
                    var r = o as Renderer;
                    if (r == null) continue;
                    Material m = null;
                    try { m = r.sharedMaterial; } catch { }
                    if (m == null) continue;
                    Shader sh = null;
                    try { sh = m.shader; } catch { }
                    if (sh == null) continue;
                    string n = sh.name;
                    if (!seen.Contains(n)) seen.Add(n);
                }
                MelonLogger.Msg("[Heads] shadery w scenie: " + string.Join(" | ", seen.ToArray()));
                foreach (string n in seen)
                {
                    if (ShaderNames.Count >= 14) break;
                    if (!ShaderNames.Contains(n) && !Skip(n)) ShaderNames.Add(n);
                }
            }
            catch (Exception e) { MelonLogger.Warning("[Heads] zbieranie shaderow ze sceny: " + e.Message); }

            ShaderSel = 0;
            if (!string.IsNullOrEmpty(preferred))
            {
                int i = ShaderNames.IndexOf(preferred);
                if (i >= 0) ShaderSel = i;
            }
        }

        public string CurrentShaderName
        {
            get { return ShaderNames.Count > 0 ? ShaderNames[Math.Min(ShaderSel, ShaderNames.Count - 1)] : "(brak)"; }
        }

        public string CycleShader()
        {
            if (ShaderNames.Count == 0) return "(brak)";
            ShaderSel = (ShaderSel + 1) % ShaderNames.Count;
            BuildMaterials();
            return CurrentShaderName;
        }

        public void BuildMaterials()
        {
            Shader sh = null;
            if (ShaderNames.Count > 0)
            {
                try { sh = Shader.Find(CurrentShaderName); } catch { }
            }
            if (sh == null) { try { sh = Shader.Find("Sprites/Default"); } catch { } }
            if (sh == null) throw new Exception("nie znaleziono zadnego shadera");
            MelonLogger.Msg("[Heads] uzywam shadera: " + sh.name);

            HeadMat = MakeMat(sh, TexOk ? Color.white : new Color(0.8f, 0.6f, 0.5f), TexOk ? Tex : null);
            TeethMat = MakeMat(sh, new Color(0.93f, 0.92f, 0.86f), null);
            TongueMat = MakeMat(sh, new Color(0.84f, 0.27f, 0.33f), null);
            LidMat = MakeMat(sh, LidColor, null);
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++) t.SetPixel(x, y, c);
            t.Apply(false, false);
            return t;
        }

        private static Material MakeMat(Shader sh, Color c, Texture tex)
        {
            var m = new Material(sh);
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            try
            {
                // shadery typu Unlit/Texture nie maja koloru - bez tekstury wychodzi bialy; dajemy jednolita teksture
                if (tex == null) { tex = Solid(c); c = Color.white; }
                m.renderQueue = 3100;       // po przezroczystych obiektach mapy - glowa nie jest przez nie "zamazywana"

                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                if (tex != null)
                {
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                }
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.05f);
            }
            catch (Exception e) { MelonLogger.Warning("[Heads] ustawianie materialu: " + e.Message); }
            return m;
        }
    }
}
