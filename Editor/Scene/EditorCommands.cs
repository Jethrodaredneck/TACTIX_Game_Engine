using System.Numerics;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Editor.Scene;

public interface IEditorCommand { void Execute(); void Undo(); }

public sealed class EditorCommandStack
{
    private readonly Stack<IEditorCommand> _undo = new();
    private readonly Stack<IEditorCommand> _redo = new();
    public event Action? Changed;

    public void Execute(IEditorCommand command)
    {
        command.Execute();
        _undo.Push(command);
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var command = _undo.Pop();
        command.Undo();
        _redo.Push(command);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var command = _redo.Pop();
        command.Execute();
        _undo.Push(command);
        Changed?.Invoke();
    }
}

public sealed class SetTransformCommand : IEditorCommand
{
    private readonly World _world;
    private readonly Entity _entity;
    private readonly TransformComponent _before;
    private readonly TransformComponent _after;

    public SetTransformCommand(World world, Entity entity, TransformComponent before, TransformComponent after)
    {
        _world = world; _entity = entity; _before = before; _after = after;
    }

    public void Execute() => _world.Set(_entity, _after);
    public void Undo() => _world.Set(_entity, _before);
}

public sealed class SetLightCommand : IEditorCommand
{
    private readonly World _world;
    private readonly Entity _entity;
    private readonly LightComponent _before;
    private readonly LightComponent _after;

    public SetLightCommand(World world, Entity entity, LightComponent before, LightComponent after)
    {
        _world = world; _entity = entity; _before = before; _after = after;
    }

    public void Execute() => _world.Set(_entity, _after);
    public void Undo() => _world.Set(_entity, _before);
}

public sealed class CreatePrimitiveCommand : IEditorCommand
{
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly BuiltInMesh _mesh;
    private readonly string _name;
    private int _entityId;

    public CreatePrimitiveCommand(World world, EditorSelection selection, BuiltInMesh mesh, string name)
    {
        _world = world; _selection = selection; _mesh = mesh; _name = name;
    }

    public void Execute()
    {
        var entity = _entityId == 0 ? _world.CreateEntity() : _world.CreateEntityWithId(_entityId);
        _entityId = entity.Id;
        _world.Add(entity, new NameComponent(_name));
        var transform = TransformComponent.Identity;
        transform.Position = new Vector3((_entityId - 1) * 0.35f, 0, 0);
        _world.Add(entity, transform);
        _world.Add(entity, new MeshRendererComponent(_mesh));
        _selection.Select(entity);
    }

    public void Undo()
    {
        var entity = new Entity(_entityId);
        _world.DestroyEntity(entity);
        if (_selection.ActiveEntity == entity) _selection.Select(null);
    }
}

public sealed class CreateAssetMeshCommand : IEditorCommand
{
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly AssetGuid _meshGuid;
    private readonly AssetGuid? _materialGuid;
    private readonly string _name;
    private int _entityId;

    public CreateAssetMeshCommand(World world, EditorSelection selection, AssetGuid meshGuid, string name, AssetGuid? materialGuid = null)
    {
        _world = world; _selection = selection; _meshGuid = meshGuid; _name = name; _materialGuid = materialGuid;
    }

    public void Execute()
    {
        var entity = _entityId == 0 ? _world.CreateEntity() : _world.CreateEntityWithId(_entityId);
        _entityId = entity.Id;
        _world.Add(entity, new NameComponent(string.IsNullOrWhiteSpace(_name) ? "Imported Mesh" : _name));
        _world.Add(entity, TransformComponent.Identity);
        _world.Add(entity, new MeshRendererComponent(_meshGuid, _materialGuid));
        _selection.Select(entity);
    }

    public void Undo()
    {
        var entity = new Entity(_entityId);
        _world.DestroyEntity(entity);
        if (_selection.ActiveEntity == entity) _selection.Select(null);
    }
}

public sealed class CreateLightCommand : IEditorCommand
{
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly LightType _type;
    private int _entityId;

    public CreateLightCommand(World world, EditorSelection selection, LightType type)
    {
        _world = world; _selection = selection; _type = type;
    }

    public void Execute()
    {
        var entity = _entityId == 0 ? _world.CreateEntity() : _world.CreateEntityWithId(_entityId);
        _entityId = entity.Id;
        var name = _type switch
        {
            LightType.Directional => "Directional Light",
            LightType.Point => "Point Light",
            LightType.Spot => "Spot Light",
            _ => "Light"
        };
        _world.Add(entity, new NameComponent(name));
        var transform = TransformComponent.Identity;
        if (_type == LightType.Directional) transform.Rotation = new Vector3(50f, -30f, 0f);
        else transform.Position = new Vector3(0f, 3f, 0f);
        _world.Add(entity, transform);
        _world.Add(entity, new LightComponent(_type));
        _selection.Select(entity);
    }

