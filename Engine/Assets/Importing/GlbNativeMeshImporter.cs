using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;
using TACTIX.Engine.Assets.Serialization;

namespace TACTIX.Engine.Assets.Importing;

/// <summary>
/// Dependency-free GLB 2.0 importer for the native TACTIX mesh/material/texture path.
/// The source format decides primitive/material-slot count. The emitted MeshAsset is
/// source-format agnostic so FBX/USD importers can target the same representation later.
/// </summary>
public sealed class GlbNativeMeshImporter : IAssetImporter
{
    public string Id => "tactix.glb.native.meshes";
    public int Version => 1;
    public AssetImportCapability Capability => AssetImportCapability.Native;
    public IReadOnlyCollection<string> Extensions { get; } = [".glb"];

    public AssetImportResult Import(AssetDatabase database, AssetImportRequest request)
    {
        if (!File.Exists(request.SourcePath))
            return new(false, Id, Array.Empty<ImportedAsset>(), $"GLB source not found: {request.SourcePath}");

        try
        {
            var source = Path.GetFullPath(request.SourcePath);
            var bytes = File.ReadAllBytes(source);
            var (jsonBytes, bin) = ReadGlb(bytes);
            using var document = JsonDocument.Parse(jsonBytes);
            var root = document.RootElement;

            var modelName = SafeName(Path.GetFileNameWithoutExtension(source));
            var imported = new List<ImportedAsset>();
            var textureGuids = ImportTextures(database, root, bin, modelName, source, imported);
            var materialGuids = ImportMaterials(database, root, modelName, textureGuids, imported);

            if (!root.TryGetProperty("meshes", out var meshes) || meshes.ValueKind != JsonValueKind.Array || meshes.GetArrayLength() == 0)
                return new(false, Id, imported, "GLB contains no meshes.");

            for (var meshIndex = 0; meshIndex < meshes.GetArrayLength(); meshIndex++)
            {
                var meshElement = meshes[meshIndex];
                var meshName = meshElement.TryGetProperty("name", out var meshNameEl) && !string.IsNullOrWhiteSpace(meshNameEl.GetString())
                    ? SafeName(meshNameEl.GetString()!)
                    : meshes.GetArrayLength() == 1 ? modelName : $"{modelName}_Mesh{meshIndex + 1}";

                var positions = new List<float>();
                var normals = new List<float>();
                var uvs = new List<float>();
                var indices = new List<uint>();
                var submeshes = new List<MeshSubmesh>();

                if (!meshElement.TryGetProperty("primitives", out var primitives) || primitives.ValueKind != JsonValueKind.Array)
                    continue;

                for (var primitiveIndex = 0; primitiveIndex < primitives.GetArrayLength(); primitiveIndex++)
                {
                    var primitive = primitives[primitiveIndex];
                    var mode = primitive.TryGetProperty("mode", out var modeEl) ? modeEl.GetInt32() : 4;
                    if (mode != 4) continue; // TRIANGLES only for the first native pass.
                    if (!primitive.TryGetProperty("attributes", out var attributes) || !attributes.TryGetProperty("POSITION", out var positionAccessorEl))
                        continue;

                    var primitivePositions = ReadFloatAccessor(root, bin, positionAccessorEl.GetInt32(), 3);
                    var vertexCount = primitivePositions.Length / 3;
                    if (vertexCount == 0) continue;

                    float[] primitiveNormals;
                    if (attributes.TryGetProperty("NORMAL", out var normalAccessorEl))
                        primitiveNormals = ReadFloatAccessor(root, bin, normalAccessorEl.GetInt32(), 3);
                    else
                        primitiveNormals = new float[vertexCount * 3];

                    float[] primitiveUvs;
                    if (attributes.TryGetProperty("TEXCOORD_0", out var uvAccessorEl))
                        primitiveUvs = ReadFloatAccessor(root, bin, uvAccessorEl.GetInt32(), 2);
                    else
                        primitiveUvs = new float[vertexCount * 2];

                    var primitiveIndices = primitive.TryGetProperty("indices", out var indexAccessorEl)
                        ? ReadIndexAccessor(root, bin, indexAccessorEl.GetInt32())
                        : Enumerable.Range(0, vertexCount).Select(i => (uint)i).ToArray();

                    var vertexBase = positions.Count / 3;
                    positions.AddRange(primitivePositions);
                    normals.AddRange(primitiveNormals.Length == vertexCount * 3 ? primitiveNormals : new float[vertexCount * 3]);
                    uvs.AddRange(primitiveUvs.Length == vertexCount * 2 ? primitiveUvs : new float[vertexCount * 2]);

                    var firstIndex = indices.Count;
                    foreach (var index in primitiveIndices) indices.Add(index + (uint)vertexBase);

                    AssetGuid? materialGuid = null;
                    var materialIndex = primitive.TryGetProperty("material", out var materialEl) ? materialEl.GetInt32() : -1;
                    if ((uint)materialIndex < (uint)materialGuids.Count) materialGuid = materialGuids[materialIndex];
                    submeshes.Add(new MeshSubmesh(firstIndex, primitiveIndices.Length, materialGuid, $"Slot {submeshes.Count}"));
                }

                if (indices.Count == 0) continue;
                GenerateMissingNormals(positions, normals, indices);

                var projectPath = $"Assets/Meshes/{meshName}.tasset";
                database.Registry.TryGetByPath(projectPath, out var existing);
                var guid = existing?.Guid ?? AssetGuid.New();
                var mesh = new MeshAsset(guid, positions.ToArray(), normals.ToArray(), uvs.ToArray(), indices.ToArray())
                {
                    DefaultMaterialGuid = submeshes.FirstOrDefault().DefaultMaterialGuid,
                    Submeshes = submeshes.ToArray()
                };
                var meta = database.SaveMesh(projectPath, mesh, meshName);
                imported.Add(new ImportedAsset(meta.Guid, meta.Type, meta.ProjectPath, meta.Name));
            }

            var meshCount = imported.Count(a => a.Type == AssetType.Mesh);
            return meshCount > 0
                ? new(true, Id, imported, $"Imported GLB: {meshCount} mesh{(meshCount == 1 ? "" : "es")} with source-defined material slots.")
                : new(false, Id, imported, "GLB contained no supported triangle primitives.");
        }
        catch (Exception ex)
        {
            return new(false, Id, Array.Empty<ImportedAsset>(), $"GLB native import failed: {ex.Message}");
        }
    }

