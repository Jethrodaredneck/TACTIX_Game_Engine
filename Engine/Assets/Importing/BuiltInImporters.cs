using System.Diagnostics;

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
        registry.Register(new PlannedInterchangeImporter("tactix.gltf", [".gltf", ".glb"]));
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

    public AssetImportResult Import(Database.AssetDatabase database, AssetImportRequest request)
        => AssetImportResult.NotImplemented(Id, $"{Id} recognizes {Path.GetExtension(request.SourcePath)}; parser integration is the next importer stage.");
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
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Tools", "Blender", "exporttotactix.py"),
            Path.Combine(Directory.GetCurrentDirectory(), "Tools", "Blender", "exporttotactix.py")
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}
