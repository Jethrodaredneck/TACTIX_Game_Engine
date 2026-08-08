using TACTIX.Engine.Core.Input;

namespace TACTIX.Engine.Runtime.Scene;

public sealed class SceneManager
{
    public Scene? ActiveScene { get; private set; }

    public void Load(Scene scene)
    {
        ActiveScene = scene;
    }

    public void FixedUpdate(double fixedDeltaSeconds, InputState input)
    {
        _ = fixedDeltaSeconds;
        _ = input;
    }

    public void Update(double deltaSeconds, InputState input)
    {
        _ = deltaSeconds;
        _ = input;
    }
}
