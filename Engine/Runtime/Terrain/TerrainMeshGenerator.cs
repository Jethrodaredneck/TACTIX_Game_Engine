using System.Numerics;
using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Engine.Runtime.Terrain;

public readonly record struct TerrainMeshData(
    float[] Positions,
    float[] Normals,
    float[] UV0,
    uint[] Indices
);

/// <summary>
/// CPU terrain mesh extraction. This deliberately produces backend-neutral data so
/// Metal owns GPU buffers rather than terrain/runtime code owning Metal objects.
/// The same seam can later feed chunking, LOD, collision, and navmesh generation.
/// </summary>
public static class TerrainMeshGenerator
{
    public static TerrainMeshData Generate(TerrainAsset terrain)
    {
        Validate(terrain);

        var resolution = terrain.Resolution;
        var vertexCount = resolution * resolution;
        var positions = new float[vertexCount * 3];
        var normals = new float[vertexCount * 3];
        var uv0 = new float[vertexCount * 2];
        var indices = new uint[(resolution - 1) * (resolution - 1) * 6];

        var stepX = terrain.SizeX / (resolution - 1);
        var stepZ = terrain.SizeZ / (resolution - 1);
        var halfX = terrain.SizeX * 0.5f;
        var halfZ = terrain.SizeZ * 0.5f;

        for (var z = 0; z < resolution; z++)
        {
            for (var x = 0; x < resolution; x++)
            {
                var vertex = z * resolution + x;
                var p = vertex * 3;
                var uv = vertex * 2;

                positions[p] = x * stepX - halfX;
                positions[p + 1] = terrain.Heights[vertex] * terrain.HeightScale;
                positions[p + 2] = z * stepZ - halfZ;

                uv0[uv] = (float)x / (resolution - 1);
                uv0[uv + 1] = (float)z / (resolution - 1);

                var left = SampleHeight(terrain, x - 1, z) * terrain.HeightScale;
                var right = SampleHeight(terrain, x + 1, z) * terrain.HeightScale;
                var down = SampleHeight(terrain, x, z - 1) * terrain.HeightScale;
                var up = SampleHeight(terrain, x, z + 1) * terrain.HeightScale;
                var normal = Vector3.Normalize(new Vector3(left - right, 2f * MathF.Max(stepX, stepZ), down - up));

                normals[p] = normal.X;
                normals[p + 1] = normal.Y;
                normals[p + 2] = normal.Z;
            }
        }

        var index = 0;
        for (var z = 0; z < resolution - 1; z++)
        {
            for (var x = 0; x < resolution - 1; x++)
            {
                var a = (uint)(z * resolution + x);
                var b = a + 1;
                var c = a + (uint)resolution;
                var d = c + 1;

                indices[index++] = a;
                indices[index++] = d;
                indices[index++] = b;
                indices[index++] = a;
                indices[index++] = c;
                indices[index++] = d;
            }
        }

        return new TerrainMeshData(positions, normals, uv0, indices);
    }

    private static float SampleHeight(TerrainAsset terrain, int x, int z)
    {
        x = Math.Clamp(x, 0, terrain.Resolution - 1);
        z = Math.Clamp(z, 0, terrain.Resolution - 1);
        return terrain.Heights[z * terrain.Resolution + x];
    }

    private static void Validate(TerrainAsset terrain)
    {
        if (terrain.Resolution < 2)
            throw new InvalidDataException("Terrain resolution must be at least 2.");
        if (terrain.Heights.Length != terrain.Resolution * terrain.Resolution)
            throw new InvalidDataException("Terrain height count does not match resolution.");
        if (terrain.SizeX <= 0f || terrain.SizeZ <= 0f || terrain.HeightScale <= 0f)
            throw new InvalidDataException("Terrain physical dimensions must be positive.");
    }
}
