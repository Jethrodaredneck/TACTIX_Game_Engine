using System.Numerics;

namespace TACTIX.Engine.Runtime.ECS;

public struct NameComponent
{
    public string Name;
    public NameComponent(string name) => Name = name;
}

public struct TransformComponent
{
    public Vector3 Position;
    public Vector3 Rotation;
    public Vector3 Scale;

    public static TransformComponent Identity => new()
    {
        Position = Vector3.Zero,
        Rotation = Vector3.Zero,
        Scale = Vector3.One
    };
}

public enum BuiltInMesh { Cube, Plane, Sphere, Cylinder, Capsule, Cone }

public struct MeshRendererComponent
{
    public BuiltInMesh Mesh;
    public string Material;
    public MeshRendererComponent(BuiltInMesh mesh, string material = "TACTIX_DefaultPrimitive")
    { Mesh = mesh; Material = material; }
}
