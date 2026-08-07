# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