    public void Undo()
    {
        var entity = new Entity(_entityId);
        _world.DestroyEntity(entity);
        if (_selection.ActiveEntity == entity) _selection.Select(null);
    }
}

public sealed class DeleteEntityCommand : IEditorCommand
{
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly Entity _entity;
    private NameComponent? _name;
    private TransformComponent? _transform;
    private MeshRendererComponent? _mesh;
    private TerrainComponent? _terrain;
    private LightComponent? _light;

    public DeleteEntityCommand(World world, EditorSelection selection, Entity entity)
    {
        _world = world; _selection = selection; _entity = entity;
    }

    public void Execute()
    {
        if (!_world.Exists(_entity)) return;
        if (_world.Has<NameComponent>(_entity)) _name = _world.Get<NameComponent>(_entity);
        if (_world.Has<TransformComponent>(_entity)) _transform = _world.Get<TransformComponent>(_entity);
        if (_world.Has<MeshRendererComponent>(_entity)) _mesh = _world.Get<MeshRendererComponent>(_entity);
        if (_world.Has<TerrainComponent>(_entity)) _terrain = _world.Get<TerrainComponent>(_entity);
        if (_world.Has<LightComponent>(_entity)) _light = _world.Get<LightComponent>(_entity);
        _world.DestroyEntity(_entity);
        if (_selection.ActiveEntity == _entity) _selection.Select(null);
    }

    public void Undo()
    {
        var entity = _world.CreateEntityWithId(_entity.Id);
        if (_name.HasValue) _world.Add(entity, _name.Value);
        if (_transform.HasValue) _world.Add(entity, _transform.Value);
        if (_mesh.HasValue) _world.Add(entity, _mesh.Value);
        if (_terrain.HasValue) _world.Add(entity, _terrain.Value);
        if (_light.HasValue) _world.Add(entity, _light.Value);
        _selection.Select(entity);
    }
}

public sealed class DuplicateEntityCommand : IEditorCommand
{
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly Entity _source;
    private int _duplicateId;

    public DuplicateEntityCommand(World world, EditorSelection selection, Entity source)
    {
        _world = world; _selection = selection; _source = source;
    }

    public void Execute()
    {
        if (!_world.Exists(_source)) return;
        var entity = _duplicateId == 0 ? _world.CreateEntity() : _world.CreateEntityWithId(_duplicateId);
        _duplicateId = entity.Id;
        var name = _world.Has<NameComponent>(_source) ? _world.Get<NameComponent>(_source).Name : $"Entity {_source.Id}";
        _world.Add(entity, new NameComponent(name + " Copy"));

        if (_world.Has<TransformComponent>(_source))
        {
            var transform = _world.Get<TransformComponent>(_source);
            var extent = MathF.Max(MathF.Abs(transform.Scale.X), MathF.Max(MathF.Abs(transform.Scale.Y), MathF.Abs(transform.Scale.Z)));
            var meshRadius = 1f;
            if (_world.Has<MeshRendererComponent>(_source))
            {
                var sourceMesh = _world.Get<MeshRendererComponent>(_source);
                if (!sourceMesh.UsesAssetMesh)
                {
                    meshRadius = sourceMesh.Mesh switch
                    {
                        BuiltInMesh.Cube => 1.75f,
                        BuiltInMesh.Plane => 1.45f,
                        BuiltInMesh.Capsule => 1.9f,
                        _ => 1.35f
                    };
                }
            }
            var separation = MathF.Max(1.5f, meshRadius * MathF.Max(extent, 0.1f) * 2.15f);
            transform.Position += new Vector3(separation, 0, 0);
            _world.Add(entity, transform);
        }

        if (_world.Has<MeshRendererComponent>(_source)) _world.Add(entity, _world.Get<MeshRendererComponent>(_source));
        if (_world.Has<TerrainComponent>(_source)) _world.Add(entity, _world.Get<TerrainComponent>(_source));
        if (_world.Has<LightComponent>(_source)) _world.Add(entity, _world.Get<LightComponent>(_source));
        _selection.Select(entity);
    }

    public void Undo()
    {
        var entity = new Entity(_duplicateId);
        _world.DestroyEntity(entity);
        if (_selection.ActiveEntity == entity) _selection.Select(_source);
    }
}
