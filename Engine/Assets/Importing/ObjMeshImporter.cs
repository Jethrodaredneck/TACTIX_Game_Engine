using System.Globalization;
using System.Numerics;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Engine.Assets.Importing;

/// <summary>
/// Small dependency-free OBJ importer used to prove the native mesh pipeline end to end.
/// Blender remains responsible for source authoring/coordinate conversion; this importer
/// turns the exported OBJ triangles into a GUID-backed TACTIX MeshAsset.
/// </summary>
public sealed class ObjMeshImporter : IAssetImporter
{
    public string Id => "tactix.obj.native";
    public int Version => 1;
    public AssetImportCapability Capability => AssetImportCapability.Native;
    public IReadOnlyCollection<string> Extensions { get; } = [".obj"];

    public AssetImportResult Import(AssetDatabase database, AssetImportRequest request)
    {
        if (!File.Exists(request.SourcePath))
            return new(false, Id, Array.Empty<ImportedAsset>(), $"OBJ source not found: {request.SourcePath}");

        try
        {
            var sourcePositions = new List<Vector3>();
            var sourceNormals = new List<Vector3>();
            var sourceUvs = new List<Vector2>();
            var positions = new List<float>();
            var normals = new List<float>();
            var uvs = new List<float>();
            var indices = new List<uint>();

            foreach (var raw in File.ReadLines(request.SourcePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                switch (parts[0])
                {
                    case "v" when parts.Length >= 4:
                        sourcePositions.Add(new Vector3(F(parts[1]), F(parts[2]), F(parts[3])));
                        break;
                    case "vn" when parts.Length >= 4:
                        sourceNormals.Add(Vector3.Normalize(new Vector3(F(parts[1]), F(parts[2]), F(parts[3]))));
                        break;
                    case "vt" when parts.Length >= 3:
                        sourceUvs.Add(new Vector2(F(parts[1]), 1f - F(parts[2])));
                        break;
                    case "f" when parts.Length >= 4:
                    {
                        var face = parts.Skip(1).Select(ParseVertex).ToArray();
                        for (var i = 1; i < face.Length - 1; i++)
                            EmitTriangle(face[0], face[i], face[i + 1], sourcePositions, sourceNormals, sourceUvs, positions, normals, uvs, indices);
                        break;
                    }
                }
            }

            if (indices.Count == 0)
                return new(false, Id, Array.Empty<ImportedAsset>(), "OBJ contains no renderable faces.");

            var mesh = new MeshAsset(AssetGuid.New(), positions.ToArray(), normals.ToArray(), uvs.ToArray(), indices.ToArray());
            var name = SafeName(Path.GetFileNameWithoutExtension(request.SourcePath));
            var destination = NormalizeDestination(request.DestinationDirectory);
            var projectPath = $"{destination}/{name}.tasset";
            var meta = database.SaveMesh(projectPath, mesh, name);
            return new(true, Id, [new ImportedAsset(meta.Guid, meta.Type, meta.ProjectPath, meta.Name)],
                $"Imported OBJ mesh ({indices.Count / 3} triangles) -> {meta.ProjectPath}");
        }
        catch (Exception ex)
        {
            return new(false, Id, Array.Empty<ImportedAsset>(), $"OBJ import failed: {ex.Message}");
        }
    }

    private readonly record struct ObjVertex(int Position, int TexCoord, int Normal);

    private static ObjVertex ParseVertex(string token)
    {
        var p = token.Split('/');
        return new ObjVertex(ParseIndex(p.ElementAtOrDefault(0)), ParseIndex(p.ElementAtOrDefault(1)), ParseIndex(p.ElementAtOrDefault(2)));
    }

    private static int ParseIndex(string? value) => string.IsNullOrWhiteSpace(value) ? 0 : int.Parse(value, CultureInfo.InvariantCulture);

    private static int ResolveIndex(int index, int count)
    {
        if (index > 0) return index - 1;
        if (index < 0) return count + index;
        return -1;
    }

    private static void EmitTriangle(
        ObjVertex a, ObjVertex b, ObjVertex c,
        List<Vector3> sourcePositions, List<Vector3> sourceNormals, List<Vector2> sourceUvs,
        List<float> positions, List<float> normals, List<float> uvs, List<uint> indices)
    {
        var pa = Position(a, sourcePositions);
        var pb = Position(b, sourcePositions);
        var pc = Position(c, sourcePositions);
        var faceNormal = Vector3.Cross(pb - pa, pc - pa);
        faceNormal = faceNormal.LengthSquared() > 1e-12f ? Vector3.Normalize(faceNormal) : Vector3.UnitY;

        Emit(a, pa, faceNormal, sourceNormals, sourceUvs, positions, normals, uvs, indices);
        Emit(b, pb, faceNormal, sourceNormals, sourceUvs, positions, normals, uvs, indices);
        Emit(c, pc, faceNormal, sourceNormals, sourceUvs, positions, normals, uvs, indices);
    }

    private static Vector3 Position(ObjVertex vertex, List<Vector3> positions)
    {
        var index = ResolveIndex(vertex.Position, positions.Count);
        if ((uint)index >= (uint)positions.Count) throw new InvalidDataException("OBJ face references an invalid position index.");
        return positions[index];
    }

    private static void Emit(
        ObjVertex vertex, Vector3 position, Vector3 fallbackNormal,
        List<Vector3> sourceNormals, List<Vector2> sourceUvs,
        List<float> positions, List<float> normals, List<float> uvs, List<uint> indices)
    {
        var normalIndex = ResolveIndex(vertex.Normal, sourceNormals.Count);
        var normal = (uint)normalIndex < (uint)sourceNormals.Count ? sourceNormals[normalIndex] : fallbackNormal;
        var uvIndex = ResolveIndex(vertex.TexCoord, sourceUvs.Count);
        var uv = (uint)uvIndex < (uint)sourceUvs.Count ? sourceUvs[uvIndex] : Vector2.Zero;

        positions.Add(position.X); positions.Add(position.Y); positions.Add(position.Z);
        normals.Add(normal.X); normals.Add(normal.Y); normals.Add(normal.Z);
        uvs.Add(uv.X); uvs.Add(uv.Y);
        indices.Add((uint)indices.Count);
    }

    private static float F(string value) => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string NormalizeDestination(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination)) return "Assets/Meshes";
        var normalized = AssetDatabase.NormalizeProjectPath(destination).TrimEnd('/');
        if (!normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) && !normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            normalized = "Assets/" + normalized;
        return normalized;
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "ImportedMesh" : result;
    }
}
