using AppKit;
using CoreGraphics;
using TACTIX.Editor.UI.Theme;

namespace TACTIX.Editor.UI.Docking;

public sealed class DockTabGroupView : NSView
{
    private const double TabBarHeight = 30.0;
    private const double DefaultTabWidth = 128.0;

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
        EditorTheme.ApplyPanel(this);

        _tabBar = new NSView(new CGRect(0, Math.Max(0, frame.Height - TabBarHeight), frame.Width, TabBarHeight));
        EditorTheme.ApplyPanel(_tabBar, EditorTheme.TabBar);
        _tabBar.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;

        _contentHost = new NSView(new CGRect(0, 0, frame.Width, Math.Max(0, frame.Height - TabBarHeight)));
        EditorTheme.ApplyPanel(_contentHost);
        _contentHost.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;

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

        var button = new DockTabButtonView(CGRect.Empty, panel.Id, panel.Title,
            () => Select(panel.Id),
            (id, e) => _manager.BeginTabDrag(this, id, e),
            e => _manager.UpdateTabDrag(e),
            e => _manager.EndTabDrag(e));

        _tabs.Add((panel, button));
        _tabBar.AddSubview(button);
        LayoutTabs();
        if (select || _selectedPanelId == null) Select(panel.Id);
    }

    public DockPanel? RemoveTab(string panelId)
    {
        var index = _tabs.FindIndex(x => x.Panel.Id == panelId);
        if (index < 0) return null;
        var removed = _tabs[index];
        removed.Button.RemoveFromSuperview();
        _tabs.RemoveAt(index);

        if (_selectedPanelId == panelId)
        {
            foreach (var existing in _contentHost.Subviews) existing.RemoveFromSuperview();
            _selectedPanelId = null;
            if (_tabs.Count > 0) Select(_tabs[Math.Min(index, _tabs.Count - 1)].Panel.Id);
        }
        LayoutTabs();
        return removed.Panel;
    }

    public void Select(string panelId)
    {
        var index = _tabs.FindIndex(x => x.Panel.Id == panelId);
        if (index < 0) return;
        var tab = _tabs[index];
        foreach (var existing in _contentHost.Subviews) existing.RemoveFromSuperview();
        var content = tab.Panel.GetContent();
        content.RemoveFromSuperview();
        content.Frame = _contentHost.Bounds;
        content.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        _contentHost.AddSubview(content);
        _selectedPanelId = panelId;
        foreach (var item in _tabs) item.Button.SetSelected(item.Panel.Id == panelId);
    }

    internal DockDropRegion ShowDropOverlay(CGPoint screenPoint)
    {
        if (Window == null) return DockDropRegion.None;
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

    internal void HideDropOverlay() => _dropOverlay?.RemoveFromSuperview();

    internal bool ContainsScreenPoint(CGPoint screenPoint)
    {
        if (Window == null) return false;
        var pointInWindow = Window.ConvertPointFromScreen(screenPoint);
        return Bounds.Contains(ConvertPointFromView(pointInWindow, null));
    }

    public override void ResizeSubviewsWithOldSize(CGSize oldSize)
    {
        base.ResizeSubviewsWithOldSize(oldSize);
        LayoutTabs();
        if (_dropOverlay != null) _dropOverlay.Frame = Bounds;
    }

    private void LayoutTabs()
    {
        var available = Math.Max(1, _tabBar.Bounds.Width);
        var tabWidth = Math.Min(DefaultTabWidth, Math.Max(84, available / Math.Max(1, _tabs.Count)));
        for (var i = 0; i < _tabs.Count; i++) _tabs[i].Button.Frame = new CGRect(i * tabWidth, 0, tabWidth, TabBarHeight);
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
        private readonly NSView _accent;
        private CGPoint _mouseDownPoint;
        private bool _dragging;

        public DockTabButtonView(CGRect frame, string panelId, string title, Action onClick,
            Action<string, NSEvent> onBeginDrag, Action<NSEvent> onDrag, Action<NSEvent> onEndDrag) : base(frame)
        {
            _panelId = panelId;
            _onClick = onClick;
            _onBeginDrag = onBeginDrag;
            _onDrag = onDrag;
            _onEndDrag = onEndDrag;
            WantsLayer = true;
            AutoresizingMask = NSViewResizingMask.MaxXMargin;

            _label = EditorTheme.Label(title, 11);
            _label.Frame = new CGRect(12, 7, Math.Max(0, frame.Width - 24), 17);
            _label.AutoresizingMask = NSViewResizingMask.WidthSizable;
            AddSubview(_label);

            _accent = new NSView(new CGRect(0, 0, frame.Width, 2)) { WantsLayer = true, AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MaxYMargin };
            _accent.Layer!.BackgroundColor = EditorTheme.Accent.CGColor;
            AddSubview(_accent);
            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            Layer!.BackgroundColor = (selected ? EditorTheme.TabSelected : EditorTheme.TabBar).CGColor;
            _label.TextColor = selected ? EditorTheme.Text : EditorTheme.TextMuted;
            _label.Font = selected ? NSFont.BoldSystemFontOfSize(11) : NSFont.SystemFontOfSize(11);
            _accent.Hidden = !selected;
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
            if (_dragging) _onDrag(theEvent);
        }

        public override void MouseUp(NSEvent theEvent)
        {
            if (_dragging) _onEndDrag(theEvent); else _onClick();
            _dragging = false;
        }
    }
}
