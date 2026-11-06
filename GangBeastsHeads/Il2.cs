using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace GBHeads
{
    // Pomocnicze funkcje do pracy z IL2CPP: tablice Il2Cpp zamiast zwyklych tablic, FindObjectsOfType przez refleksje.
    internal static class Il2
    {
        private static readonly Dictionary<string, MemberInfo> memCache = new Dictionary<string, MemberInfo>();
        private static MethodInfo findM;
        private static bool findResolved, findNeedsBool;

        // Zamienia zwykla tablice (Vector3[], int[], ...) na typ, ktorego oczekuje interop (Il2CppStructArray<T> itp.)
        public static object Wrap(Type target, Array src)
        {
            if (target == null || src == null) return null;
            if (target.IsInstanceOfType(src)) return src;
            try { return Activator.CreateInstance(target, new object[] { src }); } catch { }
            try
            {
                foreach (var m in target.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "op_Implicit") continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType.IsInstanceOfType(src) && target.IsAssignableFrom(m.ReturnType))
                        return m.Invoke(null, new object[] { src });
                }
            }
            catch { }
            return null;
        }

        public static bool SetArr(object o, string prop, Array arr)
        {
            try
            {
                var p = o.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite)
                {
                    MelonLoader.MelonLogger.Warning("[Heads] brak wlasciwosci " + prop + " w " + o.GetType().Name);
                    return false;
                }
                object v = Wrap(p.PropertyType, arr);
                if (v == null)
                {
                    MelonLoader.MelonLogger.Warning("[Heads] nie moge opakowac tablicy dla " + prop + " (typ " + p.PropertyType.Name + ")");
                    return false;
                }
                p.SetValue(o, v, null);
                return true;
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[Heads] ustawianie " + prop + ": " + (e.InnerException ?? e).Message);
                return false;
            }
        }

        // Ustawia pole/wlasciwosc struktury (np. BoneWeight) - struktura jest "w pudelku"
        public static void SetMember(object boxed, string name, object value)
        {
            Type t = boxed.GetType();
            string key = t.FullName + "." + name;
            MemberInfo m;
            if (!memCache.TryGetValue(key, out m))
            {
                m = (MemberInfo)t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) ?? t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                memCache[key] = m;
            }
            var p = m as PropertyInfo;
            if (p != null && p.CanWrite) { p.SetValue(boxed, value, null); return; }
            var f = m as FieldInfo;
            if (f != null) { f.SetValue(boxed, value); return; }
            throw new MissingMemberException(t.Name, name);
        }

        // FindObjectsOfType<T>() przez refleksje (w IL2CPP zwraca tablice Il2Cpp)
        public static List<UnityEngine.Object> FindAll(Type t)
        {
            var result = new List<UnityEngine.Object>();
            if (!findResolved)
            {
                findResolved = true;
                MethodInfo noArgs = null, boolArg = null;
                foreach (var m in typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "FindObjectsOfType" || !m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 0) noArgs = m;
                    else if (ps.Length == 1 && ps[0].ParameterType == typeof(bool)) boolArg = m;
                }
                findM = noArgs ?? boolArg;
                findNeedsBool = noArgs == null && boolArg != null;
            }
            if (findM == null) return result;
            var gm = findM.MakeGenericMethod(t);
            object res = gm.Invoke(null, findNeedsBool ? new object[] { false } : null);
            var en = res as IEnumerable;
            if (en == null) return result;
            foreach (var o in en)
            {
                var u = o as UnityEngine.Object;
                if (u != null) result.Add(u);
            }
            return result;
        }

        public static Transform FindBone(Transform start, string exact)
        {
            var q = new Queue<Transform>();
            q.Enqueue(start);
            int guard = 0;
            while (q.Count > 0 && guard++ < 700)
            {
                var t = q.Dequeue();
                if (t.name == exact) return t;
                for (int i = 0; i < t.childCount; i++) q.Enqueue(t.GetChild(i));
            }
            return null;
        }

        public static void CollectColliders(Transform top, List<Collider> list)
        {
            var q = new Queue<Transform>();
            q.Enqueue(top);
            int guard = 0;
            while (q.Count > 0 && guard++ < 700)
            {
                var t = q.Dequeue();
                Collider c = null;
                try { c = t.GetComponent<Collider>(); } catch { }
                if (c != null) list.Add(c);
                for (int i = 0; i < t.childCount; i++) q.Enqueue(t.GetChild(i));
            }
        }

        public static void CollectBodies(Transform top, List<Rigidbody> list)
        {
            var q = new Queue<Transform>();
            q.Enqueue(top);
            int guard = 0;
            while (q.Count > 0 && guard++ < 700)
            {
                var t = q.Dequeue();
                Rigidbody rb = null;
                try { rb = t.GetComponent<Rigidbody>(); } catch { }
                if (rb != null && !rb.isKinematic) list.Add(rb);
                for (int i = 0; i < t.childCount; i++) q.Enqueue(t.GetChild(i));
            }
        }
    }

    // Prosty dekoder PNG (8 bit, RGB/RGBA, bez przeplotu) - wynik: RGBA w kolejnosci wierszy z pliku (gora pierwsza)
    internal static class Png
    {
        private static int BE32(byte[] b, int o) { return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]; }

        public static byte[] DecodeRgba(byte[] png, out int w, out int h)
        {
            w = h = 0;
            int pos = 8, bitDepth = 0, colorType = 0, interlace = 0;
            var idat = new MemoryStream();
            while (pos + 8 <= png.Length)
            {
                int len = BE32(png, pos);
                string type = Encoding.ASCII.GetString(png, pos + 4, 4);
                int d = pos + 8;
                if (type == "IHDR") { w = BE32(png, d); h = BE32(png, d + 4); bitDepth = png[d + 8]; colorType = png[d + 9]; interlace = png[d + 12]; }
                else if (type == "IDAT") idat.Write(png, d, len);
                else if (type == "IEND") break;
                pos = d + len + 4;
            }
            if (bitDepth != 8 || interlace != 0 || (colorType != 2 && colorType != 6) || w <= 0 || h <= 0)
                throw new Exception("PNG: nieobslugiwany format (bit=" + bitDepth + ", typ=" + colorType + ", przeplot=" + interlace + ")");

            int bpp = colorType == 2 ? 3 : 4;
            int stride = w * bpp;
            var raw = new byte[(stride + 1) * h];
            idat.Position = 2;                                   // pomijamy naglowek zlib
            using (var ds = new DeflateStream(idat, CompressionMode.Decompress))
            {
                int total = 0;
                while (total < raw.Length)
                {
                    int r = ds.Read(raw, total, raw.Length - total);
                    if (r <= 0) break;
                    total += r;
                }
                if (total < raw.Length) throw new Exception("PNG: za malo danych po dekompresji");
            }

            var img = new byte[stride * h];
            for (int y = 0; y < h; y++)
            {
                int ft = raw[y * (stride + 1)];
                int ro = y * (stride + 1) + 1;
                int io = y * stride;
                int po = io - stride;                            // poprzedni wiersz
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? img[io + x - bpp] : 0;
                    int b = y > 0 ? img[po + x] : 0;
                    int c = (x >= bpp && y > 0) ? img[po + x - bpp] : 0;
                    int v = raw[ro + x];
                    switch (ft)
                    {
                        case 1: v += a; break;
                        case 2: v += b; break;
                        case 3: v += (a + b) >> 1; break;
                        case 4:
                            {
                                int p = a + b - c;
                                int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                                v += (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c);
                                break;
                            }
                    }
                    img[io + x] = (byte)v;
                }
            }

            var rgba = new byte[w * h * 4];
            if (bpp == 4) Buffer.BlockCopy(img, 0, rgba, 0, rgba.Length);
            else
            {
                int n = w * h;
                for (int i = 0, s = 0, t = 0; i < n; i++, s += 3, t += 4)
                {
                    rgba[t] = img[s]; rgba[t + 1] = img[s + 1]; rgba[t + 2] = img[s + 2]; rgba[t + 3] = 255;
                }
            }
            return rgba;
        }
    }
}
