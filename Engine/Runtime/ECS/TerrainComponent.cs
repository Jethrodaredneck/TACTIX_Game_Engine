using TACTIX.Engine.Assets.Database;

namespace TACTIX.Engine.Runtime.ECS;

/// <summary>
/// Runtime scene reference to terrain data owned by the asset system.
/// Terrain editing state stays in the editor; runtime entities only retain the asset identity.
/// </summary>
public struct TerrainComponent
{
    public AssetGuid TerrainAssetGuid;

    public TerrainComponent(AssetGuid terrainAssetGuid)
    {
        TerrainAssetGuid = terrainAssetGuid;
    }
}
