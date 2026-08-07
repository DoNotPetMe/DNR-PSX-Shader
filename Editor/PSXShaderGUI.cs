// ============================================================================
// DNR PSX Shader - Custom material inspector
// Organized foldout UI, rendering-mode handling (blend states, queues,
// keywords, VRChat fallback tags) and keyword validation for materials that
// were created from script.
// ============================================================================

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DNR.PSX.Editor
{
    public class PSXShaderGUI : ShaderGUI
    {
        public enum RenderMode
        {
            Opaque = 0,
            Cutout = 1,
            Transparent = 2
        }

        public const string Version = "1.3.0";

        // ------------------------------------------------------------ state
        MaterialProperty _mode, _mainTex, _color, _cutoff, _vertexColor;
        MaterialProperty _emissionEnabled, _emissionMap, _emissionColor;
        MaterialProperty _snapStrength, _snapResolution, _affineStrength;
        MaterialProperty _pixelate, _pixelResolution, _pointFilter, _noMips;
        MaterialProperty _posterize, _colorBits, _ditherStrength;
        MaterialProperty _colorGrade, _hueShift, _saturation, _contrast;
        MaterialProperty _scanlines, _scanlineCount, _scanlineIntensity;
        MaterialProperty _dotCrawl, _dotCrawlIntensity, _dotCrawlSize, _dotCrawlSpeed, _dotCrawlCoverage;
        MaterialProperty _lighting, _shadeStrength, _minBrightness;
        MaterialProperty _cull;

        static bool foldSurface = true;
        static bool foldPSX = true;
        static bool foldColorCRT = true;
        static bool foldLighting = true;
        static bool foldAdvanced;

        static readonly GUIContent AlbedoLabel = new GUIContent("Albedo", "Base texture and tint.");
        static readonly GUIContent EmissionLabel = new GUIContent("Emission", "Emission texture and HDR color, added on top of lighting.");

        // ------------------------------------------------------------ gui
        public override void OnGUI(MaterialEditor editor, MaterialProperty[] props)
        {
            FindProperties(props);

            foreach (Material mat in editor.targets)
                ValidateKeywords(mat);

            DrawHeader();
            DrawRenderModePopup(editor);

            foldSurface = Foldout(foldSurface, "Surface");
            if (foldSurface)
                DrawSurface(editor);

            foldPSX = Foldout(foldPSX, "PSX Effects");
            if (foldPSX)
                DrawPSXEffects(editor);

            foldColorCRT = Foldout(foldColorCRT, "Color & CRT");
            if (foldColorCRT)
                DrawColorCRT(editor);

            foldLighting = Foldout(foldLighting, "Lighting");
            if (foldLighting)
                DrawLighting(editor);

            foldAdvanced = Foldout(foldAdvanced, "Advanced");
            if (foldAdvanced)
                DrawAdvanced(editor);
        }

        void FindProperties(MaterialProperty[] props)
        {
            _mode            = FindProperty("_Mode", props);
            _mainTex         = FindProperty("_MainTex", props);
            _color           = FindProperty("_Color", props);
            _cutoff          = FindProperty("_Cutoff", props);
            _vertexColor     = FindProperty("_VertexColor", props);
            _emissionEnabled = FindProperty("_EmissionEnabled", props);
            _emissionMap     = FindProperty("_EmissionMap", props);
            _emissionColor   = FindProperty("_EmissionColor", props);
            _snapStrength    = FindProperty("_SnapStrength", props);
            _snapResolution  = FindProperty("_SnapResolution", props);
            _affineStrength  = FindProperty("_AffineStrength", props);
            _pixelate        = FindProperty("_Pixelate", props);
            _pixelResolution = FindProperty("_PixelResolution", props);
            _pointFilter     = FindProperty("_PointFilter", props);
            _noMips          = FindProperty("_NoMips", props);
            _posterize       = FindProperty("_Posterize", props);
            _colorBits       = FindProperty("_ColorBits", props);
            _ditherStrength  = FindProperty("_DitherStrength", props);
            _colorGrade      = FindProperty("_ColorGrade", props);
            _hueShift        = FindProperty("_HueShift", props);
            _saturation      = FindProperty("_Saturation", props);
            _contrast        = FindProperty("_Contrast", props);
            _scanlines       = FindProperty("_Scanlines", props);
            _scanlineCount   = FindProperty("_ScanlineCount", props);
            _scanlineIntensity = FindProperty("_ScanlineIntensity", props);
            _dotCrawl          = FindProperty("_DotCrawl", props);
            _dotCrawlIntensity = FindProperty("_DotCrawlIntensity", props);
            _dotCrawlSize      = FindProperty("_DotCrawlSize", props);
            _dotCrawlSpeed     = FindProperty("_DotCrawlSpeed", props);
            _dotCrawlCoverage  = FindProperty("_DotCrawlCoverage", props);
            _lighting        = FindProperty("_Lighting", props);
            _shadeStrength   = FindProperty("_ShadeStrength", props);
            _minBrightness   = FindProperty("_MinBrightness", props);
            _cull            = FindProperty("_Cull", props);
        }

        void DrawHeader()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("DNR PSX Shader", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label("v" + Version, EditorStyles.miniLabel);
            }
            EditorGUILayout.Space(2);
        }

        void DrawRenderModePopup(MaterialEditor editor)
        {
            EditorGUI.showMixedValue = _mode.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            var mode = (RenderMode)EditorGUILayout.EnumPopup("Rendering Mode", (RenderMode)_mode.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                editor.RegisterPropertyChangeUndo("Rendering Mode");
                _mode.floatValue = (float)mode;
                foreach (Material mat in editor.targets)
                    ApplyRenderMode(mat, mode);
            }
            EditorGUI.showMixedValue = false;
        }

        void DrawSurface(MaterialEditor editor)
        {
            EditorGUI.indentLevel++;
            editor.TexturePropertySingleLine(AlbedoLabel, _mainTex, _color);
            if ((RenderMode)_mode.floatValue == RenderMode.Cutout)
                editor.ShaderProperty(_cutoff, "Alpha Cutoff");
            editor.TextureScaleOffsetProperty(_mainTex);
            editor.ShaderProperty(_vertexColor, new GUIContent("Use Vertex Colors",
                "Multiplies the albedo by the mesh's vertex colors, like PS1 models did. " +
                "Leave off unless your mesh has intentional vertex colors."));

            EditorGUILayout.Space(4);
            editor.ShaderProperty(_emissionEnabled, "Enable Emission");
            if (_emissionEnabled.floatValue > 0.5f)
            {
                editor.TexturePropertySingleLine(EmissionLabel, _emissionMap, _emissionColor);
            }
            EditorGUI.indentLevel--;
        }

        void DrawPSXEffects(MaterialEditor editor)
        {
            EditorGUI.indentLevel++;
            editor.ShaderProperty(_snapStrength, new GUIContent("Vertex Snap",
                "Snaps vertices to a low-resolution pixel grid, recreating the PS1's vertex wobble. 0 disables it."));
            if (_snapStrength.floatValue > 0)
                editor.ShaderProperty(_snapResolution, new GUIContent("Snap Resolution",
                    "Vertical resolution of the virtual framebuffer. 240 matches the PS1's most common video mode. Lower = wobblier."));

            editor.ShaderProperty(_affineStrength, new GUIContent("Affine Texture Warp",
                "Blends toward PS1-style perspective-incorrect texture mapping. Most visible on large polygons viewed at an angle."));

            EditorGUILayout.Space(4);
            editor.ShaderProperty(_pixelate, new GUIContent("Pixelate Texture",
                "Quantizes UVs so the texture appears lower resolution, without changing texture import settings."));
            if (_pixelate.floatValue > 0.5f)
                editor.ShaderProperty(_pixelResolution, new GUIContent("Pixel Resolution",
                    "Virtual texture size in texels per UV tile."));
            editor.ShaderProperty(_pointFilter, new GUIContent("Force Point Filtering",
                "Samples on texel centers for a crunchy point-filtered look, overriding the texture's import filter setting."));
            editor.ShaderProperty(_noMips, new GUIContent("Disable Mipmaps",
                "Always samples the full-resolution texture for authentic distance shimmer, like hardware without mipmapping."));

            EditorGUILayout.Space(4);
            editor.ShaderProperty(_posterize, new GUIContent("Color Crush + Dither",
                "Reduces color precision and applies the PS1's ordered dither pattern."));
            if (_posterize.floatValue > 0.5f)
            {
                editor.ShaderProperty(_colorBits, new GUIContent("Bits Per Channel",
                    "5 bits matches the PS1's 15-bit framebuffer."));
                editor.ShaderProperty(_ditherStrength, new GUIContent("Dither Strength",
                    "How strongly the Bayer dither pattern is applied before quantization."));
            }
            EditorGUI.indentLevel--;
        }

        void DrawColorCRT(MaterialEditor editor)
        {
            EditorGUI.indentLevel++;
            editor.ShaderProperty(_colorGrade, new GUIContent("Enable Color Grading",
                "Hue / saturation / contrast applied to the final lit color, before the color crush."));
            if (_colorGrade.floatValue > 0.5f)
            {
                editor.ShaderProperty(_hueShift, "Hue Shift");
                editor.ShaderProperty(_saturation, "Saturation");
                editor.ShaderProperty(_contrast, "Contrast");
            }

            EditorGUILayout.Space(4);
            editor.ShaderProperty(_scanlines, new GUIContent("CRT Scanlines",
                "Screen-space scanline overlay simulating a CRT display. Subtle values work best in VR."));
            if (_scanlines.floatValue > 0.5f)
            {
                editor.ShaderProperty(_scanlineCount, new GUIContent("Scanline Count",
                    "Number of scanlines over the screen height. 240 matches the PS1's typical output."));
                editor.ShaderProperty(_scanlineIntensity, "Scanline Intensity");
            }

            EditorGUILayout.Space(4);
            editor.ShaderProperty(_dotCrawl, new GUIContent("Composite Dot Crawl",
                "Small crawling R/G/B beads along silhouette edges and high-contrast detail, like a console " +
                "hooked to a CRT over composite video."));
            if (_dotCrawl.floatValue > 0.5f)
            {
                editor.ShaderProperty(_dotCrawlIntensity, new GUIContent("Intensity",
                    "How visible the colored beads are."));
                editor.ShaderProperty(_dotCrawlSize, new GUIContent("Dot Size (Pixels)",
                    "Screen-pixel size of each bead. 2-4 reads like an old TV at typical VRChat resolutions."));
                editor.ShaderProperty(_dotCrawlSpeed, new GUIContent("Crawl Speed",
                    "How fast the beads crawl vertically along the edges."));
                editor.ShaderProperty(_dotCrawlCoverage, new GUIContent("Edge Coverage",
                    "How far in from the silhouette the beads reach. Low values keep them on the outermost rim."));
            }
            EditorGUI.indentLevel--;
        }

        void DrawLighting(MaterialEditor editor)
        {
            EditorGUI.indentLevel++;
            editor.ShaderProperty(_lighting, new GUIContent("Lighting Mode",
                "Vertex = classic PSX per-vertex lighting. Pixel = smoother modern lighting. Unlit = albedo only."));

            bool unlit = Mathf.RoundToInt(_lighting.floatValue) == 2;
            using (new EditorGUI.DisabledScope(unlit))
            {
                editor.ShaderProperty(_shadeStrength, new GUIContent("Shading Strength",
                    "1 = full diffuse shading and shadows. 0 = flat lighting that only picks up light color."));
                editor.ShaderProperty(_minBrightness, new GUIContent("Minimum Brightness",
                    "Keeps the avatar from going fully black in unlit worlds."));
            }
            EditorGUI.indentLevel--;
        }

        void DrawAdvanced(MaterialEditor editor)
        {
            EditorGUI.indentLevel++;
            editor.ShaderProperty(_cull, "Culling");
            editor.RenderQueueField();
            editor.EnableInstancingField();
            editor.DoubleSidedGIField();
            EditorGUILayout.Space(2);
            EditorGUILayout.HelpBox(
                "VRChat fallback: Toon (matching the rendering mode) for users who block this shader. " +
                "Set automatically when the rendering mode changes.", MessageType.None);
            EditorGUI.indentLevel--;
        }

        static bool Foldout(bool state, string title)
        {
            EditorGUILayout.Space(2);
            bool result = EditorGUILayout.BeginFoldoutHeaderGroup(state, title);
            EditorGUILayout.EndFoldoutHeaderGroup();
            return result;
        }

        // ------------------------------------------------------------ setup

        /// <summary>
        /// Applies blend state, render queue, keywords and VRChat fallback tag
        /// for the given rendering mode. Also used by the material converter.
        /// </summary>
        public static void ApplyRenderMode(Material mat, RenderMode mode)
        {
            mat.SetFloat("_Mode", (float)mode);
            switch (mode)
            {
                case RenderMode.Opaque:
                    mat.SetOverrideTag("RenderType", "Opaque");
                    mat.SetOverrideTag("VRCFallback", "Toon");
                    mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                    mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                    mat.SetFloat("_ZWrite", 1f);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.renderQueue = -1;
                    break;

                case RenderMode.Cutout:
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.SetOverrideTag("VRCFallback", "ToonCutout");
                    mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                    mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                    mat.SetFloat("_ZWrite", 1f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.renderQueue = (int)RenderQueue.AlphaTest;
                    break;

                case RenderMode.Transparent:
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.SetOverrideTag("VRCFallback", "ToonTransparent");
                    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.renderQueue = (int)RenderQueue.Transparent;
                    break;
            }
        }

        /// <summary>
        /// Keeps shader keywords in sync with property values. Needed for
        /// materials created from script, where property drawers never ran.
        /// </summary>
        public static void ValidateKeywords(Material mat)
        {
            SetKeyword(mat, "_EMISSION", mat.GetFloat("_EmissionEnabled") > 0.5f);
            SetKeyword(mat, "_DNR_PIXELATE", mat.GetFloat("_Pixelate") > 0.5f);
            SetKeyword(mat, "_DNR_POSTERIZE", mat.GetFloat("_Posterize") > 0.5f);
            SetKeyword(mat, "_DNR_VERTEXCOLOR", mat.GetFloat("_VertexColor") > 0.5f);
            SetKeyword(mat, "_DNR_NOMIPS", mat.GetFloat("_NoMips") > 0.5f);
            SetKeyword(mat, "_DNR_COLORGRADE", mat.GetFloat("_ColorGrade") > 0.5f);
            SetKeyword(mat, "_DNR_SCANLINES", mat.GetFloat("_Scanlines") > 0.5f);
            SetKeyword(mat, "_DNR_DOTCRAWL", mat.GetFloat("_DotCrawl") > 0.5f);

            int lighting = Mathf.RoundToInt(mat.GetFloat("_Lighting"));
            SetKeyword(mat, "_LIGHTING_VERTEX", lighting == 0);
            SetKeyword(mat, "_LIGHTING_PIXEL", lighting == 1);
            SetKeyword(mat, "_LIGHTING_UNLIT", lighting == 2);
        }

        static void SetKeyword(Material mat, string keyword, bool enabled)
        {
            if (enabled)
            {
                if (!mat.IsKeywordEnabled(keyword)) mat.EnableKeyword(keyword);
            }
            else
            {
                if (mat.IsKeywordEnabled(keyword)) mat.DisableKeyword(keyword);
            }
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            if (material.HasProperty("_Mode"))
            {
                // Infer a sensible mode from the previous shader's queue.
                RenderMode mode = RenderMode.Opaque;
                int queue = material.renderQueue;
                if (queue >= (int)RenderQueue.Transparent) mode = RenderMode.Transparent;
                else if (queue >= (int)RenderQueue.AlphaTest) mode = RenderMode.Cutout;
                ApplyRenderMode(material, mode);
                ValidateKeywords(material);
            }
        }
    }
}
