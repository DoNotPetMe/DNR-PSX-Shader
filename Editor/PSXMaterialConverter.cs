// ============================================================================
// DNR PSX Shader - Material converter
// Creates (or updates in place, preserving GUIDs) PSX materials from existing
// avatar materials.
//
// Understands the property conventions of the shaders VRChat avatars actually
// ship with - Poiyomi Toon, Poiyomi Pro, lilToon and Standard - so that
// transparency, alpha masks, culling, emission and render queues survive the
// conversion. Every lookup goes through alias lists and HasProperty checks, so
// an unknown or newer shader degrades to a sensible result instead of throwing.
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DNR.PSX.Editor
{
    /// <summary>Collects human-readable notes produced during a conversion batch.</summary>
    public class ConversionLog
    {
        public readonly List<string> warnings = new List<string>();
        public readonly List<string> textureFixes = new List<string>();
        public int converted;
    }

    public static class PSXMaterialConverter
    {
        public const string ShaderName = "DNR/PSX";
        public const string MaterialSuffix = "_PSX";

        // ------------------------------------------------------- shader family
        public enum SourceFamily { Unknown, Poiyomi, LilToon, Standard }

        public static SourceFamily DetectFamily(Material mat)
        {
            string name = mat != null && mat.shader != null ? mat.shader.name.ToLowerInvariant() : "";
            // Locked/optimized Poiyomi materials live under "Hidden/Locked/..."
            // but keep the original name inside the path, so a substring test
            // catches both Toon and Pro, locked or unlocked.
            if (name.Contains("poiyomi")) return SourceFamily.Poiyomi;
            if (name.Contains("liltoon") || name.Contains("lil/")) return SourceFamily.LilToon;
            if (name.Contains("standard")) return SourceFamily.Standard;
            return SourceFamily.Unknown;
        }

        // ------------------------------------------------------------- convert

        /// <summary>
        /// Converts <paramref name="source"/> into a PSX material asset inside
        /// <paramref name="outputFolder"/> (project-relative). An existing asset
        /// at the target path is updated in place so its GUID (and any animation
        /// references to it) survive.
        /// </summary>
        public static Material Convert(Material source, string outputFolder,
            HashSet<string> usedPaths = null, ConversionLog log = null, bool fixTextureImports = true)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new System.InvalidOperationException(
                    $"Shader \"{ShaderName}\" not found. Reimport the DNR PSX Shader package.");

            EnsureFolder(outputFolder);

            string path = $"{outputFolder}/{Sanitize(source.name)}{MaterialSuffix}.mat";
            // Two different source materials can share a name; keep paths unique
            // within a single conversion batch.
            if (usedPaths != null)
            {
                string basePath = path;
                int n = 2;
                while (usedPaths.Contains(path))
                    path = basePath.Substring(0, basePath.Length - 4) + "_" + n++ + ".mat";
                usedPaths.Add(path);
            }

            Material target = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = target == null;
            if (isNew)
                target = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            else
                target.shader = shader;

            Configure(target, source, log, fixTextureImports);

            if (isNew)
                AssetDatabase.CreateAsset(target, path);
            else
                EditorUtility.SetDirty(target);

            return target;
        }

        static void Configure(Material target, Material source, ConversionLog log, bool fixTextureImports)
        {
            SourceFamily family = DetectFamily(source);

            // --------------------------------------------------------- albedo
            Texture main = TryGetTexture(source, "_MainTex", "_BaseMap", "_BaseColorMap");
            if (main == null) main = source.mainTexture;
            target.SetTexture("_MainTex", main);
            if (source.HasProperty("_MainTex"))
            {
                target.SetTextureScale("_MainTex", source.GetTextureScale("_MainTex"));
                target.SetTextureOffset("_MainTex", source.GetTextureOffset("_MainTex"));
            }

            Color tint = TryGetColor(source, Color.white, "_Color", "_BaseColor", "_MainColor");
            target.SetColor("_Color", tint);

            float cutoff;
            target.SetFloat("_Cutoff", TryGetFloat(source, out cutoff, "_Cutoff", "_AlphaCutoff", "_Clip") ? cutoff : 0.5f);

            // ----------------------------------------------------- alpha mask
            // Poiyomi and lilToon both drive eyelash/hair transparency from a
            // separate mask texture rather than the albedo's own alpha.
            Texture alphaMask = TryGetTexture(source, "_AlphaMask", "_MainTexAlphaMask", "_AlphaMaskTexture");
            bool hasAlphaMask = alphaMask != null;
            target.SetFloat("_AlphaMaskEnabled", hasAlphaMask ? 1f : 0f);
            if (hasAlphaMask)
            {
                target.SetTexture("_AlphaMask", alphaMask);

                float invert;
                target.SetFloat("_AlphaMaskInvert",
                    TryGetFloat(source, out invert, "_AlphaMaskInvert") && invert > 0.5f ? 1f : 0f);

                // Both families read the mask's red channel by default.
                float channel;
                target.SetFloat("_AlphaMaskChannel",
                    TryGetFloat(source, out channel, "_AlphaMaskChannel") ? Mathf.Clamp(channel, 0f, 3f) : 0f);

                // Mode 0 = multiply everywhere we know of; 1 = replace in
                // lilToon. Anything else falls back to multiply.
                float maskMode;
                bool replace = TryGetFloat(source, out maskMode, "_AlphaMaskMode") && Mathf.RoundToInt(maskMode) == 1;
                target.SetFloat("_AlphaMaskMode", replace ? 1f : 0f);
            }

            // -------------------------------------------------- rendering mode
            string reason;
            PSXShaderGUI.RenderMode mode = DetectMode(source, family, hasAlphaMask, out reason);

            // Alpha To Coverage: carried over when the source used it, and
            // adopted for cutout materials that came from an alpha mask, since
            // that is almost always eyelashes or hair.
            float atoc;
            bool sourceUsesAToC = TryGetFloat(source, out atoc, "_AlphaToMask", "_AlphaToCoverage", "_ATOC") && atoc > 0.5f;
            target.SetFloat("_AlphaToMask",
                (mode == PSXShaderGUI.RenderMode.Cutout && (sourceUsesAToC || hasAlphaMask)) ? 1f : 0f);

            // Premultiplied alpha kills dark halos on blended surfaces whose
            // textures have black in the transparent texels.
            target.SetFloat("_Premultiply", mode == PSXShaderGUI.RenderMode.Transparent ? 1f : 0f);

            // ------------------------------------------------------- emission
            Texture emissionMap = TryGetTexture(source, "_EmissionMap", "_EmissionMap0", "_EmissionColorMap");
            Color emissionColor = TryGetColor(source, Color.black, "_EmissionColor", "_EmissionColor0", "_Emission");
            float emissionStrength;
            if (TryGetFloat(source, out emissionStrength, "_EmissionStrength", "_EmissionIntensity"))
                emissionColor *= Mathf.Max(emissionStrength, 0f);

            float emissionToggle;
            bool toggledOn =
                source.IsKeywordEnabled("_EMISSION") ||
                (TryGetFloat(source, out emissionToggle, "_EnableEmission", "_UseEmission", "_EmissionEnabled") && emissionToggle > 0.5f);

            bool hasEmission = toggledOn && (emissionMap != null || emissionColor.maxColorComponent > 0.001f);
            target.SetFloat("_EmissionEnabled", hasEmission ? 1f : 0f);
            if (hasEmission)
            {
                target.SetTexture("_EmissionMap", emissionMap);
                target.SetColor("_EmissionColor", emissionColor);
            }

            // ---------------------------------------------------------- culling
            // Poiyomi commonly ships hair, eyelashes and skirts double-sided;
            // forcing back-face culling would make half of them vanish.
            float cull;
            if (TryGetFloat(source, out cull, "_Cull", "_CullMode"))
                target.SetFloat("_Cull", Mathf.Clamp(cull, 0f, 2f));

            // ------------------------------------------------ apply + validate
            PSXShaderGUI.ApplyRenderMode(target, mode);
            PSXShaderGUI.ValidateKeywords(target);

            // Preserve a deliberately customized queue (avatar creators use
            // these to fix sorting), but never keep an opaque-range queue on a
            // material we just made transparent.
            int sourceQueue = EffectiveQueue(source);
            if (mode == PSXShaderGUI.RenderMode.Transparent && sourceQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent)
                target.renderQueue = sourceQueue;
            else if (mode == PSXShaderGUI.RenderMode.Cutout && sourceQueue > (int)UnityEngine.Rendering.RenderQueue.Geometry)
                target.renderQueue = sourceQueue;

            // --------------------------------------------------------- report
            if (log != null)
            {
                if (fixTextureImports && mode != PSXShaderGUI.RenderMode.Opaque)
                    FixAlphaImportSettings(main, log);

                if (mode == PSXShaderGUI.RenderMode.Opaque && TextureHasAlpha(main))
                    log.warnings.Add(
                        $"\"{source.name}\" converted as Opaque, but its texture has an alpha channel. " +
                        "If it should be see-through (eyelashes, hair, decals), set Rendering Mode to " +
                        "Cutout on the generated material.");

                if (family == SourceFamily.Poiyomi && IsExoticPoiyomiBlend(source))
                    log.warnings.Add(
                        $"\"{source.name}\" used a Poiyomi additive/multiplicative blend mode, which this " +
                        "shader has no equivalent for. It was converted as Transparent.");

                if (!string.IsNullOrEmpty(reason))
                    log.warnings.Add($"[info] \"{source.name}\" → {mode} ({reason}).");
            }
        }

        // -------------------------------------------------------- mode detect

        /// <summary>
        /// Works out the rendering mode of a source material. Poiyomi is the
        /// tricky one: its default preset is Opaque with Alpha To Coverage doing
        /// the cutout work, so a naive RenderType/queue check reports "Opaque"
        /// for eyelashes and throws their transparency away.
        /// </summary>
        static PSXShaderGUI.RenderMode DetectMode(Material source, SourceFamily family, bool hasAlphaMask, out string reason)
        {
            int queue = EffectiveQueue(source);
            string renderType = source.GetTag("RenderType", false, "");

            // --- strongest signals, regardless of shader family ---
            float atoc;
            if (TryGetFloat(source, out atoc, "_AlphaToMask", "_AlphaToCoverage", "_ATOC") && atoc > 0.5f)
            {
                reason = "source used Alpha To Coverage";
                return PSXShaderGUI.RenderMode.Cutout;
            }

            // --- family-specific rendering-preset properties ---
            float modeValue;
            if (family == SourceFamily.Poiyomi &&
                TryGetFloat(source, out modeValue, "_Mode", "_RenderingPreset"))
            {
                int m = Mathf.RoundToInt(modeValue);
                if (m == 1) { reason = "Poiyomi Cutout preset"; return PSXShaderGUI.RenderMode.Cutout; }
                if (m >= 2) { reason = "Poiyomi transparent preset"; return PSXShaderGUI.RenderMode.Transparent; }
                // m == 0 (Opaque) falls through to the heuristics below, because
                // Poiyomi's Opaque preset is still used with alpha masks + A2C.
            }

            if (family == SourceFamily.LilToon &&
                TryGetFloat(source, out modeValue, "_TransparentMode"))
            {
                int m = Mathf.RoundToInt(modeValue);
                if (m == 1) { reason = "lilToon Cutout mode"; return PSXShaderGUI.RenderMode.Cutout; }
                if (m >= 2) { reason = "lilToon transparent mode"; return PSXShaderGUI.RenderMode.Transparent; }
            }

            // --- generic signals ---
            if (renderType == "Transparent" || renderType == "TransparentFade" ||
                source.IsKeywordEnabled("_ALPHABLEND_ON") || source.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") ||
                queue >= (int)UnityEngine.Rendering.RenderQueue.Transparent)
            {
                reason = "transparent render type or queue";
                return PSXShaderGUI.RenderMode.Transparent;
            }

            if (renderType == "TransparentCutout" || source.IsKeywordEnabled("_ALPHATEST_ON") ||
                queue >= (int)UnityEngine.Rendering.RenderQueue.AlphaTest)
            {
                reason = "cutout render type or queue";
                return PSXShaderGUI.RenderMode.Cutout;
            }

            // An alpha mask is only ever authored to make something see-through,
            // so trust it even when everything else says opaque.
            if (hasAlphaMask)
            {
                reason = "source has an alpha mask texture";
                return PSXShaderGUI.RenderMode.Cutout;
            }

            // A tint alpha below 1 means the author wanted it see-through.
            Color tint = TryGetColor(source, Color.white, "_Color", "_BaseColor", "_MainColor");
            if (tint.a < 0.99f)
            {
                reason = "source tint alpha is below 1";
                return PSXShaderGUI.RenderMode.Transparent;
            }

            reason = "no transparency signals found";
            return PSXShaderGUI.RenderMode.Opaque;
        }

        static bool IsExoticPoiyomiBlend(Material source)
        {
            float mode;
            return TryGetFloat(source, out mode, "_Mode", "_RenderingPreset") && Mathf.RoundToInt(mode) >= 4;
        }

        // --------------------------------------------------- texture importer

        /// <summary>
        /// A texture whose alpha is unused by the importer renders fully opaque,
        /// and one without "Alpha Is Transparency" shows black halos where
        /// bilinear filtering bleeds invisible black texels into visible edges.
        /// Both are the usual cause of "my eyelashes are black rectangles".
        /// </summary>
        static void FixAlphaImportSettings(Texture texture, ConversionLog log)
        {
            if (texture == null) return;
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path)) return;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || !importer.DoesSourceTextureHaveAlpha()) return;

            bool changed = false;
            if (importer.alphaSource == TextureImporterAlphaSource.None)
            {
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                changed = true;
            }
            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                log.textureFixes.Add(Path.GetFileName(path));
            }
        }

        static bool TextureHasAlpha(Texture texture)
        {
            if (texture == null) return false;
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path)) return false;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            return importer != null && importer.DoesSourceTextureHaveAlpha();
        }

        // ------------------------------------------------------------ helpers

        static int EffectiveQueue(Material mat)
        {
            if (mat.renderQueue >= 0) return mat.renderQueue;
            return mat.shader != null ? mat.shader.renderQueue : (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }

        static bool TryGetFloat(Material mat, out float value, params string[] names)
        {
            foreach (string name in names)
            {
                if (mat.HasProperty(name))
                {
                    value = mat.GetFloat(name);
                    return true;
                }
            }
            value = 0f;
            return false;
        }

        static Color TryGetColor(Material mat, Color fallback, params string[] names)
        {
            foreach (string name in names)
                if (mat.HasProperty(name))
                    return mat.GetColor(name);
            return fallback;
        }

        static Texture TryGetTexture(Material mat, params string[] names)
        {
            foreach (string name in names)
            {
                if (!mat.HasProperty(name)) continue;
                Texture tex = mat.GetTexture(name);
                if (tex != null) return tex;
            }
            return null;
        }

        /// <summary>Creates a project-relative folder path (and parents) if missing.</summary>
        public static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder))
                return;

            string[] parts = folder.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets")
                throw new System.ArgumentException($"Output folder must be inside the project's Assets folder: {folder}");

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        public static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
