using AppKit;
using CoreGraphics;
using System.Globalization;
using TACTIX.Editor.Scene;
using TACTIX.Editor.UI.Theme;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Editor.UI.Inspector;

public sealed class InspectorPanelView : NSView
{
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly EditorCommandStack _commands;
    private readonly AssetDatabase _assets;
    private readonly NSTextField _title;
    private readonly NSTextField _subtitle;
    private readonly NSView _transformCard;
    private readonly NSTextField[] _transformFields = new NSTextField[9];
    private readonly NSView _componentCard;
    private readonly NSTextField _componentTitle;
    private readonly NSTextField _componentSummary;
    private readonly NSTextField[] _lightLabels = new NSTextField[4];
    private readonly NSTextField[] _lightFields = new NSTextField[4];
    private readonly NSTextField _materialLabel;
    private readonly NSPopUpButton _materialPopup;
    private readonly List<AssetGuid?> _materialChoices = new();
    private bool _refreshingMaterialPopup;

    public InspectorPanelView(CGRect frame, World world, EditorSelection selection, EditorCommandStack commands, AssetDatabase assets) : base(frame)
    {
        _world = world;
        _selection = selection;
        _commands = commands;
        _assets = assets;
        EditorTheme.ApplyPanel(this);

        _title = EditorTheme.Label("Nothing selected", 14, false, true);
        _subtitle = EditorTheme.Label("Select an entity in the Hierarchy or Scene", 10, true);
        AddSubview(_title);
        AddSubview(_subtitle);

        _transformCard = new NSView();
        EditorTheme.Card(_transformCard);
        AddSubview(_transformCard);
        var transformHeader = EditorTheme.Label("Transform", 12, false, true);
        transformHeader.Frame = new CGRect(12, 116, 220, 20);
        _transformCard.AddSubview(transformHeader);

        string[] rowNames = { "Position", "Rotation", "Scale" };
        string[] axisNames = { "X", "Y", "Z" };
        for (var row = 0; row < 3; row++)
        {
            var rowLabel = EditorTheme.Label(rowNames[row], 10, true);
            rowLabel.Frame = new CGRect(12, 86 - row * 34, 54, 18);
            _transformCard.AddSubview(rowLabel);
            for (var col = 0; col < 3; col++)
            {
                var index = row * 3 + col;
                var axis = EditorTheme.Label(axisNames[col], 9, true, true);
                axis.Frame = new CGRect(70 + col * 70, 88 - row * 34, 12, 16);
                _transformCard.AddSubview(axis);
                var field = new NSTextField(new CGRect(84 + col * 70, 84 - row * 34, 52, 22)) { StringValue = "0" };
                EditorTheme.StyleField(field);
                var captured = index;
                field.EditingEnded += (_, _) => ApplyTransform(captured);
                _transformFields[index] = field;
                _transformCard.AddSubview(field);
            }
        }

        _componentCard = new NSView();
        EditorTheme.Card(_componentCard);
        AddSubview(_componentCard);
        _componentTitle = EditorTheme.Label("Component", 12, false, true);
        _componentSummary = EditorTheme.Label("", 10, true);
        _componentCard.AddSubview(_componentTitle);
        _componentCard.AddSubview(_componentSummary);

        string[] lightNames = { "Intensity", "Range", "Inner Cone", "Outer Cone" };
        for (var i = 0; i < 4; i++)
        {
            var label = EditorTheme.Label(lightNames[i], 10, true);
            _lightLabels[i] = label;
            _componentCard.AddSubview(label);
            var field = new NSTextField { StringValue = "0" };
            EditorTheme.StyleField(field);
            var captured = i;
            field.EditingEnded += (_, _) => ApplyLight(captured);
            _lightFields[i] = field;
            _componentCard.AddSubview(field);
        }

        _materialLabel = EditorTheme.Label("Material", 10, true);
        _componentCard.AddSubview(_materialLabel);
        _materialPopup = new NSPopUpButton(new CGRect(0, 0, 180, 24), false);
        _materialPopup.Activated += (_, _) => ApplyMaterial();
        _componentCard.AddSubview(_materialPopup);

        _selection.Changed += Refresh;
        _world.Changed += Refresh;
        Refresh();
    }

