// ============================================================================
// DNR PSX Shader - Core Include
// ----------------------------------------------------------------------------
// Shared vertex/fragment programs for the ForwardBase and ForwardAdd passes,
// plus the PSX effect helpers (vertex snapping, affine texture warping,
// color posterization and ordered dithering).
//
// The pass type is detected through UNITY_PASS_FORWARDBASE / FORWARDADD,
// which Unity defines automatically from the pass LightMode tag.
// ============================================================================

#ifndef DNR_PSX_CORE_INCLUDED
#define DNR_PSX_CORE_INCLUDED

#include "UnityCG.cginc"
#include "Lighting.cginc"
#include "AutoLight.cginc"

// ----------------------------------------------------------------------------
// Material properties
// ----------------------------------------------------------------------------
sampler2D _MainTex;
float4    _MainTex_ST;
float4    _MainTex_TexelSize;
fixed4    _Color;
fixed     _Cutoff;

sampler2D _EmissionMap;
float4    _EmissionMap_TexelSize;
half4     _EmissionColor;

half _SnapStrength;
half _SnapResolution;
half _AffineStrength;

half _PixelResolution;
half _PointFilter;

half _ColorBits;
half _DitherStrength;

half _HueShift;
half _Saturation;
half _Contrast;

half _ScanlineCount;
half _ScanlineIntensity;

half _DotCrawlIntensity;
half _DotCrawlSize;
half _DotCrawlSpeed;
half _DotCrawlCoverage;
half _DotCrawlViewMotion;
half _DotCrawlAvatarMotion;

half _ShadeStrength;
half _MinBrightness;

// ----------------------------------------------------------------------------
// PSX helpers
// ----------------------------------------------------------------------------

// Snaps a clip-space position onto the pixel grid of a virtual low-resolution
// framebuffer. This reproduces the PS1's lack of sub-pixel vertex precision
// (the characteristic "wobble" as vertices pop between pixels).
float4 DNRSnapVertex(float4 clipPos)
{
    if (_SnapStrength > 0.0001 && clipPos.w > 0.0001)
    {
        // Virtual resolution: user controls the vertical size, horizontal is
        // derived from the current aspect ratio so pixels stay square.
        float2 res  = float2(_SnapResolution * (_ScreenParams.x / _ScreenParams.y), _SnapResolution);
        float2 cell = 2.0 / max(res, 1.0);                  // NDC size of one virtual pixel
        float2 ndc  = clipPos.xy / clipPos.w;
        float2 snapped = (floor(ndc / cell) + 0.5) * cell;  // snap to virtual pixel centers
        ndc = lerp(ndc, snapped, saturate(_SnapStrength));
        clipPos.xy = ndc * clipPos.w;
    }
    return clipPos;
}

// 4x4 Bayer threshold matrix, matching the ordered dither pattern the PS1
// GPU used when writing 24-bit colors into the 15-bit framebuffer.
static const float DNR_BAYER4[16] =
{
     0.0,  8.0,  2.0, 10.0,
    12.0,  4.0, 14.0,  6.0,
     3.0, 11.0,  1.0,  9.0,
    15.0,  7.0, 13.0,  5.0
};

// Quantizes color down to _ColorBits bits per channel, with optional
// screen-space ordered dithering applied before the quantization step.
fixed3 DNRPosterize(fixed3 col, float2 pixelPos)
{
    float steps = exp2(round(_ColorBits)) - 1.0;
    uint2 p = (uint2)pixelPos % 4u;
    float threshold = (DNR_BAYER4[p.y * 4u + p.x] + 0.5) / 16.0 - 0.5; // [-0.5, 0.5)
    col = saturate(col + threshold * (_DitherStrength / steps));
    return floor(col * steps + 0.5) / steps;
}

// Snaps a UV to the center of the nearest texel so a bilinear sampler returns
// (effectively) the point-filtered result, overriding the import setting.
float2 DNRPointFilterUV(float2 uv, float4 texelSize)
{
    if (_PointFilter > 0.5 && texelSize.z > 1.0)
        uv = (floor(uv * texelSize.zw) + 0.5) * texelSize.xy;
    return uv;
}

// Texture sampling that can bypass mipmaps for authentic distance shimmer.
inline fixed4 DNRSampleTex(sampler2D tex, float2 uv)
{
#if defined(_DNR_NOMIPS)
    return tex2Dlod(tex, float4(uv, 0, 0));
#else
    return tex2D(tex, uv);
#endif
}

