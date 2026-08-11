using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Engine.Assets.Importing;

/// <summary>
/// GLB orchestration importer. It composes the existing native mesh/material/texture
/// importer with the legacy ModelAsset/reimport plumbing, then records a generic node
/// hierarchy. Runtime/editor code consumes ModelAsset.Nodes and never parses GLB.
/// </summary>
public sealed class GlbSceneModelImporter : IAssetImporter
{
    public string Id => "tactix.glb.scene-model";
    public int Version => 1;
    public AssetImportCapability Capability => AssetImportCapability.Native;
    public IReadOnlyCollection<string> Extensions { get; } = [".glb"];

    public AssetImportResult Import(AssetDatabase database, AssetImportRequest request)
    {
        if (!File.Exists(request.SourcePath))
            return new(false, Id, [], $"GLB source not found: {request.SourcePath}");

        try
        {
            // Keep source/reimport metadata in the existing ModelAsset path.
            var modelResult = new GlbModelImporter().Import(database, request);
            if (!modelResult.Success)
                return modelResult;

            // Emit native renderer assets from the same source.
            var nativeResult = new GlbNativeMeshImporter().Import(database, request);
            if (!nativeResult.Success)
                return nativeResult;

            var modelImported = modelResult.Assets.First(a => a.Type == AssetType.Model);
            var model = database.LoadModel(modelImported.Guid);
            var nodes = ReadNodes(database, request.SourcePath);
            model = model with { Nodes = nodes, ObjectCount = nodes.Length };

            var meta = database.SaveModel(
                modelImported.ProjectPath,
                model,
                modelImported.Name,
                model.SourcePath,
                model.SourceHash,
                Id,
                Version,
                "");

            var assets = modelResult.Assets
                .Concat(nativeResult.Assets)
                .Where(a => a.Guid != meta.Guid)
                .Prepend(new ImportedAsset(meta.Guid, meta.Type, meta.ProjectPath, meta.Name))
                .GroupBy(a => a.Guid)
                .Select(g => g.First())
                .ToArray();

            var meshNodes = nodes.Count(n => n.MeshGuid.HasValue);
            return new(true, Id, assets, $"Imported GLB model hierarchy: {nodes.Length} nodes, {meshNodes} mesh nodes.");
        }
        catch (Exception ex)
        {
            return new(false, Id, [], $"GLB scene-model import failed: {ex.Message}");
        }
    }

