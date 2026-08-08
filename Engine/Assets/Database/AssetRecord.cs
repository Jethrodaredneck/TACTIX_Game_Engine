namespace TACTIX.Engine.Assets.Database;

public sealed record AssetRecord(
    AssetGuid Guid,
    AssetType Type,
    string Name,
    string SourcePath,
    string ArtifactPath,
    DateTimeOffset ImportedAtUtc
);