#if defined(_DNR_DOTCRAWL)
// Composite-video "dot crawl": on a CRT fed a composite signal, luma and
// chroma share one wire, so sharp edges leak small colored beads that crawl
// vertically along silhouettes and high-contrast detail. Reproduced here as
// screen-space R/G/B beads masked to glancing view angles and luma edges,
// with a time-animated vertical phase.
fixed3 DNRDotCrawl(fixed3 col, float2 pixelPos, float3 worldNormal, float3 worldPos)
{
    // Silhouette band: only the outermost glancing-angle rim lights up.
    float3 viewDir = normalize(_WorldSpaceCameraPos - worldPos);
    float rim = 1.0 - saturate(dot(normalize(worldNormal), viewDir));
    float mask = smoothstep(1.0 - saturate(_DotCrawlCoverage), 1.0, rim);

    // High-contrast texture edges leak a little too.
    half luma = dot(col, half3(0.299, 0.587, 0.114));
    mask = max(mask, saturate(fwidth(luma) * 4.0) * 0.75);

    // Crawling bead pattern: checkerboard cells whose vertical phase advances
    // with (a) time, (b) the viewer's own head/camera motion, and (c) the
    // avatar's motion. On a real CRT the interference pattern shifted whenever
    // the on-screen image changed, so the crawl is per-viewer: each person
    // sees the dots move when *they* move or when the avatar moves.
    float cellSize = max(_DotCrawlSize, 1.0);
    float crawlCells = _Time.y * _DotCrawlSpeed;

    // Viewer motion: camera translation plus a rotation term from the camera
    // forward axis, so both walking and looking around advance the phase.
    float3 camFwd = -UNITY_MATRIX_V[2].xyz;
    crawlCells += (_WorldSpaceCameraPos.x + _WorldSpaceCameraPos.y + _WorldSpaceCameraPos.z)
                  * _DotCrawlViewMotion * 24.0;
    crawlCells += (camFwd.x + 2.0 * camFwd.y + 3.0 * camFwd.z) * _DotCrawlViewMotion * 6.0;

    // Avatar motion: the renderer's world position advances the phase.
    float3 objPos = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
    crawlCells += (objPos.x + objPos.y + objPos.z) * _DotCrawlAvatarMotion * 24.0;

    float2 crawlPos = float2(pixelPos.x, pixelPos.y + crawlCells * cellSize);
    int2 cell = (int2)floor(crawlPos / cellSize);
    float checker = (float)((cell.x + cell.y) & 1);
    uint channel = (uint)abs(cell.x) % 3u;
    fixed3 bead = fixed3(channel == 0u ? 1.0 : 0.0,
                         channel == 1u ? 1.0 : 0.0,
                         channel == 2u ? 1.0 : 0.0);
    return lerp(col, bead, mask * checker * _DotCrawlIntensity);
}
#endif

