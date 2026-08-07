# DNR PSX Shader

A PlayStation 1 style shader for **VRChat avatars**, with a setup tool that converts your
avatar's materials in one click and builds an **in-game Action Menu toggle** to swap between
your avatar's normal look and the PSX look.

![Unity 2019.4+](https://img.shields.io/badge/Unity-2019.4%2B-blue) ![VRChat SDK3](https://img.shields.io/badge/VRChat-SDK3%20Avatars-green) ![License MIT](https://img.shields.io/badge/License-MIT-lightgrey)

---

## Features

**Shader — `DNR/PSX`**

- **Vertex snapping** — vertices snap to a virtual low-resolution pixel grid, recreating the
  PS1's signature vertex wobble. Adjustable strength and virtual resolution (240 = authentic).
- **Affine texture warping** — blendable perspective-incorrect texture mapping, just like real
  PS1 hardware.
- **Color crush + dithering** — quantizes output color (5 bits/channel = the PS1's 15-bit
  framebuffer) with the PS1 GPU's 4×4 ordered Bayer dither pattern.
- **Chunky texture sampling** — optional virtual texture resolution, forced point filtering that
  overrides the texture's import setting, and a mipmap bypass for authentic distance shimmer.
- **Full color grading** — hue shift, saturation, and contrast applied to the final lit color,
  before the color crush.
- **CRT scanlines** — screen-space scanline overlay with adjustable count and intensity.
- **Three lighting modes** — *Vertex* (classic PSX per-vertex lighting), *Pixel* (smoother), and
  *Unlit*. All modes respect ambient probes, the main directional light with realtime shadows,
  additional realtime lights (ForwardAdd), and up to four per-vertex point lights.
- **Practical avatar features** — emission, opaque / cutout / transparent modes, optional vertex
  colors, minimum brightness so you never go pitch black in dark worlds, correct fog support,
  single-pass stereo (VR) support, GPU instancing, and a proper shadow caster pass.
- **Sensible fallback** — sets the `VRCFallback` tag (Toon / ToonCutout / ToonTransparent) so
  users who block your shader still see a reasonable avatar.

**Tool — `Tools ▸ DNR PSX ▸ Avatar Setup`**

- Scans your avatar, lets you pick which renderers to convert.
- Generates PSX materials from your existing ones (Standard, lilToon, etc. — carries over main
  texture, tint, emission, cutoff, and render mode). **Your original materials are never touched.**
- One-click preview on the avatar, with one-click restore.
- **Builds the complete in-game toggle**: two animation clips (original ↔ PSX material swaps),
  an FX animator layer, a synced + saved Expression Parameter, and an Action Menu toggle.
  Re-running the tool updates everything in place instead of duplicating it.

---

## Installation

**Requirements:** a VRChat avatar project (Unity version per current VRChat docs) with the
**VRChat Avatars SDK** installed via the VRChat Creator Companion (VCC). The shader itself works
in any Built-in Render Pipeline project; only the toggle builder needs the SDK.

### Option A — Add to your project via VCC/UPM (recommended)

1. Download or clone this repository.
2. In VCC, or via Unity's Package Manager (`+ ▸ Add package from disk...`), add the package
   folder (`package.json` is at the repository root).

### Option B — Drop into Assets

1. Download this repository.
2. Copy the whole folder anywhere inside your project's `Assets/` directory.

---

## Quick start

1. Open **Tools ▸ DNR PSX ▸ Avatar Setup**.
2. Drag your avatar (the object with the VRC Avatar Descriptor) into the **Avatar** field.
3. **Step 1** — untick any renderers you want to leave alone.
4. **Step 2** — click **Generate PSX Materials**. Tweak the generated materials to taste
   (they're in the output folder). Use **Preview PSX On Avatar** / **Restore Originals** to compare.
5. **Step 3** — click **Build In-Game Toggle**. Done — upload your avatar and flip the
   **PSX Shader** toggle in your Action Menu in game.

> The toggle's default (OFF) state is your original materials, so your avatar looks unchanged
> until you switch it on. The parameter is synced (everyone sees it) and saved (persists between
> worlds).

---

## Shader settings

| Section | Setting | What it does |
|---|---|---|
| — | Rendering Mode | Opaque / Cutout / Transparent. Sets blending, queue, and VRC fallback automatically. |
| Surface | Albedo / Color | Main texture and tint. |
| Surface | Use Vertex Colors | Multiplies albedo by mesh vertex colors (PS1 models used these heavily). |
| Surface | Emission | Emission map + HDR color, added on top of lighting. |
| PSX Effects | Vertex Snap | Strength of the vertex-grid snapping (0 = off). |
| PSX Effects | Snap Resolution | Vertical resolution of the virtual framebuffer. 240 is authentic; lower = wobblier. |
| PSX Effects | Affine Texture Warp | Perspective-incorrect texture blending. Most visible on large polygons. |
| PSX Effects | Pixelate Texture | Optional virtual texture resolution (texels per UV tile). |
| PSX Effects | Force Point Filtering | Crunchy texel-center sampling, overriding the texture's import filter. |
| PSX Effects | Disable Mipmaps | Full-res sampling at all distances for authentic shimmer. |
| PSX Effects | Color Crush + Dither | Bits per channel (5 = authentic) and Bayer dither strength. |
| Color & CRT | Color Grading | Hue shift / saturation / contrast on the final lit color. |
| Color & CRT | CRT Scanlines | Scanline count (240 = authentic) and intensity. Keep subtle in VR. |
| Lighting | Lighting Mode | Vertex (classic PSX), Pixel (smooth), or Unlit. |
| Lighting | Shading Strength | 1 = full diffuse shading/shadows, 0 = flat lighting that only picks up light color. |
| Lighting | Minimum Brightness | Floor so the avatar never goes fully black in unlit worlds. |
| Advanced | Culling / Queue / Instancing | The usual suspects. |

---

## How the toggle works

The builder is fully non-destructive and idempotent:

- **`PSX Off.anim` / `PSX On.anim`** — animate each converted material slot
  (`m_Materials.Array.data[i]`) to the original or PSX material. Both clips animate the exact
  same property set, so they are Write-Defaults-safe either way; new states match the Write
  Defaults convention already used on your avatar.
- **FX layer `DNR PSX Toggle`** — a two-state machine driven by a bool parameter (default name
  `PSXShader`), with instant transitions. If your avatar has no FX controller, one is created.
- **Expression Parameter** — a synced, saved bool (1 bit). The tool checks your remaining
  parameter budget first and refuses cleanly if there's no space.
- **Menu control** — a Toggle added to your root menu or any menu you choose. If the target
  menu's 8 slots are full, the tool tells you instead of corrupting anything.

Re-running the builder updates the existing clips, layer, parameter and control in place
(material assets are also updated in place, preserving GUIDs so the animations keep working).

---

## Troubleshooting & FAQ

**My avatar is invisible / pink for other people sometimes.**
That's their shader-blocking safety settings — they see the `VRCFallback` Toon shader instead,
which this shader sets automatically. Nothing to fix.

**Does this work on Quest avatars?**
No — VRChat only permits its own mobile shaders on Quest avatars. This shader is for PC avatars;
Quest users will see your Quest version / fallback as usual.

**The toggle does nothing in game.**
Make sure you re-uploaded after building the toggle, and that you didn't rename the parameter in
the FX controller without rebuilding. Rebuilding the toggle is always safe.

**Emission / textures didn't carry over from my material.**
The converter reads the common property names (`_MainTex`, `_Color`, `_EmissionMap`,
`_EmissionColor`, `_Cutoff`). Shaders using exotic property names may need a quick manual copy —
the generated material is a normal material you can edit freely.

**Something about Write Defaults?**
Both toggle states animate the same properties, so the layer is safe with WD on or off. New
states automatically match whichever convention your existing FX layers use.

**Can I use this on worlds/props?**
Sure — it's a normal Built-in Render Pipeline shader. The setup tool is avatar-focused, but the
shader doesn't care what it's on.

---

## Project layout

```
Shaders/
  DNR_PSX.shader          The shader (ForwardBase / ForwardAdd / ShadowCaster)
  Includes/DNRPSXCore.cginc  Shared vertex/fragment programs + PSX helpers
Editor/
  PSXShaderGUI.cs         Custom material inspector
  PSXMaterialConverter.cs Material conversion (GUID-preserving updates)
  PSXToggleBuilder.cs     FX layer / parameters / menu builder (needs VRC SDK)
  PSXAvatarSetupWindow.cs The setup window (Tools ▸ DNR PSX ▸ Avatar Setup)
```

## License

MIT — see [LICENSE.md](LICENSE.md).
