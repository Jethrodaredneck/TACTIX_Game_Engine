using AppKit;
using CoreGraphics;
using TACTIX.Editor.Scene;
using TACTIX.Editor.Terrain;
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

    public HierarchyPanelView(CGRect frame, TactixScene scene, EditorSelection selection, EditorCommandStack commands, AssetDatabase assets):base(frame)
    {
        _scene=scene; _world=scene.World; _selection=selection; _commands=commands; _assets=assets;
        _scenePath=Path.Combine(assets.ProjectRoot,"Assets","Scenes","Main.tactixscene");
        WantsLayer=true;Layer!.BackgroundColor=NSColor.FromRgb(25,25,28).CGColor;

        var primitives=new NSStackView(new CGRect(8,frame.Height-38,(nfloat)Math.Max(100.0,(double)frame.Width-16.0),30))
        {Orientation=NSUserInterfaceLayoutOrientation.Horizontal,Alignment=NSLayoutAttribute.CenterY,Spacing=4,AutoresizingMask=NSViewResizingMask.WidthSizable|NSViewResizingMask.MinYMargin};
        AddButton(primitives,"+ Cube",()=>Create(BuiltInMesh.Cube,"Cube"));
        AddButton(primitives,"+ Sphere",()=>Create(BuiltInMesh.Sphere,"Sphere"));
        AddButton(primitives,"+ Plane",()=>Create(BuiltInMesh.Plane,"Plane"));
        AddButton(primitives,"+ Terrain",CreateTerrain);
        AddSubview(primitives);

        var lights=new NSStackView(new CGRect(8,frame.Height-68,(nfloat)Math.Max(100.0,(double)frame.Width-16.0),26))
        {Orientation=NSUserInterfaceLayoutOrientation.Horizontal,Alignment=NSLayoutAttribute.CenterY,Spacing=4,AutoresizingMask=NSViewResizingMask.WidthSizable|NSViewResizingMask.MinYMargin};
        AddButton(lights,"+ Sun",()=>CreateLight(LightType.Directional));
        AddButton(lights,"+ Point",()=>CreateLight(LightType.Point));
        AddButton(lights,"+ Spot",()=>CreateLight(LightType.Spot));
        AddSubview(lights);

        var actions=new NSStackView(new CGRect(8,frame.Height-98,(nfloat)Math.Max(100.0,(double)frame.Width-16.0),26))
        {Orientation=NSUserInterfaceLayoutOrientation.Horizontal,Alignment=NSLayoutAttribute.CenterY,Spacing=4,AutoresizingMask=NSViewResizingMask.WidthSizable|NSViewResizingMask.MinYMargin};
        AddButton(actions,"Dup",Duplicate); AddButton(actions,"Del",Delete); AddButton(actions,"Undo",()=>_commands.Undo()); AddButton(actions,"Redo",()=>_commands.Redo()); AddButton(actions,"Save",Save); AddButton(actions,"Load",Load);
        AddSubview(actions);

        _stack=new NSStackView(new CGRect(8,8,(nfloat)Math.Max(80.0,(double)frame.Width-16.0),(nfloat)Math.Max(80.0,(double)frame.Height-116.0)))
        {Orientation=NSUserInterfaceLayoutOrientation.Vertical,Alignment=NSLayoutAttribute.Leading,Spacing=3,AutoresizingMask=NSViewResizingMask.WidthSizable|NSViewResizingMask.HeightSizable};
        AddSubview(_stack);
        _world.Changed+=Refresh;_selection.Changed+=Refresh;_commands.Changed+=Refresh;Refresh();
    }

    private static void AddButton(NSStackView stack,string title,Action action)
    {
        var b=new NSButton(new CGRect(0,0,68,24)){Title=title,BezelStyle=NSBezelStyle.Rounded};b.Activated+=(_,_)=>action();stack.AddArrangedSubview(b);
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
            var suffix=_world.Has<LightComponent>(e)?$"  [{_world.Get<LightComponent>(e).Type}]":_world.Has<TerrainComponent>(e)?"  [Terrain]":"";
            var b=new NSButton(new CGRect(0,0,220,24)){Title=(_selection.ActiveEntity==e?"● ":"  ")+name+suffix,BezelStyle=NSBezelStyle.Inline};var copy=e;b.Activated+=(_,_)=>_selection.Select(copy);_stack.AddArrangedSubview(b);
        }
    }
}