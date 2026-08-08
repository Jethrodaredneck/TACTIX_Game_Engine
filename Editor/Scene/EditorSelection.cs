using TACTIX.Engine.Runtime.ECS;
namespace TACTIX.Editor.Scene;
public sealed class EditorSelection
{
    public Entity? ActiveEntity { get; private set; }
    public event Action? Changed;
    public void Select(Entity? entity) { ActiveEntity = entity; Changed?.Invoke(); }
}
