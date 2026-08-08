using TACTIX.Engine.Assets.Database;

namespace TACTIX.Engine.Assets.Importing;

public enum AssetImportCapability
{
    Native,
    ExternalTool,
    Planned
}

public sealed record AssetImportRequest(
    string SourcePath,
    string DestinationDirectory,
    IReadOnlyDictionary<string, string>? Settings = null
);

public sealed record ImportedAsset(
    AssetGuid Guid,
    AssetType Type,
    string ProjectPath,
    string Name
);

public sealed record AssetImportResult(
    bool Success,
    string ImporterId,
    IReadOnlyList<ImportedAsset> Assets,
    string Message
)
{
    public static AssetImportResult NotImplemented(string importerId, string message)
        => new(false, importerId, Array.Empty<ImportedAsset>(), message);
}

public interface IAssetImporter
{
    string Id { get; }
    int Version { get; }
    AssetImportCapability Capability { get; }
    IReadOnlyCollection<string> Extensions { get; }

    AssetImportResult Import(AssetDatabase database, AssetImportRequest request);
}

public interface ISourceAssetConverter
{
    string Id { get; }
    int Version { get; }
    AssetImportCapability Capability { get; }
    IReadOnlyCollection<string> Extensions { get; }
    string OutputExtension { get; }

    SourceConversionResult Convert(SourceConversionRequest request);
}

public sealed record SourceConversionRequest(
    string SourcePath,
    string OutputDirectory,
    IReadOnlyDictionary<string, string>? Settings = null
);

public sealed record SourceConversionResult(
    bool Success,
    string ConverterId,
    string OutputPath,
    string Message
);
