using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Editor.Terrain;

public enum TerrainBrushMode
{
    RaiseLower,
    Smooth,
    Flatten
}

/// <summary>
/// Pure terrain-height editing operations. The editor can preview these values and
/// commit the before/after TerrainAsset through the command stack as one transaction.
/// Keeping brush math free of AppKit/Metal makes it testable and reusable.
/// </summary>
public static class TerrainBrush
{
    public static TerrainAsset Apply(
        TerrainAsset source,
        TerrainBrushMode mode,
        float normalizedX,
        float normalizedZ,
        float radiusNormalized,
        float strength,
        float flattenHeight = 0f)
    {
        Validate(source);
        radiusNormalized = Math.Clamp(radiusNormalized, 0.0001f, 1f);
        strength = Math.Clamp(strength, -1f, 1f);

        var heights = (float[])source.Heights.Clone();
        var resolution = source.Resolution;
        var cx = normalizedX * (resolution - 1);
        var cz = normalizedZ * (resolution - 1);
        var radius = Math.Max(1f, radiusNormalized * (resolution - 1));
        var minX = Math.Max(0, (int)MathF.Floor(cx - radius));
        var maxX = Math.Min(resolution - 1, (int)MathF.Ceiling(cx + radius));
        var minZ = Math.Max(0, (int)MathF.Floor(cz - radius));
        var maxZ = Math.Min(resolution - 1, (int)MathF.Ceiling(cz + radius));

        for (var z = minZ; z <= maxZ; z++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var dx = x - cx;
                var dz = z - cz;
                var distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance > radius) continue;

                var falloff = SmoothFalloff(1f - distance / radius);
                var index = z * resolution + x;
                var current = source.Heights[index];
                var blend = MathF.Abs(strength) * falloff;

                heights[index] = mode switch
                {
                    TerrainBrushMode.RaiseLower => Math.Clamp(current + strength * falloff * 0.035f, -1f, 1f),
                    TerrainBrushMode.Smooth => Lerp(current, NeighborhoodAverage(source, x, z), blend),
                    TerrainBrushMode.Flatten => Lerp(current, flattenHeight, blend),
                    _ => current
                };
            }
        }

        return source with { Heights = heights };
    }

    private static float NeighborhoodAverage(TerrainAsset terrain, int x, int z)
    {
        var sum = 0f;
        var count = 0;
        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var sx = Math.Clamp(x + dx, 0, terrain.Resolution - 1);
                var sz = Math.Clamp(z + dz, 0, terrain.Resolution - 1);
                sum += terrain.Heights[sz * terrain.Resolution + sx];
                count++;
            }
        }
        return sum / count;
    }

    private static float SmoothFalloff(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    private static void Validate(TerrainAsset terrain)
    {
        if (terrain.Resolution < 2 || terrain.Heights.Length != terrain.Resolution * terrain.Resolution)
            throw new InvalidDataException("Invalid terrain height data.");
    }
}