    public override void Layout()
    {
        base.Layout();
        var width = Math.Max(240, Bounds.Width - 20);
        _title.Frame = new CGRect(12, Bounds.Height - 30, width - 8, 20);
        _subtitle.Frame = new CGRect(12, Bounds.Height - 49, width - 8, 16);
        _transformCard.Frame = new CGRect(10, Bounds.Height - 202, width, 142);
        _componentCard.Frame = new CGRect(10, Math.Max(10, Bounds.Height - 410), width, 196);
        _componentTitle.Frame = new CGRect(12, 164, width - 24, 20);
        _componentSummary.Frame = new CGRect(12, 144, width - 24, 17);
        _materialLabel.Frame = new CGRect(12, 108, 72, 18);
        _materialPopup.Frame = new CGRect(88, 104, Math.Max(120, width - 102), 24);
        for (var i = 0; i < 4; i++)
        {
            var y = 94 - i * 29;
            _lightLabels[i].Frame = new CGRect(12, y + 2, 90, 18);
            _lightFields[i].Frame = new CGRect(108, y, Math.Max(70, width - 122), 22);
        }
    }

    private void Refresh()
    {
        var selected = _selection.ActiveEntity;
        if (selected is null || !_world.Exists(selected.Value))
        {
            _title.StringValue = "Nothing selected";
            _subtitle.StringValue = "Select an entity in the Hierarchy or Scene";
            _transformCard.Hidden = true;
            _componentCard.Hidden = true;
            return;
        }

        var entity = selected.Value;
        _title.StringValue = _world.Has<NameComponent>(entity) ? _world.Get<NameComponent>(entity).Name : $"Entity {entity.Id}";
        _subtitle.StringValue = $"Entity {entity.Id}";
        _transformCard.Hidden = !_world.Has<TransformComponent>(entity);
        if (_world.Has<TransformComponent>(entity))
        {
            var t = _world.Get<TransformComponent>(entity);
            float[] values = { t.Position.X,t.Position.Y,t.Position.Z,t.Rotation.X,t.Rotation.Y,t.Rotation.Z,t.Scale.X,t.Scale.Y,t.Scale.Z };
            for (var i = 0; i < values.Length; i++) _transformFields[i].StringValue = values[i].ToString("0.###", CultureInfo.InvariantCulture);
        }

        SetAllLightControls(false);
        ShowMaterialControls(false);
        if (_world.Has<LightComponent>(entity))
        {
            var light = _world.Get<LightComponent>(entity);
            _componentCard.Hidden = false;
            _componentTitle.StringValue = $"Light  ·  {light.Type}";
            _componentSummary.StringValue = light.Enabled ? "Enabled" : "Disabled";
            float[] values = { light.Intensity, light.Range, light.InnerConeDegrees, light.OuterConeDegrees };
            for (var i = 0; i < 4; i++) _lightFields[i].StringValue = values[i].ToString("0.###", CultureInfo.InvariantCulture);
            ShowLight(0, true);
            ShowLight(1, light.Type != LightType.Directional);
            ShowLight(2, light.Type == LightType.Spot);
            ShowLight(3, light.Type == LightType.Spot);
        }
        else if (_world.Has<TerrainComponent>(entity))
        {
            _componentCard.Hidden = false;
            _componentTitle.StringValue = "Terrain";
            _componentSummary.StringValue = $"Asset  {_world.Get<TerrainComponent>(entity).TerrainAssetGuid}";
        }
        else if (_world.Has<MeshRendererComponent>(entity))
        {
            var mesh = _world.Get<MeshRendererComponent>(entity);
            _componentCard.Hidden = false;
            _componentTitle.StringValue = "Mesh Renderer";
            _componentSummary.StringValue = mesh.UsesAssetMesh ? $"Mesh Asset  {mesh.MeshAssetGuid}" : $"Primitive  {mesh.Mesh}";
            RefreshMaterialChoices(mesh.MaterialAssetGuid);
            ShowMaterialControls(true);
        }
        else _componentCard.Hidden = true;
    }

