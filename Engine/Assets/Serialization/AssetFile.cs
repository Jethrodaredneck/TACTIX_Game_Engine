using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Engine.Assets.Serialization;

public sealed class AssetFile
{
    public required AssetMeta Meta { get; init; }

    // Exactly one of these should be present based on Meta.Type
    public MeshAsset? Mesh { get; init; }
    public MaterialAsset? Material { get; init; }

    public static AssetFile ForMesh(AssetMeta meta, MeshAsset mesh)
        => new() { Meta = meta, Mesh = mesh, Material = null };

    public static AssetFile ForMaterial(AssetMeta meta, MaterialAsset material)
        => new() { Meta = meta, Mesh = null, Material = material };
}
