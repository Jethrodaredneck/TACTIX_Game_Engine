using System.Globalization;
using System.Numerics;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Engine.Assets.Importing;

/// <summary>
/// Dependency-free OBJ importer used by the static-mesh bootstrap path. It converts
/// Wavefront geometry into native TACTIX MeshAssets, imports referenced MTL libraries,
/// preserves the first usemtl binding as the mesh default, and normalizes triangle
/// winding against authored normals so imported meshes do not appear inside-out.
/// </summary>
public sealed class ObjMeshImporter : IAssetImporter
{
    public string Id => "tactix.obj.native";
    public int Version => 2;
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
            var materialLibraries = new List<string>();
            string? firstMaterialName = null;

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
                    {
                        var n = new Vector3(F(parts[1]), F(parts[2]), F(parts[3]));
                        sourceNormals.Add(n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY);
                        break;
                    }
                    case "vt" when parts.Length >= 3:
                        sourceUvs.Add(new Vector2(F(parts[1]), 1f - F(parts[2])));
                        break;
                    case "mtllib" when parts.Length >= 2:
                        materialLibraries.Add(string.Join(" ", parts.Skip(1)));
                        break;
                    case "usemtl" when parts.Length >= 2 && string.IsNullOrWhiteSpace(firstMaterialName):
                        firstMaterialName = string.Join(" ", parts.Skip(1));
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

            var imported = new List<ImportedAsset>();
            var materialGuid = ImportReferencedMaterials(database, request.SourcePath, materialLibraries, firstMaterialName, imported);

            var mesh = new MeshAsset(AssetGuid.New(), positions.ToArray(), normals.ToArray(), uvs.ToArray(), indices.ToArray())
            {
                DefaultMaterialGuid = materialGuid
            };
            var name = SafeName(Path.GetFileNameWithoutExtension(request.SourcePath));
            var destination = NormalizeDestination(request.DestinationDirectory);
            var projectPath = $"{destination}/{name}.tasset";
            var meta = database.SaveMesh(projectPath, mesh, name);
            imported.Add(new ImportedAsset(meta.Guid, meta.Type, meta.ProjectPath, meta.Name));

            var materialText = materialGuid.HasValue ? " with MTL material" : "";
            return new(true, Id, imported,
                $"Imported OBJ mesh ({indices.Count / 3} triangles){materialText} -> {meta.ProjectPath}");
        }
        catch (Exception ex)
        {
            return new(false, Id, Array.Empty<ImportedAsset>(), $"OBJ import failed: {ex.Message}");
        }
    }

    private static AssetGuid? ImportReferencedMaterials(
        AssetDatabase database,
        string objPath,
        IReadOnlyList<string> libraries,
        string? firstMaterialName,
        List<ImportedAsset> imported)
    {
        if (libraries.Count == 0) return null;

        var objDirectory = Path.GetDirectoryName(Path.GetFullPath(objPath)) ?? Directory.GetCurrentDirectory();
        var mtlImporter = new MtlMaterialImporter();
        AssetGuid? fallback = null;
        var desired = string.IsNullOrWhiteSpace(firstMaterialName) ? "" : SafeName(firstMaterialName);

        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var mtlPath = Path.GetFullPath(Path.Combine(objDirectory, library));
            if (!File.Exists(mtlPath)) continue;

            var result = mtlImporter.Import(database, new AssetImportRequest(mtlPath, "Assets/Materials"));
            if (!result.Success) continue;

            foreach (var asset in result.Assets)
            {
                if (asset.Type != AssetType.Material) continue;
                if (!fallback.HasValue) fallback = asset.Guid;
                if (!imported.Any(x => x.Guid == asset.Guid)) imported.Add(asset);
                if (!string.IsNullOrWhiteSpace(desired) && string.Equals(asset.Name, desired, StringComparison.OrdinalIgnoreCase))
                    fallback = asset.Guid;
            }
        }

        return fallback;
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

        // Some OBJ exporters change handedness while converting Blender coordinates.
        // If authored normals disagree with the resulting winding, flip the triangle so
        // Metal back-face culling sees the same exterior surface the author intended.
        var expected = AuthoredNormal(a, b, c, sourceNormals);
        if (expected.HasValue && Vector3.Dot(faceNormal, expected.Value) < 0f)
        {
            (b, c) = (c, b);
            (pb, pc) = (pc, pb);
            faceNormal = -faceNormal;
        }

        Emit(a, pa, faceNormal, sourceNormals, sourceUvs, positions, normals, uvs, indices);
        Emit(b, pb, faceNormal, sourceNormals, sourceUvs, positions, normals, uvs, indices);
        Emit(c, pc, faceNormal, sourceNormals, sourceUvs, positions, normals, uvs, indices);
    }

    private static Vector3? AuthoredNormal(ObjVertex a, ObjVertex b, ObjVertex c, List<Vector3> sourceNormals)
    {
        var values = new[] { a, b, c }
            .Select(v => ResolveIndex(v.Normal, sourceNormals.Count))
            .Where(i => (uint)i < (uint)sourceNormals.Count)
            .Select(i => sourceNormals[i])
            .ToArray();
        if (values.Length == 0) return null;
        var sum = values.Aggregate(Vector3.Zero, (current, n) => current + n);
        return sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : null;
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
