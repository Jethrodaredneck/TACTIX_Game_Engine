# TACTIX Current Development Status

Updated after consolidation of the former stacked editor branches into `main`.

## Authoritative branch

- `main` is now the authoritative development head.
- PR #4 (`agent/terrain-sculpt-loop`) was merged into PR #3's terrain/assets branch.
- PR #3 was merged into PR #2's transform/editor branch.
- PR #2 was then merged into `main`.
- New feature work should branch from `main`; the former stacked branches are historical only.

## Working engine/editor foundation

- Native macOS editor shell with docking layout, Hierarchy, Inspector, Project/Content Browser, and Scene viewport.
- Metal renderer with built-in primitives, imported mesh rendering, basic material/texture support, directional/local lighting, and terrain rendering.
- ECS world, scene serialization, editor selection, command stack, undo/redo, object creation/deletion/duplication, and transform gizmos.
- Interactive editor camera plus Move/Rotate/Scale tools, axis constraints, World/Local orientation, snapping, cancel, and undo-safe transform commits.
- GUID-backed asset database and `.tasset` formats for meshes, materials, textures, models, and terrain.
- GLB mesh/model import path, imported model hierarchy creation, source metadata/hashes, reimport plumbing, Blender-to-GLB bridge, and Content Browser import/reimport controls.
- Terrain asset creation, CPU mesh generation, scene references, and Metal terrain GPU cache invalidation through asset content hashes.
- CLI, MCP tooling, and AI bridge foundations remain present in the consolidated tree.

## Terrain authoring validation

Physical Apple Silicon smoke testing passed for the terrain authoring loop:

- Terrain creation from the Hierarchy.
- Terrain mode activation in the Scene viewport.
- Raise/Lower sculpting.
- Smooth/Soften sculpting.
- Flatten sculpting.
- Brush radius and strength controls.
- Undo and redo per sculpt stroke.
- Camera navigation while Terrain mode is active.
- Terrain duplication independence.
- Save/reload persistence of edited terrain assets.

The implemented terrain loop also includes CPU ray/triangle picking, throttled live preview rebuilds, stroke-level command transactions, Escape/cancel restore, modifier-assisted sculpt modes, validated brush inputs, shared normalized-height sampling, and independent duplicated terrain assets.

## Asset pipeline reality check

The repository already contains native implementations for:

- `ObjMeshImporter`
- `MtlMaterialImporter`
- `TextureAssetImporter`

However, the default built-in importer registry still registers OBJ and texture routes through planned placeholder handlers. The next importer cleanup should register and validate the native implementations end-to-end before adding more formats.

## Next development priorities

1. Wire and validate native OBJ -> MTL -> texture/material -> mesh import through the default registry.
2. Validate the transform-gizmo/editor interaction stack on the physical Mac if any cases remain untested.
3. Add terrain material-layer painting and a dedicated terrain material shader path.
4. Add terrain chunking, LOD, collision, and later navigation integration behind the existing backend-neutral terrain seam.
5. Add/finish FBX and USD interchange routes behind the common importer contracts.
6. Harden imported-model/object picking beyond the current approximate bounds path.
7. Add dependency-driven automatic reimport and improve Content Browser drag/drop/editor UX.

## Consolidation point

The October 3, 2026 consolidation establishes `main` as the single source of truth for the current TACTIX editor/engine foundation. Do not restart work from the old stacked branches.
