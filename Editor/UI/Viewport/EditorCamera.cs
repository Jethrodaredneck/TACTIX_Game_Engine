using System.Numerics;

namespace TACTIX.Editor.UI.Viewport;

/// <summary>
/// Editor-only orbit camera. Runtime scenes do not depend on this type.
/// Coordinates match the Metal renderer convention: forward points from the
/// camera toward the orbit target and projected depth is positive along forward.
/// </summary>
public sealed class EditorCamera
{
    private const float MinDistance = 0.35f;
    private const float MaxDistance = 250f;
    private const float MinPitch = -89f;
    private const float MaxPitch = 89f;

    public Vector3 Target { get; private set; } = Vector3.Zero;
    public float YawDegrees { get; private set; } = 0f;
    public float PitchDegrees { get; private set; } = 8f;
    public float Distance { get; private set; } = 7f;
    public float ProjectionScale { get; set; } = 1.7f;

    public Vector3 Forward
    {
        get
        {
            var yaw = DegreesToRadians(YawDegrees);
            var pitch = DegreesToRadians(PitchDegrees);
            return Vector3.Normalize(new Vector3(
                MathF.Sin(yaw) * MathF.Cos(pitch),
                MathF.Sin(pitch),
                MathF.Cos(yaw) * MathF.Cos(pitch)));
        }
    }

    public Vector3 Right
    {
        get
        {
            var right = Vector3.Cross(Vector3.UnitY, Forward);
            return right.LengthSquared() < 0.0001f ? Vector3.UnitX : Vector3.Normalize(right);
        }
    }

    public Vector3 Up => Vector3.Normalize(Vector3.Cross(Forward, Right));
    public Vector3 Position => Target - Forward * Distance;

    public void Orbit(float deltaX, float deltaY)
    {
        YawDegrees += deltaX * 0.28f;
        PitchDegrees = Math.Clamp(PitchDegrees + deltaY * 0.28f, MinPitch, MaxPitch);
    }

    public void Pan(float deltaX, float deltaY)
    {
        var unitsPerPixel = MathF.Max(0.001f, Distance * 0.0022f);
        Target += (-Right * deltaX + Up * deltaY) * unitsPerPixel;
    }

    public void Dolly(float delta)
    {
        var factor = MathF.Exp(delta * 0.08f);
        Distance = Math.Clamp(Distance * factor, MinDistance, MaxDistance);
    }

    public void Frame(Vector3 target, float radius = 1f)
    {
        Target = target;
        Distance = Math.Clamp(MathF.Max(radius * 3.25f, 2f), MinDistance, MaxDistance);
    }

    public (Vector3 Origin, Vector3 Direction) ScreenPointToRay(float x, float y, float width, float height)
    {
        width = MathF.Max(width, 1f);
        height = MathF.Max(height, 1f);
        var aspect = width / height;
        var ndcX = (x / width) * 2f - 1f;
        var ndcY = (y / height) * 2f - 1f;
        var dir = Vector3.Normalize(
            Forward +
            Right * (ndcX * aspect / ProjectionScale) +
            Up * (ndcY / ProjectionScale));
        return (Position, dir);
    }

    /// <summary>
    /// Projects a world-space point using the exact perspective convention used
    /// by Triangle.metal. Returns false when the point is behind the editor camera.
    /// </summary>
    public bool TryProject(Vector3 world, float width, float height, out Vector2 screen, out float depth)
    {
        width = MathF.Max(width, 1f);
        height = MathF.Max(height, 1f);
        var rel = world - Position;
        var viewX = Vector3.Dot(rel, Right);
        var viewY = Vector3.Dot(rel, Up);
        depth = Vector3.Dot(rel, Forward);
        if (depth <= 0.001f)
        {
            screen = default;
            return false;
        }

        var aspect = width / height;
        var ndcX = viewX * ProjectionScale / (aspect * depth);
        var ndcY = viewY * ProjectionScale / depth;
        screen = new Vector2((ndcX * 0.5f + 0.5f) * width, (ndcY * 0.5f + 0.5f) * height);
        return true;
    }

    public float WorldUnitsPerPixel(Vector3 world, float height)
    {
        height = MathF.Max(height, 1f);
        var depth = Vector3.Dot(world - Position, Forward);
        return MathF.Max(0.00001f, (2f * MathF.Max(depth, 0.01f)) / (ProjectionScale * height));
    }

    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);
}
