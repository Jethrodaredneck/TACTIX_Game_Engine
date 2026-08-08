using System;
using System.Collections.Generic;
using System.Linq;

namespace TACTIX.Engine.Assets.Database;

public sealed class AssetRegistry
{
    private readonly Dictionary<AssetGuid, AssetMeta> _byGuid = new();
    private readonly Dictionary<string, AssetGuid> _byPath =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<AssetMeta> All => _byGuid.Values;

    public bool TryGet(AssetGuid guid, out AssetMeta meta) => _byGuid.TryGetValue(guid, out meta!);

    public bool TryGetByPath(string projectPath, out AssetMeta meta)
    {
        if (_byPath.TryGetValue(NormPath(projectPath), out var g) && _byGuid.TryGetValue(g, out var m))
        {
            meta = m;
            return true;
        }
        meta = null!;
        return false;
    }

    public void Upsert(AssetMeta meta)
    {
        var norm = NormPath(meta.ProjectPath);

        // If the path existed for a different guid, remove old
        if (_byPath.TryGetValue(norm, out var existingGuid) && existingGuid.Value != meta.Guid.Value)
        {
            _byGuid.Remove(existingGuid);
        }

        _byGuid[meta.Guid] = meta;
        _byPath[norm] = meta.Guid;
    }

    public void Remove(AssetGuid guid)
    {
        if (_byGuid.TryGetValue(guid, out var meta))
        {
            _byGuid.Remove(guid);
            _byPath.Remove(NormPath(meta.ProjectPath));
        }
    }

    public IEnumerable<AssetMeta> ByType(AssetType type) => _byGuid.Values.Where(m => m.Type == type);

    private static string NormPath(string p) => p.Replace('\\', '/').Trim();
}