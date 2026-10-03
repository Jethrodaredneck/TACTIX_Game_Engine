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
)
{
    // Format-independent imported hierarchy. GLB/FBX/USD importers all target this shape.
    public ModelNodeAsset[] Nodes { get; init; } = [];
}

public readonly record struct ModelNodeAsset(
    string Name,
    int ParentIndex,
    AssetGuid? MeshGuid,
    float[] Translation,
    float[] RotationEulerDegrees,
    float[] Scale
);
