using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using UnityEngine;

namespace UnderspaceAccess
{
    // Character colours are only numbered in the game. Each one is a CharacterPart whose
    // swappedMaterial paints the body, so the mod measures that material's colour (tint
    // times the average of its texture) and turns it into words like "dark red".
    internal static class ColorNames
    {
        static readonly Dictionary<IntPtr, string> cache = new();
        static readonly string[] tintProps = { "_BaseColor", "_Color", "_MainColor", "_TintColor", "_Tint" };

        static readonly HashSet<string> logged = new();
        static CharacterPart[] allParts;

        // The part's own name when it is a real name ("Hood"), otherwise the colour of its paint.
        public static string Of(ShipCustomizationButton row)
        {
            try
            {
                string rowName = row.bodyPartName != null ? MenuNav.StripTags(row.bodyPartName.text) : "";
                var bp = row.linkedBodyPartType;
                var part = CurrentPart(row, bp);
                var mat = part != null && part.swappedMaterial != null ? part.swappedMaterial : RendererMaterial(bp);
                string key = rowName + "|" + row.currentBodyPart;
                if (logged.Add(key))
                {
                    string id = row.bodyPartsAvailable != null && row.currentBodyPart >= 0 && row.currentBodyPart < row.bodyPartsAvailable.Count ? row.bodyPartsAvailable[row.currentBodyPart] : "?";
                    Log.Write($"colour row {rowName} #{row.currentBodyPart}: available id {id}, slot {(bp != null ? bp.bodyPartName + "/" + bp.partType : "none")}, " +
                              $"part {(part != null ? part.characterPartName + " / " + part.characterPartTypeName + " / " + part.internalPartID : "none")}, material {(mat != null ? mat.name : "none")}");
                }
                string own = part != null ? MenuNav.StripTags(part.characterPartName) : "";
                if (Meaningful(own, rowName)) return own;
                // Parts without a name or paint still have an id like "ijuni_HasFronds": say "Has Fronds".
                if (mat == null && part != null)
                {
                    string id = FromId(part.internalPartID);
                    if (Meaningful(id, rowName)) return id;
                }
                if (mat == null) return null;
                if (cache.TryGetValue(mat.Pointer, out var known)) return known;

                Color c = Color.white;
                string prop = null;
                foreach (var p in tintProps)
                    if (mat.HasProperty(p)) { c = mat.GetColor(p); prop = p; break; }
                var tex = mat.mainTexture;
                Color avg = tex != null ? Average(tex) : Color.white;
                var final = new Color(c.r * avg.r, c.g * avg.g, c.b * avg.b);
                string name = Name(final);
                Log.Write($"colour {rowName}: material {mat.name}, shader {mat.shader?.name}, tint {prop}={c}, texture {(tex != null ? tex.name : "none")} avg {avg} -> {name}");
                cache[mat.Pointer] = name;
                return name;
            }
            catch (Exception e) { Log.Write("colour name failed: " + e.Message); return null; }
        }

        static CharacterPart CurrentPart(ShipCustomizationButton row, characterBodyPart bp)
        {
            CharacterPart part = bp != null ? bp.currentCharacterPart : null;
            if (part == null) part = row.linkedBodyPart;
            if (part != null) return part;
            // Look the chosen id up among all body parts of the game.
            var ids = row.bodyPartsAvailable;
            if (ids == null || row.currentBodyPart < 0 || row.currentBodyPart >= ids.Count) return null;
            string id = ids[row.currentBodyPart];
            allParts ??= Resources.FindObjectsOfTypeAll<CharacterPart>().ToArray();
            foreach (var p in allParts)
                if (p != null && (p.internalPartID == id || p.characterPartName == id)) return p;
            return null;
        }

        // What the character is painted with right now, for slots that swap a material.
        static Material RendererMaterial(characterBodyPart bp)
        {
            if (bp == null || bp.rendererReplacements == null || bp.rendererReplacements.Count == 0) return null;
            var r = bp.rendererReplacements[0];
            if (r == null) return null;
            int idx = bp.rendererIndex != null && bp.rendererIndex.Count > 0 ? bp.rendererIndex[0] : 0;
            var mats = r.sharedMaterials;
            return idx >= 0 && idx < mats.Length ? mats[idx] : null;
        }

        static string FromId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "";
            int u = id.LastIndexOf('_');
            if (u >= 0) id = id.Substring(u + 1); // drop the race prefix
            id = System.Text.RegularExpressions.Regex.Replace(id, @"(?<=[a-z])(?=[A-Z0-9])", " ");
            return id.Trim();
        }

        // "Arm Color 2" or "3" says nothing new; "Hood" does.
        static bool Meaningful(string name, string rowName)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var rest = name.Replace(rowName, "", StringComparison.OrdinalIgnoreCase).Trim();
            if (rest.Length == 0 || rest.All(ch => char.IsDigit(ch) || ch == ' ' || ch == '#' || ch == '_')) return false;
            return true;
        }

        // Copies the texture into a small readable picture and averages its pixels.
        static Color Average(Texture tex)
        {
            var rt = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var t2 = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                t2.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                t2.Apply();
                var px = t2.GetPixels();
                float r = 0, g = 0, b = 0;
                for (int i = 0; i < px.Length; i++) { r += px[i].r; g += px[i].g; b += px[i].b; }
                UnityEngine.Object.Destroy(t2);
                int n = Math.Max(1, px.Length);
                return new Color(r / n, g / n, b / n);
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        public static string Name(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (v < 0.12f) return "black";
            if (s < 0.15f)
            {
                if (v > 0.85f) return "white";
                if (v > 0.6f) return "light grey";
                if (v > 0.3f) return "grey";
                return "dark grey";
            }
            float deg = h * 360f;
            string hue =
                deg < 15 ? "red" :
                deg < 40 ? (v < 0.6f ? "brown" : "orange") :
                deg < 65 ? (v < 0.55f ? "olive" : "yellow") :
                deg < 95 ? "lime green" :
                deg < 150 ? "green" :
                deg < 175 ? "teal" :
                deg < 200 ? "cyan" :
                deg < 255 ? "blue" :
                deg < 285 ? "purple" :
                deg < 320 ? "magenta" :
                deg < 345 ? "pink" : "red";
            string shade = v < 0.4f ? "dark " : (v > 0.8f && s < 0.5f ? "light " : "");
            if (hue == "brown" || hue == "olive") shade = v < 0.3f ? "dark " : "";
            string pale = s < 0.35f && shade == "" ? "pale " : "";
            return shade + pale + hue;
        }
    }
}
