using System.Numerics;
using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Editor.Scene;

public interface IEditorCommand { void Execute(); void Undo(); }

public sealed class EditorCommandStack
{
    private readonly Stack<IEditorCommand> _undo = new();
    private readonly Stack<IEditorCommand> _redo = new();
    public event Action? Changed;

    public void Execute(IEditorCommand c){ c.Execute(); _undo.Push(c); _redo.Clear(); Changed?.Invoke(); }
    public void Undo(){ if(_undo.Count==0)return; var c=_undo.Pop(); c.Undo(); _redo.Push(c); Changed?.Invoke(); }
    public void Redo(){ if(_redo.Count==0)return; var c=_redo.Pop(); c.Execute(); _undo.Push(c); Changed?.Invoke(); }
}

public sealed class SetTransformCommand : IEditorCommand
{
    private readonly World _world; private readonly Entity _entity; private readonly TransformComponent _before,_after;
    public SetTransformCommand(World w, Entity e, TransformComponent before, TransformComponent after){_world=w;_entity=e;_before=before;_after=after;}
    public void Execute()=>_world.Set(_entity,_after);
    public void Undo()=>_world.Set(_entity,_before);
}

public sealed class CreatePrimitiveCommand : IEditorCommand
{
    private readonly World _world; private readonly EditorSelection _selection; private readonly BuiltInMesh _mesh; private readonly string _name;
    private int _entityId;

    public CreatePrimitiveCommand(World world, EditorSelection selection, BuiltInMesh mesh, string name)
    { _world=world; _selection=selection; _mesh=mesh; _name=name; }

    public void Execute()
    {
        var e = _entityId == 0 ? _world.CreateEntity() : _world.CreateEntityWithId(_entityId);
        _entityId = e.Id;
        _world.Add(e, new NameComponent(_name));
        var t = TransformComponent.Identity;
        t.Position = new Vector3((_entityId - 1) * 0.35f, 0, 0);
        _world.Add(e, t);
        _world.Add(e, new MeshRendererComponent(_mesh));
        _selection.Select(e);
    }

    public void Undo()
    {
        var e = new Entity(_entityId);
        _world.DestroyEntity(e);
        if (_selection.ActiveEntity == e) _selection.Select(null);
    }
}

public sealed class DeleteEntityCommand : IEditorCommand
{
    private readonly World _world; private readonly EditorSelection _selection; private readonly Entity _entity;
    private NameComponent? _name; private TransformComponent? _transform; private MeshRendererComponent? _mesh;

    public DeleteEntityCommand(World world, EditorSelection selection, Entity entity)
    { _world=world; _selection=selection; _entity=entity; }

    public void Execute()
    {
        if (!_world.Exists(_entity)) return;
        if (_world.Has<NameComponent>(_entity)) _name=_world.Get<NameComponent>(_entity);
        if (_world.Has<TransformComponent>(_entity)) _transform=_world.Get<TransformComponent>(_entity);
        if (_world.Has<MeshRendererComponent>(_entity)) _mesh=_world.Get<MeshRendererComponent>(_entity);
        _world.DestroyEntity(_entity);
        if (_selection.ActiveEntity == _entity) _selection.Select(null);
    }

    public void Undo()
    {
        var e=_world.CreateEntityWithId(_entity.Id);
        if(_name.HasValue)_world.Add(e,_name.Value);
        if(_transform.HasValue)_world.Add(e,_transform.Value);
        if(_mesh.HasValue)_world.Add(e,_mesh.Value);
        _selection.Select(e);
    }
}

public sealed class DuplicateEntityCommand : IEditorCommand
{
    private readonly World _world; private readonly EditorSelection _selection; private readonly Entity _source;
    private int _duplicateId;

    public DuplicateEntityCommand(World world, EditorSelection selection, Entity source)
    { _world=world; _selection=selection; _source=source; }

    public void Execute()
    {
        if(!_world.Exists(_source)) return;
        var e=_duplicateId==0?_world.CreateEntity():_world.CreateEntityWithId(_duplicateId);
        _duplicateId=e.Id;
        var name=_world.Has<NameComponent>(_source)?_world.Get<NameComponent>(_source).Name:$"Entity {_source.Id}";
        _world.Add(e,new NameComponent(name+" Copy"));
        if(_world.Has<TransformComponent>(_source))
        {
            var t=_world.Get<TransformComponent>(_source); t.Position += new Vector3(0.35f,0.2f,0); _world.Add(e,t);
        }
        if(_world.Has<MeshRendererComponent>(_source))_world.Add(e,_world.Get<MeshRendererComponent>(_source));
        _selection.Select(e);
    }

    public void Undo()
    {
        var e=new Entity(_duplicateId); _world.DestroyEntity(e);
        if(_selection.ActiveEntity==e)_selection.Select(_source);
    }
}
