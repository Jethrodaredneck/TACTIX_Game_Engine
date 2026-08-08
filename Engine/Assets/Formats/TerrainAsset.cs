using TACTIX.Engine.Assets.Database;

namespace TACTIX.Engine.Assets.Formats;

public readonly record struct TerrainLayer(
    AssetGuid? MaterialGuid,
    float TilingX,
    float TilingZ,
    float BlendStrength
);

public readonly record struct TerrainAsset(
    AssetGuid Guid,
    int Resolution,
    float SizeX,
    float SizeZ,
    float HeightScale,
    float[] Heights,
    TerrainLayer[] Layers
)
{
    public static TerrainAsset CreateFlat(int resolution = 129, float sizeX = 64f, float sizeZ = 64f, float heightScale = 16f)
    {
        if (resolution < 2) throw new ArgumentOutOfRangeException(nameof(resolution));
        if (sizeX <= 0f) throw new ArgumentOutOfRangeException(nameof(sizeX));
        if (sizeZ <= 0f) throw new ArgumentOutOfRangeException(nameof(sizeZ));
        if (heightScale <= 0f) throw new ArgumentOutOfRangeException(nameof(heightScale));

        return new TerrainAsset(
            AssetGuid.New(),
            resolution,
            sizeX,
            sizeZ,
            heightScale,
            new float[resolution * resolution],
            Array.Empty<TerrainLayer>());
    }

    public int IndexOf(int x, int z)
    {
        if ((uint)x >= (uint)Resolution) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)z >= (uint)Resolution) throw new ArgumentOutOfRangeException(nameof(z));
        return z * Resolution + x;
    }

    public float HeightAt(int x, int z) => Heights[IndexOf(x, z)];
}
