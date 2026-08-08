using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Engine.Runtime.Scene;

public sealed class Scene
{
    public string Name { get; }
    public World World { get; } = new();

    public Scene(string name)
    {
        Name = name;
    }
}
