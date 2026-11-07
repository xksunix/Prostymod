using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;

namespace GBPigeons
{
    // Jeden wypieczony klip animacji (poza wszystkich kosci na kazdej klatce, w ukladzie Unity)
    internal class PClip
    {
        public string Name;
        public float Fps;
        public bool Loop;
        public int Frames;
        public float[] Data;     // Frames * NB * 7: poz(3) + rot(x,y,z,w)
    }

    internal class PSound
    {
        public AudioClip Clip;
        public float[] Env;      // glosnosc co 20 ms (0..1) - do "nadymania" szyi przy gruchaniu
        public float Dur;
    }

    // Wspolne zasoby wszystkich golebi: siatka, kosci, klipy, tekstura (z wariantami kolorow), dzwieki. Tworzone raz.
    internal class PigeonAssets
    {
        public const int Rate = 32000;
        public const string FolderName = "GangBeastsPigeons";

        public bool Ok;

        // kosci (przestrzen modelu, uklad Unity)
        public int NB;
        public string[] BoneNames;
        public int[] BoneParent;
        public Vector3[] RestPos;
        public Quaternion[] RestRot;
        public Vector3[] WorldPos;
        public Quaternion[] WorldRot;
        public Vector3[] AxLat, AxUp, AxFwd;     // osie modelu (prawo/gora/przod) zapisane w ukladzie rodzica kosci

        public float MinY, MaxY, MinZ, MaxZ, MinX, MaxX;
        public Vector3 Pivot;

        public Mesh Mesh;
        public int TexW, TexH;
        private byte[] texRgba;                  // wiersze od dolu (jak w Unity)
        public readonly Dictionary<string, PClip> Clips = new Dictionary<string, PClip>();
        public readonly Dictionary<string, List<PSound>> Sounds = new Dictionary<string, List<PSound>>();

        public readonly List<string> ShaderNames = new List<string>();
        public int ShaderSel;
        private Shader shader;
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        // ---------- Wczytanie ----------

