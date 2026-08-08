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
    public static MeshAsset CreateTriangle()
    {
        // 3 verts, xyz
        var pos = new float[]
        {
            0.0f,  0.6f, 0.0f,
           -0.6f, -0.6f, 0.0f,
            0.6f, -0.6f, 0.0f
        };
        return new MeshAsset(AssetGuid.New(), pos, null, null, null);
    }
}
