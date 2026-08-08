using System;
using System.IO;
using System.Text.Json;

namespace TACTIX.Engine.Assets.Serialization;

public static class JsonAssetSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = AssetJsonContext.Default,
        WriteIndented = true
    };

    public static void Save(string fullPath, AssetFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var tmp = fullPath + ".tmp";
        var json = JsonSerializer.Serialize(file, Options);

        File.WriteAllText(tmp, json);

        if (File.Exists(fullPath))
            File.Delete(fullPath);

        File.Move(tmp, fullPath, overwrite: true);
    }

    public static AssetFile Load(string fullPath)
    {
        var json = File.ReadAllText(fullPath);
        var file = JsonSerializer.Deserialize<AssetFile>(json, Options);
        if (file == null) throw new InvalidDataException("Asset JSON did not deserialize.");
        return file;
    }
}
