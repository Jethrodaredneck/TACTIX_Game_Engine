using System.Numerics;
using System.Text.Json;
using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Engine.Runtime.Scene;

public static class SceneSerializer
{
    private sealed class SceneFile { public int Version { get; set; } = 1; public string Name { get; set; } = "Main"; public List<EntityFile> Entities { get; set; } = new(); }
    private sealed class EntityFile
    {
        public int Id { get; set; }
        public string Name { get; set; } = "Entity";
        public float[] Position { get; set; } = [0,0,0];
        public float[] Rotation { get; set; } = [0,0,0];
        public float[] Scale { get; set; } = [1,1,1];
        public BuiltInMesh? Mesh { get; set; }
        public string Material { get; set; } = "TACTIX_DefaultPrimitive";
    }

    public static void Save(Scene scene, string path)
    {
        var file=new SceneFile{Name=scene.Name};
        foreach(var e in scene.World.Entities)
        {
            var f=new EntityFile{Id=e.Id};
            if(scene.World.Has<NameComponent>(e))f.Name=scene.World.Get<NameComponent>(e).Name;
            if(scene.World.Has<TransformComponent>(e))
            {
                var t=scene.World.Get<TransformComponent>(e);
                f.Position=[t.Position.X,t.Position.Y,t.Position.Z]; f.Rotation=[t.Rotation.X,t.Rotation.Y,t.Rotation.Z]; f.Scale=[t.Scale.X,t.Scale.Y,t.Scale.Z];
            }
            if(scene.World.Has<MeshRendererComponent>(e))
            {
                var m=scene.World.Get<MeshRendererComponent>(e); f.Mesh=m.Mesh; f.Material=m.Material;
            }
            file.Entities.Add(f);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,JsonSerializer.Serialize(file,new JsonSerializerOptions{WriteIndented=true}));
    }

    public static void LoadInto(Scene scene, string path)
    {
        if(!File.Exists(path))return;
        var file=JsonSerializer.Deserialize<SceneFile>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        if(file==null)return;
        scene.World.Clear();
        foreach(var f in file.Entities)
        {
            var e=scene.World.CreateEntityWithId(f.Id);
            scene.World.Add(e,new NameComponent(f.Name));
            var t=TransformComponent.Identity;
            if(f.Position.Length>=3)t.Position=new Vector3(f.Position[0],f.Position[1],f.Position[2]);
            if(f.Rotation.Length>=3)t.Rotation=new Vector3(f.Rotation[0],f.Rotation[1],f.Rotation[2]);
            if(f.Scale.Length>=3)t.Scale=new Vector3(f.Scale[0],f.Scale[1],f.Scale[2]);
            scene.World.Add(e,t);
            if(f.Mesh.HasValue)scene.World.Add(e,new MeshRendererComponent(f.Mesh.Value,f.Material));
        }
    }
}
