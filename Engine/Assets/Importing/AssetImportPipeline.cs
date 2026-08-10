using TACTIX.Engine.Assets.Database;

namespace TACTIX.Engine.Assets.Importing;

public sealed class AssetImportPipeline
{
    private readonly AssetImporterRegistry _registry;

    public AssetImportPipeline(AssetImporterRegistry registry)
    {
        _registry = registry;
    }

    public static AssetImportPipeline CreateDefault()
    {
        var registry = BuiltInImporters.CreateDefault();
        registry.Register(new ObjMeshImporter());
        registry.Register(new MtlMaterialImporter());
        registry.Register(new TextureAssetImporter());
        return new AssetImportPipeline(registry);
    }

    public AssetImportResult Import(AssetDatabase database, AssetImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(database);
        var sourcePath = Path.GetFullPath(request.SourcePath);
        var settings = CopySettings(request.Settings);

        var converter = _registry.ResolveConverter(sourcePath);
        if (converter != null)
        {
            var outputDirectory = Path.Combine(database.ProjectRoot, "Imports");
            var conversion = converter.Convert(new SourceConversionRequest(sourcePath, outputDirectory, settings));
            if (!conversion.Success)
                return new(false, converter.Id, Array.Empty<ImportedAsset>(), conversion.Message);

            settings["authoritativeSourcePath"] = sourcePath;
            settings["converterId"] = converter.Id;
            settings["converterVersion"] = converter.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sourcePath = conversion.OutputPath;
        }

        var importer = _registry.ResolveImporter(sourcePath);
        if (importer == null)
            return AssetImportResult.NotImplemented("tactix.import", $"No importer registered for {Path.GetExtension(sourcePath)}.");

        return importer.Import(database, request with
        {
            SourcePath = sourcePath,
            Settings = settings
        });
    }

    public IReadOnlyList<AssetImportResult> ImportPending(AssetDatabase database, string destinationDirectory = "Assets/Models")
    {
        ArgumentNullException.ThrowIfNull(database);
        var importsRoot = Path.Combine(database.ProjectRoot, "Imports");
        if (!Directory.Exists(importsRoot))
            return Array.Empty<AssetImportResult>();

        var candidates = Directory.EnumerateFiles(importsRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(IsImportCandidate)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = new List<AssetImportResult>();
        foreach (var candidate in candidates)
        {
            results.Add(Import(database, new AssetImportRequest(candidate, destinationDirectory)));
        }

        return results;
    }

    public IReadOnlyCollection<string> SupportedSourceExtensions() => _registry.SupportedSourceExtensions();

    private bool IsImportCandidate(string path)
        => _registry.ResolveImporter(path) != null || _registry.ResolveConverter(path) != null;

    private static Dictionary<string, string> CopySettings(IReadOnlyDictionary<string, string>? settings)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (settings == null)
            return copy;

        foreach (var pair in settings)
            copy[pair.Key] = pair.Value;
        return copy;
    }
}
