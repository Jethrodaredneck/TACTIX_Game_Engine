using System.Numerics;

namespace TACTIX.Engine.Runtime.ECS;

public enum LightType
{
    Directional = 0,
    Point = 1,
    Spot = 2
}

/// <summary>
/// Backend-neutral runtime lighting data. Position/orientation come from TransformComponent;
/// the editor owns gizmos and Inspector UI, while render backends consume this component.
/// </summary>
public struct LightComponent
{
    public LightType Type;
    public Vector3 Color;
    public float Intensity;
    public float Range;
    public float InnerConeDegrees;
    public float OuterConeDegrees;
    public bool CastShadows;
    public bool Enabled;

    public LightComponent(LightType type)
    {
        Type = type;
        Color = Vector3.One;
        Intensity = type == LightType.Directional ? 1.25f : 5f;
        Range = 12f;
        InnerConeDegrees = 20f;
        OuterConeDegrees = 35f;
        CastShadows = false;
        Enabled = true;
    }
}
