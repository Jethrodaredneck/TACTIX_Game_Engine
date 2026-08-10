using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Engine.Assets.Serialization;

public sealed class AssetFile
{
    public required AssetMeta Meta { get; init; }

    // Exactly one payload should be present based on Meta.Type.
    public MeshAsset? Mesh { get; init; }
    public MaterialAsset? Material { get; init; }
    public TextureAsset? Texture { get; init; }
    public TerrainAsset? Terrain { get; init; }
    public ModelAsset? Model { get; init; }

    public static AssetFile ForMesh(AssetMeta meta, MeshAsset mesh)
        => new() { Meta = meta, Mesh = mesh, Material = null, Texture = null, Terrain = null, Model = null };

    public static AssetFile ForMaterial(AssetMeta meta, MaterialAsset material)
        => new() { Meta = meta, Mesh = null, Material = material, Texture = null, Terrain = null, Model = null };

    public static AssetFile ForTexture(AssetMeta meta, TextureAsset texture)
        => new() { Meta = meta, Mesh = null, Material = null, Texture = texture, Terrain = null, Model = null };

    public static AssetFile ForTerrain(AssetMeta meta, TerrainAsset terrain)
        => new() { Meta = meta, Mesh = null, Material = null, Texture = null, Terrain = terrain, Model = null };

    public static AssetFile ForModel(AssetMeta meta, ModelAsset model)
        => new() { Meta = meta, Mesh = null, Material = null, Texture = null, Terrain = null, Model = model };
}
