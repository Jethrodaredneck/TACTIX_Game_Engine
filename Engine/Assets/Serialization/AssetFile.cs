using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Engine.Assets.Serialization;

public sealed class AssetFile
{
    public required AssetMeta Meta { get; init; }

    // Exactly one payload should be present based on Meta.Type.
    public MeshAsset? Mesh { get; init; }
    public MaterialAsset? Material { get; init; }
    public TerrainAsset? Terrain { get; init; }

    public static AssetFile ForMesh(AssetMeta meta, MeshAsset mesh)
        => new() { Meta = meta, Mesh = mesh, Material = null, Terrain = null };

    public static AssetFile ForMaterial(AssetMeta meta, MaterialAsset material)
        => new() { Meta = meta, Mesh = null, Material = material, Terrain = null };

    public static AssetFile ForTerrain(AssetMeta meta, TerrainAsset terrain)
        => new() { Meta = meta, Mesh = null, Material = null, Terrain = terrain };
}
