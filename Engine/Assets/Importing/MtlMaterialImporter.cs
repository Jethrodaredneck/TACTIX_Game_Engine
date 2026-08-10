using System.Globalization;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Engine.Assets.Importing;

/// <summary>
/// Imports the common Wavefront MTL surface properties into native TACTIX MaterialAssets.
/// Texture references are preserved by the source MTL for the next texture-binding stage;
/// this importer currently maps color/opacity/specular hardness into the native material model.
/// </summary>
public sealed class MtlMaterialImporter : IAssetImporter
{
    public string Id => "tactix.mtl.native";
    public int Version => 1;
    public AssetImportCapability Capability => AssetImportCapability.Native;
    public IReadOnlyCollection<string> Extensions { get; } = [".mtl"];

    public AssetImportResult Import(AssetDatabase database, AssetImportRequest request)
    {
        if (!File.Exists(request.SourcePath))
            return new(false, Id, Array.Empty<ImportedAsset>(), $"MTL source not found: {request.SourcePath}");

        try
        {
            var materials = Parse(request.SourcePath);
            if (materials.Count == 0)
                return new(false, Id, Array.Empty<ImportedAsset>(), "MTL contains no material definitions (newmtl).");

            var destination = NormalizeDestination(request.DestinationDirectory);
            var imported = new List<ImportedAsset>();
            foreach (var parsed in materials)
            {
                var safeName = SafeName(parsed.Name);
                var projectPath = $"{destination}/{safeName}.tasset";
                database.Registry.TryGetByPath(projectPath, out var existing);
                var guid = existing?.Guid ?? AssetGuid.New();
                var material = new MaterialAsset(
                    guid,
                    parsed.R,
                    parsed.G,
                    parsed.B,
                    parsed.A,
                    0f,
                    parsed.Roughness,
                    null);
                var meta = database.SaveMaterial(projectPath, material, safeName);
                imported.Add(new ImportedAsset(meta.Guid, meta.Type, meta.ProjectPath, meta.Name));
            }

            return new(true, Id, imported, $"Imported {imported.Count} MTL material{(imported.Count == 1 ? "" : "s")}.");
        }
        catch (Exception ex)
        {
            return new(false, Id, Array.Empty<ImportedAsset>(), $"MTL import failed: {ex.Message}");
        }
    }

    private sealed class ParsedMaterial
    {
        public string Name = "Material";
        public float R = 1f, G = 1f, B = 1f, A = 1f;
        public float Roughness = 0.5f;
    }

    private static List<ParsedMaterial> Parse(string path)
    {
        var result = new List<ParsedMaterial>();
        ParsedMaterial? current = null;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "newmtl" when parts.Length >= 2:
                    current = new ParsedMaterial { Name = string.Join("_", parts.Skip(1)) };
                    result.Add(current);
                    break;
                case "Kd" when current != null && parts.Length >= 4:
                    current.R = F(parts[1]); current.G = F(parts[2]); current.B = F(parts[3]);
                    break;
                case "d" when current != null && parts.Length >= 2:
                    current.A = Math.Clamp(F(parts[1]), 0f, 1f);
                    break;
                case "Tr" when current != null && parts.Length >= 2:
                    current.A = 1f - Math.Clamp(F(parts[1]), 0f, 1f);
                    break;
                case "Ns" when current != null && parts.Length >= 2:
                    // Wavefront Ns is normally 0..1000. Convert specular hardness to a
                    // roughness-like value while keeping the mapping stable and bounded.
                    var ns = Math.Clamp(F(parts[1]), 0f, 1000f);
                    current.Roughness = Math.Clamp(MathF.Sqrt(2f / (ns + 2f)), 0.04f, 1f);
                    break;
            }
        }

        return result;
    }

    private static float F(string value) => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string NormalizeDestination(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination) || destination.Equals("Assets/Models", StringComparison.OrdinalIgnoreCase))
            return "Assets/Materials";
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
        return string.IsNullOrWhiteSpace(result) ? "ImportedMaterial" : result;
    }
}
