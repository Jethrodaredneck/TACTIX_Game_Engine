using System.Numerics;
using TACTIX.Editor.Scene;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;
using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Editor.Terrain;

/// <summary>
/// Terrain duplication must deep-copy the backing terrain asset. Reusing the same
/// TerrainAsset GUID would make sculpting either duplicate silently edit both entities.
/// </summary>
public sealed class DuplicateTerrainCommand : IEditorCommand
{
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly AssetDatabase _assets;
    private readonly Entity _source;

    private int _duplicateId;
    private AssetGuid _duplicateTerrainGuid;
    private string _duplicateName = "Terrain Copy";
    private string _assetProjectPath = "";
    private TransformComponent _duplicateTransform;
    private TerrainAsset _terrainSnapshot;
    private bool _initialized;

    public DuplicateTerrainCommand(World world, EditorSelection selection, AssetDatabase assets, Entity source)
    {
        _world = world;
        _selection = selection;
        _assets = assets;
        _source = source;
    }

    public void Execute()
    {
        if (!_initialized && (!_world.Exists(_source) || !_world.Has<TerrainComponent>(_source)))
            return;

        var entity = _duplicateId == 0 ? _world.CreateEntity() : _world.CreateEntityWithId(_duplicateId);
        _duplicateId = entity.Id;

        try
        {
            if (!_initialized)
                InitializeFromSource();

            _assets.SaveTerrain(_assetProjectPath, Clone(_terrainSnapshot), _duplicateName);
            _world.Add(entity, new NameComponent(_duplicateName));
            _world.Add(entity, _duplicateTransform);
            _world.Add(entity, new TerrainComponent(_duplicateTerrainGuid));
            _selection.Select(entity);
        }
        catch
        {
            if (_world.Exists(entity))
                _world.DestroyEntity(entity);
            throw;
        }
    }

    public void Undo()
    {
        var entity = new Entity(_duplicateId);
        if (_world.Exists(entity))
            _world.DestroyEntity(entity);

        if (!string.IsNullOrWhiteSpace(_assetProjectPath))
        {
            var fullPath = _assets.ResolveProjectPath(_assetProjectPath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        if (_duplicateTerrainGuid.Value != Guid.Empty)
            _assets.Registry.Remove(_duplicateTerrainGuid);

        if (_selection.ActiveEntity == entity)
            _selection.Select(_world.Exists(_source) ? _source : null);
    }

    private void InitializeFromSource()
    {
        var sourceTerrainComponent = _world.Get<TerrainComponent>(_source);
        var sourceTerrain = _assets.LoadTerrain(sourceTerrainComponent.TerrainAssetGuid);
        var sourceName = _world.Has<NameComponent>(_source)
            ? _world.Get<NameComponent>(_source).Name
            : "Terrain";

        _duplicateName = string.IsNullOrWhiteSpace(sourceName) ? "Terrain Copy" : sourceName + " Copy";
        _duplicateTerrainGuid = AssetGuid.New();
        _terrainSnapshot = Clone(sourceTerrain) with { Guid = _duplicateTerrainGuid };
        _assetProjectPath = $"Assets/Terrain/{SafeFileName(_duplicateName)}_{_duplicateTerrainGuid}.tasset";

        _duplicateTransform = _world.Has<TransformComponent>(_source)
            ? _world.Get<TransformComponent>(_source)
            : TransformComponent.Identity;

        var horizontalScale = MathF.Max(
            MathF.Max(MathF.Abs(_duplicateTransform.Scale.X), MathF.Abs(_duplicateTransform.Scale.Z)),
            0.1f);
        var terrainSpan = MathF.Max(sourceTerrain.SizeX, sourceTerrain.SizeZ);
        var separation = MathF.Max(2f, terrainSpan * horizontalScale + 2f);
        _duplicateTransform.Position += new Vector3(separation, 0f, 0f);
        _initialized = true;
    }

    private static TerrainAsset Clone(TerrainAsset terrain)
        => terrain with
        {
            Heights = (float[])terrain.Heights.Clone(),
            Layers = (TerrainLayer[])terrain.Layers.Clone()
        };

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        var safe = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "Terrain_Copy" : safe;
    }
}
