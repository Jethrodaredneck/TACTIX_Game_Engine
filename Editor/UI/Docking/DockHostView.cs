using AppKit;
using CoreGraphics;
using Metal;
using TACTIX.Editor.Tools;
using TACTIX.Editor.UI.Viewport;
using TACTIX.Engine.Core.Logging;
using TACTIX.Engine.AI;
using TACTIX.Engine.Runtime.Scene;
using TactixScene = TACTIX.Engine.Runtime.Scene.Scene;
using TACTIX.Editor.Scene;
using TACTIX.Engine.Assets.Database;

namespace TACTIX.Editor.UI.Docking;

public sealed class DockHostView : NSView
{
    private readonly DockManager _dockManager = new();
    private readonly SceneEditorTool _sceneTool;
    private readonly NSView _layoutRoot;
    private bool _layoutApplied;

    public ViewportPanelView Viewport => _sceneTool.Viewport;

    public DockHostView(CGRect frame, IMTLDevice device, AIBridgeServer aiBridge, TactixScene scene, EditorSelection selection, EditorCommandStack commands, AssetDatabase assets) : base(frame)
    {
        WantsLayer = true;
        Layer!.BackgroundColor = NSColor.FromRgb(20, 20, 22).CGColor;

        _sceneTool = new SceneEditorTool(device, aiBridge, scene, selection, commands, assets);
        _sceneTool.RegisterPanels(_dockManager);

        _layoutRoot = _dockManager.Build(_sceneTool.CreateDefaultLayout(), Bounds);
        _layoutRoot.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        AddSubview(_layoutRoot);
    }

    public override void ViewDidMoveToWindow()
    {
        base.ViewDidMoveToWindow();

        if (Window != null && !_layoutApplied)
        {
            _layoutApplied = true;
            _dockManager.ApplySavedLayout();
            Log.Info("DockHostView: Stage 3 interactive dock tree attached");
        }
    }

    public void SaveLayout() => _dockManager.SaveLayout();
}