using AppKit;
using CoreGraphics;
using Metal;
using TACTIX.Editor.UI.Docking;
using TACTIX.Editor.UI.Panels;
using TACTIX.Editor.UI.Viewport;
using TACTIX.Editor.UI.Content;
using TACTIX.Editor.AI;
using TACTIX.Engine.AI;
using TACTIX.Engine.Runtime.Scene;
using TactixScene = TACTIX.Engine.Runtime.Scene.Scene;
using TACTIX.Editor.Scene;
using TACTIX.Editor.UI.Hierarchy;
using TACTIX.Editor.UI.Inspector;
using TACTIX.Engine.Assets.Database;

namespace TACTIX.Editor.Tools;

public sealed class SceneEditorTool : EditorTool
{
    private readonly IMTLDevice _device;
    private readonly AIBridgeServer _aiBridge;
    private readonly TactixScene _scene;
    private readonly EditorSelection _selection;
    private readonly EditorCommandStack _commands;
    private readonly AssetDatabase _assets;

    public override string ToolId => "SceneEditor";
    public ViewportPanelView Viewport { get; }

    public SceneEditorTool(IMTLDevice device, AIBridgeServer aiBridge, TactixScene scene, EditorSelection selection, EditorCommandStack commands, AssetDatabase assets)
    {
        _device = device;
        _aiBridge = aiBridge;
        _scene = scene;
        _selection = selection;
        _commands = commands;
        _assets = assets;
        Viewport = new ViewportPanelView(new CGRect(0, 0, 640, 360), _device, _scene.World, _selection, _commands);
    }

    public override void RegisterPanels(DockManager dockManager)
    {
        dockManager.RegisterPanel(new DockPanel("Scene.Hierarchy", "Hierarchy",
            () => new HierarchyPanelView(new CGRect(0,0,320,240), _scene, _selection, _commands, _assets)));

        dockManager.RegisterPanel(new DockPanel("Scene.Viewport", "Scene",
            () => Viewport));

        dockManager.RegisterPanel(new DockPanel("Scene.Inspector", "Inspector",
            () => new InspectorPanelView(new CGRect(0,0,320,240), _scene.World, _selection, _commands)));

        dockManager.RegisterPanel(new DockPanel("Scene.Content", "Project",
            () => new ContentBrowserPanelView(new CGRect(0,0,720,240), _assets.ProjectRoot)));

        dockManager.RegisterPanel(new DockPanel("Scene.Console", "Console",
            () => Placeholder("Console coming later")));

        // Keep the AI Bridge available as a dockable editor tool, but do not occupy
        // the primary game-authoring workspace with it by default.
        dockManager.RegisterPanel(new DockPanel("Scene.AIBridge", "AI Bridge",
            () => new AIBridgePanelView(new CGRect(0, 0, 520, 260), _aiBridge)));
    }

    public override DockNode CreateDefaultLayout()
    {
        // V2 IDs intentionally reset the old saved split ratios so existing users
        // receive the new professional workspace on first launch after this change.
        // The layout follows the familiar game-editor convention:
        // Hierarchy | Scene | Inspector, with Project/Console below the Scene.
        var hierarchy = new TabDockNode("SceneV2.Left", new[] { "Scene.Hierarchy" });
        var viewport = new TabDockNode("SceneV2.ViewportDock", new[] { "Scene.Viewport" });
        var bottom = new TabDockNode("SceneV2.Bottom", new[] { "Scene.Content", "Scene.Console" }, "Scene.Content");
        var inspector = new TabDockNode("SceneV2.Right", new[] { "Scene.Inspector" });

        // Give the Scene view the majority of vertical space while leaving a useful
        // Project/Console strip that can be resized through the existing dock system.
        var center = new SplitDockNode("SceneV2.CenterVertical", vertical: false, ratio: 0.76, viewport, bottom);

        // Inspector gets a stable professional-editor width instead of feeling like
        // a narrow debug sidebar.
        var centerAndRight = new SplitDockNode("SceneV2.CenterRight", vertical: true, ratio: 0.72, center, inspector);

        // Keep Hierarchy compact so the viewport remains the visual center of TACTIX.
        return new SplitDockNode("SceneV2.Root", vertical: true, ratio: 0.17, hierarchy, centerAndRight);
    }

    private static NSView Placeholder(string message)
        => new PlaceholderPanelView(new CGRect(0, 0, 320, 240), message);
}
