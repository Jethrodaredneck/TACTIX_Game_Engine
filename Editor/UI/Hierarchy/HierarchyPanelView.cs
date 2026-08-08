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

    public HierarchyPanelView(CGRect frame, TactixScene scene, EditorSelection selection, EditorCommandStack commands, AssetDatabase assets):base(frame)
    {
        _scene=scene; _world=scene.World; _selection=selection; _commands=commands; _assets=assets;
        _scenePath=Path.Combine(assets.ProjectRoot,"Assets","Scenes","Main.tactixscene");
        EditorTheme.ApplyPanel(this);

        _sceneTitle = EditorTheme.Label(scene.Name, 12, false, true);
        AddSubview(_sceneTitle);

        var create = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Alignment = NSLayoutAttribute.CenterY,
            Spacing = 4
        };
        AddButton(create,"Cube",()=>Create(BuiltInMesh.Cube,"Cube"));
        AddButton(create,"Sphere",()=>Create(BuiltInMesh.Sphere,"Sphere"));
        AddButton(create,"Plane",()=>Create(BuiltInMesh.Plane,"Plane"));
        AddButton(create,"Terrain",CreateTerrain,true);
        AddSubview(create);

        var lights = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Alignment = NSLayoutAttribute.CenterY,
            Spacing = 4
        };
        AddButton(lights,"Sun",()=>CreateLight(LightType.Directional));
        AddButton(lights,"Point",()=>CreateLight(LightType.Point));
        AddButton(lights,"Spot",()=>CreateLight(LightType.Spot));
        AddSubview(lights);

        var actions = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Alignment = NSLayoutAttribute.CenterY,
            Spacing = 4
        };
        AddButton(actions,"Dup",Duplicate);
        AddButton(actions,"Del",Delete);
        AddButton(actions,"Undo",()=>_commands.Undo());
        AddButton(actions,"Redo",()=>_commands.Redo());
        AddSubview(actions);

        var fileActions = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Alignment = NSLayoutAttribute.CenterY,
            Spacing = 4
        };
        AddButton(fileActions,"Save",Save,true);
        AddButton(fileActions,"Load",Load);
        AddSubview(fileActions);

        _stack = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 2
        };
        AddSubview(_stack);

        _sceneTitle.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        create.AutoresizingMask = lights.AutoresizingMask = actions.AutoresizingMask = fileActions.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        _stack.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;

        _world.Changed+=Refresh; _selection.Changed+=Refresh; _commands.Changed+=Refresh;
        Refresh();
    }

    public override void Layout()
    {
        base.Layout();
        var w = Math.Max(120, Bounds.Width - 16);
        _sceneTitle.Frame = new CGRect(10, Bounds.Height - 27, w, 18);
        var stacks = Subviews.OfType<NSStackView>().Where(v => !ReferenceEquals(v, _stack)).ToArray();
        if (stacks.Length >= 4)
        {
            stacks[0].Frame = new CGRect(8, Bounds.Height - 59, w, 25);
            stacks[1].Frame = new CGRect(8, Bounds.Height - 88, w, 25);
            stacks[2].Frame = new CGRect(8, Bounds.Height - 117, w, 25);
            stacks[3].Frame = new CGRect(8, Bounds.Height - 146, w, 25);
        }
        _stack.Frame = new CGRect(6, 6, Math.Max(100, Bounds.Width - 12), Math.Max(70, Bounds.Height - 158));
    }

    private static void AddButton(NSStackView stack,string title,Action action,bool accent=false)
    {
        var b=new NSButton(new CGRect(0,0,62,22)){Title=title};
        EditorTheme.StyleButton(b, accent);
        b.Activated+=(_,_)=>action();
        stack.AddArrangedSubview(b);
    }

    private void Create(BuiltInMesh mesh,string name)=>_commands.Execute(new CreatePrimitiveCommand(_world,_selection,mesh,name));
    private void CreateTerrain()=>_commands.Execute(new CreateTerrainCommand(_world,_selection,_assets));
    private void CreateLight(LightType type)=>_commands.Execute(new CreateLightCommand(_world,_selection,type));
    private void Duplicate(){var e=_selection.ActiveEntity;if(e.HasValue&&_world.Exists(e.Value))_commands.Execute(new DuplicateEntityCommand(_world,_selection,e.Value));}
    private void Delete(){var e=_selection.ActiveEntity;if(e.HasValue&&_world.Exists(e.Value))_commands.Execute(new DeleteEntityCommand(_world,_selection,e.Value));}
    private void Save()=>SceneSerializer.Save(_scene,_scenePath);
    private void Load(){SceneSerializer.LoadInto(_scene,_scenePath);_selection.Select(_world.Entities.Count>0?_world.Entities[0]:null);}

    private void Refresh()
    {
        foreach(var v in _stack.ArrangedSubviews.ToArray()){_stack.RemoveArrangedSubview(v);v.RemoveFromSuperview();v.Dispose();}
        foreach(var e in _world.Entities)
        {
            var name=_world.Has<NameComponent>(e)?_world.Get<NameComponent>(e).Name:$"Entity {e.Id}";
            var icon=_world.Has<LightComponent>(e)?"☀":_world.Has<TerrainComponent>(e)?"▦":_world.Has<MeshRendererComponent>(e)?"◆":"•";
            var type=_world.Has<LightComponent>(e)?_world.Get<LightComponent>(e).Type.ToString():_world.Has<TerrainComponent>(e)?"Terrain":"";
            var selected=_selection.ActiveEntity==e;
            var b=new NSButton(new CGRect(0,0,240,25))
            {
                Title=$"{icon}  {name}" + (string.IsNullOrEmpty(type)?"":$"    {type}"),
                BezelStyle=NSBezelStyle.Inline,
                Alignment=NSTextAlignment.Left,
                Font=selected?NSFont.BoldSystemFontOfSize(11):NSFont.SystemFontOfSize(11),
                ContentTintColor=selected?EditorTheme.Accent:EditorTheme.Text
            };
            var copy=e;
            b.Activated+=(_,_)=>_selection.Select(copy);
            _stack.AddArrangedSubview(b);
        }
    }
}
