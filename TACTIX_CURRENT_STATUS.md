# TACTIX Current Development Status

This document tracks the actual development head rather than the older checklist text on stacked draft pull requests.

## Branch lineage

- `main` — authoritative TACTIX 0.3 baseline.
- `agent/transform-gizmos` — interactive viewport and transform-gizmo candidate stacked on `main`.
- `agent/terrain-assets-foundation` — terrain, asset-pipeline, GLB/model-hierarchy work stacked on the transform branch.
- `agent/terrain-sculpt-loop` — current terrain-authoring continuation stacked on `agent/terrain-assets-foundation`.

Do not restart feature work from `main` until the stacked branches have been smoke-tested and consolidated.

## Working engine/editor foundation

- macOS editor shell, docking layout, Hierarchy, Inspector, Project/Content Browser, and Scene viewport.
- Metal renderer with built-in primitives, imported mesh rendering, basic material/texture support, directional/local lighting, and terrain rendering.
- ECS world, scene serialization, editor selection, command stack, undo/redo, object creation/deletion/duplication, and transform gizmos.
- GUID-backed asset database and `.tasset` formats for meshes, materials, textures, models, and terrain.
- GLB mesh/model import path, imported model node hierarchy creation, source metadata/hashes, reimport plumbing, Blender-to-GLB bridge, and Content Browser import/reimport controls.
- Terrain asset creation, CPU mesh generation, scene references, Raise/Lower/Smooth/Flatten brush math, and Metal terrain GPU cache invalidation through asset content hashes.

## Terrain sculpt loop added on this branch

- CPU ray/triangle picking against the actual transformed terrain heightfield.
- Modular viewport overlay that stays out of the existing transform tool path until Terrain mode is enabled.
- Left-drag sculpting with Raise/Lower, Smooth, and Flatten modes.
- Option temporarily lowers in Raise/Lower mode; Shift temporarily smooths; Control temporarily flattens.
- Adjustable world-space brush radius and brush strength.
- Flatten captures the first contact height as the stable target for the stroke.
- Preview writes and terrain GPU rebuilds are throttled to a bounded cadence while every brush stamp remains in memory for the final result.
- One complete mouse stroke commits as one `IEditorCommand`, so Command-Z/Redo operates per stroke rather than per brush sample.
- Escape/cancel restores the terrain from the pre-stroke snapshot.
- Terrain brush inputs are clamped/validated and normalized-height sampling is shared rather than duplicated in UI code.
- Terrain duplication deep-copies the backing `TerrainAsset` to a new GUID/project path so sculpting one terrain cannot silently modify its duplicate.
- Undoing a terrain duplication removes its generated asset; redoing it recreates the same independent asset from the command snapshot.

## Required Mac smoke test before promotion

1. Build and launch the `agent/terrain-sculpt-loop` branch on Apple Silicon.
2. Create a terrain from the Hierarchy.
3. Enable the Terrain button in the Scene viewport.
4. Verify Raise/Lower, Smooth, and Flatten visibly refresh the Metal terrain mesh while dragging.
5. Verify Option lowers, Shift smooths, and Control flattens.
6. Verify brush radius/strength controls behave predictably.
7. Verify one Command-Z removes one full stroke and Shift-Command-Z restores it.
8. Verify Escape during a stroke restores its starting terrain.
9. Orbit/pan/dolly while Terrain mode is enabled and confirm camera interaction remains stable.
10. Duplicate a terrain, sculpt only one copy, and verify the other copy remains unchanged; then Undo/Redo the duplication.
11. Save/reload the scene and confirm terrain GUIDs still resolve to the edited `.tasset` files.

## Next dependencies after the sculpt smoke test

- Tighten terrain picking/performance if the 129x129 CPU heightfield pass is measurably expensive during dense strokes; then move to cell/chunk acceleration rather than renderer-coupled picking.
- Add terrain material-layer painting and a proper terrain material shader path.
- Add terrain chunking, LOD, collision, and later navmesh integration behind the existing backend-neutral terrain seam.
- Finish and register the remaining interchange importers behind the common importer contracts, especially FBX/OBJ/USD routes that are still incomplete or planned.
- Harden model/asset bounds picking beyond the current primitive sphere approximation.
- Perform the physical-Mac transform-gizmo smoke test that still blocks promotion of the stacked editor branches.
- After the stack is validated, consolidate the branch chain so `main` again reflects the real engine state before larger gameplay/runtime systems are layered on top.
