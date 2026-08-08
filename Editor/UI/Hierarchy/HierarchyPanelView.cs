using AppKit;
using CoreGraphics;
using TACTIX.Editor.Scene;
using TACTIX.Editor.Terrain;
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
    private readonly string _projectRoot;
    private readonly string _scenePath;
    private readonly NSStackView _stack;

    public HierarchyPanelView(CGRect frame, TactixScene scene, EditorSelection selection, EditorCommandStack commands, string projectRoot):base(frame)
    {
        _scene=scene; _world=scene.World; _selection=selection; _commands=commands; _projectRoot=projectRoot;
        _scenePath=Path.Combine(projectRoot,"Assets","Scenes","Main.tactixscene");
        WantsLayer=true;Layer!.BackgroundColor=NSColor.FromRgb(25,25,28).CGColor;

        var toolbar=new NSStackView(new CGRect(8,frame.Height-38,(nfloat)Math.Max(100.0,(double)frame.Width-16.0),30))
        {Orientation=NSUserInterfaceLayoutOrientation.Horizontal,Alignment=NSLayoutAttribute.CenterY,Spacing=4,AutoresizingMask=NSViewResizingMask.WidthSizable|NSViewResizingMask.MinYMargin};
        AddButton(toolbar,"+ Cube",()=>Create(BuiltInMesh.Cube,"Cube"));
        AddButton(toolbar,"+ Sphere",()=>Create(BuiltInMesh.Sphere,"Sphere"));
        AddButton(toolbar,"+ Plane",()=>Create(BuiltInMesh.Plane,"Plane"));
        AddButton(toolbar,"+ Terrain",CreateTerrain);
        AddSubview(toolbar);

        var actions=new NSStackView(new CGRect(8,frame.Height-70,(nfloat)Math.Max(100.0,(double)frame.Width-16.0),26))
        {Orientation=NSUserInterfaceLayoutOrientation.Horizontal,Alignment=NSLayoutAttribute.CenterY,Spacing=4,AutoresizingMask=NSViewResizingMask.WidthSizable|NSViewResizingMask.MinYMargin};
        AddButton(actions,"Dup",Duplicate); AddButton(actions,"Del",Delete); AddButton(actions,"Undo",()=>_commands.Undo()); AddButton(actions,"Redo",()=>_commands.Redo()); AddButton(actions,"Save",Save); AddButton(actions,"Load",Load);
        AddSubview(actions);

        _stack=new NSStackView(new CGRect(8,8,(nfloat)Math.Max(80.0,(double)frame.Width-16.0),(nfloat)Math.Max(80.0,(double)frame.Height-86.0)))
        {Orientation=NSUserInterfaceLayoutOrientation.Vertical,Alignment=NSLayoutAttribute.Leading,Spacing=3,AutoresizingMask=NSViewResizingMask.WidthSizable|NSViewResizingMask.HeightSizable};
        AddSubview(_stack);
        _world.Changed+=Refresh;_selection.Changed+=Refresh;_commands.Changed+=Refresh;Refresh();
    }

    private static void AddButton(NSStackView stack,string title,Action action)
    {
        var b=new NSButton(new CGRect(0,0,68,24)){Title=title,BezelStyle=NSBezelStyle.Rounded};b.Activated+=(_,_)=>action();stack.AddArrangedSubview(b);
    }
    private void Create(BuiltInMesh mesh,string name)=>_commands.Execute(new CreatePrimitiveCommand(_world,_selection,mesh,name));
    private void CreateTerrain()=>_commands.Execute(new CreateTerrainCommand(_world,_selection,_projectRoot));
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
            var b=new NSButton(new CGRect(0,0,220,24)){Title=(_selection.ActiveEntity==e?"● ":"  ")+name,BezelStyle=NSBezelStyle.Inline};var copy=e;b.Activated+=(_,_)=>_selection.Select(copy);_stack.AddArrangedSubview(b);
        }
    }
}
