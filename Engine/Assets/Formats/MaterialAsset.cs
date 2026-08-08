using TACTIX.Engine.Assets.Database;

namespace TACTIX.Engine.Assets.Formats;

public readonly record struct MaterialAsset(
    AssetGuid Guid,
    float BaseColorR,
    float BaseColorG,
    float BaseColorB,
    float BaseColorA,
    float Metallic,
    float Roughness,
    AssetGuid? BaseColorTextureGuid
)
{
    public static MaterialAsset DefaultLit()
        => new(AssetGuid.New(), 1f, 1f, 1f, 1f, 0.0f, 0.5f, null);
}
