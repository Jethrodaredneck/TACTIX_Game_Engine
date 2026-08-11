using AppKit;
using CoreGraphics;
using TACTIX.Editor.Scene;
using TACTIX.Editor.Terrain;
using TACTIX.Editor.UI.Theme;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Runtime.ECS;
using TACTIX.Engine.Runtime.Scene;
using TactixScene = TACTIX.Engine.Runtime.Scene.Scene;

namespace TACTIX.Editor.UI.Hierarchy;

public sealed class HierarchyPanelView : NSView
{
    private readonly TactixScene _scene;
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly EditorCommandStack _commands;
    private readonly AssetDatabase _assets;
    private readonly string _scenePath;
    private readonly NSStackView _stack;
    private readonly NSTextField _sceneTitle;
    private readonly NSPopUpButton _createMenu;
    private readonly NSStackView _actions;
    private readonly NSStackView _fileActions;

    public HierarchyPanelView(CGRect frame, TactixScene scene, EditorSelection selection, EditorCommandStack commands, AssetDatabase assets) : base(frame)
    {
        _scene = scene;
        _world = scene.World;
        _selection = selection;
        _commands = commands;
        _assets = assets;
        _scenePath = Path.Combine(assets.ProjectRoot, "Assets", "Scenes", "Main.tactixscene");
        EditorTheme.ApplyPanel(this);

        _sceneTitle = EditorTheme.Label(scene.Name, 12, false, true);
        AddSubview(_sceneTitle);

        _createMenu = new NSPopUpButton(new CGRect(0, 0, 150, 26), true)
        {
            PullsDown = true,
            Menu = BuildCreateMenu()
        };
        EditorTheme.StyleButton(_createMenu, true);
        AddSubview(_createMenu);

        _actions = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Alignment = NSLayoutAttribute.CenterY,
            Spacing = 4
        };
        AddButton(_actions, "Duplicate", Duplicate);
        AddButton(_actions, "Delete", Delete);
        AddButton(_actions, "Undo", () => _commands.Undo());
        AddButton(_actions, "Redo", () => _commands.Redo());
        AddSubview(_actions);

