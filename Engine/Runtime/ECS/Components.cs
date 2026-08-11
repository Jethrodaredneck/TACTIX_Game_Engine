using System.Numerics;
using TACTIX.Engine.Assets.Database;

namespace TACTIX.Engine.Runtime.ECS;

public struct NameComponent
{
    public string Name;
    public NameComponent(string name) => Name = name;
}

public struct TransformComponent
{
    public Vector3 Position;
    public Vector3 Rotation;
    public Vector3 Scale;

    public static TransformComponent Identity => new()
    {
        Position = Vector3.Zero,
        Rotation = Vector3.Zero,
        Scale = Vector3.One
    };
}

public enum BuiltInMesh { Cube, Plane, Sphere, Cylinder, Capsule, Cone }

public struct MeshRendererComponent
{
    public BuiltInMesh Mesh;
    public AssetGuid MeshAssetGuid;

    // Backward-compatible single material override. For multi-slot imported meshes this
    // remains a convenient "override all" value unless an explicit slot override exists.
    public AssetGuid MaterialAssetGuid;

    // Importers decide material-slot count on MeshAsset. The entity only stores optional
    // overrides by slot index; null/empty entries inherit the material authored in the asset.
    public AssetGuid[]? MaterialOverrides;

    public string Material;
    public bool UsesAssetMesh => MeshAssetGuid.Value != Guid.Empty;
    public bool UsesAssetMaterial => MaterialAssetGuid.Value != Guid.Empty || (MaterialOverrides?.Any(g => g.Value != Guid.Empty) ?? false);

    public MeshRendererComponent(BuiltInMesh mesh, string material = "TACTIX_DefaultPrimitive")
    {
        Mesh = mesh;
        MeshAssetGuid = default;
        MaterialAssetGuid = default;
        MaterialOverrides = null;
        Material = material;
    }

    public MeshRendererComponent(AssetGuid meshAssetGuid, AssetGuid? materialAssetGuid = null, string material = "TACTIX_DefaultPrimitive")
    {
        Mesh = BuiltInMesh.Cube;
        MeshAssetGuid = meshAssetGuid;
        MaterialAssetGuid = materialAssetGuid ?? default;
        MaterialOverrides = null;
        Material = material;
    }

    public AssetGuid? MaterialForSlot(int slot)
    {
        if (MaterialOverrides != null && (uint)slot < (uint)MaterialOverrides.Length && MaterialOverrides[slot].Value != Guid.Empty)
            return MaterialOverrides[slot];
        return MaterialAssetGuid.Value != Guid.Empty ? MaterialAssetGuid : null;
    }
}
