// ============================================================================
// DNR PSX Shader - Look presets
// One-click starting points for the common PS1-era styles. Presets only touch
// stylistic properties; textures, tint, transparency, culling and emission are
// left exactly as they are.
// ============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DNR.PSX.Editor
{
    public class PSXPreset
    {
        public string name;
        public string tooltip;
        public Dictionary<string, float> floats = new Dictionary<string, float>();
        public Dictionary<string, Color> colors = new Dictionary<string, Color>();
    }

    public static class PSXPresets
    {
        // Baseline every preset starts from, so switching presets never leaves
        // a stray effect enabled from the previous one.
        static Dictionary<string, float> Base()
        {
            return new Dictionary<string, float>
            {
                { "_SnapStrength", 1f },       { "_SnapResolution", 240f },  { "_AffineStrength", 1f },
                { "_Pixelate", 0f },           { "_PixelResolution", 256f },
                { "_PointFilter", 0f },        { "_NoMips", 0f },
                { "_Posterize", 1f },          { "_ColorBits", 5f },         { "_DitherStrength", 1f },
                { "_ColorGrade", 0f },         { "_HueShift", 0f },          { "_Saturation", 1f },
                { "_Contrast", 1f },
                { "_Scanlines", 0f },          { "_ScanlineCount", 240f },   { "_ScanlineIntensity", 0.25f },
                { "_DotCrawl", 0f },           { "_DotCrawlIntensity", 0.5f },
                { "_DotCrawlSize", 3f },       { "_DotCrawlSpeed", 8f },
                { "_DotCrawlViewMotion", 0.5f },{ "_DotCrawlAvatarMotion", 0.5f },
                { "_DotCrawlCoverage", 0.35f },
                { "_Horror", 0f },
                { "_HorrorFogStart", 2f },     { "_HorrorFogEnd", 12f },     { "_HorrorFogDensity", 0.5f },
                { "_GrainStrength", 0.15f },   { "_GrainSize", 2f },         { "_GrainAnimate", 1f },
                { "_VignetteStrength", 0.35f },{ "_VignetteSoftness", 0.5f },
                { "_HorrorCrush", 0.05f },     { "_HorrorLift", 0f },
                { "_Lighting", 0f },           { "_ShadeStrength", 1f },     { "_MinBrightness", 0.03f },
            };
        }

        static PSXPreset Make(string name, string tooltip,
            Dictionary<string, float> overrides, Dictionary<string, Color> colors = null)
        {
            var preset = new PSXPreset { name = name, tooltip = tooltip, floats = Base() };
            if (overrides != null)
                foreach (var kv in overrides)
                    preset.floats[kv.Key] = kv.Value;
            preset.colors["_HorrorTint"] = Color.white;
            preset.colors["_HorrorFogColor"] = new Color(0.30f, 0.32f, 0.29f);
            if (colors != null)
                foreach (var kv in colors)
                    preset.colors[kv.Key] = kv.Value;
            return preset;
        }

        public static readonly PSXPreset[] All =
        {
            Make("Authentic PS1",
                "Straight PlayStation 1: 240p vertex snapping, full affine warping, 15-bit dithered color, " +
                "per-vertex lighting. No post-processing on top.",
                null),

            Make("Survival Horror",
                "Resident Evil / Silent Hill: fog closing in a few metres out, animated film grain, heavy " +
                "vignette, desaturated sepia palette with crushed blacks, and chunky low-res point-filtered " +
                "textures.",
                new Dictionary<string, float>
                {
                    { "_SnapResolution", 224f },
                    { "_Pixelate", 1f },          { "_PixelResolution", 128f },
                    { "_PointFilter", 1f },       { "_NoMips", 1f },
                    { "_ColorGrade", 1f },        { "_Saturation", 0.55f },   { "_Contrast", 1.15f },
                    { "_Horror", 1f },
                    { "_HorrorFogStart", 1.5f },  { "_HorrorFogEnd", 9f },    { "_HorrorFogDensity", 0.75f },
                    { "_GrainStrength", 0.35f },  { "_GrainSize", 2f },
                    { "_VignetteStrength", 0.55f },{ "_VignetteSoftness", 0.55f },
                    { "_HorrorCrush", 0.1f },     { "_HorrorLift", 0.02f },
                    { "_DotCrawl", 1f },          { "_DotCrawlIntensity", 0.35f },
                    { "_MinBrightness", 0.02f },
                },
                new Dictionary<string, Color>
                {
                    { "_HorrorFogColor", new Color(0.28f, 0.29f, 0.26f) },
                    { "_HorrorTint", new Color(0.95f, 0.92f, 0.82f) },
                }),

            Make("Foggy Nightmare",
                "Survival Horror pushed to the limit: near-total fog, coarse grain, near-monochrome palette " +
                "and 4-bit color. Barely visible past a few metres - use sparingly.",
                new Dictionary<string, float>
                {
                    { "_SnapResolution", 160f },
                    { "_Pixelate", 1f },          { "_PixelResolution", 96f },
                    { "_PointFilter", 1f },       { "_NoMips", 1f },
                    { "_ColorBits", 4f },
                    { "_ColorGrade", 1f },        { "_Saturation", 0.25f },   { "_Contrast", 1.25f },
                    { "_Horror", 1f },
                    { "_HorrorFogStart", 0.5f },  { "_HorrorFogEnd", 5f },    { "_HorrorFogDensity", 1f },
                    { "_GrainStrength", 0.5f },   { "_GrainSize", 3f },
                    { "_VignetteStrength", 0.8f },{ "_VignetteSoftness", 0.65f },
                    { "_HorrorCrush", 0.15f },
                    { "_DotCrawl", 1f },          { "_DotCrawlIntensity", 0.5f },
                    { "_MinBrightness", 0.01f },
                },
                new Dictionary<string, Color>
                {
                    { "_HorrorFogColor", new Color(0.11f, 0.12f, 0.11f) },
                    { "_HorrorTint", new Color(0.86f, 0.90f, 0.86f) },
                }),

            Make("Clean Retro",
                "Subtle retro flavour that stays readable: gentle snapping, light warping, 6-bit color and " +
                "smooth per-pixel lighting. Good when you want the vibe without the crunch.",
                new Dictionary<string, float>
                {
                    { "_SnapStrength", 0.5f },    { "_SnapResolution", 320f },
                    { "_AffineStrength", 0.35f },
                    { "_ColorBits", 6f },         { "_DitherStrength", 0.5f },
                    { "_Lighting", 1f },          { "_ShadeStrength", 0.85f }, { "_MinBrightness", 0.05f },
                }),
        };

        /// <summary>
        /// Applies a preset's stylistic values to a material. The grunge
        /// overlay is left untouched because it needs a texture the user
        /// supplies themselves.
        /// </summary>
        public static void Apply(PSXPreset preset, Material mat)
        {
            if (preset == null || mat == null)
                return;

            foreach (var kv in preset.floats)
                if (mat.HasProperty(kv.Key))
                    mat.SetFloat(kv.Key, kv.Value);

            foreach (var kv in preset.colors)
                if (mat.HasProperty(kv.Key))
                    mat.SetColor(kv.Key, kv.Value);

            PSXShaderGUI.ValidateKeywords(mat);
            EditorUtility.SetDirty(mat);
        }
    }
}
