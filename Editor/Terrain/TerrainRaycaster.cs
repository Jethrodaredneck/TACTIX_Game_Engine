using System.Numerics;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;
using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Editor.Terrain;

public readonly record struct TerrainHit(
    Entity Entity,
    AssetGuid TerrainAssetGuid,
    Vector3 WorldPosition,
    Vector3 LocalPosition,
    float NormalizedX,
    float NormalizedZ,
    float Distance
);

/// <summary>
/// Editor-side CPU terrain picking. It deliberately depends only on the runtime world,
/// terrain assets, and math types so the Metal renderer remains free of editor concerns.
/// Ray parameters are preserved through the inverse S/R/T transform, which lets terrain
/// hits compare directly with other world-space pick distances.
/// </summary>
public static class TerrainRaycaster
{
    private const float Epsilon = 0.00001f;

    public static bool TryPick(
        World world,
        AssetDatabase assets,
        Vector3 origin,
        Vector3 direction,
        out TerrainHit hit,
        Entity? onlyEntity = null)
    {
        hit = default;
        if (direction.LengthSquared() <= Epsilon)
            return false;

        direction = Vector3.Normalize(direction);
        var found = false;
        var bestDistance = float.PositiveInfinity;

        foreach (var (entity, component) in world.Query<TerrainComponent>())
        {
            if (onlyEntity.HasValue && entity != onlyEntity.Value)
                continue;
            if (!world.Has<TransformComponent>(entity))
                continue;

            TerrainAsset terrain;
            try
            {
                terrain = assets.LoadTerrain(component.TerrainAssetGuid);
            }
            catch
            {
                // A stale/broken asset reference should not make viewport picking fail globally.
                continue;
            }

            var transform = world.Get<TransformComponent>(entity);
            if (!TryPickTerrain(terrain, transform, origin, direction, out var localHit, out var distance))
                continue;
            if (distance >= bestDistance)
                continue;

            var halfX = terrain.SizeX * 0.5f;
            var halfZ = terrain.SizeZ * 0.5f;
            var normalizedX = Math.Clamp((localHit.X + halfX) / terrain.SizeX, 0f, 1f);
            var normalizedZ = Math.Clamp((localHit.Z + halfZ) / terrain.SizeZ, 0f, 1f);
            var worldHit = origin + direction * distance;

            hit = new TerrainHit(
                entity,
                component.TerrainAssetGuid,
                worldHit,
                localHit,
                normalizedX,
                normalizedZ,
                distance);
            bestDistance = distance;
            found = true;
        }

        return found;
    }