        _fileActions = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Alignment = NSLayoutAttribute.CenterY,
            Spacing = 4
        };
        AddButton(_fileActions, "Save", Save, true);
        AddButton(_fileActions, "Load", Load);
        AddSubview(_fileActions);

        _stack = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 2
        };
        AddSubview(_stack);

        _sceneTitle.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        _createMenu.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        _actions.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        _fileActions.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        _stack.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;

        // Native right-click menu mirrors the same categorized creation surface.
        Menu = BuildHierarchyContextMenu();

        _world.Changed += Refresh;
        _selection.Changed += Refresh;
        _commands.Changed += Refresh;
        Refresh();
    }

    public override void Layout()
    {
        base.Layout();
        var width = Math.Max(120, Bounds.Width - 16);
        _sceneTitle.Frame = new CGRect(10, Bounds.Height - 27, width, 18);
        _createMenu.Frame = new CGRect(8, Bounds.Height - 62, width, 27);
        _actions.Frame = new CGRect(8, Bounds.Height - 94, width, 25);
        _fileActions.Frame = new CGRect(8, Bounds.Height - 124, width, 25);
        _stack.Frame = new CGRect(6, 6, Math.Max(100, Bounds.Width - 12), Math.Max(70, Bounds.Height - 136));
    }

    private NSMenu BuildCreateMenu()
    {
        var menu = new NSMenu("Create");
        menu.AddItem(new NSMenuItem("Create"));
        menu.AddItem(NSMenuItem.SeparatorItem);

        var objects = new NSMenu("3D Objects");
        AddMenuAction(objects, "Cube", () => Create(BuiltInMesh.Cube, "Cube"));
        AddMenuAction(objects, "Sphere", () => Create(BuiltInMesh.Sphere, "Sphere"));
        AddMenuAction(objects, "Capsule", () => Create(BuiltInMesh.Capsule, "Capsule"));
        AddMenuAction(objects, "Cylinder", () => Create(BuiltInMesh.Cylinder, "Cylinder"));
        AddMenuAction(objects, "Cone", () => Create(BuiltInMesh.Cone, "Cone"));
        AddMenuAction(objects, "Plane", () => Create(BuiltInMesh.Plane, "Plane"));
        AddSubmenu(menu, "3D Objects", objects);

        var environment = new NSMenu("Environment");
        AddMenuAction(environment, "Terrain", CreateTerrain);
        AddSubmenu(menu, "Environment", environment);

        var lights = new NSMenu("Lights");
        AddMenuAction(lights, "Directional Light", () => CreateLight(LightType.Directional));
        AddMenuAction(lights, "Point Light", () => CreateLight(LightType.Point));
        AddMenuAction(lights, "Spot Light", () => CreateLight(LightType.Spot));
        AddSubmenu(menu, "Lights", lights);

        return menu;
    }

    private NSMenu BuildHierarchyContextMenu()
    {
        var menu = new NSMenu("Hierarchy");
        var create = new NSMenu("Create");

        var objects = new NSMenu("3D Objects");
        AddMenuAction(objects, "Cube", () => Create(BuiltInMesh.Cube, "Cube"));
        AddMenuAction(objects, "Sphere", () => Create(BuiltInMesh.Sphere, "Sphere"));
        AddMenuAction(objects, "Capsule", () => Create(BuiltInMesh.Capsule, "Capsule"));
        AddMenuAction(objects, "Cylinder", () => Create(BuiltInMesh.Cylinder, "Cylinder"));
        AddMenuAction(objects, "Cone", () => Create(BuiltInMesh.Cone, "Cone"));
        AddMenuAction(objects, "Plane", () => Create(BuiltInMesh.Plane, "Plane"));
        AddSubmenu(create, "3D Objects", objects);

        var environment = new NSMenu("Environment");
        AddMenuAction(environment, "Terrain", CreateTerrain);
        AddSubmenu(create, "Environment", environment);

        var lights = new NSMenu("Lights");
        AddMenuAction(lights, "Directional Light", () => CreateLight(LightType.Directional));
        AddMenuAction(lights, "Point Light", () => CreateLight(LightType.Point));
        AddMenuAction(lights, "Spot Light", () => CreateLight(LightType.Spot));
        AddSubmenu(create, "Lights", lights);

        AddSubmenu(menu, "Create", create);
        menu.AddItem(NSMenuItem.SeparatorItem);
        AddMenuAction(menu, "Duplicate", Duplicate);
        AddMenuAction(menu, "Delete", Delete);
        return menu;
    }

    private static void AddSubmenu(NSMenu parent, string title, NSMenu submenu)
    {
        var item = new NSMenuItem(title) { Submenu = submenu };
        parent.AddItem(item);
    }

    private static void AddMenuAction(NSMenu menu, string title, Action action)
    {
        var item = new NSMenuItem(title);
        item.Activated += (_, _) => action();
        menu.AddItem(item);
    }

    private static void AddButton(NSStackView stack, string title, Action action, bool accent = false)
    {
        var button = new NSButton(new CGRect(0, 0, 72, 22)) { Title = title };
        EditorTheme.StyleButton(button, accent);
        button.Activated += (_, _) => action();
        stack.AddArrangedSubview(button);
    }

    private void Create(BuiltInMesh mesh, string name) => _commands.Execute(new CreatePrimitiveCommand(_world, _selection, mesh, name));
    private void CreateTerrain() => _commands.Execute(new CreateTerrainCommand(_world, _selection, _assets));
    private void CreateLight(LightType type) => _commands.Execute(new CreateLightCommand(_world, _selection, type));
    private void Duplicate() { var entity = _selection.ActiveEntity; if (entity.HasValue && _world.Exists(entity.Value)) _commands.Execute(new DuplicateEntityCommand(_world, _selection, entity.Value)); }
    private void Delete() { var entity = _selection.ActiveEntity; if (entity.HasValue && _world.Exists(entity.Value)) _commands.Execute(new DeleteEntityCommand(_world, _selection, entity.Value)); }
    private void Save() => SceneSerializer.Save(_scene, _scenePath);
    private void Load() { SceneSerializer.LoadInto(_scene, _scenePath); _selection.Select(_world.Entities.Count > 0 ? _world.Entities[0] : null); }

    private void Refresh()
    {
        foreach (var view in _stack.ArrangedSubviews.ToArray())
        {
            _stack.RemoveArrangedSubview(view);
            view.RemoveFromSuperview();
            view.Dispose();
        }

        foreach (var entity in _world.Entities)
        {
            var name = _world.Has<NameComponent>(entity) ? _world.Get<NameComponent>(entity).Name : $"Entity {entity.Id}";
            var icon = _world.Has<LightComponent>(entity) ? "☀" : _world.Has<TerrainComponent>(entity) ? "▦" : _world.Has<MeshRendererComponent>(entity) ? "◆" : "•";
            var type = _world.Has<LightComponent>(entity) ? _world.Get<LightComponent>(entity).Type.ToString() : _world.Has<TerrainComponent>(entity) ? "Terrain" : "";
            var selected = _selection.ActiveEntity == entity;
            var button = new NSButton(new CGRect(0, 0, 240, 25))
            {
                Title = $"{icon}  {name}" + (string.IsNullOrEmpty(type) ? "" : $"    {type}"),
                BezelStyle = NSBezelStyle.Inline,
                Alignment = NSTextAlignment.Left,
                Font = selected ? NSFont.BoldSystemFontOfSize(11) : NSFont.SystemFontOfSize(11),
                ContentTintColor = selected ? EditorTheme.Accent : EditorTheme.Text
            };
            var copy = entity;
            button.Activated += (_, _) => _selection.Select(copy);
            _stack.AddArrangedSubview(button);
        }
    }
}