    private static ModelNodeAsset[] ReadNodes(AssetDatabase database, string sourcePath)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        var json = ReadJsonChunk(bytes);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("nodes", out var nodesElement) || nodesElement.ValueKind != JsonValueKind.Array)
            return [];

        root.TryGetProperty("meshes", out var meshesElement);
        var meshGuids = ResolveMeshGuids(database, sourcePath, meshesElement);
        var parentByNode = Enumerable.Repeat(-1, nodesElement.GetArrayLength()).ToArray();

        for (var parent = 0; parent < nodesElement.GetArrayLength(); parent++)
        {
            var node = nodesElement[parent];
            if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array) continue;
            foreach (var child in children.EnumerateArray())
            {
                var index = child.GetInt32();
                if ((uint)index < (uint)parentByNode.Length) parentByNode[index] = parent;
            }
        }

        var result = new ModelNodeAsset[nodesElement.GetArrayLength()];
        for (var i = 0; i < result.Length; i++)
        {
            var node = nodesElement[i];
            var name = node.TryGetProperty("name", out var nameEl) && !string.IsNullOrWhiteSpace(nameEl.GetString())
                ? nameEl.GetString()! : $"Node {i}";

            Vector3 translation = Vector3.Zero;
            Quaternion rotation = Quaternion.Identity;
            Vector3 scale = Vector3.One;

            if (node.TryGetProperty("matrix", out var matrixEl) && matrixEl.ValueKind == JsonValueKind.Array)
            {
                var m = matrixEl.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                if (m.Length == 16)
                {
                    var matrix = new Matrix4x4(
                        m[0],m[1],m[2],m[3], m[4],m[5],m[6],m[7],
                        m[8],m[9],m[10],m[11], m[12],m[13],m[14],m[15]);
                    if (!Matrix4x4.Decompose(matrix, out scale, out rotation, out translation))
                    {
                        translation = new Vector3(m[12], m[13], m[14]);
                        scale = Vector3.One;
                        rotation = Quaternion.Identity;
                    }
                }
            }
            else
            {
                if (node.TryGetProperty("translation", out var t)) translation = ReadVec3(t, Vector3.Zero);
                if (node.TryGetProperty("scale", out var s)) scale = ReadVec3(s, Vector3.One);
                if (node.TryGetProperty("rotation", out var r))
                {
                    var q = r.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    if (q.Length >= 4) rotation = Quaternion.Normalize(new Quaternion(q[0], q[1], q[2], q[3]));
                }
            }

            AssetGuid? meshGuid = null;
            if (node.TryGetProperty("mesh", out var meshEl))
            {
                var meshIndex = meshEl.GetInt32();
                if ((uint)meshIndex < (uint)meshGuids.Length) meshGuid = meshGuids[meshIndex];
            }

            var euler = QuaternionToEulerDegrees(rotation);
            result[i] = new ModelNodeAsset(
                name,
                parentByNode[i],
                meshGuid,
                [translation.X, translation.Y, translation.Z],
                [euler.X, euler.Y, euler.Z],
                [scale.X, scale.Y, scale.Z]);
        }
        return result;
    }

    private static AssetGuid?[] ResolveMeshGuids(AssetDatabase database, string sourcePath, JsonElement meshes)
    {
        if (meshes.ValueKind != JsonValueKind.Array) return [];
        var modelName = SafeName(Path.GetFileNameWithoutExtension(sourcePath));
        var result = new AssetGuid?[meshes.GetArrayLength()];
        for (var i = 0; i < result.Length; i++)
        {
            var mesh = meshes[i];
            var name = mesh.TryGetProperty("name", out var nameEl) && !string.IsNullOrWhiteSpace(nameEl.GetString())
                ? SafeName(nameEl.GetString()!)
                : result.Length == 1 ? modelName : $"{modelName}_Mesh{i + 1}";
            if (database.Registry.TryGetByPath($"Assets/Meshes/{name}.tasset", out var meta)) result[i] = meta.Guid;
        }
        return result;
    }

    private static byte[] ReadJsonChunk(byte[] bytes)
    {
        if (bytes.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)) != 0x46546C67)
            throw new InvalidDataException("Not a GLB file.");
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)));
            var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += 8;
            if (offset + length > bytes.Length) throw new InvalidDataException("GLB chunk exceeds file length.");
            if (type == 0x4E4F534A) return bytes.AsSpan(offset, length).ToArray();
            offset += length;
        }
        throw new InvalidDataException("GLB JSON chunk missing.");
    }

    private static Vector3 ReadVec3(JsonElement element, Vector3 fallback)
    {
        if (element.ValueKind != JsonValueKind.Array) return fallback;
        var v = element.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        return v.Length >= 3 ? new Vector3(v[0], v[1], v[2]) : fallback;
    }

    private static Vector3 QuaternionToEulerDegrees(Quaternion q)
    {
        var sinr = 2f * (q.W * q.X + q.Y * q.Z);
        var cosr = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        var x = MathF.Atan2(sinr, cosr);
        var sinp = 2f * (q.W * q.Y - q.Z * q.X);
        var y = MathF.Abs(sinp) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinp) : MathF.Asin(sinp);
        var siny = 2f * (q.W * q.Z + q.X * q.Y);
        var cosy = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        var z = MathF.Atan2(siny, cosy);
        const float r2d = 57.29577951308232f;
        return new Vector3(x * r2d, y * r2d, z * r2d);
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "Model" : result;
    }
}