    private static (byte[] Json, byte[] Bin) ReadGlb(byte[] bytes)
    {
        if (bytes.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)) != 0x46546C67)
            throw new InvalidDataException("Not a valid GLB file.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != 2)
            throw new InvalidDataException("Only GLB 2.0 is supported.");

        byte[]? json = null;
        byte[]? bin = null;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)));
            var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += 8;
            if (offset + length > bytes.Length) throw new InvalidDataException("GLB chunk exceeds file length.");
            if (type == 0x4E4F534A) json = bytes.AsSpan(offset, length).ToArray();
            else if (type == 0x004E4942) bin = bytes.AsSpan(offset, length).ToArray();
            offset += length;
        }
        return (json ?? throw new InvalidDataException("GLB JSON chunk missing."), bin ?? []);
    }

    private static List<AssetGuid?> ImportTextures(AssetDatabase database, JsonElement root, byte[] bin, string modelName, string sourcePath, List<ImportedAsset> imported)
    {
        var result = new List<AssetGuid?>();
        if (!root.TryGetProperty("textures", out var textures) || textures.ValueKind != JsonValueKind.Array) return result;
        root.TryGetProperty("images", out var images);

        for (var textureIndex = 0; textureIndex < textures.GetArrayLength(); textureIndex++)
        {
            AssetGuid? guid = null;
            var texture = textures[textureIndex];
            var sourceIndex = texture.TryGetProperty("source", out var sourceEl) ? sourceEl.GetInt32() : -1;
            if (images.ValueKind == JsonValueKind.Array && (uint)sourceIndex < (uint)images.GetArrayLength())
            {
                var image = images[sourceIndex];
                var imageName = image.TryGetProperty("name", out var nameEl) && !string.IsNullOrWhiteSpace(nameEl.GetString())
                    ? SafeName(nameEl.GetString()!) : $"{modelName}_Texture{textureIndex + 1}";
                string? imageFile = null;

                if (image.TryGetProperty("bufferView", out var viewEl))
                {
                    var data = ReadBufferView(root, bin, viewEl.GetInt32());
                    var mime = image.TryGetProperty("mimeType", out var mimeEl) ? mimeEl.GetString() ?? "image/png" : "image/png";
                    var extension = mime.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
                    var projectImagePath = $"Assets/Textures/{imageName}{extension}";
                    imageFile = database.ResolveProjectPath(projectImagePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(imageFile)!);
                    File.WriteAllBytes(imageFile, data);
                }
                else if (image.TryGetProperty("uri", out var uriEl))
                {
                    var uri = uriEl.GetString();
                    if (!string.IsNullOrWhiteSpace(uri) && !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        var candidate = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, Uri.UnescapeDataString(uri)));
                        if (File.Exists(candidate)) imageFile = candidate;
                    }
                }

                if (!string.IsNullOrWhiteSpace(imageFile) && File.Exists(imageFile))
                {
                    var textureImport = new TextureAssetImporter().Import(database, new AssetImportRequest(imageFile, "Assets/Textures"));
                    var textureAsset = textureImport.Assets.FirstOrDefault(a => a.Type == AssetType.Texture);
                    if (textureAsset != null)
                    {
                        guid = textureAsset.Guid;
                        if (!imported.Any(a => a.Guid == textureAsset.Guid)) imported.Add(textureAsset);
                    }
                }
            }
            result.Add(guid);
        }
        return result;
    }

    private static List<AssetGuid?> ImportMaterials(AssetDatabase database, JsonElement root, string modelName, IReadOnlyList<AssetGuid?> textureGuids, List<ImportedAsset> imported)
    {
        var result = new List<AssetGuid?>();
        if (!root.TryGetProperty("materials", out var materials) || materials.ValueKind != JsonValueKind.Array) return result;

        for (var i = 0; i < materials.GetArrayLength(); i++)
        {
            var sourceMaterial = materials[i];
            var name = sourceMaterial.TryGetProperty("name", out var nameEl) && !string.IsNullOrWhiteSpace(nameEl.GetString())
                ? SafeName(nameEl.GetString()!) : $"{modelName}_Material{i + 1}";
            float r = 1, g = 1, b = 1, a = 1, metallic = 1, roughness = 1;
            AssetGuid? textureGuid = null;

            if (sourceMaterial.TryGetProperty("pbrMetallicRoughness", out var pbr))
            {
                if (pbr.TryGetProperty("baseColorFactor", out var factor) && factor.ValueKind == JsonValueKind.Array)
                {
                    var values = factor.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    if (values.Length >= 4) { r = values[0]; g = values[1]; b = values[2]; a = values[3]; }
                }
                if (pbr.TryGetProperty("metallicFactor", out var metallicEl)) metallic = metallicEl.GetSingle();
                if (pbr.TryGetProperty("roughnessFactor", out var roughnessEl)) roughness = roughnessEl.GetSingle();
                if (pbr.TryGetProperty("baseColorTexture", out var baseTexture) && baseTexture.TryGetProperty("index", out var textureIndexEl))
                {
                    var textureIndex = textureIndexEl.GetInt32();
                    if ((uint)textureIndex < (uint)textureGuids.Count) textureGuid = textureGuids[textureIndex];
                }
            }

            var projectPath = $"Assets/Materials/{name}.tasset";
            database.Registry.TryGetByPath(projectPath, out var existing);
            var guid = existing?.Guid ?? AssetGuid.New();
            var material = new MaterialAsset(guid, r, g, b, a, metallic, roughness, textureGuid);
            var meta = database.SaveMaterial(projectPath, material, name);
            result.Add(meta.Guid);
            imported.Add(new ImportedAsset(meta.Guid, meta.Type, meta.ProjectPath, meta.Name));
        }
        return result;
    }

    private static float[] ReadFloatAccessor(JsonElement root, byte[] bin, int accessorIndex, int expectedComponents)
    {
        var accessors = root.GetProperty("accessors");
        var accessor = accessors[accessorIndex];
        var componentType = accessor.GetProperty("componentType").GetInt32();
        if (componentType != 5126) throw new InvalidDataException("GLB float attributes must use FLOAT component type.");
        var count = accessor.GetProperty("count").GetInt32();
        var components = ComponentsForType(accessor.GetProperty("type").GetString() ?? "");
        if (components != expectedComponents) throw new InvalidDataException($"Unexpected accessor width {components}; expected {expectedComponents}.");
        var viewIndex = accessor.GetProperty("bufferView").GetInt32();
        var views = root.GetProperty("bufferViews");
        var view = views[viewIndex];
        var viewOffset = view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0;
        var accessorOffset = accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
        var stride = view.TryGetProperty("byteStride", out var strideEl) ? strideEl.GetInt32() : components * 4;
        var output = new float[count * components];
        for (var i = 0; i < count; i++)
        {
            var sourceOffset = viewOffset + accessorOffset + i * stride;
            for (var c = 0; c < components; c++)
                output[i * components + c] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bin.AsSpan(sourceOffset + c * 4, 4)));
        }
        return output;
    }

    private static uint[] ReadIndexAccessor(JsonElement root, byte[] bin, int accessorIndex)
    {
        var accessor = root.GetProperty("accessors")[accessorIndex];
        var count = accessor.GetProperty("count").GetInt32();
        var componentType = accessor.GetProperty("componentType").GetInt32();
        var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        var viewOffset = view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0;
        var accessorOffset = accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
        var componentSize = componentType switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => throw new InvalidDataException("Unsupported GLB index component type.") };
        var stride = view.TryGetProperty("byteStride", out var strideEl) ? strideEl.GetInt32() : componentSize;
        var output = new uint[count];
        for (var i = 0; i < count; i++)
        {
            var offset = viewOffset + accessorOffset + i * stride;
            output[i] = componentType switch
            {
                5121 => bin[offset],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(bin.AsSpan(offset, 2)),
                5125 => BinaryPrimitives.ReadUInt32LittleEndian(bin.AsSpan(offset, 4)),
                _ => 0
            };
        }
        return output;
    }

    private static byte[] ReadBufferView(JsonElement root, byte[] bin, int viewIndex)
    {
        var view = root.GetProperty("bufferViews")[viewIndex];
        var offset = view.TryGetProperty("byteOffset", out var offsetEl) ? offsetEl.GetInt32() : 0;
        var length = view.GetProperty("byteLength").GetInt32();
        return bin.AsSpan(offset, length).ToArray();
    }

    private static int ComponentsForType(string type) => type switch
    {
        "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4,
        _ => throw new InvalidDataException($"Unsupported GLB accessor type {type}.")
    };

    private static void GenerateMissingNormals(List<float> positions, List<float> normals, List<uint> indices)
    {
        if (normals.Count != positions.Count) return;
        var hasNormal = false;
        for (var i = 0; i < normals.Count; i++) if (MathF.Abs(normals[i]) > 1e-8f) { hasNormal = true; break; }
        if (hasNormal) return;

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var ia = checked((int)indices[i]) * 3;
            var ib = checked((int)indices[i + 1]) * 3;
            var ic = checked((int)indices[i + 2]) * 3;
            var ax = positions[ia]; var ay = positions[ia + 1]; var az = positions[ia + 2];
            var bx = positions[ib]; var by = positions[ib + 1]; var bz = positions[ib + 2];
            var cx = positions[ic]; var cy = positions[ic + 1]; var cz = positions[ic + 2];
            var abx = bx - ax; var aby = by - ay; var abz = bz - az;
            var acx = cx - ax; var acy = cy - ay; var acz = cz - az;
            var nx = aby * acz - abz * acy; var ny = abz * acx - abx * acz; var nz = abx * acy - aby * acx;
            var length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (length > 1e-8f) { nx /= length; ny /= length; nz /= length; }
            foreach (var v in new[] { ia, ib, ic }) { normals[v] += nx; normals[v + 1] += ny; normals[v + 2] += nz; }
        }
        for (var i = 0; i + 2 < normals.Count; i += 3)
        {
            var x = normals[i]; var y = normals[i + 1]; var z = normals[i + 2];
            var length = MathF.Sqrt(x * x + y * y + z * z);
            if (length > 1e-8f) { normals[i] = x / length; normals[i + 1] = y / length; normals[i + 2] = z / length; }
            else { normals[i] = 0; normals[i + 1] = 1; normals[i + 2] = 0; }
        }
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "GLBAsset" : result;
    }
}