    private void RefreshMaterialChoices(AssetGuid current)
    {
        _refreshingMaterialPopup = true;
        _assets.ScanAssetsFolder();
        _materialPopup.RemoveAllItems();
        _materialChoices.Clear();
        _materialPopup.AddItem("Default Material");
        _materialChoices.Add(null);
        var selectedIndex = 0;
        foreach (var material in _assets.Registry.ByType(AssetType.Material).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            _materialPopup.AddItem(material.Name);
            _materialChoices.Add(material.Guid);
            if (current.Value != Guid.Empty && current == material.Guid) selectedIndex = _materialChoices.Count - 1;
        }
        _materialPopup.SelectItem(selectedIndex);
        _refreshingMaterialPopup = false;
    }

    private void ShowMaterialControls(bool visible)
    {
        _materialLabel.Hidden = !visible;
        _materialPopup.Hidden = !visible;
    }

    private void SetAllLightControls(bool visible)
    {
        for (var i = 0; i < 4; i++) ShowLight(i, visible);
    }

    private void ShowLight(int index, bool visible)
    {
        _lightLabels[index].Hidden = !visible;
        _lightFields[index].Hidden = !visible;
    }

    private void ApplyMaterial()
    {
        if (_refreshingMaterialPopup) return;
        var selected = _selection.ActiveEntity;
        if (selected is null || !_world.Has<MeshRendererComponent>(selected.Value)) return;
        var index = (int)_materialPopup.IndexOfSelectedItem;
        if (index < 0 || index >= _materialChoices.Count) return;
        var before = _world.Get<MeshRendererComponent>(selected.Value);
        var after = before;
        after.MaterialAssetGuid = _materialChoices[index] ?? default;
        _commands.Execute(new SetMeshRendererCommand(_world, selected.Value, before, after));
    }

    private void ApplyTransform(int index)
    {
        var selected = _selection.ActiveEntity;
        if (selected is null || !_world.Has<TransformComponent>(selected.Value)) return;
        if (!float.TryParse(_transformFields[index].StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) { Refresh(); return; }
        var before = _world.Get<TransformComponent>(selected.Value);
        var after = before;
        if (index < 3) { var v = after.Position; if (index == 0) v.X = value; else if (index == 1) v.Y = value; else v.Z = value; after.Position = v; }
        else if (index < 6) { var v = after.Rotation; if (index == 3) v.X = value; else if (index == 4) v.Y = value; else v.Z = value; after.Rotation = v; }
        else { var v = after.Scale; if (index == 6) v.X = value; else if (index == 7) v.Y = value; else v.Z = value; after.Scale = v; }
        _commands.Execute(new SetTransformCommand(_world, selected.Value, before, after));
    }

    private void ApplyLight(int index)
    {
        var selected = _selection.ActiveEntity;
        if (selected is null || !_world.Has<LightComponent>(selected.Value)) return;
        if (!float.TryParse(_lightFields[index].StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) { Refresh(); return; }
        var before = _world.Get<LightComponent>(selected.Value);
        var after = before;
        switch (index)
        {
            case 0: after.Intensity = MathF.Max(0, value); break;
            case 1: after.Range = MathF.Max(0.01f, value); break;
            case 2: after.InnerConeDegrees = Math.Clamp(value, 0, 179); break;
            case 3: after.OuterConeDegrees = Math.Clamp(value, 0, 179); break;
        }
        if (after.InnerConeDegrees > after.OuterConeDegrees) after.InnerConeDegrees = after.OuterConeDegrees;
        _commands.Execute(new SetLightCommand(_world, selected.Value, before, after));
    }
}
