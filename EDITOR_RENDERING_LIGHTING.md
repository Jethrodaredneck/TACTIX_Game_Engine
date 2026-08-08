# TACTIX Rendering + Lighting Milestone

This iteration replaces the prototype position/UV primitive path with a reusable lit mesh vertex contract.

## Implemented

- Primitive vertices now carry position, normal, and UV data.
- Cube faces use explicit outward normals and consistent front-face winding.
- Metal vertex descriptors and shaders consume normals directly.
- Non-uniform scale applies inverse-scale normal correction before object rotation.
- Diffuse directional lighting replaces the old world-Z fake shading term.
- `LightComponent` is backend-neutral and supports Directional, Point, and Spot identities/settings.
- Directional lights are consumed by Metal now; a renderer fallback sun keeps scenes visible when no light exists.
- Light entities participate in editor Commands/Undo, duplication, deletion, and scene Save/Load.
- Hierarchy creation controls and Inspector light settings are present.
- Terrain's temporary plane render bridge automatically uses the same normal-aware lit pipeline.

## Next lighting work

- Point-light distance attenuation.
- Spot-light cone attenuation.
- Multiple active lights / clustered or tiled light collection as scene scale grows.
- Light editor icons/gizmos.
- Color editing and enable/shadow controls in Inspector.
- Shadow maps, then environment/sky lighting.

## Renderer direction

Imported GLB/FBX/OBJ meshes should feed this same position/normal/UV vertex contract (with tangents added for normal maps) rather than getting a separate import-only renderer.
