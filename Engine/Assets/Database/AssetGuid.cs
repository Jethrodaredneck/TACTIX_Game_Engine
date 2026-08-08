using System;

namespace TACTIX.Engine.Assets.Database;

public readonly record struct AssetGuid(Guid Value)
{
    public static AssetGuid New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");

    public static bool TryParse(string s, out AssetGuid guid)
    {
        if (Guid.TryParse(s, out var g))
        {
            guid = new AssetGuid(g);
            return true;
        }
        guid = default;
        return false;
    }

    public static AssetGuid Parse(string s) => new(Guid.Parse(s));
}
