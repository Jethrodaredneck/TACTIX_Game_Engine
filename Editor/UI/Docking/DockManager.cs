using AppKit;
using CoreGraphics;
using Foundation;
using System.Runtime.InteropServices;
using TACTIX.Engine.Core.Logging;

namespace TACTIX.Editor.UI.Docking;

/// <summary>
/// Owns dock panels, materializes the declarative Stage 2 tree, and coordinates
/// Stage 3 tab dragging / floating / redocking.
/// </summary>
public sealed class DockManager
{
    private const string SplitPrefix = "TACTIX.Dock.Split.";
    private const string TabPrefix = "TACTIX.Dock.Tab.";

    private readonly Dictionary<string, DockPanel> _panels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (NSSplitView View, SplitDockNode Node)> _splits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DockTabGroupView> _tabGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<DockTabGroupView, NSWindow> _floatingWindows = new();

    private DockTabGroupView? _dragSource;
    private string? _dragPanelId;
    private DockTabGroupView? _dragTarget;
    private DockDropRegion _dragRegion;
    private int _dynamicId;

    public void RegisterPanel(DockPanel panel)
    {
        if (!_panels.TryAdd(panel.Id, panel))
            throw new InvalidOperationException($"Dock panel already registered: {panel.Id}");
    }

    public NSView Build(DockNode root, CGRect frame)
    {
        _splits.Clear();
        _tabGroups.Clear();
        return BuildNode(root, frame);
    }

    public void ApplySavedLayout()
    {
        var defaults = NSUserDefaults.StandardUserDefaults;

        foreach (var pair in _splits.Values)
        {
            var split = pair.View;
            var node = pair.Node;
            var saved = defaults.DoubleForKey(SplitPrefix + node.Id);
            var ratio = saved > 0.05 && saved < 0.95 ? saved : node.Ratio;
            var extent = node.Vertical ? split.Bounds.Width : split.Bounds.Height;

            if (extent > 1)
                split.SetPositionOfDivider((NFloat)(extent * ratio), IntPtr.Zero);
        }

        foreach (var pair in _tabGroups)
        {
            var selected = defaults.StringForKey(TabPrefix + pair.Key);
            if (!string.IsNullOrWhiteSpace(selected))
                pair.Value.Select(selected!);
        }
    }

    public void SaveLayout()
    {
        var defaults = NSUserDefaults.StandardUserDefaults;

        foreach (var pair in _splits.Values)
        {
            var split = pair.View;
            var node = pair.Node;
            if (split.Superview == null || split.Subviews.Length < 2)
                continue;

            var extent = node.Vertical ? split.Bounds.Width : split.Bounds.Height;
            var firstExtent = node.Vertical ? split.Subviews[0].Frame.Width : split.Subviews[0].Frame.Height;
            if (extent > 1)
                defaults.SetDouble(Math.Clamp(firstExtent / extent, 0.05, 0.95), SplitPrefix + node.Id);
        }

        foreach (var pair in _tabGroups)
        {
            if (pair.Value.Superview != null && !string.IsNullOrWhiteSpace(pair.Value.SelectedPanelId))
                defaults.SetString(pair.Value.SelectedPanelId!, TabPrefix + pair.Key);
        }
    }

    internal void BeginTabDrag(DockTabGroupView source, string panelId, NSEvent e)
    {
        if (!source.ContainsPanel(panelId))
            return;

        ClearDragVisuals();
        _dragSource = source;
        _dragPanelId = panelId;
        UpdateTabDrag(e);
        Log.Info($"Dock drag begin: {panelId}");
    }

    internal void UpdateTabDrag(NSEvent e)
    {
        if (_dragSource == null || _dragPanelId == null)
            return;

        var screenPoint = ScreenPointForEvent(e);
        var target = FindTarget(screenPoint);

        if (!ReferenceEquals(target, _dragTarget))
        {
            _dragTarget?.HideDropOverlay();
            _dragTarget = target;
        }

        _dragRegion = _dragTarget?.ShowDropOverlay(screenPoint) ?? DockDropRegion.None;
    }

    internal void EndTabDrag(NSEvent e)
    {
        if (_dragSource == null || _dragPanelId == null)
            return;

        var source = _dragSource;
        var panelId = _dragPanelId;
        var screenPoint = ScreenPointForEvent(e);
        var target = FindTarget(screenPoint);
        var region = target?.ShowDropOverlay(screenPoint) ?? DockDropRegion.None;

        ClearDragVisuals();

        try
        {
            if (target == null || region == DockDropRegion.None)
            {
                FloatPanel(source, panelId, screenPoint);
                return;
            }

            if (ReferenceEquals(source, target) && region == DockDropRegion.Center)
                return;

            if (ReferenceEquals(source, target) && source.TabCount == 1)
                return;

            if (region == DockDropRegion.Center)
                MovePanelToGroup(source, panelId, target);
            else
                SplitPanelAtTarget(source, panelId, target, region);
        }
        finally
        {
            _dragSource = null;
            _dragPanelId = null;
            _dragTarget = null;
            _dragRegion = DockDropRegion.None;
        }
    }

    private NSView BuildNode(DockNode node, CGRect frame)
    {
        switch (node)
        {
            case SplitDockNode splitNode:
            {
                var split = CreateSplitView(frame, splitNode.Vertical);
                split.AddSubview(BuildNode(splitNode.First, split.Bounds));
                split.AddSubview(BuildNode(splitNode.Second, split.Bounds));
                split.AdjustSubviews();
                _splits[splitNode.Id] = (split, splitNode);
                return split;
            }

            case TabDockNode tabNode:
            {
                var group = CreateTabGroup(frame, tabNode.Id);

                foreach (var panelId in tabNode.PanelIds)
                {
                    if (!_panels.TryGetValue(panelId, out var panel))
                        throw new InvalidOperationException($"Dock layout references unknown panel '{panelId}'.");
                    group.AddTab(panel);
                }

                group.Select(tabNode.DefaultSelectedPanelId!);
                return group;
            }

            default:
                throw new NotSupportedException($"Unsupported dock node type: {node.GetType().Name}");
        }
    }

