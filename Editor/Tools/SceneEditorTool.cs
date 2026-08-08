using AppKit;
using CoreGraphics;
using Metal;
using TACTIX.Editor.UI.Docking;
using TACTIX.Editor.UI.Panels;
using TACTIX.Editor.UI.Viewport;
using TACTIX.Editor.AI;
using TACTIX.Engine.AI;
using TACTIX.Engine.Runtime.Scene;
using TactixScene = TACTIX.Engine.Runtime.Scene.Scene;
using TACTIX.Editor.Scene;
using TACTIX.Editor.UI.Hierarchy;
using TACTIX.Editor.UI.Inspector;

namespace TACTIX.Editor.Tools;

/// <summary>
/// Main scene-editing tool. Its layout is declarative and independent from the
/// DockHostView so later tools can provide their own arrangements.
/// </summary>
public sealed class SceneEditorTool : EditorTool
{
    private readonly IMTLDevice _device;
    private readonly AIBridgeServer _aiBridge;
    private readonly TactixScene _scene;
    private readonly EditorSelection _selection;
    private readonly EditorCommandStack _commands;
    private readonly string _projectRoot;

    public override string ToolId => "SceneEditor";
    public ViewportPanelView Viewport { get; }

    public SceneEditorTool(IMTLDevice device, AIBridgeServer aiBridge, TactixScene scene, EditorSelection selection, EditorCommandStack commands, string projectRoot)
    {
        _device = device;
        _aiBridge = aiBridge;
        _scene = scene; _selection = selection; _commands = commands; _projectRoot = projectRoot;
        Viewport = new ViewportPanelView(new CGRect(0, 0, 640, 360), _device);
    }

    public override void RegisterPanels(DockManager dockManager)
    {
        dockManager.RegisterPanel(new DockPanel("Scene.Hierarchy", "Hierarchy",
            () => new HierarchyPanelView(new CGRect(0,0,320,240), _scene, _selection, _commands, _projectRoot)));

        dockManager.RegisterPanel(new DockPanel("Scene.Viewport", "Viewport",
            () => Viewport));

        dockManager.RegisterPanel(new DockPanel("Scene.Inspector", "Inspector",
            () => new InspectorPanelView(new CGRect(0,0,320,240), _scene.World, _selection, _commands)));

        dockManager.RegisterPanel(new DockPanel("Scene.Content", "Content Drawer",
            () => Placeholder("Project assets")));

        dockManager.RegisterPanel(new DockPanel("Scene.Console", "Console",
            () => Placeholder("Engine output")));

        dockManager.RegisterPanel(new DockPanel("Scene.AIBridge", "AI Bridge",
            () => new AIBridgePanelView(new CGRect(0, 0, 520, 260), _aiBridge)));
    }

    public override DockNode CreateDefaultLayout()
    {
        // Equivalent conceptually to Esoterica's per-tool docking layout: the tool
        // describes what belongs where, while DockManager owns AppKit realization.
        var hierarchy = new TabDockNode("Scene.Left", new[] { "Scene.Hierarchy" });
        var viewport = new TabDockNode("Scene.ViewportDock", new[] { "Scene.Viewport" });
        var bottom = new TabDockNode("Scene.Bottom", new[] { "Scene.Content", "Scene.Console", "Scene.AIBridge" }, "Scene.Content");
        var inspector = new TabDockNode("Scene.Right", new[] { "Scene.Inspector" });

        var center = new SplitDockNode("Scene.CenterVertical", vertical: false, ratio: 0.72, viewport, bottom);
        var centerAndRight = new SplitDockNode("Scene.CenterRight", vertical: true, ratio: 0.78, center, inspector);
        return new SplitDockNode("Scene.Root", vertical: true, ratio: 0.18, hierarchy, centerAndRight);
    }

    private static NSView Placeholder(string message)
        => new PlaceholderPanelView(new CGRect(0, 0, 320, 240), message);
}