        public static PigeonAssets Load(string preferredShader)
        {
            var A = new PigeonAssets();
            var asm = typeof(PigeonAssets).Assembly;
            MelonLogger.Msg("[Pigeons] zasoby w dll: " + string.Join(", ", asm.GetManifestResourceNames()));

            byte[] bin = ReadRes(asm, "pigeon.bin");
            if (bin == null) { MelonLogger.Error("[Pigeons] brak zasobu pigeon.bin w dll."); return A; }
            try { A.Parse(bin); }
            catch (Exception e) { MelonLogger.Error("[Pigeons] blad budowy modelu golebia: " + e); return A; }

            try { A.LoadSounds(asm); }
            catch (Exception e) { MelonLogger.Warning("[Pigeons] blad dzwiekow (mod dziala bez dzwieku): " + e.Message); }

            try { A.BuildShaderList(preferredShader); A.BuildShader(); }
            catch (Exception e) { MelonLogger.Error("[Pigeons] blad shaderow: " + e); return A; }

            A.Ok = A.Mesh != null && A.shader != null;
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

        public int Bone(string name)
        {
            for (int i = 0; i < NB; i++) if (BoneNames[i] == name) return i;
            return -1;
        }

        private void Parse(byte[] bin)
        {
            var br = new BinaryReader(new MemoryStream(bin));
            string magic = new string(br.ReadChars(4));
            if (magic != "GBP1") throw new Exception("zly naglowek pigeon.bin: " + magic);
            int nv = br.ReadInt32(), ni = br.ReadInt32(), nb = br.ReadInt32(), nclips = br.ReadInt32();
            TexW = br.ReadInt32(); TexH = br.ReadInt32();
            float[] info = ReadFloats(br, 9);
            MinY = info[0]; MaxY = info[1]; MinZ = info[2]; MaxZ = info[3]; MinX = info[4]; MaxX = info[5];
            Pivot = new Vector3(info[6], info[7], info[8]);

            NB = nb;
            BoneNames = new string[nb];
            BoneParent = new int[nb];
            RestPos = new Vector3[nb];
            RestRot = new Quaternion[nb];
            for (int i = 0; i < nb; i++)
            {
                int len = br.ReadByte();
                BoneNames[i] = new string(br.ReadChars(len));
                BoneParent[i] = br.ReadInt32();
                float[] t = ReadFloats(br, 7);
                RestPos[i] = new Vector3(t[0], t[1], t[2]);
                RestRot[i] = new Quaternion(t[3], t[4], t[5], t[6]);
            }

            Vector3[] pos = ReadV3(br, nv), nrm = ReadV3(br, nv);
            Vector2[] uv = ReadV2(br, nv);
            byte[] bi = br.ReadBytes(nv * 4);
            byte[] bw = br.ReadBytes(nv * 4);
            byte[] ib = br.ReadBytes(ni * 2);
            var tris = new int[ni];
            for (int i = 0; i < ni; i++) tris[i] = BitConverter.ToUInt16(ib, i * 2);

            for (int c = 0; c < nclips; c++)
            {
                var clip = new PClip();
                int len = br.ReadByte();
                clip.Name = new string(br.ReadChars(len));
                clip.Fps = br.ReadSingle();
                clip.Loop = br.ReadByte() != 0;
                clip.Frames = br.ReadInt32();
                clip.Data = ReadFloats(br, clip.Frames * nb * 7);
                Clips[clip.Name] = clip;
            }
            int pngLen = br.ReadInt32();
            byte[] png = br.ReadBytes(pngLen);

            // pozycje i obroty kosci w przestrzeni modelu + osie modelu w ukladzie rodzica
            WorldPos = new Vector3[nb];
            WorldRot = new Quaternion[nb];
            AxLat = new Vector3[nb]; AxUp = new Vector3[nb]; AxFwd = new Vector3[nb];
            for (int i = 0; i < nb; i++)
            {
                int p = BoneParent[i];
                if (p < 0)
                {
                    WorldPos[i] = RestPos[i];
                    WorldRot[i] = RestRot[i];
                    AxLat[i] = Vector3.right; AxUp[i] = Vector3.up; AxFwd[i] = Vector3.forward;
                }
                else
                {
                    if (p > i) throw new Exception("kosci nie sa posortowane (rodzic po dziecku)");
                    WorldPos[i] = WorldPos[p] + WorldRot[p] * RestPos[i];
                    WorldRot[i] = WorldRot[p] * RestRot[i];
                    Quaternion inv = Quaternion.Inverse(WorldRot[p]);
                    AxLat[i] = inv * Vector3.right; AxUp[i] = inv * Vector3.up; AxFwd[i] = inv * Vector3.forward;
                }
            }

            Mesh = MakeMesh(pos, nrm, uv, bi, bw, tris);

            try { BuildBaseTexture(png); }
            catch (Exception e) { MelonLogger.Error("[Pigeons] tekstura golebia nie zaladowana (golab bedzie jednolity): " + e.Message); texRgba = null; }
            MelonLogger.Msg("[Pigeons] model: " + nv + " wierzcholkow, " + (ni / 3) + " trojkatow, " + nb + " kosci, " + nclips + " klipow, tekstura " + TexW + "x" + TexH
                + (texRgba != null ? " (ok)" : " (BLAD)") + ", wysokosc " + (MaxY - MinY).ToString("0.000") + ", dlugosc " + (MaxZ - MinZ).ToString("0.000"));
        }

        private Mesh MakeMesh(Vector3[] v, Vector3[] n, Vector2[] uv, byte[] bi, byte[] bw, int[] tris)
        {
            var m = new Mesh();
            m.name = "GBPigeonMesh";
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (!Il2.SetArr(m, "vertices", v)) throw new Exception("Mesh.vertices");
            Il2.SetArr(m, "normals", n);
            Il2.SetArr(m, "uv", uv);
            // siatka dwustronna: doklejamy odwrocone trojkaty (pior nie znikaja z drugiej strony niezaleznie od cullingu shadera)
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
                Il2.SetArr(m, "boneWeights", MakeBoneWeights(bi, bw, v.Length));
                var bind = new Matrix4x4[NB];
                for (int i = 0; i < NB; i++)
                {
                    // odwrotnosc macierzy kosci (obrot + przesuniecie, skala 1)
                    Quaternion inv = Quaternion.Inverse(WorldRot[i]);
                    bind[i] = Matrix4x4.TRS(-(inv * WorldPos[i]), inv, Vector3.one);
                }
                Il2.SetArr(m, "bindposes", bind);
            }
            catch (Exception e) { MelonLogger.Warning("[Pigeons] wagi kosci: " + e.Message); }

            m.RecalculateBounds();
            m.bounds = new Bounds(new Vector3(0f, 0.15f, 0f), new Vector3(2f, 2f, 2f));
            return m;
        }