    private NSSplitView CreateSplitView(CGRect frame, bool vertical)
    {
        return new NSSplitView(frame)
        {
            IsVertical = vertical,
            DividerStyle = NSSplitViewDividerStyle.Thin,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
        };
    }

    private DockTabGroupView CreateTabGroup(CGRect frame, string id)
    {
        var group = new DockTabGroupView(frame, id, this)
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
        };
        _tabGroups[id] = group;
        return group;
    }

    private void MovePanelToGroup(DockTabGroupView source, string panelId, DockTabGroupView target)
    {
        var panel = source.RemoveTab(panelId);
        if (panel == null)
            return;

        target.AddTab(panel, select: true);
        CleanupEmptyGroup(source);
        Log.Info($"Docked {panelId} into {target.NodeId}");
    }

    private void SplitPanelAtTarget(DockTabGroupView source, string panelId, DockTabGroupView target, DockDropRegion region)
    {
        if (!_panels.TryGetValue(panelId, out var registeredPanel))
            return;

        var parent = target.Superview;
        if (parent == null)
            return;

        var targetFrame = target.Frame;
        var panel = source.RemoveTab(panelId) ?? registeredPanel;
        var newGroup = CreateTabGroup(targetFrame, $"Dynamic.{++_dynamicId}");
        newGroup.AddTab(panel, select: true);

        var vertical = region is DockDropRegion.Left or DockDropRegion.Right;
        var newSplit = CreateSplitView(targetFrame, vertical);

        ReplaceChild(parent, target, newSplit);

        var newFirst = region is DockDropRegion.Left or DockDropRegion.Top;
        if (newFirst)
        {
            newSplit.AddSubview(newGroup);
            newSplit.AddSubview(target);
        }
        else
        {
            newSplit.AddSubview(target);
            newSplit.AddSubview(newGroup);
        }

        newSplit.AdjustSubviews();
        var extent = vertical ? newSplit.Bounds.Width : newSplit.Bounds.Height;
        if (extent > 1)
            newSplit.SetPositionOfDivider((NFloat)(extent * 0.5), IntPtr.Zero);

        CleanupEmptyGroup(source, preserveIf: target);
        Log.Info($"Split-docked {panelId} {region} of {target.NodeId}");
    }

    private void FloatPanel(DockTabGroupView source, string panelId, CGPoint screenPoint)
    {
        if (!_panels.TryGetValue(panelId, out var registeredPanel))
            return;

        var panel = source.RemoveTab(panelId) ?? registeredPanel;
        var width = 520.0;
        var height = 340.0;
        var frame = new CGRect(screenPoint.X - width * 0.5, screenPoint.Y - 24, width, height);

        var window = new NSWindow(
            frame,
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable,
            NSBackingStore.Buffered,
            false)
        {
            Title = panel.Title
        };

        var group = CreateTabGroup(new CGRect(0, 0, width, height), $"Floating.{++_dynamicId}");
        group.AddTab(panel, select: true);
        group.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        window.ContentView = group;
        _floatingWindows[group] = window;
        window.MakeKeyAndOrderFront(null);

        CleanupEmptyGroup(source);
        Log.Info($"Floated {panelId}");
    }

    private void CleanupEmptyGroup(DockTabGroupView group, DockTabGroupView? preserveIf = null)
    {
        if (group.TabCount != 0 || ReferenceEquals(group, preserveIf))
            return;

        group.HideDropOverlay();
        _tabGroups.Remove(group.NodeId);

        if (_floatingWindows.TryGetValue(group, out var floating))
        {
            _floatingWindows.Remove(group);
            floating.Close();
            return;
        }

        if (group.Superview is not NSSplitView split || split.Subviews.Length != 2)
        {
            group.RemoveFromSuperview();
            return;
        }

        var sibling = ReferenceEquals(split.Subviews[0], group) ? split.Subviews[1] : split.Subviews[0];
        var parent = split.Superview;
        if (parent == null)
            return;

        var oldFrame = split.Frame;
        sibling.RemoveFromSuperview();
        ReplaceChild(parent, split, sibling);
        sibling.Frame = oldFrame;
        sibling.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;

        if (parent is NSSplitView parentSplit)
            parentSplit.AdjustSubviews();
    }

    private static void ReplaceChild(NSView parent, NSView oldChild, NSView newChild)
    {
        var oldFrame = oldChild.Frame;
        newChild.Frame = oldFrame;
        parent.ReplaceSubviewWith(oldChild, newChild);

        if (parent is NSSplitView split)
            split.AdjustSubviews();
    }

    private DockTabGroupView? FindTarget(CGPoint screenPoint)
    {
        // Reverse enumeration favors recently created dynamic/floating groups when
        // windows overlap.
        foreach (var group in _tabGroups.Values.Reverse())
        {
            if (group.Superview != null && group.Window != null && group.ContainsScreenPoint(screenPoint))
                return group;
        }

        return null;
    }

    private CGPoint ScreenPointForEvent(NSEvent e)
    {
        var window = _dragSource?.Window;
        return window != null ? window.ConvertPointToScreen(e.LocationInWindow) : CGPoint.Empty;
    }

    private void ClearDragVisuals()
    {
        foreach (var group in _tabGroups.Values)
            group.HideDropOverlay();
        _dragTarget = null;
        _dragRegion = DockDropRegion.None;
    }
}