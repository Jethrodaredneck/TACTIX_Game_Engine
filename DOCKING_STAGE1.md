# TACTIX Docking Stage 1

This build introduces the editor docking shell without changing the existing Metal renderer.

## Current layout

- Left: Hierarchy
- Center: Viewport (existing MetalView / MetalRenderer)
- Right: Inspector
- Bottom-left: Content Drawer
- Bottom-right: Console

All separators are native AppKit `NSSplitView` dividers and can be resized with the mouse.
The last side/bottom panel sizes are persisted with `NSUserDefaults` and restored on the next launch.

## Deliberately not in Stage 1

- Dragging panels between dock targets
- Floating/undocked editor windows
- Tab stacks and tab reordering
- Close/hide panel buttons
- Real hierarchy/inspector/content/console data

Those build on the dock host after this first shell is verified on macOS.
