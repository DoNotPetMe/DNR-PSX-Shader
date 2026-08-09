# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.7.0] - 2026-08-09

### Added
- **Survival Horror grade** (`_DNR_HORROR`), recreating the PS1 horror look:
  - Per-material distance fog with color, start, end and density. Independent of the world's
    fog settings, which avatars cannot change - and the horror look needs fog far tighter than
    any social world uses. Additive lights fade into it too, so they don't punch through.
  - Animated film grain that steps at 24fps for a film flicker, with adjustable cell size, or
    static for a dirty-lens look.
  - Vignette with adjustable strength and softness.
  - Black crush and black lift for crushed or faded-tape palettes, plus a grade tint.
- **Grunge overlay** (`_DNR_GRUNGEMAP`): a user-supplied dirt/rust/noise texture layered over
  the albedo with multiply or overlay blending, following the mesh UVs or projected in screen
  space like muck on the camera lens.
- **Built-in presets** - Authentic PS1, Survival Horror, Foggy Nightmare and Clean Retro -
  available as one-click buttons in both the material inspector (applies to all selected
  materials, undoable) and the setup window's global preset. Each preset resets from a common
  baseline, so switching never leaves a stray effect enabled from the previous style.
- Global look settings gained a "Horror" group, which propagates its colors and the grunge
  texture (with tiling) alongside the numeric settings.

## [1.6.0] - 2026-08-09

### Added
- **Global look settings** (step 5 in the setup window): a preset material whose stylistic
  settings can be pushed to every PSX material on the avatar at once, so all meshes share one
  look. Per-material identity (textures, tint, cutoff, emission, alpha masks, transparency
  mode, culling, render queue) is never touched.
  - Property groups can be applied selectively: PSX Effects, Color & CRT, Lighting.
  - The preset is edited through the real material inspector embedded in the window, so the
    tool can never drift out of sync with the shader's own UI.
  - "Pull Settings From Avatar Into Preset" seeds the preset from an existing material, and
    "Create Preset From Current Look" starts from what the avatar already has.
  - Optional live sync propagates each edit automatically while the window is open.
  - Optionally covers every PSX material in the output folder, not just those on the avatar.
  - Applied as a single undoable step, with keywords re-derived on each target.

## [1.5.0] - 2026-08-09

### Added
- **Alpha mask support** in the shader: a second texture (with channel select, invert, and
  multiply/replace modes) drives transparency, matching how Poiyomi and lilToon author
  eyelashes, hair cards and decals. Applied in the shadow caster too, so cutout shadows agree
  with the visible surface.
- **Alpha To Coverage** (`AlphaToMask`) for MSAA-smoothed cutout edges without sorting issues.
- **Premultiplied alpha** option for blended surfaces, removing dark halos where a texture's
  invisible texels are black.
- New "Transparency" section in the material inspector, with guidance when the current
  rendering mode makes alpha a no-op.
- Conversion report (dialog + Console) listing detected modes, textures repaired, and any
  material converted as Opaque whose texture still has an alpha channel.

### Fixed
- **Poiyomi materials converted as fully opaque, rendering eyelashes and hair as black
  shapes.** The converter now understands Poiyomi Toon and Poiyomi Pro (locked or unlocked):
  it reads the `_Mode`/`_RenderingPreset` preset, Alpha To Coverage flags, alpha mask
  textures, and tint alpha instead of trusting `RenderType`/queue alone - Poiyomi's default
  Opaque preset is routinely used *with* alpha, which is what produced the black shapes.
- Culling is now carried over, so Poiyomi's double-sided hair, eyelashes and skirts no longer
  lose a side.
- Emission now respects `_EnableEmission` and `_EmissionStrength` (Poiyomi) alongside the
  Standard `_EMISSION` keyword, and lilToon's `_UseEmission`.
- Deliberate render queues are preserved for cutout and transparent materials.
- Source textures whose importer has "Alpha Is Transparency" (or the alpha channel itself)
  disabled are repaired for see-through materials, removing black halos on transparent edges.
  Opt out with the checkbox in the setup window.

## [1.4.0] - 2026-08-07

### Added
- **Dot crawl motion response**: two new sliders, "Viewer Motion" (the observer's own walking
  and looking around advances the crawl - per-viewer, since the shader runs on each client)
  and "Avatar Motion" (the avatar moving through the world advances it). With "Auto Crawl
  Speed" at 0 the beads now freeze until someone moves, matching how the artifact behaved on
  real CRTs where the interference only shifted when the on-screen image changed.

### Changed
- "Crawl Speed" renamed to "Auto Crawl Speed" to distinguish it from the motion-driven crawl.
- Rewrote the "Force Point Filtering" tooltip and added a README FAQ entry explaining when
  the effect is visible (magnified/low-res textures) and why high-res textures show no change.

## [1.3.0] - 2026-08-07

### Added
- **In-game setting radials**: optional step 4 in the setup window. Pick any of 8 shader
  settings (Vertex Snap, Affine Warp, Pixelation, Color Crush, Dither, Scanlines, Dot Crawl,
  Hue Shift) and the tool builds, per setting: a motion-time FX layer, a synced + saved float
  parameter, and a Radial Puppet in a "PSX Settings" submenu linked into your menu. Effects
  needed by a radial are force-enabled on the PSX materials (keywords cannot be animated),
  with material defaults aligned to the radial's default position. Idempotent re-runs,
  up-front parameter budget check, and a live bit-cost readout in the window.

## [1.2.0] - 2026-08-07

### Added
- **Composite dot crawl**: screen-space R/G/B beads that cling to silhouette edges (glancing
  view angles) and high-contrast texture detail, crawling vertically over time - recreating
  the composite-video artifact old CRT TVs showed on PS1/PS2 characters. Controls for
  intensity, dot size, crawl speed, and edge coverage. Base pass only, so additional
  realtime lights never double the effect.

## [1.1.0] - 2026-08-07

### Added
- **Force Point Filtering**: texel-center sampling that overrides the texture's import filter
  setting (applies to albedo and emission independently, using each texture's own texel size).
- **Disable Mipmaps**: full-resolution sampling at all distances for authentic PS1 shimmer.
- **Color grading**: hue shift, saturation, and contrast on the final lit color, applied
  before the color crush so the graded result still dithers correctly.
- **CRT scanlines**: screen-space scanline overlay with adjustable count and intensity,
  applied after posterization (simulating the display, not the console output).
- New "Color & CRT" section in the material inspector; shared version constant.

## [1.0.0] - 2026-08-07

### Added
- `DNR/PSX` shader: vertex snapping, affine texture warping, color crush with 4×4 Bayer
  dithering, texture pixelation, vertex/pixel/unlit lighting modes, emission, vertex colors,
  opaque/cutout/transparent rendering modes, realtime shadows (cast + receive), ForwardAdd
  lights, fog, single-pass stereo and GPU instancing support, VRCFallback tags.
- Custom material inspector with foldout sections and automatic render-mode handling.
- **Avatar Setup** window (`Tools ▸ DNR PSX ▸ Avatar Setup`): renderer selection, one-click
  PSX material generation from existing materials (non-destructive, GUID-preserving on
  regeneration), on-avatar preview/restore.
- **In-game toggle builder**: generates material-swap animation clips, FX layer, synced +
  saved Expression Parameter, and Action Menu toggle; idempotent re-runs; parameter budget
  and menu capacity checks; matches the avatar's existing Write Defaults convention.
- VCC/UPM-compatible package layout with stable GUIDs.
