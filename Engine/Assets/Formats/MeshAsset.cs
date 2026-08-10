using System;
using TACTIX.Engine.Assets.Database;

namespace TACTIX.Engine.Assets.Formats;

public readonly record struct MeshAsset(
    AssetGuid Guid,
    float[] Positions,
    float[]? Normals,
    float[]? UV0,
    uint[]? Indices
)
{
    // Import-time default material. This is intentionally only a default binding;
    // runtime/editor entities remain free to override it through MeshRendererComponent.
    public AssetGuid? DefaultMaterialGuid { get; init; }

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
