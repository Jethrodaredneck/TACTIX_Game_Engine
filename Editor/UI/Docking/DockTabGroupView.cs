using AppKit;
using CoreGraphics;

namespace TACTIX.Editor.UI.Docking;

/// <summary>
/// Native AppKit tab host. Stage 3 adds direct mouse-driven dragging so tabs can
/// move between groups, split a target group, or become floating windows.
/// </summary>
public sealed class DockTabGroupView : NSView
{
    private const double TabBarHeight = 28.0;
    private const double DefaultTabWidth = 132.0;

    private readonly DockManager _manager;
    private readonly NSView _tabBar;
    private readonly NSView _contentHost;
    private readonly List<(DockPanel Panel, DockTabButtonView Button)> _tabs = new();
    private DockDropOverlayView? _dropOverlay;
    private string? _selectedPanelId;

    public string NodeId { get; }
    public string? SelectedPanelId => _selectedPanelId;
    public int TabCount => _tabs.Count;
    public IReadOnlyList<string> PanelIds => _tabs.Select(x => x.Panel.Id).ToArray();

    public DockTabGroupView(CGRect frame, string nodeId, DockManager manager) : base(frame)
    {
        NodeId = nodeId;
        _manager = manager;
        WantsLayer = true;
        Layer!.BackgroundColor = NSColor.FromRgb(25, 25, 28).CGColor;

        _tabBar = new NSView(new CGRect(0, Math.Max(0, frame.Height - TabBarHeight), frame.Width, TabBarHeight))
        {
            WantsLayer = true,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin
        };
        _tabBar.Layer!.BackgroundColor = NSColor.FromRgb(42, 42, 46).CGColor;

        _contentHost = new NSView(new CGRect(0, 0, frame.Width, Math.Max(0, frame.Height - TabBarHeight)))
        {
            WantsLayer = true,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
        };
        _contentHost.Layer!.BackgroundColor = NSColor.FromRgb(25, 25, 28).CGColor;

        AddSubview(_contentHost);
        AddSubview(_tabBar);
    }

    public bool ContainsPanel(string panelId) => _tabs.Any(x => x.Panel.Id == panelId);

    public void AddTab(DockPanel panel, bool select = false)
    {
        if (_tabs.Any(x => x.Panel.Id == panel.Id))
        {
            if (select) Select(panel.Id);
            return;
        }

        var button = new DockTabButtonView(
            CGRect.Empty,
            panel.Id,
            panel.Title,
            () => Select(panel.Id),
            (id, e) => _manager.BeginTabDrag(this, id, e),
            e => _manager.UpdateTabDrag(e),
            e => _manager.EndTabDrag(e));

        _tabs.Add((panel, button));
        _tabBar.AddSubview(button);
        LayoutTabs();

        if (select || _selectedPanelId == null)
            Select(panel.Id);
    }

    public DockPanel? RemoveTab(string panelId)
    {
        var index = _tabs.FindIndex(x => x.Panel.Id == panelId);
        if (index < 0)
            return null;

        var removed = _tabs[index];
        removed.Button.RemoveFromSuperview();
        _tabs.RemoveAt(index);

        if (_selectedPanelId == panelId)
        {
            foreach (var existing in _contentHost.Subviews)
                existing.RemoveFromSuperview();
            _selectedPanelId = null;

            if (_tabs.Count > 0)
                Select(_tabs[Math.Min(index, _tabs.Count - 1)].Panel.Id);
        }

        LayoutTabs();
        return removed.Panel;
    }

    public void Select(string panelId)
    {
        var index = _tabs.FindIndex(x => x.Panel.Id == panelId);
        if (index < 0)
            return;

        var tab = _tabs[index];

        foreach (var existing in _contentHost.Subviews)
            existing.RemoveFromSuperview();

        var content = tab.Panel.GetContent();
        content.RemoveFromSuperview();
        content.Frame = _contentHost.Bounds;
        content.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        _contentHost.AddSubview(content);

        _selectedPanelId = panelId;
        foreach (var item in _tabs)
            item.Button.SetSelected(item.Panel.Id == panelId);
    }

