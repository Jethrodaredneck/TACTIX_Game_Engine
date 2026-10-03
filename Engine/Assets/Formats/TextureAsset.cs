using TACTIX.Engine.Assets.Database;

namespace TACTIX.Engine.Assets.Formats;

public readonly record struct TextureAsset(
    AssetGuid Guid,
    string ImageProjectPath,
    string SourcePath,
    string SourceHash,
    bool Srgb
);
