# TACTIX Stage 2 — Asset System (Drop-in)

This zip contains **Stage 2** files only (no Stage 1 rewrites):
- Asset GUIDs
- Asset registry + database
- Internal mesh + material formats
- JSON serialization/deserialization (.tasset)

## Install
From your repo root (the folder that contains `Engine/` and `TACTIX.csproj`):

1) Copy the `Engine/Assets/**` folders from this zip into your project.
2) Create a top-level `Assets/` folder (if you don't already have one).

## Usage (example)
```csharp
var db = new AssetDatabase(projectRootPath);
db.Initialize(); // scans ./Assets for .tasset files

// Create and save a mesh asset
var mesh = MeshAsset.CreateTriangle();
var meta = db.SaveMesh("Assets/Meshes/Triangle.tasset", mesh, name: "Triangle");

// Load it later
var loaded = db.LoadMesh(meta.Guid);
```

Stage 3 will populate assets automatically from the `Imports/` pipeline.


## Docking Stage 2
See `DOCKING_STAGE2.md`. The editor shell now uses DockManager/DockNode/EditorTool with native selectable tab groups.
