namespace TACTIX.Engine.Runtime.ECS;

public readonly record struct Entity(int Id)
{
    public override string ToString() => $"Entity({Id})";
}
