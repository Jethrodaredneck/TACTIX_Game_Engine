using TACTIX.Editor.Scene;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;
using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Editor.Terrain;

/// <summary>
/// Creates both the project-owned TerrainAsset and the scene entity that references it.
/// Undo removes the entity and the asset file; redo recreates both with the same identities.
/// The renderer consumes TerrainComponent directly and generates the heightfield mesh from
/// TerrainAsset, so terrain no longer carries a temporary plane MeshRendererComponent.
/// </summary>
public sealed class CreateTerrainCommand : IEditorCommand
{
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly AssetDatabase _assets;
    private readonly string _name;

    private int _entityId;
    private string _assetProjectPath = "";
    private TerrainAsset _terrain;

    public CreateTerrainCommand(World world, EditorSelection selection, AssetDatabase assets, string name = "Terrain")
    {
        _world = world;
        _selection = selection;
        _assets = assets;
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

        var meta = _assets.SaveTerrain(_assetProjectPath, _terrain, _name);
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
            var fullPath = _assets.ResolveProjectPath(_assetProjectPath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            _assets.Registry.Remove(_terrain.Guid);
        }
    }
}