// ============================================================================
// DNR PSX Shader - Material converter
// Creates (or updates in place, preserving GUIDs) PSX materials from existing
// avatar materials, carrying over the main texture, tint, emission, cutoff
// and rendering mode.
// ============================================================================

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DNR.PSX.Editor
{
    public static class PSXMaterialConverter
    {
        public const string ShaderName = "DNR/PSX";
        public const string MaterialSuffix = "_PSX";

        /// <summary>
        /// Converts <paramref name="source"/> into a PSX material asset inside
        /// <paramref name="outputFolder"/> (project-relative, e.g. "Assets/...").
        /// If an asset already exists at the target path it is updated in place
        /// so its GUID (and any animation references to it) survive.
        /// </summary>
        public static Material Convert(Material source, string outputFolder, HashSet<string> usedPaths = null)
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
            {
                target = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            }
            else
            {
                target.shader = shader;
            }

            Configure(target, source);

            if (isNew)
                AssetDatabase.CreateAsset(target, path);
            else
                EditorUtility.SetDirty(target);

            return target;
        }

        /// <summary>Copies the transferable surface settings from source onto target.</summary>
        static void Configure(Material target, Material source)
        {
            // Main texture + tiling/offset.
            Texture main = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : source.mainTexture;
            target.SetTexture("_MainTex", main);
            if (source.HasProperty("_MainTex"))
            {
                target.SetTextureScale("_MainTex", source.GetTextureScale("_MainTex"));
                target.SetTextureOffset("_MainTex", source.GetTextureOffset("_MainTex"));
            }

            // Tint.
            Color color = source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            target.SetColor("_Color", color);

            // Alpha cutoff.
            if (source.HasProperty("_Cutoff"))
                target.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));

            // Emission (Standard-style keyword or lilToon-style float toggle).
            Texture emissionMap = source.HasProperty("_EmissionMap") ? source.GetTexture("_EmissionMap") : null;
            Color emissionColor = source.HasProperty("_EmissionColor") ? source.GetColor("_EmissionColor") : Color.black;
            bool sourceWantsEmission =
                source.IsKeywordEnabled("_EMISSION") ||
                (source.HasProperty("_UseEmission") && source.GetFloat("_UseEmission") > 0.5f);
            bool hasEmission = sourceWantsEmission && (emissionMap != null || emissionColor.maxColorComponent > 0.001f);
            target.SetFloat("_EmissionEnabled", hasEmission ? 1f : 0f);
            if (hasEmission)
            {
                target.SetTexture("_EmissionMap", emissionMap);
                target.SetColor("_EmissionColor", emissionColor);
            }

            // Rendering mode, inferred from tags / keywords / queue.
            PSXShaderGUI.ApplyRenderMode(target, DetectMode(source));
            PSXShaderGUI.ValidateKeywords(target);
        }

        static PSXShaderGUI.RenderMode DetectMode(Material source)
        {
            string renderType = source.GetTag("RenderType", false, "");
            int queue = source.renderQueue;

            if (renderType == "Transparent" || renderType == "TransparentFade" ||
                source.IsKeywordEnabled("_ALPHABLEND_ON") || source.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") ||
                queue >= (int)UnityEngine.Rendering.RenderQueue.Transparent)
                return PSXShaderGUI.RenderMode.Transparent;

            if (renderType == "TransparentCutout" ||
                source.IsKeywordEnabled("_ALPHATEST_ON") ||
                queue >= (int)UnityEngine.Rendering.RenderQueue.AlphaTest)
                return PSXShaderGUI.RenderMode.Cutout;

            return PSXShaderGUI.RenderMode.Opaque;
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
