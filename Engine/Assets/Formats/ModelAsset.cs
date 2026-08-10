using TACTIX.Engine.Assets.Database;

namespace TACTIX.Engine.Assets.Formats;

public readonly record struct ModelAsset(
    AssetGuid Guid,
    string InterchangeProjectPath,
    string ImportMetadataProjectPath,
    string SourcePath,
    string SourceHash,
    string InterchangeHash,
    string InterchangeFormat,
    int ObjectCount
);
