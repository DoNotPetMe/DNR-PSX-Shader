// ============================================================================
// DNR PSX - PlayStation 1 style shader for VRChat avatars
// ----------------------------------------------------------------------------
// Features: vertex snapping, affine texture warping, color posterization with
// ordered (Bayer) dithering, texture pixelation, per-vertex or per-pixel
// lighting with realtime shadows, emission, cutout and transparent modes.
//
// Use "Tools > DNR PSX > Avatar Setup" to convert an avatar's materials and
// build an in-game toggle that swaps between the original and PSX materials.
// ============================================================================

Shader "DNR/PSX"
{
    Properties
    {
        // ------------------------------------------------------------ surface
        [HideInInspector] _Mode ("Rendering Mode", Float) = 0
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Toggle(_DNR_VERTEXCOLOR)] _VertexColor ("Use Vertex Colors", Float) = 0

        [Toggle(_EMISSION)] _EmissionEnabled ("Enable Emission", Float) = 0
        _EmissionMap ("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)

        // -------------------------------------------------------- psx effects
        _SnapStrength ("Vertex Snap", Range(0, 1)) = 1
        _SnapResolution ("Snap Resolution (Vertical)", Range(48, 480)) = 240
        _AffineStrength ("Affine Texture Warp", Range(0, 1)) = 1

        [Toggle(_DNR_PIXELATE)] _Pixelate ("Pixelate Texture", Float) = 0
        _PixelResolution ("Pixel Resolution", Range(16, 1024)) = 256
        [Toggle] _PointFilter ("Force Point Filtering", Float) = 0
        [Toggle(_DNR_NOMIPS)] _NoMips ("Disable Mipmaps", Float) = 0

        [Toggle(_DNR_POSTERIZE)] _Posterize ("Color Crush + Dither", Float) = 1
        [IntRange] _ColorBits ("Bits Per Channel", Range(3, 8)) = 5
        _DitherStrength ("Dither Strength", Range(0, 1)) = 1

        // -------------------------------------------------------- color / crt
        [Toggle(_DNR_COLORGRADE)] _ColorGrade ("Enable Color Grading", Float) = 0
        _HueShift ("Hue Shift", Range(-180, 180)) = 0
        _Saturation ("Saturation", Range(0, 2)) = 1
        _Contrast ("Contrast", Range(0, 2)) = 1

        [Toggle(_DNR_SCANLINES)] _Scanlines ("CRT Scanlines", Float) = 0
        _ScanlineCount ("Scanline Count", Range(50, 480)) = 240
        _ScanlineIntensity ("Scanline Intensity", Range(0, 1)) = 0.25

        // ----------------------------------------------------------- lighting
        [KeywordEnum(Vertex, Pixel, Unlit)] _Lighting ("Lighting Mode", Float) = 0
        _ShadeStrength ("Shading Strength", Range(0, 1)) = 1
        _MinBrightness ("Minimum Brightness", Range(0, 1)) = 0.03

        // ----------------------------------------------------------- advanced
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Culling", Float) = 2
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
            "VRCFallback" = "Toon"
        }
        LOD 100
        Cull [_Cull]

        // -------------------------------------------------------------------
        // Base pass: ambient + main directional light + vertex point lights
        // -------------------------------------------------------------------
        Pass
        {
            Name "FORWARD_BASE"
            Tags { "LightMode" = "ForwardBase" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            CGPROGRAM
            #pragma vertex DNRVert
            #pragma fragment DNRFrag
            #pragma target 3.0

            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // Avatars are never lightmapped; trim those variants.
            #pragma skip_variants LIGHTMAP_ON DYNAMICLIGHTMAP_ON DIRLIGHTMAP_COMBINED SHADOWS_SHADOWMASK LIGHTMAP_SHADOW_MIXING

            #pragma shader_feature_local _LIGHTING_VERTEX _LIGHTING_PIXEL _LIGHTING_UNLIT
            #pragma shader_feature_local _EMISSION
            #pragma shader_feature_local _DNR_PIXELATE
            #pragma shader_feature_local _DNR_POSTERIZE
            #pragma shader_feature_local _DNR_VERTEXCOLOR
            #pragma shader_feature_local _DNR_NOMIPS
            #pragma shader_feature_local _DNR_COLORGRADE
            #pragma shader_feature_local _DNR_SCANLINES
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _ALPHABLEND_ON

            #include "Includes/DNRPSXCore.cginc"
            ENDCG
        }

        // -------------------------------------------------------------------
        // Additive pass: extra realtime point / spot / directional lights
        // -------------------------------------------------------------------
        Pass
        {
            Name "FORWARD_ADD"
            Tags { "LightMode" = "ForwardAdd" }
            Blend [_SrcBlend] One
            ZWrite Off

            CGPROGRAM
            #pragma vertex DNRVert
            #pragma fragment DNRFrag
            #pragma target 3.0

            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #pragma shader_feature_local _LIGHTING_VERTEX _LIGHTING_PIXEL _LIGHTING_UNLIT
            #pragma shader_feature_local _DNR_PIXELATE
            #pragma shader_feature_local _DNR_POSTERIZE
            #pragma shader_feature_local _DNR_VERTEXCOLOR
            #pragma shader_feature_local _DNR_NOMIPS
            #pragma shader_feature_local _DNR_COLORGRADE
            #pragma shader_feature_local _DNR_SCANLINES
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _ALPHABLEND_ON

            #include "Includes/DNRPSXCore.cginc"
            ENDCG
        }

        // -------------------------------------------------------------------
        // Shadow caster
        // -------------------------------------------------------------------
        Pass
        {
            Name "SHADOW_CASTER"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual

            CGPROGRAM
            #pragma vertex DNRVertShadow
            #pragma fragment DNRFragShadow
            #pragma target 3.0

            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _ALPHABLEND_ON

            #include "Includes/DNRPSXCore.cginc"
            ENDCG
        }
    }

    Fallback "VertexLit"
    CustomEditor "DNR.PSX.Editor.PSXShaderGUI"
}
