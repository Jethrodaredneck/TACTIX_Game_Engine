using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Engine.Assets.Importing;

/// <summary>
/// Registers the initial public-facing source format surface. GLTF/FBX/OBJ parsing is
/// intentionally behind IAssetImporter so parser libraries can change without leaking
/// format details into scene/runtime code.
/// </summary>
public static class BuiltInImporters
{
    public static AssetImporterRegistry CreateDefault()
    {
        var registry = new AssetImporterRegistry();
        registry.Register(new GlbModelImporter());
        registry.Register(new PlannedInterchangeImporter("tactix.fbx", [".fbx"]));
        registry.Register(new PlannedInterchangeImporter("tactix.obj", [".obj"]));
        registry.Register(new PlannedInterchangeImporter("tactix.usd", [".usd", ".usda", ".usdc", ".usdz"]));
        registry.Register(new PlannedInterchangeImporter("tactix.texture", [".png", ".jpg", ".jpeg", ".tga", ".hdr", ".exr", ".dds"]));
        registry.Register(new BlenderSourceConverter());
        return registry;
    }
}

public sealed class PlannedInterchangeImporter : IAssetImporter
{
    public string Id { get; }
    public int Version => 1;
    public AssetImportCapability Capability => AssetImportCapability.Planned;
    public IReadOnlyCollection<string> Extensions { get; }

    public PlannedInterchangeImporter(string id, IReadOnlyCollection<string> extensions)
    {
        Id = id;
        Extensions = extensions;
    }

    public AssetImportResult Import(AssetDatabase database, AssetImportRequest request)
        => AssetImportResult.NotImplemented(Id, $"{Id} recognizes {Path.GetExtension(request.SourcePath)}; parser integration is the next importer stage.");
}

public sealed class GlbModelImporter : IAssetImporter
{
    public string Id => "tactix.gltf";
    public int Version => 1;
    public AssetImportCapability Capability => AssetImportCapability.Native;
    public IReadOnlyCollection<string> Extensions { get; } = [".gltf", ".glb"];

    public AssetImportResult Import(AssetDatabase database, AssetImportRequest request)
    {
        if (!File.Exists(request.SourcePath))
            return new(false, Id, Array.Empty<ImportedAsset>(), $"Model source not found: {request.SourcePath}");

        var sourcePath = Path.GetFullPath(request.SourcePath);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var metadataSourcePath = ResolveMetadataSourcePath(request);
        var bridgeMetadata = ReadBridgeMetadata(metadataSourcePath);
        var assetName = SafeFileName(Path.GetFileNameWithoutExtension(sourcePath));
        var projectPath = ResolveAssetProjectPath(database, request, assetName);
        var projectDirectory = Path.GetDirectoryName(projectPath)?.Replace('\\', '/') ?? "Assets/Models";
        assetName = Path.GetFileNameWithoutExtension(projectPath);

        var interchangeProjectPath = $"{projectDirectory}/{assetName}{extension}";
        CopyAtomic(sourcePath, database.ResolveProjectPath(interchangeProjectPath));

        var metadataProjectPath = "";
        if (!string.IsNullOrWhiteSpace(metadataSourcePath) && File.Exists(metadataSourcePath))
        {
            metadataProjectPath = $"{projectDirectory}/{assetName}.tactiximport.json";
            CopyAtomic(metadataSourcePath, database.ResolveProjectPath(metadataProjectPath));
        }

        var sourceForReimport = ResolveAuthoritativeSourcePath(database, request, bridgeMetadata) ?? sourcePath;
        var storedSourcePath = StoreSourcePath(database, sourceForReimport);
        var sourceHashPath = ResolveStoredSourcePath(database, storedSourcePath);
        var sourceHash = File.Exists(sourceHashPath) ? ComputeFileHash(sourceHashPath) : ComputeFileHash(sourcePath);
        var interchangeHash = ComputeFileHash(database.ResolveProjectPath(interchangeProjectPath));

        database.Registry.TryGetByPath(projectPath, out var existing);
        var guid = existing?.Guid ?? AssetGuid.New();
        var model = new ModelAsset(
            guid,
            interchangeProjectPath,
            metadataProjectPath,
            storedSourcePath,
            sourceHash,
            interchangeHash,
            extension.TrimStart('.').ToUpperInvariant(),
            bridgeMetadata?.Objects?.Length ?? 0);

        var meta = database.SaveModel(
            projectPath,
            model,
            assetName,
            storedSourcePath,
            sourceHash,
            Id,
            Version,
            ComputeSettingsHash(request.Settings));

        var imported = new ImportedAsset(meta.Guid, meta.Type, meta.ProjectPath, meta.Name);
        return new(true, Id, [imported], $"Imported model -> {meta.ProjectPath}");
    }

