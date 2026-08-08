using System.Text.Json.Serialization;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;

namespace TACTIX.Engine.Assets.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(AssetFile))]
[JsonSerializable(typeof(AssetMeta))]
[JsonSerializable(typeof(AssetGuid))]
[JsonSerializable(typeof(MeshAsset))]
[JsonSerializable(typeof(MaterialAsset))]
internal partial class AssetJsonContext : JsonSerializerContext
{
}
