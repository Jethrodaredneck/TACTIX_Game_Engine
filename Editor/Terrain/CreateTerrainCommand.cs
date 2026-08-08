using TACTIX.Editor.Scene;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;
using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Editor.Terrain;

/// <summary>
/// Creates both the project-owned TerrainAsset and the scene entity that references it.
/// Undo removes the entity and the asset file; redo recreates both with the same identities.
/// </summary>
public sealed class CreateTerrainCommand : IEditorCommand
{
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly string _projectRoot;
    private readonly string _name;

    private int _entityId;
    private string _assetProjectPath = "";
    private TerrainAsset _terrain;

    public CreateTerrainCommand(World world, EditorSelection selection, string projectRoot, string name = "Terrain")
    {
        _world = world;
        _selection = selection;
        _projectRoot = projectRoot;
        _name = name;
    }

    public void Execute()
    {
        var entity = _entityId == 0 ? _world.CreateEntity() : _world.CreateEntityWithId(_entityId);
        _entityId = entity.Id;

        if (_terrain.Guid.Value == Guid.Empty)
            _terrain = TerrainAsset.CreateFlat();

        if (string.IsNullOrEmpty(_assetProjectPath))
            _assetProjectPath = $"Assets/Terrain/{_name}_{_entityId}.tasset";

        var database = new AssetDatabase(_projectRoot);
        database.Initialize();
        var meta = database.SaveTerrain(_assetProjectPath, _terrain, _name);
        _terrain = _terrain with { Guid = meta.Guid };

        _world.Add(entity, new NameComponent(_name));
        _world.Add(entity, TransformComponent.Identity);
        _world.Add(entity, new TerrainComponent(meta.Guid));
        _selection.Select(entity);
    }

    public void Undo()
    {
        var entity = new Entity(_entityId);
        _world.DestroyEntity(entity);
        if (_selection.ActiveEntity == entity)
            _selection.Select(null);

        if (!string.IsNullOrEmpty(_assetProjectPath))
        {
            var database = new AssetDatabase(_projectRoot);
            var fullPath = database.ResolveProjectPath(_assetProjectPath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
    }
}
