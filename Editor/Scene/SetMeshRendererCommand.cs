using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Editor.Scene;

public sealed class SetMeshRendererCommand : IEditorCommand
{
    private readonly World _world;
    private readonly Entity _entity;
    private readonly MeshRendererComponent _before;
    private readonly MeshRendererComponent _after;

    public SetMeshRendererCommand(World world, Entity entity, MeshRendererComponent before, MeshRendererComponent after)
    {
        _world = world;
        _entity = entity;
        _before = before;
        _after = after;
    }

    public void Execute() => _world.Set(_entity, _after);
    public void Undo() => _world.Set(_entity, _before);
}