#if defined(_DNR_COLORGRADE)
// Compact RGB<->HSV (Sam Hocevar's branchless formulation).
float3 DNRRgbToHsv(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 DNRHsvToRgb(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
}

// Hue shift -> saturation -> contrast, applied to the final lit color.
fixed3 DNRColorGrade(fixed3 col)
{
    float3 hsv = DNRRgbToHsv(saturate(col));
    hsv.x = frac(hsv.x + _HueShift / 360.0 + 1.0);
    col = DNRHsvToRgb(hsv);
    half luma = dot(col, half3(0.299, 0.587, 0.114));
    col = lerp(luma.xxx, col, _Saturation);
    col = (col - 0.5) * _Contrast + 0.5;
    return saturate(col);
}
#endif

// ----------------------------------------------------------------------------
// Structures
// ----------------------------------------------------------------------------
struct appdata
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float2 uv     : TEXCOORD0;
    float2 uv1    : TEXCOORD1;
    fixed4 color  : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// The forward programs use AutoLight's per-light-type macros, which only exist
// when a forward pass is being compiled - keep them out of the shadow caster.
#if defined(UNITY_PASS_FORWARDBASE) || defined(UNITY_PASS_FORWARDADD)

struct v2f
{
    float4 pos         : SV_POSITION;
    float4 uvw         : TEXCOORD0; // xy = uv * w (affine numerator), z = w
    float2 uv          : TEXCOORD1; // perspective-correct uv
    float3 worldPos    : TEXCOORD2;
    float3 worldNormal : TEXCOORD3;
    fixed3 vLight      : TEXCOORD4; // per-vertex ambient / vertex point lights
    fixed3 vDirLight   : TEXCOORD5; // per-vertex directional diffuse (pre-attenuation)
    UNITY_FOG_COORDS(6)
    DECLARE_LIGHT_COORDS(7)
    UNITY_SHADOW_COORDS(8)
    fixed4 vColor      : COLOR;
    UNITY_VERTEX_OUTPUT_STEREO
};

// ----------------------------------------------------------------------------
// Vertex program
// ----------------------------------------------------------------------------
v2f DNRVert(appdata v)
{
    v2f o;
    UNITY_INITIALIZE_OUTPUT(v2f, o);
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    float4 clipPos = UnityObjectToClipPos(v.vertex);
    clipPos = DNRSnapVertex(clipPos);
    o.pos = clipPos;

    float2 uv = TRANSFORM_TEX(v.uv, _MainTex);
    o.uv  = uv;
    // Pre-multiplying by w and dividing by the interpolated w in the fragment
    // shader cancels the GPU's perspective correction, giving PS1-style
    // screen-space (affine) texture interpolation.
    o.uvw = float4(uv * clipPos.w, clipPos.w, 0);

    o.worldPos    = mul(unity_ObjectToWorld, v.vertex).xyz;
    float3 wn     = UnityObjectToWorldNormal(v.normal);
    o.worldNormal = wn;
    o.vColor      = v.color;

#if !defined(_LIGHTING_UNLIT)
    #if defined(UNITY_PASS_FORWARDBASE)
        #if !defined(_LIGHTING_PIXEL)
            // Classic PSX: everything per-vertex. Ambient probes and up to four
            // point lights go into vLight; the main directional light is kept
            // separate so it can still receive shadows per-pixel.
            o.vLight = ShadeSH9(float4(wn, 1.0));
            #if defined(VERTEXLIGHT_ON)
                o.vLight += Shade4PointLights(
                    unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
                    unity_LightColor[0].rgb, unity_LightColor[1].rgb,
                    unity_LightColor[2].rgb, unity_LightColor[3].rgb,
                    unity_4LightAtten0, o.worldPos, wn);
            #endif
            half ndl = saturate(dot(wn, _WorldSpaceLightPos0.xyz));
            o.vDirLight = _LightColor0.rgb * lerp(1.0, ndl, _ShadeStrength);
        #else
            // Pixel lighting: only the non-important point lights stay
            // per-vertex (that is all Unity provides for them).
            #if defined(VERTEXLIGHT_ON)
                o.vLight = Shade4PointLights(
                    unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
                    unity_LightColor[0].rgb, unity_LightColor[1].rgb,
                    unity_LightColor[2].rgb, unity_LightColor[3].rgb,
                    unity_4LightAtten0, o.worldPos, wn);
            #endif
        #endif
    #elif defined(UNITY_PASS_FORWARDADD)
        #if !defined(_LIGHTING_PIXEL)
            half ndlAdd = saturate(dot(wn, normalize(UnityWorldSpaceLightDir(o.worldPos))));
            o.vDirLight = _LightColor0.rgb * lerp(1.0, ndlAdd, _ShadeStrength);
        #endif
    #endif
#endif

    COMPUTE_LIGHT_COORDS(o)
    UNITY_TRANSFER_SHADOW(o, v.uv1);
    UNITY_TRANSFER_FOG(o, o.pos);
    return o;
}

// ----------------------------------------------------------------------------
// Fragment program
// ----------------------------------------------------------------------------
fixed4 DNRFrag(v2f i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

    // Affine texture warp, then optional texel quantization.
    float2 uv = lerp(i.uv, i.uvw.xy / max(i.uvw.z, 0.0001), saturate(_AffineStrength));
#if defined(_DNR_PIXELATE)
    float px = max(_PixelResolution, 1.0);
    uv = (floor(uv * px) + 0.5) / px;
#endif

    fixed4 albedo = DNRSampleTex(_MainTex, DNRPointFilterUV(uv, _MainTex_TexelSize)) * _Color;
#if defined(_DNR_VERTEXCOLOR)
    albedo *= i.vColor;
#endif

#if defined(_ALPHATEST_ON)
    clip(albedo.a - _Cutoff);
#endif

    // ------------------------------------------------------------------ light
    fixed3 lighting = fixed3(1, 1, 1);
#if defined(_LIGHTING_UNLIT)
    #if defined(UNITY_PASS_FORWARDADD)
        lighting = fixed3(0, 0, 0); // additive passes contribute nothing when unlit
    #endif
#else
    UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
    #if defined(UNITY_PASS_FORWARDADD)
        // Note: atten includes distance falloff, so it is applied in full.
        // Only the N.L term is softened by the shading slider.
        #if defined(_LIGHTING_PIXEL)
            half ndl = saturate(dot(normalize(i.worldNormal), normalize(UnityWorldSpaceLightDir(i.worldPos))));
            lighting = _LightColor0.rgb * lerp(1.0, ndl, _ShadeStrength) * atten;
        #else
            lighting = i.vDirLight * atten;
        #endif
    #else
        half attenSoft = lerp(1.0, atten, _ShadeStrength);
        #if defined(_LIGHTING_PIXEL)
            float3 n = normalize(i.worldNormal);
            half ndl = saturate(dot(n, _WorldSpaceLightPos0.xyz));
            lighting = ShadeSH9(float4(n, 1.0)) + i.vLight
                     + _LightColor0.rgb * lerp(1.0, ndl, _ShadeStrength) * attenSoft;
        #else
            lighting = i.vLight + i.vDirLight * attenSoft;
        #endif
        lighting = max(lighting, _MinBrightness.xxx);
    #endif
#endif

    fixed3 col = albedo.rgb * lighting;

#if defined(_EMISSION) && defined(UNITY_PASS_FORWARDBASE)
    col += DNRSampleTex(_EmissionMap, DNRPointFilterUV(uv, _EmissionMap_TexelSize)).rgb * _EmissionColor.rgb;
#endif

#if defined(_DNR_COLORGRADE)
    col = DNRColorGrade(col);
#endif

#if defined(_DNR_POSTERIZE)
    col = DNRPosterize(col, i.pos.xy);
#endif

#if defined(_DNR_DOTCRAWL) && defined(UNITY_PASS_FORWARDBASE)
    col = DNRDotCrawl(col, i.pos.xy, i.worldNormal, i.worldPos);
#endif

#if defined(_DNR_SCANLINES)
    // Smooth rolling scanline mask over screen height, after posterization -
    // it simulates the display, not the console output.
    float scan = 0.5 + 0.5 * cos(UNITY_TWO_PI * i.pos.y * _ScanlineCount / _ScreenParams.y);
    col *= 1.0 - _ScanlineIntensity * scan;
#endif

    fixed alpha = albedo.a;
#if !defined(_ALPHABLEND_ON)
    alpha = 1.0;
#endif

    fixed4 result = fixed4(col, alpha);
    // UNITY_APPLY_FOG automatically fogs toward black in additive passes.
    UNITY_APPLY_FOG(i.fogCoord, result);
    return result;
}

#endif // UNITY_PASS_FORWARDBASE || UNITY_PASS_FORWARDADD

// ----------------------------------------------------------------------------
// Shadow caster
// ----------------------------------------------------------------------------
#if defined(UNITY_PASS_SHADOWCASTER)
// Vertex snapping is intentionally NOT applied here: the caster renders from
// the light's point of view, where snapping would only cause shadow acne. The
// offset this introduces is at most one virtual pixel and is not noticeable.
struct v2fShadow
{
    V2F_SHADOW_CASTER;
    float2 uv : TEXCOORD1;
    UNITY_VERTEX_OUTPUT_STEREO
};

v2fShadow DNRVertShadow(appdata v)
{
    v2fShadow o;
    UNITY_INITIALIZE_OUTPUT(v2fShadow, o);
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
    o.uv = TRANSFORM_TEX(v.uv, _MainTex);
    return o;
}

fixed4 DNRFragShadow(v2fShadow i) : SV_Target
{
#if defined(_ALPHATEST_ON)
    fixed a = tex2D(_MainTex, i.uv).a * _Color.a;
    clip(a - _Cutoff);
#elif defined(_ALPHABLEND_ON)
    fixed a = tex2D(_MainTex, i.uv).a * _Color.a;
    clip(a - 0.5); // blended surfaces cast shadow only where mostly opaque
#endif
    SHADOW_CASTER_FRAGMENT(i)
}

#endif // UNITY_PASS_SHADOWCASTER

#endif // DNR_PSX_CORE_INCLUDED
