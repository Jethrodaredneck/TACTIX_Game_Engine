using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TACTIX.Engine.Assets.Formats;
using TACTIX.Engine.Assets.Serialization;

namespace TACTIX.Engine.Assets.Database;

public sealed class AssetDatabase
{
    public string ProjectRoot { get; }
    public string AssetsRoot => Path.Combine(ProjectRoot, "Assets");

    public AssetRegistry Registry { get; } = new();

    public AssetDatabase(string projectRoot)
    {
        ProjectRoot = Path.GetFullPath(projectRoot);
    }

    public void Initialize()
    {
        Directory.CreateDirectory(AssetsRoot);
        ScanAssetsFolder();
    }

    public void ScanAssetsFolder()
    {
        Directory.CreateDirectory(AssetsRoot);

        foreach (var file in Directory.EnumerateFiles(AssetsRoot, "*.tasset", SearchOption.AllDirectories))
        {
            try
            {
                var assetFile = JsonAssetSerializer.Load(file);
                Registry.Upsert(assetFile.Meta);
            }
            catch
            {
                // Ignore malformed files; Stage 5 will surface in console panel.
            }
        }
    }

    public AssetMeta SaveMesh(string projectPath, MeshAsset mesh, string name = "")
    {
        var full = ResolveProjectPath(projectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var guid = mesh.Guid.Value == Guid.Empty ? AssetGuid.New() : mesh.Guid;
        mesh = mesh with { Guid = guid };

        var meta = new AssetMeta
        {
            Guid = guid,
            Type = AssetType.Mesh,
            ProjectPath = NormalizeProjectPath(projectPath),
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(projectPath) : name,
            ContentHash = ComputeHash(mesh),
            ImportedAtUtc = DateTimeOffset.UtcNow
        };

        JsonAssetSerializer.Save(full, AssetFile.ForMesh(meta, mesh));
        Registry.Upsert(meta);
        return meta;
    }

    public AssetMeta SaveMaterial(string projectPath, MaterialAsset mat, string name = "")
    {
        var full = ResolveProjectPath(projectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var guid = mat.Guid.Value == Guid.Empty ? AssetGuid.New() : mat.Guid;
        mat = mat with { Guid = guid };

        var meta = new AssetMeta
        {
            Guid = guid,
            Type = AssetType.Material,
            ProjectPath = NormalizeProjectPath(projectPath),
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(projectPath) : name,
            ContentHash = ComputeHash(mat),
            ImportedAtUtc = DateTimeOffset.UtcNow
        };

        JsonAssetSerializer.Save(full, AssetFile.ForMaterial(meta, mat));
        Registry.Upsert(meta);
        return meta;
    }

    public AssetMeta SaveTerrain(string projectPath, TerrainAsset terrain, string name = "")
    {
        var full = ResolveProjectPath(projectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var guid = terrain.Guid.Value == Guid.Empty ? AssetGuid.New() : terrain.Guid;
        terrain = terrain with { Guid = guid };

        var meta = new AssetMeta
        {
            Guid = guid,
            Type = AssetType.Terrain,
            ProjectPath = NormalizeProjectPath(projectPath),
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(projectPath) : name,
            ContentHash = ComputeHash(terrain),
            ImportedAtUtc = DateTimeOffset.UtcNow
        };

        JsonAssetSerializer.Save(full, AssetFile.ForTerrain(meta, terrain));
        Registry.Upsert(meta);
        return meta;
    }

    public AssetMeta SaveModel(
        string projectPath,
        ModelAsset model,
        string name = "",
        string sourcePath = "",
        string sourceHash = "",
        string importerId = "",
        int importerVersion = 0,
        string importSettingsHash = "")
    {
        var full = ResolveProjectPath(projectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var guid = model.Guid.Value == Guid.Empty ? AssetGuid.New() : model.Guid;
        model = model with { Guid = guid };

        var meta = new AssetMeta
        {
            Guid = guid,
            Type = AssetType.Model,
            ProjectPath = NormalizeProjectPath(projectPath),
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(projectPath) : name,
            ContentHash = ComputeHash(model),
            SourcePath = sourcePath,
            SourceHash = sourceHash,
            ImporterId = importerId,
            ImporterVersion = importerVersion,
            ImportSettingsHash = importSettingsHash,
            ImportedAtUtc = DateTimeOffset.UtcNow
        };

        JsonAssetSerializer.Save(full, AssetFile.ForModel(meta, model));
        Registry.Upsert(meta);
        return meta;
    }

    public MeshAsset LoadMesh(AssetGuid guid)
    {
        if (!Registry.TryGet(guid, out var meta))
            throw new FileNotFoundException($"Asset not registered: {guid}");

        if (meta.Type != AssetType.Mesh)
            throw new InvalidOperationException($"Asset {guid} is {meta.Type}, not Mesh.");

        var full = ResolveProjectPath(meta.ProjectPath);
        var file = JsonAssetSerializer.Load(full);
        if (file.Mesh == null) throw new InvalidDataException("Mesh payload missing.");
        return file.Mesh!.Value;
    }

    public MaterialAsset LoadMaterial(AssetGuid guid)
    {
        if (!Registry.TryGet(guid, out var meta))
            throw new FileNotFoundException($"Asset not registered: {guid}");

        if (meta.Type != AssetType.Material)
            throw new InvalidOperationException($"Asset {guid} is {meta.Type}, not Material.");

        var full = ResolveProjectPath(meta.ProjectPath);
        var file = JsonAssetSerializer.Load(full);
        if (file.Material == null) throw new InvalidDataException("Material payload missing.");
        return file.Material!.Value;
    }

    public TerrainAsset LoadTerrain(AssetGuid guid)
    {
        if (!Registry.TryGet(guid, out var meta))
            throw new FileNotFoundException($"Asset not registered: {guid}");

        if (meta.Type != AssetType.Terrain)
            throw new InvalidOperationException($"Asset {guid} is {meta.Type}, not Terrain.");

        var full = ResolveProjectPath(meta.ProjectPath);
        var file = JsonAssetSerializer.Load(full);
        if (file.Terrain == null) throw new InvalidDataException("Terrain payload missing.");
        return file.Terrain.Value;
    }

    public ModelAsset LoadModel(AssetGuid guid)
    {
        if (!Registry.TryGet(guid, out var meta))
            throw new FileNotFoundException($"Asset not registered: {guid}");

        if (meta.Type != AssetType.Model)
            throw new InvalidOperationException($"Asset {guid} is {meta.Type}, not Model.");

        var full = ResolveProjectPath(meta.ProjectPath);
        var file = JsonAssetSerializer.Load(full);
        if (file.Model == null) throw new InvalidDataException("Model payload missing.");
        return file.Model.Value;
    }

    public string ResolveProjectPath(string projectPath)
    {
        var norm = NormalizeProjectPath(projectPath);
        return Path.GetFullPath(Path.Combine(ProjectRoot, norm));
    }

    public static string NormalizeProjectPath(string projectPath)
    {
        var p = projectPath.Replace('\\', '/').Trim();
        if (p.StartsWith("/")) p = p.TrimStart('/');
        return p;
    }

    private static string ComputeHash(MeshAsset mesh)
    {
        using var sha = SHA256.Create();
        void AddBytes(byte[] b) => sha.TransformBlock(b, 0, b.Length, null, 0);

        AddBytes(BitConverter.GetBytes(mesh.Positions.Length));
        foreach (var v in mesh.Positions) AddBytes(BitConverter.GetBytes(v));

        if (mesh.Normals != null)
        {
            AddBytes(BitConverter.GetBytes(mesh.Normals.Length));
            foreach (var v in mesh.Normals) AddBytes(BitConverter.GetBytes(v));
        }

        if (mesh.UV0 != null)
        {
            AddBytes(BitConverter.GetBytes(mesh.UV0.Length));
            foreach (var v in mesh.UV0) AddBytes(BitConverter.GetBytes(v));
        }

        if (mesh.Indices != null)
        {
            AddBytes(BitConverter.GetBytes(mesh.Indices.Length));
            foreach (var v in mesh.Indices) AddBytes(BitConverter.GetBytes(v));
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static string ComputeHash(MaterialAsset mat)
    {
        using var sha = SHA256.Create();
        var s = $"{mat.BaseColorR:F4},{mat.BaseColorG:F4},{mat.BaseColorB:F4},{mat.BaseColorA:F4}|{mat.Metallic:F4}|{mat.Roughness:F4}|{mat.BaseColorTextureGuid?.ToString() ?? ""}";
        var bytes = Encoding.UTF8.GetBytes(s);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeHash(TerrainAsset terrain)
    {
        using var sha = SHA256.Create();
        void AddBytes(byte[] bytes) => sha.TransformBlock(bytes, 0, bytes.Length, null, 0);

        AddBytes(BitConverter.GetBytes(terrain.Resolution));
        AddBytes(BitConverter.GetBytes(terrain.SizeX));
        AddBytes(BitConverter.GetBytes(terrain.SizeZ));
        AddBytes(BitConverter.GetBytes(terrain.HeightScale));
        foreach (var height in terrain.Heights) AddBytes(BitConverter.GetBytes(height));
        foreach (var layer in terrain.Layers)
        {
            AddBytes(Encoding.UTF8.GetBytes(layer.MaterialGuid?.ToString() ?? ""));
            AddBytes(BitConverter.GetBytes(layer.TilingX));
            AddBytes(BitConverter.GetBytes(layer.TilingZ));
            AddBytes(BitConverter.GetBytes(layer.BlendStrength));
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static string ComputeHash(ModelAsset model)
    {
        using var sha = SHA256.Create();
        var s = string.Join("|",
            model.InterchangeProjectPath,
            model.ImportMetadataProjectPath,
            model.SourcePath,
            model.SourceHash,
            model.InterchangeHash,
            model.InterchangeFormat,
            model.ObjectCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var bytes = Encoding.UTF8.GetBytes(s);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
