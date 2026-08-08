using System;

namespace TACTIX.Engine.Assets.Database;

public sealed class AssetMeta
{
    public required AssetGuid Guid { get; init; }
    public required AssetType Type { get; init; }

    // Virtual path inside the project (e.g. "Assets/Meshes/Ship.tasset")
    public required string ProjectPath { get; init; }

    public string Name { get; init; } = "";
    public string ContentHash { get; init; } = "";

    public DateTimeOffset ImportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}