    private static bool TryPickTerrain(
        TerrainAsset terrain,
        TransformComponent transform,
        Vector3 worldOrigin,
        Vector3 worldDirection,
        out Vector3 localHit,
        out float distance)
    {
        localHit = default;
        distance = 0f;

        if (!TryInverseTransformRay(transform, worldOrigin, worldDirection, out var origin, out var direction))
            return false;

        var minHeight = float.PositiveInfinity;
        var maxHeight = float.NegativeInfinity;
        foreach (var height in terrain.Heights)
        {
            minHeight = MathF.Min(minHeight, height * terrain.HeightScale);
            maxHeight = MathF.Max(maxHeight, height * terrain.HeightScale);
        }

        var halfX = terrain.SizeX * 0.5f;
        var halfZ = terrain.SizeZ * 0.5f;
        var verticalPad = MathF.Max(0.001f, terrain.HeightScale * 0.001f);
        var boundsMin = new Vector3(-halfX, minHeight - verticalPad, -halfZ);
        var boundsMax = new Vector3(halfX, maxHeight + verticalPad, halfZ);
        if (!RayAabb(origin, direction, boundsMin, boundsMax, out var boundsEnter, out var boundsExit))
            return false;

        var resolution = terrain.Resolution;
        if (resolution < 2 || terrain.Heights.Length != resolution * resolution)
            return false;

        var stepX = terrain.SizeX / (resolution - 1);
        var stepZ = terrain.SizeZ / (resolution - 1);
        var best = float.PositiveInfinity;
        var found = false;

        Vector3 Vertex(int x, int z)
            => new(
                x * stepX - halfX,
                terrain.Heights[z * resolution + x] * terrain.HeightScale,
                z * stepZ - halfZ);

        for (var z = 0; z < resolution - 1; z++)
        {
            for (var x = 0; x < resolution - 1; x++)
            {
                var a = Vertex(x, z);
                var b = Vertex(x + 1, z);
                var c = Vertex(x, z + 1);
                var d = Vertex(x + 1, z + 1);

                TestTriangle(a, d, b);
                TestTriangle(a, c, d);
            }
        }

        if (!found)
            return false;

        distance = best;
        localHit = origin + direction * best;
        return true;

        void TestTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            if (!RayTriangle(origin, direction, a, b, c, out var t))
                return;
            if (t < MathF.Max(0f, boundsEnter) - Epsilon || t > boundsExit + Epsilon || t >= best)
                return;
            best = t;
            found = true;
        }
    }

    private static bool TryInverseTransformRay(
        TransformComponent transform,
        Vector3 worldOrigin,
        Vector3 worldDirection,
        out Vector3 localOrigin,
        out Vector3 localDirection)
    {
        var sx = transform.Scale.X;
        var sy = transform.Scale.Y;
        var sz = transform.Scale.Z;
        if (MathF.Abs(sx) <= Epsilon || MathF.Abs(sy) <= Epsilon || MathF.Abs(sz) <= Epsilon)
        {
            localOrigin = default;
            localDirection = default;
            return false;
        }

        var translated = worldOrigin - transform.Position;
        translated = InverseRotateEuler(translated, transform.Rotation);
        var direction = InverseRotateEuler(worldDirection, transform.Rotation);

        localOrigin = new Vector3(translated.X / sx, translated.Y / sy, translated.Z / sz);
        localDirection = new Vector3(direction.X / sx, direction.Y / sy, direction.Z / sz);
        return localDirection.LengthSquared() > Epsilon;
    }

    private static Vector3 InverseRotateEuler(Vector3 value, Vector3 degrees)
    {
        var d2r = MathF.PI / 180f;
        value = RotateZ(value, -degrees.Z * d2r);
        value = RotateY(value, -degrees.Y * d2r);
        value = RotateX(value, -degrees.X * d2r);
        return value;
    }

    private static Vector3 RotateX(Vector3 v, float radians)
    {
        var s = MathF.Sin(radians);
        var c = MathF.Cos(radians);
        return new Vector3(v.X, c * v.Y - s * v.Z, s * v.Y + c * v.Z);
    }

    private static Vector3 RotateY(Vector3 v, float radians)
    {
        var s = MathF.Sin(radians);
        var c = MathF.Cos(radians);
        return new Vector3(c * v.X + s * v.Z, v.Y, -s * v.X + c * v.Z);
    }

    private static Vector3 RotateZ(Vector3 v, float radians)
    {
        var s = MathF.Sin(radians);
        var c = MathF.Cos(radians);
        return new Vector3(c * v.X - s * v.Y, s * v.X + c * v.Y, v.Z);
    }

    private static bool RayAabb(
        Vector3 origin,
        Vector3 direction,
        Vector3 min,
        Vector3 max,
        out float enter,
        out float exit)
    {
        enter = 0f;
        exit = float.PositiveInfinity;
        return Slab(origin.X, direction.X, min.X, max.X, ref enter, ref exit) &&
               Slab(origin.Y, direction.Y, min.Y, max.Y, ref enter, ref exit) &&
               Slab(origin.Z, direction.Z, min.Z, max.Z, ref enter, ref exit) &&
               exit >= MathF.Max(enter, 0f);
    }

    private static bool Slab(float origin, float direction, float min, float max, ref float enter, ref float exit)
    {
        if (MathF.Abs(direction) <= Epsilon)
            return origin >= min && origin <= max;

        var inv = 1f / direction;
        var t0 = (min - origin) * inv;
        var t1 = (max - origin) * inv;
        if (t0 > t1) (t0, t1) = (t1, t0);
        enter = MathF.Max(enter, t0);
        exit = MathF.Min(exit, t1);
        return enter <= exit;
    }

    private static bool RayTriangle(
        Vector3 origin,
        Vector3 direction,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out float distance)
    {
        distance = 0f;
        var edge1 = b - a;
        var edge2 = c - a;
        var p = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, p);
        if (MathF.Abs(determinant) <= Epsilon)
            return false;

        var inverse = 1f / determinant;
        var t = origin - a;
        var u = Vector3.Dot(t, p) * inverse;
        if (u < 0f || u > 1f)
            return false;

        var q = Vector3.Cross(t, edge1);
        var v = Vector3.Dot(direction, q) * inverse;
        if (v < 0f || u + v > 1f)
            return false;

        var rayDistance = Vector3.Dot(edge2, q) * inverse;
        if (rayDistance <= Epsilon)
            return false;

        distance = rayDistance;
        return true;
    }
}