    private static string ResolveAssetProjectPath(AssetDatabase database, AssetImportRequest request, string assetName)
    {
        if (TryGetSetting(request.Settings, "assetProjectPath", out var requestedProjectPath))
        {
            var projectPath = AssetDatabase.NormalizeProjectPath(requestedProjectPath);
            if (!projectPath.EndsWith(".tasset", StringComparison.OrdinalIgnoreCase))
                projectPath += ".tasset";
            EnsureAssetsPath(projectPath);
            return projectPath;
        }

        var directory = NormalizeProjectDirectory(database, request.DestinationDirectory);
        return $"{directory}/{assetName}.tasset";
    }

    private static string NormalizeProjectDirectory(AssetDatabase database, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            directory = "Assets/Models";

        if (Path.IsPathRooted(directory))
        {
            var root = Path.GetFullPath(database.ProjectRoot);
            var full = Path.GetFullPath(directory);
            if (!full.Equals(root, StringComparison.Ordinal) &&
                !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException($"Import destination escapes project root: {directory}");
            directory = Path.GetRelativePath(root, full);
        }

        directory = AssetDatabase.NormalizeProjectPath(directory).TrimEnd('/');
        if (!directory.Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
            !directory.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            directory = "Assets/" + directory;

        EnsureAssetsPath(directory);
        return directory;
    }

    private static void EnsureAssetsPath(string projectPath)
    {
        var normalized = AssetDatabase.NormalizeProjectPath(projectPath);
        if (!normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Imported assets must be written under Assets/: {projectPath}");
    }

    private static string? ResolveMetadataSourcePath(AssetImportRequest request)
    {
        if (TryGetSetting(request.Settings, "metadataPath", out var configured) && File.Exists(configured))
            return Path.GetFullPath(configured);

        var sidecar = Path.ChangeExtension(request.SourcePath, ".tactiximport.json");
        return File.Exists(sidecar) ? Path.GetFullPath(sidecar) : null;
    }

    private static BridgeMetadata? ReadBridgeMetadata(string? metadataPath)
    {
        if (string.IsNullOrWhiteSpace(metadataPath) || !File.Exists(metadataPath))
            return null;

        try
        {
            var json = File.ReadAllText(metadataPath);
            return JsonSerializer.Deserialize<BridgeMetadata>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveAuthoritativeSourcePath(AssetDatabase database, AssetImportRequest request, BridgeMetadata? metadata)
    {
        if (TryGetSetting(request.Settings, "authoritativeSourcePath", out var authoritativeSource))
            return ResolveStoredSourcePath(database, authoritativeSource);

        if (!string.IsNullOrWhiteSpace(metadata?.SourceBlend))
            return ResolveStoredSourcePath(database, metadata.SourceBlend);

        if (!string.IsNullOrWhiteSpace(metadata?.AuthoritativeSource))
            return ResolveStoredSourcePath(database, metadata.AuthoritativeSource);

        if (!string.IsNullOrWhiteSpace(metadata?.SourceAsset))
            return ResolveStoredSourcePath(database, metadata.SourceAsset);

        return null;
    }

    private static string StoreSourcePath(AssetDatabase database, string sourcePath)
    {
        var full = ResolveStoredSourcePath(database, sourcePath);
        if (File.Exists(full))
        {
            var root = Path.GetFullPath(database.ProjectRoot);
            if (full.Equals(root, StringComparison.Ordinal) ||
                full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return Path.GetRelativePath(root, full).Replace('\\', '/');
        }

        return sourcePath;
    }

    private static string ResolveStoredSourcePath(AssetDatabase database, string sourcePath)
    {
        if (Path.IsPathRooted(sourcePath))
            return Path.GetFullPath(sourcePath);

        var projectPath = Path.GetFullPath(Path.Combine(database.ProjectRoot, sourcePath));
        return File.Exists(projectPath) ? projectPath : sourcePath;
    }

    private static string ComputeSettingsHash(IReadOnlyDictionary<string, string>? settings)
    {
        if (settings == null || settings.Count == 0)
            return "";

        var text = string.Join("\n", settings
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}"));
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string ComputeFileHash(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static void CopyAtomic(string sourcePath, string destinationPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destinationPath = Path.GetFullPath(destinationPath);
        if (string.Equals(sourcePath, destinationPath, StringComparison.Ordinal))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var tmp = destinationPath + ".tmp";
        File.Copy(sourcePath, tmp, overwrite: true);
        File.Move(tmp, destinationPath, overwrite: true);
    }

    private static bool TryGetSetting(IReadOnlyDictionary<string, string>? settings, string key, out string value)
    {
        value = "";
        if (settings == null)
            return false;

        foreach (var pair in settings)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        return false;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(invalid.Contains(c) || c is '/' or '\\' ? '_' : c);
        }

        var safe = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(safe) ? "Model" : safe;
    }

    private sealed class BridgeMetadata
    {
        public string SourceBlend { get; set; } = "";
        public string SourceAsset { get; set; } = "";
        public string AuthoritativeSource { get; set; } = "";
        public BridgeObject[] Objects { get; set; } = [];
    }

    private sealed class BridgeObject
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
    }
}

/// <summary>
/// Native .blend support is implemented as a source conversion stage. Blender remains
/// authoritative for reading .blend; TACTIX asks Blender to emit GLB, which then enters
/// the normal GLTF importer. This avoids reverse-engineering Blender's private file format.
/// </summary>
public sealed class BlenderSourceConverter : ISourceAssetConverter
{
    public string Id => "tactix.blender";
    public int Version => 2;
    public AssetImportCapability Capability => AssetImportCapability.ExternalTool;
    public IReadOnlyCollection<string> Extensions { get; } = [".blend"];
    public string OutputExtension => ".glb";

    public SourceConversionResult Convert(SourceConversionRequest request)
    {
        if (!File.Exists(request.SourcePath))
            return new(false, Id, "", $"Blender source not found: {request.SourcePath}");

        var blender = FindBlenderExecutable();
        if (blender is null)
            return new(false, Id, "", "Blender is required for native .blend import. Install Blender or set TACTIX_BLENDER_PATH.");

        Directory.CreateDirectory(request.OutputDirectory);
        var output = Path.Combine(request.OutputDirectory, Path.GetFileNameWithoutExtension(request.SourcePath) + ".glb");
        var script = ResolveBridgeScript();
        if (script is null)
            return new(false, Id, "", "TACTIX Blender bridge script could not be located.");

        var psi = new ProcessStartInfo
        {
            FileName = blender,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--background");
        psi.ArgumentList.Add(request.SourcePath);
        psi.ArgumentList.Add("--python");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add(output);
        psi.ArgumentList.Add("--all");

        using var process = Process.Start(psi);
        if (process is null)
            return new(false, Id, "", "Failed to start Blender.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 || !File.Exists(output))
        {
            var message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return new(false, Id, "", $"Blender conversion failed (exit {process.ExitCode}): {message.Trim()}");
        }

        return new(true, Id, output, "Converted .blend to GLB for the TACTIX GLTF importer.");
    }

    private static string? FindBlenderExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("TACTIX_BLENDER_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        string[] candidates =
        [
            "/Applications/Blender.app/Contents/MacOS/Blender",
            "/Applications/Blender 4.5.app/Contents/MacOS/Blender",
            "/Applications/Blender 4.4.app/Contents/MacOS/Blender",
            "/Applications/Blender 4.3.app/Contents/MacOS/Blender"
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? ResolveBridgeScript()
    {
        var projectRoot = Environment.GetEnvironmentVariable("TACTIX_PROJECT_ROOT");
        string[] candidates =
        [
            string.IsNullOrWhiteSpace(projectRoot) ? "" : Path.Combine(projectRoot, "Tools", "Blender", "exporttotactix.py"),
            Path.Combine(AppContext.BaseDirectory, "Tools", "Blender", "exporttotactix.py"),
            Path.Combine(Directory.GetCurrentDirectory(), "Tools", "Blender", "exporttotactix.py")
        ];
        return candidates.Where(path => !string.IsNullOrWhiteSpace(path)).FirstOrDefault(File.Exists);
    }
}
