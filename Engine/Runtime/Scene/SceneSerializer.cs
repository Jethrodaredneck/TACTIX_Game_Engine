using System.Numerics;
using System.Text.Json;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Engine.Runtime.Scene;

public static class SceneSerializer
{
    private sealed class SceneFile
    {
        public int Version { get; set; } = 4;
        public string Name { get; set; } = "Main";
        public List<EntityFile> Entities { get; set; } = new();
    }

    private sealed class EntityFile
    {
        public int Id { get; set; }
        public string Name { get; set; } = "Entity";
        public float[] Position { get; set; } = [0,0,0];
        public float[] Rotation { get; set; } = [0,0,0];
        public float[] Scale { get; set; } = [1,1,1];
        public BuiltInMesh? Mesh { get; set; }
        public string MeshAssetGuid { get; set; } = "";
        public string Material { get; set; } = "TACTIX_DefaultPrimitive";
        public string TerrainAssetGuid { get; set; } = "";
        public LightFile? Light { get; set; }
    }

    private sealed class LightFile
    {
        public LightType Type { get; set; }
        public float[] Color { get; set; } = [1,1,1];
        public float Intensity { get; set; } = 1f;
        public float Range { get; set; } = 12f;
        public float InnerConeDegrees { get; set; } = 20f;
        public float OuterConeDegrees { get; set; } = 35f;
        public bool CastShadows { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public static void Save(Scene scene, string path)
    {
        var file = new SceneFile { Name = scene.Name };
        foreach (var entity in scene.World.Entities)
        {
            var entry = new EntityFile { Id = entity.Id };
            if (scene.World.Has<NameComponent>(entity)) entry.Name = scene.World.Get<NameComponent>(entity).Name;
            if (scene.World.Has<TransformComponent>(entity))
            {
                var t = scene.World.Get<TransformComponent>(entity);
                entry.Position = [t.Position.X,t.Position.Y,t.Position.Z];
                entry.Rotation = [t.Rotation.X,t.Rotation.Y,t.Rotation.Z];
                entry.Scale = [t.Scale.X,t.Scale.Y,t.Scale.Z];
            }
            if (scene.World.Has<MeshRendererComponent>(entity))
            {
                var mesh = scene.World.Get<MeshRendererComponent>(entity);
                if (mesh.UsesAssetMesh) entry.MeshAssetGuid = mesh.MeshAssetGuid.ToString();
                else entry.Mesh = mesh.Mesh;
                entry.Material = mesh.Material;
            }
            if (scene.World.Has<TerrainComponent>(entity))
                entry.TerrainAssetGuid = scene.World.Get<TerrainComponent>(entity).TerrainAssetGuid.ToString();
            if (scene.World.Has<LightComponent>(entity))
            {
                var light = scene.World.Get<LightComponent>(entity);
                entry.Light = new LightFile
                {
                    Type = light.Type,
                    Color = [light.Color.X, light.Color.Y, light.Color.Z],
                    Intensity = light.Intensity,
                    Range = light.Range,
                    InnerConeDegrees = light.InnerConeDegrees,
                    OuterConeDegrees = light.OuterConeDegrees,
                    CastShadows = light.CastShadows,
                    Enabled = light.Enabled
                };
            }
            file.Entities.Add(entry);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void LoadInto(Scene scene, string path)
    {
        if (!File.Exists(path)) return;
        var file = JsonSerializer.Deserialize<SceneFile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (file == null) return;
        scene.World.Clear();
        foreach (var entry in file.Entities)
        {
            var entity = scene.World.CreateEntityWithId(entry.Id);
            scene.World.Add(entity, new NameComponent(entry.Name));
            var transform = TransformComponent.Identity;
            if (entry.Position.Length >= 3) transform.Position = new Vector3(entry.Position[0],entry.Position[1],entry.Position[2]);
            if (entry.Rotation.Length >= 3) transform.Rotation = new Vector3(entry.Rotation[0],entry.Rotation[1],entry.Rotation[2]);
            if (entry.Scale.Length >= 3) transform.Scale = new Vector3(entry.Scale[0],entry.Scale[1],entry.Scale[2]);
            scene.World.Add(entity, transform);
            if (!string.IsNullOrWhiteSpace(entry.MeshAssetGuid) && AssetGuid.TryParse(entry.MeshAssetGuid, out var meshGuid))
                scene.World.Add(entity, new MeshRendererComponent(meshGuid, entry.Material));
            else if (entry.Mesh.HasValue)
                scene.World.Add(entity, new MeshRendererComponent(entry.Mesh.Value, entry.Material));
            if (!string.IsNullOrWhiteSpace(entry.TerrainAssetGuid) && AssetGuid.TryParse(entry.TerrainAssetGuid, out var terrainGuid))
                scene.World.Add(entity, new TerrainComponent(terrainGuid));
            if (entry.Light != null)
            {
                var light = new LightComponent(entry.Light.Type)
                {
                    Color = entry.Light.Color.Length >= 3 ? new Vector3(entry.Light.Color[0],entry.Light.Color[1],entry.Light.Color[2]) : Vector3.One,
                    Intensity = entry.Light.Intensity,
                    Range = entry.Light.Range,
                    InnerConeDegrees = entry.Light.InnerConeDegrees,
                    OuterConeDegrees = entry.Light.OuterConeDegrees,
                    CastShadows = entry.Light.CastShadows,
                    Enabled = entry.Light.Enabled
                };
                scene.World.Add(entity, light);
            }
        }
    }
}
