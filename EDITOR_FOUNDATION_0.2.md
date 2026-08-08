# TACTIX Editor Foundation 0.2

This pass establishes the first real editor data loop without replacing the existing docking, Metal, asset database, AI bridge, or Ollama work.

Implemented foundation:
- Runtime Entity/World remains the source of truth.
- NameComponent, TransformComponent and MeshRendererComponent.
- Built-in mesh identifiers for Cube, Plane, Sphere, Cylinder, Capsule and Cone.
- Shared EditorSelection service.
- Editor command stack with undo/redo foundation and undoable transform edits.
- Hierarchy panel now reads actual scene entities and controls shared selection.
- Inspector panel now reads/writes the selected entity Transform.
- Startup logo cube is now represented in the runtime scene as an actual entity.

Next integration pass:
1. Render extraction: MeshRendererComponent -> RenderWorld -> Metal backend.
2. Built-in primitive mesh library and TACTIX_DefaultPrimitive ghost material.
3. Viewport picking and editor camera.
4. Transform gizmos using the command stack.
5. Versioned .tactixscene serialization and Save/Open.
6. Content Drawer backed by the existing AssetDatabase.
7. Play-mode runtime scene clone.

Architectural rule: Editor UI, AI tools, and future scripting must modify scene state through shared engine/editor operations rather than owning parallel scene representations.