        private static Array MakeBoneWeights(byte[] bi, byte[] bw, int n)
        {
            Type bwT = typeof(BoneWeight);
            Array arr = Array.CreateInstance(bwT, n);
            for (int i = 0; i < n; i++)
            {
                object b = Activator.CreateInstance(bwT);
                float sum = 0f;
                for (int k = 0; k < 4; k++) sum += bw[i * 4 + k];
                if (sum < 1f) sum = 1f;
                for (int k = 0; k < 4; k++)
                {
                    float w = bw[i * 4 + k] / sum;
                    Il2.SetMember(b, "boneIndex" + k, w > 0f ? (int)bi[i * 4 + k] : 0);
                    Il2.SetMember(b, "weight" + k, w);
                }
                arr.SetValue(b, i);
            }
            return arr;
        }

        // ---------- Tekstura i warianty kolorow ----------

        private void BuildBaseTexture(byte[] png)
        {
            int w, h;
            byte[] rgba = Png.DecodeRgba(png, out w, out h);
            // PNG ma pierwszy wiersz u gory, Unity chce pierwszy wiersz na dole - odwracamy
            var flipped = new byte[rgba.Length];
            int stride = w * 4;
            for (int y = 0; y < h; y++) Buffer.BlockCopy(rgba, y * stride, flipped, (h - 1 - y) * stride, stride);
            texRgba = flipped;
            TexW = w; TexH = h;
        }

        // Tekstura golebia przefarbowana na kolor gracza (hue 0..1; sat/val - mnozniki). key "base" = oryginal.
        public Texture2D GetTexture(string key, float hue, float satMul, float valMul)
        {
            Texture2D t;
            if (textures.TryGetValue(key, out t) && t != null) return t;
            if (texRgba == null) return null;

            byte[] px = texRgba;
            if (key != "base")
            {
                px = (byte[])texRgba.Clone();
                int n = px.Length / 4;
                for (int i = 0; i < n; i++)
                {
                    float r = px[i * 4] / 255f, g = px[i * 4 + 1] / 255f, b = px[i * 4 + 2] / 255f;
                    float h, s, v;
                    RgbToHsv(r, g, b, out h, out s, out v);
                    float deg = h * 360f;
                    if (s > 0.12f && deg >= 195f && deg <= 235f)     // tylko niebieskie cialo, nie lapy, dziob, zielona szyja i fiolet piersi
                    {
                        float nh = hue + (deg - 213f) / 360f * 0.4f;
                        nh -= Mathf.Floor(nh);
                        HsvToRgb(nh, Mathf.Clamp01(s * satMul), Mathf.Clamp01(v * valMul), out r, out g, out b);
                        px[i * 4] = (byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255);
                        px[i * 4 + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255);
                        px[i * 4 + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255);
                    }
                }
            }
            t = MakeTexture("GBPigeonTex_" + key, px, TexW, TexH);
            if (t != null) textures[key] = t;
            return t;
        }

        private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            float max = Mathf.Max(r, Mathf.Max(g, b)), min = Mathf.Min(r, Mathf.Min(g, b));
            float d = max - min;
            v = max;
            s = max <= 0f ? 0f : d / max;
            if (d <= 1e-6f) { h = 0f; return; }
            if (max == r) h = ((g - b) / d) % 6f;
            else if (max == g) h = (b - r) / d + 2f;
            else h = (r - g) / d + 4f;
            h /= 6f;
            if (h < 0f) h += 1f;
        }

        private static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
        {
            h = (h - Mathf.Floor(h)) * 6f;
            int i = (int)h;
            float f = h - i, p = v * (1f - s), q = v * (1f - s * f), t = v * (1f - s * (1f - f));
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }

        private static Texture2D MakeTexture(string name, byte[] rgba, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.name = name;
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
                catch (Exception e) { MelonLogger.Warning("[Pigeons] LoadRawTextureData(tablica): " + (e.InnerException ?? e).Message); }
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
                    catch (Exception e) { MelonLogger.Warning("[Pigeons] LoadRawTextureData(wskaznik): " + (e.InnerException ?? e).Message); }
                    finally { gh.Free(); }
                }
            }
            if (!loaded) { MelonLogger.Error("[Pigeons] zadna wersja LoadRawTextureData nie zadzialala"); return null; }

            tex.Apply(false, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        // ---------- Animacje ----------

        // Probkuje klip dla kosci b w czasie t (sekundy). Klipy petlowe: ostatnia klatka = pierwsza.
        public void Sample(PClip c, float t, int b, out Vector3 pos, out Quaternion rot)
        {
            int nb = NB;
            float f = t * c.Fps;
            int i0, i1;
            float a;
            if (c.Frames <= 1) { i0 = i1 = 0; a = 0f; }
            else if (c.Loop)
            {
                int period = c.Frames - 1;
                f = f % period;
                if (f < 0f) f += period;
                i0 = (int)f; a = f - i0;
                i1 = i0 + 1;                 // i0 <= period-1, wiec i1 <= Frames-1 (ostatnia klatka = pierwsza)
            }
            else
            {
                f = Mathf.Clamp(f, 0f, c.Frames - 1);
                i0 = Mathf.Min((int)f, c.Frames - 1); a = f - i0;
                i1 = Mathf.Min(i0 + 1, c.Frames - 1);
            }
            int o0 = (i0 * nb + b) * 7, o1 = (i1 * nb + b) * 7;
            float[] d = c.Data;
            Vector3 p0 = new Vector3(d[o0], d[o0 + 1], d[o0 + 2]);
            Vector3 p1 = new Vector3(d[o1], d[o1 + 1], d[o1 + 2]);
            Quaternion q0 = new Quaternion(d[o0 + 3], d[o0 + 4], d[o0 + 5], d[o0 + 6]);
            Quaternion q1 = new Quaternion(d[o1 + 3], d[o1 + 4], d[o1 + 5], d[o1 + 6]);
            pos = Vector3.Lerp(p0, p1, a);
            rot = Quaternion.Slerp(q0, q1, a);
        }

        // ---------- Dzwieki ----------

        private static readonly string[] SoundNames =
        {
            "coo0", "coo1", "coo2", "coo3", "coo4", "hurt0", "hurt1", "angry0", "angry1", "sleep0", "laugh0", "flap0", "flap1"
        };
        private static readonly string[] Categories = { "coo", "hurt", "angry", "sleep", "laugh", "flap" };

        public static string UserFolder()
        {
            try { return Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "Mods"), FolderName); }
            catch { return FolderName; }
        }

        private void LoadSounds(Assembly asm)
        {
            foreach (string c in Categories) Sounds[c] = new List<PSound>();

            foreach (string name in SoundNames)
            {
                byte[] raw = ReadRes(asm, name + ".pcm");
                if (raw == null) continue;
                int n = raw.Length / 2;
                var data = new float[n];
                for (int i = 0; i < n; i++) data[i] = BitConverter.ToInt16(raw, i * 2) / 32768f;
                AddSound(Category(name), "gbpigeon_" + name, data, Rate);
            }

            // wlasne nagrania uzytkownika: Mods/GangBeastsPigeons/coo*.wav, hurt*.wav, angry*.wav, sleep*.wav, laugh*.wav, flap*.wav
            // - jesli sa pliki danej kategorii, zastepuja wbudowane
            try
            {
                string dir = UserFolder();
                try { Directory.CreateDirectory(dir); } catch { }
                if (Directory.Exists(dir))
                {
                    var user = new Dictionary<string, List<PSound>>();
                    foreach (string c in Categories) user[c] = new List<PSound>();
                    foreach (string path in Directory.GetFiles(dir, "*.wav"))
                    {
                        string fn = Path.GetFileNameWithoutExtension(path);
                        string cat = Category(fn.ToLowerInvariant());
                        if (cat == null) { MelonLogger.Warning("[Pigeons] plik '" + fn + ".wav' pominiety - nazwa musi zaczynac sie od coo, hurt, angry, sleep, laugh lub flap."); continue; }
                        try
                        {
                            float[] data; int rate;
                            if (!ParseWav(File.ReadAllBytes(path), out data, out rate)) { MelonLogger.Warning("[Pigeons] nieobslugiwany format WAV: " + fn); continue; }
                            var s = MakeSound("gbpigeon_user_" + fn, data, rate);
                            if (s != null) user[cat].Add(s);
                        }
                        catch (Exception e) { MelonLogger.Warning("[Pigeons] WAV '" + fn + "': " + e.Message); }
                    }
                    foreach (string c in Categories)
                        if (user[c].Count > 0)
                        {
                            Sounds[c] = user[c];
                            MelonLogger.Msg("[Pigeons] wlasne dzwieki '" + c + "': " + user[c].Count + " (zastepuja wbudowane)");
                        }
                }
            }
            catch (Exception e) { MelonLogger.Warning("[Pigeons] folder z wlasnymi dzwiekami: " + e.Message); }

            var sb = new System.Text.StringBuilder();
            foreach (string c in Categories) sb.Append(c).Append('=').Append(Sounds[c].Count).Append(' ');
            MelonLogger.Msg("[Pigeons] dzwieki: " + sb + "(folder na wlasne: " + UserFolder() + ")");
        }

        private static string Category(string name)
        {
            foreach (string c in Categories) if (name.StartsWith(c)) return c;
            return null;
        }

        private void AddSound(string cat, string name, float[] data, int rate)
        {
            if (cat == null) return;
            var s = MakeSound(name, data, rate);
            if (s != null) Sounds[cat].Add(s);
        }

        private static PSound MakeSound(string name, float[] data, int rate)
        {
            if (data == null || data.Length < 16) return null;
            AudioClip clip = MakeClip(name, data, rate);
            if (clip == null) { MelonLogger.Warning("[Pigeons] nie udalo sie zrobic klipu " + name); return null; }
            int hop = Math.Max(1, rate / 50);
            int cnt = Math.Max(1, data.Length / hop);
            var env = new float[cnt];
            for (int k = 0; k < cnt; k++)
            {
                double s = 0; int c = 0;
                for (int i = k * hop; i < Math.Min(data.Length, (k + 1) * hop); i++) { s += data[i] * data[i]; c++; }
                env[k] = c > 0 ? (float)Math.Sqrt(s / c) : 0f;
            }
            var sorted = (float[])env.Clone();
            Array.Sort(sorted);
            float p90 = Math.Max(0.02f, sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.9f))]);
            for (int k = 0; k < cnt; k++) env[k] = Mathf.Clamp01(env[k] / p90);
            return new PSound { Clip = clip, Env = env, Dur = data.Length / (float)rate };
        }

        private static AudioClip MakeClip(string name, float[] data, int rate)
        {
            try
            {
                AudioClip clip = AudioClip.Create(name, data.Length, 1, rate, false);
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
            catch (Exception e) { MelonLogger.Warning("[Pigeons] dzwiek " + name + ": " + (e.InnerException ?? e).Message); }
            return null;
        }

        // WAV: PCM 8/16/24/32 bit lub float32, dowolna liczba kanalow (miksowana do mono)
        private static bool ParseWav(byte[] b, out float[] data, out int rate)
        {
            data = null; rate = 0;
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') return false;
            int pos = 12, fmtTag = 0, ch = 0, bits = 0, dataOff = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                string id = new string(new[] { (char)b[pos], (char)b[pos + 1], (char)b[pos + 2], (char)b[pos + 3] });
                int len = BitConverter.ToInt32(b, pos + 4);
                int body = pos + 8;
                if (id == "fmt ")
                {
                    fmtTag = BitConverter.ToUInt16(b, body);
                    ch = BitConverter.ToUInt16(b, body + 2);
                    rate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToUInt16(b, body + 14);
                    if (fmtTag == 0xFFFE && len >= 26) fmtTag = BitConverter.ToUInt16(b, body + 24);
                }
                else if (id == "data") { dataOff = body; dataLen = Math.Min(len < 0 ? int.MaxValue : len, b.Length - body); break; }
                pos = body + len + (len & 1);
            }
            if (dataOff < 0 || ch < 1 || rate < 4000) return false;
            int bytes = bits / 8;
            if (bytes < 1 || (fmtTag != 1 && fmtTag != 3)) return false;
            int frames = dataLen / (bytes * ch);
            if (frames < 16) return false;
            data = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0f;
                for (int c = 0; c < ch; c++)
                {
                    int o = dataOff + (i * ch + c) * bytes;
                    float v;
                    if (fmtTag == 3 && bytes == 4) v = BitConverter.ToSingle(b, o);
                    else if (bytes == 1) v = (b[o] - 128) / 128f;
                    else if (bytes == 2) v = BitConverter.ToInt16(b, o) / 32768f;
                    else if (bytes == 3) v = ((b[o] | (b[o + 1] << 8) | (b[o + 2] << 16)) << 8 >> 8) / 8388608f;
                    else v = BitConverter.ToInt32(b, o) / 2147483648f;
                    sum += v;
                }
                data[i] = sum / ch;
            }
            return true;
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
            // "Sprites/Default" ma wylaczony zapis glebokosci - na golebiu z duzą liczba nakladajacych sie pior wygladalby gorzej,
            // wiec idzie na koniec listy znanych shaderow
            string[] order =
            {
                "Universal Render Pipeline/Unlit", "Unlit/Texture", "Universal Render Pipeline/Simple Lit",
                "Universal Render Pipeline/Lit", "Mobile/Diffuse", "Sprites/Default"
            };
            foreach (string n in order)
            {
                Shader s = null;
                try { s = Shader.Find(n); } catch { }
                MelonLogger.Msg("[Pigeons] shader '" + n + "': " + (s != null ? "jest" : "brak"));
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
                MelonLogger.Msg("[Pigeons] shadery w scenie: " + string.Join(" | ", seen.ToArray()));
                foreach (string n in seen)
                {
                    if (ShaderNames.Count >= 14) break;
                    if (!ShaderNames.Contains(n) && !Skip(n)) ShaderNames.Add(n);
                }
            }
            catch (Exception e) { MelonLogger.Warning("[Pigeons] zbieranie shaderow ze sceny: " + e.Message); }

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
            BuildShader();
            return CurrentShaderName;
        }

        public void BuildShader()
        {
            Shader sh = null;
            if (ShaderNames.Count > 0)
            {
                try { sh = Shader.Find(CurrentShaderName); } catch { }
            }
            if (sh == null) { try { sh = Shader.Find("Sprites/Default"); } catch { } }
            if (sh == null) throw new Exception("nie znaleziono zadnego shadera");
            MelonLogger.Msg("[Pigeons] uzywam shadera: " + sh.name);
            shader = sh;
            materials.Clear();        // materialy zostana zrobione od nowa na zadanie
        }

        // Material dla wariantu koloru (key z GetTexture). Pigeony odswiezaja go po zmianie shadera.
        public Material GetMaterial(string key, float hue, float satMul, float valMul)
        {
            Material m;
            if (materials.TryGetValue(key, out m) && m != null) return m;
            Texture2D tex = GetTexture(key, hue, satMul, valMul);
            m = MakeMat(shader, tex);
            materials[key] = m;
            return m;
        }

        private static Texture2D solid;
        private static Material MakeMat(Shader sh, Texture tex)
        {
            var m = new Material(sh);
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            try
            {
                if (tex == null)
                {
                    if (solid == null)
                    {
                        var px = new byte[16];
                        for (int i = 0; i < 4; i++) { px[i * 4] = 130; px[i * 4 + 1] = 160; px[i * 4 + 2] = 205; px[i * 4 + 3] = 255; }
                        solid = MakeTexture("GBPigeonSolid", px, 2, 2);
                    }
                    tex = solid;
                }
                m.renderQueue = 3100;       // po przezroczystych obiektach mapy
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
                if (tex != null)
                {
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                }
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.05f);
            }
            catch (Exception e) { MelonLogger.Warning("[Pigeons] ustawianie materialu: " + e.Message); }
            return m;
        }
    }
}
