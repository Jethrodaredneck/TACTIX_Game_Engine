namespace TACTIX.Engine.Assets.Importing;

/// <summary>
/// Maps source formats to importers/converters without teaching AssetDatabase or
/// editor UI about individual file extensions. Public/plugin importers can register here later.
/// </summary>
public sealed class AssetImporterRegistry
{
    private readonly List<IAssetImporter> _importers = new();
    private readonly List<ISourceAssetConverter> _converters = new();

    public IReadOnlyList<IAssetImporter> Importers => _importers;
    public IReadOnlyList<ISourceAssetConverter> Converters => _converters;

    public void Register(IAssetImporter importer)
    {
        ArgumentNullException.ThrowIfNull(importer);
        if (_importers.Any(i => string.Equals(i.Id, importer.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Importer already registered: {importer.Id}");
        _importers.Add(importer);
    }

    public void Register(ISourceAssetConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        if (_converters.Any(i => string.Equals(i.Id, converter.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Source converter already registered: {converter.Id}");
        _converters.Add(converter);
    }

    public IAssetImporter? ResolveImporter(string path)
    {
        var extension = NormalizeExtension(Path.GetExtension(path));
        return _importers
            .Where(importer => importer.Extensions.Any(e => NormalizeExtension(e) == extension))
            .OrderBy(importer => importer.Capability == AssetImportCapability.Native ? 0 : importer.Capability == AssetImportCapability.ExternalTool ? 1 : 2)
            .FirstOrDefault();
    }

    public ISourceAssetConverter? ResolveConverter(string path)
    {
        var extension = NormalizeExtension(Path.GetExtension(path));
        return _converters.FirstOrDefault(converter => converter.Extensions.Any(e => NormalizeExtension(e) == extension));
    }

    public IReadOnlyCollection<string> SupportedSourceExtensions()
        => _importers.SelectMany(i => i.Extensions)
            .Concat(_converters.SelectMany(i => i.Extensions))
            .Select(NormalizeExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeExtension(string extension)
    {
        extension = extension.Trim().ToLowerInvariant();
        return extension.StartsWith('.') ? extension : "." + extension;
    }
}
