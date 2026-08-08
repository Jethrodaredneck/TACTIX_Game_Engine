# TACTIX Docking Stage 3

Stage 3 extends the Stage 2 declarative dock tree without changing the Metal renderer.

Implemented:
- drag tabs directly by their tab headers
- center drop = add as a tab to the target dock
- left/right/top/bottom drop = create a new split dock at that target
- drop outside all docks = create a floating native AppKit window
- floating tabs use the same drag path and can be redocked
- when the last tab leaves a dock, the empty dock collapses
- visible five-region dock overlay while dragging
- existing Stage 2 split/tab persistence remains intact for the default tree

Notes:
- dynamic splits/floating-window positions are currently runtime state. Full serialization of the mutated dock tree is the next persistence pass.
- the Scene viewport still owns the same MetalView/MetalRenderer path from the hardened Stage 2 base.
