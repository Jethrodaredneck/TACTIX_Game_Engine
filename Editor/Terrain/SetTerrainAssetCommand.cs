using TACTIX.Editor.Scene;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Editor.Terrain;

/// <summary>
/// Commits one complete terrain sculpt stroke as one editor command. Preview writes
/// may happen while the mouse is down, but the durable before/after mutation belongs
/// to this command so Command-Z restores the complete stroke in one step.
/// </summary>
public sealed class SetTerrainAssetCommand : IEditorCommand
{
    private readonly AssetDatabase _assets;
    private readonly string _projectPath;
    private readonly string _name;
    private readonly TerrainAsset _before;
    private readonly TerrainAsset _after;

    public SetTerrainAssetCommand(AssetDatabase assets, TerrainAsset before, TerrainAsset after)
    {
        _assets = assets;

        if (before.Guid.Value == Guid.Empty || before.Guid != after.Guid)
            throw new ArgumentException("Terrain sculpt command requires matching, non-empty asset GUIDs.");
        if (before.Resolution != after.Resolution ||
            before.SizeX != after.SizeX ||
            before.SizeZ != after.SizeZ ||
            before.HeightScale != after.HeightScale)
            throw new ArgumentException("Terrain sculpt command may only change terrain height/layer content, not terrain geometry metadata.");
        if (!_assets.Registry.TryGet(before.Guid, out var meta) || meta.Type != AssetType.Terrain)
            throw new InvalidOperationException($"Terrain asset is not registered: {before.Guid}");

        _projectPath = meta.ProjectPath;
        _name = meta.Name;
        _before = Clone(before);
        _after = Clone(after);
    }

    public void Execute() => Save(_after);
    public void Undo() => Save(_before);

    private void Save(TerrainAsset terrain)
        => _assets.SaveTerrain(_projectPath, Clone(terrain), _name);

    private static TerrainAsset Clone(TerrainAsset terrain)
        => terrain with
        {
            Heights = (float[])terrain.Heights.Clone(),
            Layers = (TerrainLayer[])terrain.Layers.Clone()
        };
}