    internal DockDropRegion ShowDropOverlay(CGPoint screenPoint)
    {
        if (Window == null)
            return DockDropRegion.None;

        _dropOverlay ??= new DockDropOverlayView(Bounds);
        if (_dropOverlay.Superview == null)
        {
            _dropOverlay.Frame = Bounds;
            AddSubview(_dropOverlay);
        }

        var pointInWindow = Window.ConvertPointFromScreen(screenPoint);
        var local = ConvertPointFromView(pointInWindow, null);
        var region = _dropOverlay.RegionAt(local);
        _dropOverlay.SetActive(region);
        return region;
    }

    internal void HideDropOverlay()
    {
        _dropOverlay?.RemoveFromSuperview();
    }

    internal bool ContainsScreenPoint(CGPoint screenPoint)
    {
        if (Window == null)
            return false;

        var pointInWindow = Window.ConvertPointFromScreen(screenPoint);
        var local = ConvertPointFromView(pointInWindow, null);
        return Bounds.Contains(local);
    }

    public override void ResizeSubviewsWithOldSize(CGSize oldSize)
    {
        base.ResizeSubviewsWithOldSize(oldSize);
        LayoutTabs();
        if (_dropOverlay != null)
            _dropOverlay.Frame = Bounds;
    }

    private void LayoutTabs()
    {
        var available = Math.Max(1, _tabBar.Bounds.Width);
        var tabWidth = Math.Min(DefaultTabWidth, Math.Max(86, available / Math.Max(1, _tabs.Count)));

        for (var i = 0; i < _tabs.Count; i++)
            _tabs[i].Button.Frame = new CGRect(i * tabWidth, 0, tabWidth, TabBarHeight);
    }

    private sealed class DockTabButtonView : NSView
    {
        private const double DragThreshold = 5.0;

        private readonly string _panelId;
        private readonly Action _onClick;
        private readonly Action<string, NSEvent> _onBeginDrag;
        private readonly Action<NSEvent> _onDrag;
        private readonly Action<NSEvent> _onEndDrag;
        private readonly NSTextField _label;
        private CGPoint _mouseDownPoint;
        private bool _dragging;

        public DockTabButtonView(
            CGRect frame,
            string panelId,
            string title,
            Action onClick,
            Action<string, NSEvent> onBeginDrag,
            Action<NSEvent> onDrag,
            Action<NSEvent> onEndDrag) : base(frame)
        {
            _panelId = panelId;
            _onClick = onClick;
            _onBeginDrag = onBeginDrag;
            _onDrag = onDrag;
            _onEndDrag = onEndDrag;
            WantsLayer = true;
            AutoresizingMask = NSViewResizingMask.MaxXMargin;

            _label = new NSTextField(new CGRect(10, 5, Math.Max(0, frame.Width - 20), 18))
            {
                StringValue = title,
                Editable = false,
                Selectable = false,
                Bezeled = false,
                DrawsBackground = false,
                TextColor = NSColor.FromRgb(205, 205, 210),
                Font = NSFont.SystemFontOfSize(12),
                AutoresizingMask = NSViewResizingMask.WidthSizable
            };

            AddSubview(_label);
            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            Layer!.BackgroundColor = selected
                ? NSColor.FromRgb(55, 55, 60).CGColor
                : NSColor.FromRgb(42, 42, 46).CGColor;
            _label.Font = selected ? NSFont.BoldSystemFontOfSize(12) : NSFont.SystemFontOfSize(12);
        }

        public override void MouseDown(NSEvent theEvent)
        {
            _mouseDownPoint = theEvent.LocationInWindow;
            _dragging = false;
            _onClick();
        }

        public override void MouseDragged(NSEvent theEvent)
        {
            var p = theEvent.LocationInWindow;
            var dx = p.X - _mouseDownPoint.X;
            var dy = p.Y - _mouseDownPoint.Y;

            if (!_dragging && Math.Sqrt(dx * dx + dy * dy) >= DragThreshold)
            {
                _dragging = true;
                _onBeginDrag(_panelId, theEvent);
            }

            if (_dragging)
                _onDrag(theEvent);
        }

        public override void MouseUp(NSEvent theEvent)
        {
            if (_dragging)
                _onEndDrag(theEvent);
            else
                _onClick();

            _dragging = false;
        }
    }
}
