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
        ValidateFinite(normalizedX, nameof(normalizedX));
        ValidateFinite(normalizedZ, nameof(normalizedZ));
        ValidateFinite(radiusNormalized, nameof(radiusNormalized));
        ValidateFinite(strength, nameof(strength));
        ValidateFinite(flattenHeight, nameof(flattenHeight));

        normalizedX = Math.Clamp(normalizedX, 0f, 1f);
        normalizedZ = Math.Clamp(normalizedZ, 0f, 1f);
        radiusNormalized = Math.Clamp(radiusNormalized, 0.0001f, 1f);
        strength = Math.Clamp(strength, -1f, 1f);
        flattenHeight = Math.Clamp(flattenHeight, -1f, 1f);

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

    /// <summary>
    /// Bilinear height sampling in normalized terrain coordinates. This is used by
    /// flatten strokes so the first contact point becomes a stable target height.
    /// </summary>
    public static float SampleHeightNormalized(TerrainAsset terrain, float normalizedX, float normalizedZ)
    {
        Validate(terrain);
        ValidateFinite(normalizedX, nameof(normalizedX));
        ValidateFinite(normalizedZ, nameof(normalizedZ));

        normalizedX = Math.Clamp(normalizedX, 0f, 1f);
        normalizedZ = Math.Clamp(normalizedZ, 0f, 1f);

        var x = normalizedX * (terrain.Resolution - 1);
        var z = normalizedZ * (terrain.Resolution - 1);
        var x0 = Math.Clamp((int)MathF.Floor(x), 0, terrain.Resolution - 1);
        var z0 = Math.Clamp((int)MathF.Floor(z), 0, terrain.Resolution - 1);
        var x1 = Math.Min(x0 + 1, terrain.Resolution - 1);
        var z1 = Math.Min(z0 + 1, terrain.Resolution - 1);
        var tx = x - x0;
        var tz = z - z0;

        var h00 = terrain.Heights[z0 * terrain.Resolution + x0];
        var h10 = terrain.Heights[z0 * terrain.Resolution + x1];
        var h01 = terrain.Heights[z1 * terrain.Resolution + x0];
        var h11 = terrain.Heights[z1 * terrain.Resolution + x1];
        var hx0 = Lerp(h00, h10, tx);
        var hx1 = Lerp(h01, h11, tx);
        return Lerp(hx0, hx1, tz);
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
        if (terrain.SizeX <= 0f || terrain.SizeZ <= 0f || terrain.HeightScale <= 0f)
            throw new InvalidDataException("Terrain physical dimensions must be positive.");
    }

    private static void ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(name, "Terrain brush values must be finite.");
    }
}
