using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace GBWilly
{
    /// <summary>Odczyt koloru postaci z materialu jej prawdziwej siatki (nie z koliderow).</summary>
    internal static class BodyColor
    {
        private static readonly string[] Preferred =
        {
            "_BaseColor", "_Color", "_MainColor", "_TintColor", "_Tint", "_SkinColor",
            "_ColorTint", "_PrimaryColor", "_Primary", "_Albedo"
        };

        // Szuka renderera ciala: SkinnedMeshRenderer (najlepiej 'body'), a dopiero w ostatecznosci kolidery
        public static Renderer FindBodyRenderer(Transform top)
        {
            Renderer best = null;
            int bestScore = -1;
            var q = new Queue<Transform>();
            q.Enqueue(top);
            int guard = 0;
            while (q.Count > 0 && guard++ < 800)
            {
                var t = q.Dequeue();
                for (int i = 0; i < t.childCount; i++) q.Enqueue(t.GetChild(i));
                string n = t.name.ToLowerInvariant();
                if (n.StartsWith("gb") || n.StartsWith("costume") || n.StartsWith("hat") || n.Contains("mouth")
                    || n.Contains("brow") || n.Contains("tongue") || n.Contains("teeth") || n.Contains("lip")) continue;
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                int score;
                if (t.GetComponent<SkinnedMeshRenderer>() != null) score = 10 + (n.Contains("body") ? 2 : 0);
                else if (n.Contains("collider") || n.Contains("helper")) score = 1;
                else score = 3;
                if (score > bestScore) { bestScore = score; best = r; }
            }
            return best;
        }

        public static bool TryRead(Renderer r, out Color color, out string prop)
        {
            color = Color.white;
            prop = null;
            Material m = null;
            try { m = r.sharedMaterial; } catch { }
            if (m == null) return false;

            var names = new List<string>(Preferred);
            try { AddShaderColorProps(m, names); } catch { }

            foreach (var pn in names)
            {
                bool has = false;
                try { has = m.HasProperty(pn); } catch { }
                if (!has) continue;
                Color c;
                try { c = m.GetColor(pn); } catch { continue; }
                if (c.r > 0.93f && c.g > 0.93f && c.b > 0.93f) continue;   // biel = kolor z tekstury, nie z parametru
                if (c.r + c.g + c.b < 0.06f) continue;                       // czern = nieustawione
                color = new Color(c.r, c.g, c.b, 1f);
                prop = pn;
                return true;
            }
            return false;
        }

        private static void AddShaderColorProps(Material m, List<string> names)
        {
            Shader sh = m.shader;
            int n = sh.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                if (sh.GetPropertyType(i) != ShaderPropertyType.Color) continue;
                string pn = sh.GetPropertyName(i);
                if (!names.Contains(pn)) names.Add(pn);
            }
        }

        public static string Describe(Renderer r)
        {
            var sb = new StringBuilder();
            try
            {
                Material m = r.sharedMaterial;
                if (m == null) return "(brak materialu)";
                sb.Append("shader=").Append(m.shader != null ? m.shader.name : "?");
                var names = new List<string>(Preferred);
                try { AddShaderColorProps(m, names); } catch { }
                foreach (var pn in names)
                {
                    bool has = false;
                    try { has = m.HasProperty(pn); } catch { }
                    if (!has) continue;
                    try
                    {
                        Color c = m.GetColor(pn);
                        sb.Append("; ").Append(pn).Append("=").Append(c.r.ToString("0.00")).Append(",").Append(c.g.ToString("0.00")).Append(",").Append(c.b.ToString("0.00"));
                    }
                    catch { }
                }
                try { sb.Append("; tekstura=").Append(m.mainTexture != null ? m.mainTexture.name : "brak"); } catch { }
            }
            catch (Exception e) { sb.Append("(blad: ").Append(e.Message).Append(")"); }
            return sb.ToString();
        }
    }
}
