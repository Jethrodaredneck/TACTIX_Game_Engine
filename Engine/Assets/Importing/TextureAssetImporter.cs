using System.Security.Cryptography;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;
using TACTIX.Engine.Assets.Serialization;

namespace TACTIX.Engine.Assets.Importing;

/// <summary>Copies common image sources into Assets/Textures and registers a GUID-backed TextureAsset.</summary>
public sealed class TextureAssetImporter : IAssetImporter
{
    public string Id => "tactix.texture.native";
    public int Version => 1;
    public AssetImportCapability Capability => AssetImportCapability.Native;
    public IReadOnlyCollection<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".tga", ".bmp", ".tif", ".tiff"];

    public AssetImportResult Import(AssetDatabase database, AssetImportRequest request)
    {
        if (!File.Exists(request.SourcePath))
            return new(false, Id, Array.Empty<ImportedAsset>(), $"Texture source not found: {request.SourcePath}");

        try
        {
            var source = Path.GetFullPath(request.SourcePath);
            var name = SafeName(Path.GetFileNameWithoutExtension(source));
            var ext = Path.GetExtension(source).ToLowerInvariant();
            var directory = "Assets/Textures";
            var imageProjectPath = $"{directory}/{name}{ext}";
            var assetProjectPath = $"{directory}/{name}.tasset";
            var imageFullPath = database.ResolveProjectPath(imageProjectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(imageFullPath)!);
            if (!string.Equals(source, imageFullPath, StringComparison.Ordinal)) File.Copy(source, imageFullPath, true);

            database.Registry.TryGetByPath(assetProjectPath, out var existing);
            var guid = existing?.Guid ?? AssetGuid.New();
            var sourceHash = FileHash(source);
            var texture = new TextureAsset(guid, imageProjectPath, source, sourceHash, true);
            var meta = new AssetMeta
            {
                Guid = guid,
                Type = AssetType.Texture,
                ProjectPath = assetProjectPath,
                Name = name,
                ContentHash = sourceHash,
                SourcePath = source,
                SourceHash = sourceHash,
                ImporterId = Id,
                ImporterVersion = Version,
                ImportedAtUtc = DateTimeOffset.UtcNow
            };
            JsonAssetSerializer.Save(database.ResolveProjectPath(assetProjectPath), AssetFile.ForTexture(meta, texture));
            database.Registry.Upsert(meta);
            return new(true, Id, [new ImportedAsset(guid, AssetType.Texture, assetProjectPath, name)], $"Imported texture -> {assetProjectPath}");
        }
        catch (Exception ex)
        {
            return new(false, Id, Array.Empty<ImportedAsset>(), $"Texture import failed: {ex.Message}");
        }
    }

    private static string FileHash(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "Texture" : result;
    }
}
