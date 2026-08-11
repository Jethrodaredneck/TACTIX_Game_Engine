using System.Numerics;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;
using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Editor.Scene;

/// <summary>Undoable instantiation of a generic ModelAsset hierarchy into Scene entities.</summary>
public sealed class CreateModelInstanceCommand : IEditorCommand
{
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly AssetDatabase _assets;
    private readonly AssetGuid _modelGuid;
    private readonly string _name;
    private readonly List<int> _entityIds = new();

    public CreateModelInstanceCommand(World world, EditorSelection selection, AssetDatabase assets, AssetGuid modelGuid, string name)
    {
        _world = world;
        _selection = selection;
        _assets = assets;
        _modelGuid = modelGuid;
        _name = string.IsNullOrWhiteSpace(name) ? "Model" : name;
    }

    public void Execute()
    {
        var model = _assets.LoadModel(_modelGuid);
        if (model.Nodes.Length == 0) return;

        var root = _entityIds.Count > 0 ? _world.CreateEntityWithId(_entityIds[0]) : _world.CreateEntity();
        if (_entityIds.Count == 0) _entityIds.Add(root.Id);
        _world.Add(root, new NameComponent(_name));
        _world.Add(root, TransformComponent.Identity);

        var nodeEntities = new Entity[model.Nodes.Length];
        var worldMatrices = new Matrix4x4[model.Nodes.Length];

        for (var i = 0; i < model.Nodes.Length; i++)
        {
            var idSlot = i + 1;
            var entity = idSlot < _entityIds.Count ? _world.CreateEntityWithId(_entityIds[idSlot]) : _world.CreateEntity();
            if (idSlot >= _entityIds.Count) _entityIds.Add(entity.Id);
            nodeEntities[i] = entity;

            var node = model.Nodes[i];
            _world.Add(entity, new NameComponent(string.IsNullOrWhiteSpace(node.Name) ? $"Node {i}" : node.Name));

            var local = LocalMatrix(node);
            var parentWorld = node.ParentIndex >= 0 && node.ParentIndex < i ? worldMatrices[node.ParentIndex] : Matrix4x4.Identity;
            var world = local * parentWorld;
            worldMatrices[i] = world;
            _world.Add(entity, ToTransform(world));

            var parentEntityId = node.ParentIndex >= 0 && node.ParentIndex < nodeEntities.Length && nodeEntities[node.ParentIndex].Id != 0
                ? nodeEntities[node.ParentIndex].Id
                : root.Id;
            _world.Add(entity, new ParentComponent(parentEntityId));

            if (node.MeshGuid.HasValue && node.MeshGuid.Value.Value != Guid.Empty)
            {
                AssetGuid? defaultMaterial = null;
                try { defaultMaterial = _assets.LoadMesh(node.MeshGuid.Value).DefaultMaterialGuid; } catch { }
                _world.Add(entity, new MeshRendererComponent(node.MeshGuid.Value, defaultMaterial));
            }
        }

        _selection.Select(root);
    }

    public void Undo()
    {
        for (var i = _entityIds.Count - 1; i >= 0; i--)
        {
            var entity = new Entity(_entityIds[i]);
            if (_world.Exists(entity)) _world.DestroyEntity(entity);
        }
        _selection.Select(null);
    }

    private static Matrix4x4 LocalMatrix(ModelNodeAsset node)
    {
        var t = node.Translation.Length >= 3 ? new Vector3(node.Translation[0], node.Translation[1], node.Translation[2]) : Vector3.Zero;
        var r = node.RotationEulerDegrees.Length >= 3 ? new Vector3(node.RotationEulerDegrees[0], node.RotationEulerDegrees[1], node.RotationEulerDegrees[2]) : Vector3.Zero;
        var s = node.Scale.Length >= 3 ? new Vector3(node.Scale[0], node.Scale[1], node.Scale[2]) : Vector3.One;
        const float d2r = MathF.PI / 180f;
        return Matrix4x4.CreateScale(s)
             * Matrix4x4.CreateRotationX(r.X * d2r)
             * Matrix4x4.CreateRotationY(r.Y * d2r)
             * Matrix4x4.CreateRotationZ(r.Z * d2r)
             * Matrix4x4.CreateTranslation(t);
    }

    private static TransformComponent ToTransform(Matrix4x4 matrix)
    {
        if (!Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation))
            return TransformComponent.Identity;
        var euler = QuaternionToEulerDegrees(rotation);
        return new TransformComponent { Position = translation, Rotation = euler, Scale = scale };
    }

    private static Vector3 QuaternionToEulerDegrees(Quaternion q)
    {
        var sinr = 2f * (q.W * q.X + q.Y * q.Z);
        var cosr = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        var x = MathF.Atan2(sinr, cosr);
        var sinp = 2f * (q.W * q.Y - q.Z * q.X);
        var y = MathF.Abs(sinp) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinp) : MathF.Asin(sinp);
        var siny = 2f * (q.W * q.Z + q.X * q.Y);
        var cosy = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        var z = MathF.Atan2(siny, cosy);
        const float r2d = 57.29577951308232f;
        return new Vector3(x * r2d, y * r2d, z * r2d);
    }
}
