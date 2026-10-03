# TACTIX Editor Transform Gizmos - Candidate 1

Built on the authoritative TACTIX 0.3 baseline.

## Viewport controls

- Left click: select a scene entity or grab a visible transform axis.
- Right drag: orbit editor camera.
- Middle drag: pan editor camera.
- Scroll: dolly editor camera.
- F: frame selected entity.
- Q: Select tool.
- W: Move tool.
- E: Rotate tool.
- R: Scale tool.
- X: toggle World / Local gizmo orientation.
- Shift while dragging: snap (move 0.25 units, rotate 15 degrees, scale 0.1).
- Escape: cancel the active transform drag and restore the starting transform.
- Command-Z: undo.
- Shift-Command-Z: redo.

## Architecture

The gizmo is editor-only AppKit overlay state. It performs hit testing and transform preview against the runtime World, but commits the final edit through `SetTransformCommand`, producing one undo step per drag. Camera projection helpers mirror the perspective convention in `Shaders/Triangle.metal` so gizmo handles remain aligned with rendered scene entities.

The Metal renderer remains unaware of editor tool types. It only receives camera basis/projection data and selected entity identity.

## Candidate limitations

- Picking is sphere-based around built-in mesh bounds rather than GPU/object-ID picking.
- Rotation dragging is screen-space around the gizmo center; it is axis constrained but not yet a full ray/plane rotation solver.
- Multi-selection and pivot modes are not implemented.
- No hierarchy parenting exists in 0.3, so Local orientation uses the selected entity's own Euler rotation.
