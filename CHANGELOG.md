# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
