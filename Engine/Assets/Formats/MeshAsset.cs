using System;
using TACTIX.Engine.Assets.Database;

namespace TACTIX.Engine.Assets.Formats;

/// <summary>
/// A render range inside one mesh. Importers decide how many submeshes/material slots an
/// authored asset contains; runtime/editor code consumes this generic representation and
/// does not care whether the source was GLB, FBX, OBJ, USD, etc.
/// </summary>
public readonly record struct MeshSubmesh(
    int FirstIndex,
    int IndexCount,
    AssetGuid? DefaultMaterialGuid,
    string Name = ""
);

public readonly record struct MeshAsset(
    AssetGuid Guid,
    float[] Positions,
    float[]? Normals,
    float[]? UV0,
    uint[]? Indices
)
{
    // Legacy/single-slot default kept for compatibility with existing OBJ assets.
    public AssetGuid? DefaultMaterialGuid { get; init; }

    // Asset-defined material topology. Empty means the whole mesh is one draw range.
    // This is deliberately source-format agnostic so future FBX/USD importers can emit
    // exactly the material-slot count authored in the source asset.
    public MeshSubmesh[] Submeshes { get; init; } = [];

    public int MaterialSlotCount => Submeshes.Length > 0 ? Submeshes.Length : 1;

    public static MeshAsset CreateTriangle()
    {
        var pos = new float[]
        {
            0.0f,  0.6f, 0.0f,
           -0.6f, -0.6f, 0.0f,
            0.6f, -0.6f, 0.0f
        };
        return new MeshAsset(AssetGuid.New(), pos, null, null, null);
    }
}
