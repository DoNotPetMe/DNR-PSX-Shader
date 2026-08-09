// ============================================================================
// DNR PSX Shader - Global look settings
// Copies the "look" settings of one PSX material onto many, so every mesh on
// an avatar shares one consistent PSX style.
//
// Per-material identity is deliberately never touched: textures, tint, cutoff,
// emission, alpha masks, transparency mode, culling and render queue all stay
// exactly as the converter set them up for that specific mesh. Only the
// stylistic knobs travel.
// ============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DNR.PSX.Editor
{
    public static class PSXGlobalSettings
    {
        /// <summary>PSX Effects section: the retro filter itself.</summary>
        public static readonly string[] EffectProperties =
        {
            "_SnapStrength", "_SnapResolution", "_AffineStrength",
            "_Pixelate", "_PixelResolution", "_PointFilter", "_NoMips",
            "_Posterize", "_ColorBits", "_DitherStrength",
        };

        /// <summary>Color &amp; CRT section: grading, scanlines, dot crawl.</summary>
        public static readonly string[] ColorProperties =
        {
            "_ColorGrade", "_HueShift", "_Saturation", "_Contrast",
            "_Scanlines", "_ScanlineCount", "_ScanlineIntensity",
            "_DotCrawl", "_DotCrawlIntensity", "_DotCrawlSize", "_DotCrawlSpeed",
            "_DotCrawlViewMotion", "_DotCrawlAvatarMotion", "_DotCrawlCoverage",
        };

        /// <summary>Survival Horror &amp; Grunge section.</summary>
        public static readonly string[] HorrorProperties =
        {
            "_Horror", "_HorrorFogStart", "_HorrorFogEnd", "_HorrorFogDensity",
            "_GrainStrength", "_GrainSize", "_GrainAnimate",
            "_VignetteStrength", "_VignetteSoftness",
            "_HorrorCrush", "_HorrorLift",
            "_GrungeEnabled", "_GrungeStrength", "_GrungeBlend", "_GrungeScreenSpace",
        };

        /// <summary>Colors belonging to the horror group.</summary>
        public static readonly string[] HorrorColorProperties = { "_HorrorFogColor", "_HorrorTint" };

        /// <summary>Textures belonging to the horror group (tiling travels too).</summary>
        public static readonly string[] HorrorTextureProperties = { "_GrungeMap" };

        /// <summary>Lighting section.</summary>
        public static readonly string[] LightingProperties =
        {
            "_Lighting", "_ShadeStrength", "_MinBrightness",
        };

        public static IEnumerable<string> Gather(bool effects, bool color, bool lighting, bool horror)
        {
            if (effects) foreach (string p in EffectProperties) yield return p;
            if (color) foreach (string p in ColorProperties) yield return p;
            if (horror) foreach (string p in HorrorProperties) yield return p;
            if (lighting) foreach (string p in LightingProperties) yield return p;
        }

        /// <summary>Copies the non-float properties of the enabled groups.</summary>
        static void CopyNonFloats(Material source, Material target, bool horror)
        {
            if (!horror) return;

            foreach (string property in HorrorColorProperties)
                if (source.HasProperty(property) && target.HasProperty(property))
                    target.SetColor(property, source.GetColor(property));

            foreach (string property in HorrorTextureProperties)
            {
                if (!source.HasProperty(property) || !target.HasProperty(property)) continue;
                target.SetTexture(property, source.GetTexture(property));
                target.SetTextureScale(property, source.GetTextureScale(property));
                target.SetTextureOffset(property, source.GetTextureOffset(property));
            }
        }

        public static bool IsPSXMaterial(Material mat)
        {
            return mat != null && mat.shader != null && mat.shader.name == PSXMaterialConverter.ShaderName;
        }

        /// <summary>
        /// Pushes the selected property groups from <paramref name="source"/>
        /// onto every PSX material in <paramref name="targets"/>. Returns how
        /// many materials were changed. Undoable as a single step.
        /// </summary>
        public static int Apply(Material source, IList<Material> targets,
            bool effects, bool color, bool lighting, bool horror)
        {
            if (!IsPSXMaterial(source))
                throw new System.InvalidOperationException(
                    "The preset material must use the DNR/PSX shader.");

            var properties = new List<string>(Gather(effects, color, lighting, horror));
            if (properties.Count == 0)
                throw new System.InvalidOperationException(
                    "No property groups selected. Tick at least one group to apply.");

            var changed = new List<Material>();
            foreach (Material target in targets)
            {
                if (!IsPSXMaterial(target) || target == source)
                    continue;
                changed.Add(target);
            }
            if (changed.Count == 0)
                return 0;

            Undo.RecordObjects(changed.ToArray(), "Apply PSX Global Settings");

            foreach (Material target in changed)
            {
                foreach (string property in properties)
                {
                    // Both materials use the same shader, but guard anyway so a
                    // future property rename can never throw mid-batch.
                    if (source.HasProperty(property) && target.HasProperty(property))
                        target.SetFloat(property, source.GetFloat(property));
                }
                CopyNonFloats(source, target, horror);

                // Several of these properties only drive shader keywords, so
                // the keywords have to be re-derived after copying the floats.
                PSXShaderGUI.ValidateKeywords(target);
                EditorUtility.SetDirty(target);
            }

            AssetDatabase.SaveAssets();
            return changed.Count;
        }

        /// <summary>Copies every look setting from one PSX material to another.</summary>
        public static void CopyAll(Material source, Material target)
        {
            if (!IsPSXMaterial(source) || !IsPSXMaterial(target))
                return;

            foreach (string property in Gather(true, true, true, true))
                if (source.HasProperty(property) && target.HasProperty(property))
                    target.SetFloat(property, source.GetFloat(property));
            CopyNonFloats(source, target, true);
            PSXShaderGUI.ValidateKeywords(target);
            EditorUtility.SetDirty(target);
        }

        /// <summary>
        /// Cheap fingerprint of a material's look settings, used to detect
        /// edits while live sync is enabled.
        /// </summary>
        public static int Fingerprint(Material mat, bool effects, bool color, bool lighting, bool horror)
        {
            if (!IsPSXMaterial(mat))
                return 0;

            unchecked
            {
                int hash = 17;
                foreach (string property in Gather(effects, color, lighting, horror))
                    if (mat.HasProperty(property))
                        hash = hash * 31 + mat.GetFloat(property).GetHashCode();
                if (horror)
                {
                    foreach (string property in HorrorColorProperties)
                        if (mat.HasProperty(property))
                            hash = hash * 31 + mat.GetColor(property).GetHashCode();
                    foreach (string property in HorrorTextureProperties)
                    {
                        if (!mat.HasProperty(property)) continue;
                        Texture tex = mat.GetTexture(property);
                        hash = hash * 31 + (tex != null ? tex.GetInstanceID() : 0);
                        hash = hash * 31 + mat.GetTextureScale(property).GetHashCode();
                    }
                }
                return hash;
            }
        }

        /// <summary>
        /// Creates the preset material asset, seeding it from an existing PSX
        /// material when one is available so the preset starts from the look
        /// the avatar already has.
        /// </summary>
        public static Material CreatePreset(string outputFolder, Material seed)
        {
            Shader shader = Shader.Find(PSXMaterialConverter.ShaderName);
            if (shader == null)
                throw new System.InvalidOperationException(
                    $"Shader \"{PSXMaterialConverter.ShaderName}\" not found.");

            PSXMaterialConverter.EnsureFolder(outputFolder);
            string path = outputFolder + "/PSX Global Settings.mat";

            Material preset = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (preset == null)
            {
                preset = new Material(shader) { name = "PSX Global Settings" };
                AssetDatabase.CreateAsset(preset, path);
            }
            else
            {
                preset.shader = shader;
            }

            if (IsPSXMaterial(seed))
            {
                foreach (string property in Gather(true, true, true, true))
                    if (seed.HasProperty(property) && preset.HasProperty(property))
                        preset.SetFloat(property, seed.GetFloat(property));
                CopyNonFloats(seed, preset, true);
                PSXShaderGUI.ValidateKeywords(preset);
            }

            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
            return preset;
        }
    }
}
