# TACTIX Docking Stage 2

This pass replaces the fixed Stage 1 panel layout with a reusable docking model.

## New architecture

- `DockManager` registers panels, builds dock trees, and persists layout state.
- `DockNode` is the base layout node.
- `SplitDockNode` describes reusable left/right or top/bottom splits.
- `TabDockNode` describes one dock region containing one or more panel tabs.
- `DockPanel` separates a panel identity/title/content factory from where it is docked.
- `DockTabGroupView` is the native AppKit tab host.
- `EditorTool` lets each editor tool provide its own panels and default layout.
- `SceneEditorTool` owns the current Hierarchy, Viewport, Inspector, Content Drawer, and Console configuration.

## Current default scene layout

Hierarchy | Viewport | Inspector
          | Content Drawer / Console tabs

The existing Metal renderer and MetalView are unchanged. The same ViewportPanelView
instance is registered as the Scene editor's viewport panel.

## Working in this stage

- Reusable declarative split tree
- Real selectable tab groups
- Content Drawer and Console share the bottom dock as tabs
- Unique internal panel IDs (for example `Scene.Inspector`)
- Split ratios persist by dock-node ID
- Selected tabs persist by dock-node ID
- Scene editor layout is supplied by `SceneEditorTool`, not hard-coded in `DockHostView`

## Deliberately deferred to Stage 3

- Mouse drag of tabs between dock nodes
- Drop-target overlays
- Floating native panel windows
- Redocking floating windows
- Runtime mutation/serialization of the full dock tree
