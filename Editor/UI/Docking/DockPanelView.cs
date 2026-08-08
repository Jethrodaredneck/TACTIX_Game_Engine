using AppKit;
using CoreGraphics;

namespace TACTIX.Editor.UI.Docking;

/// <summary>
/// Basic dockable editor panel chrome. Drag/reparent behavior is intentionally
/// kept out of this class so the panel contents remain reusable when floating
/// windows and tab dragging are added.
/// </summary>
public sealed class DockPanelView : NSView
{
    private const double HeaderHeight = 28.0;

    private readonly NSView _header;
    private readonly NSTextField _title;
    private readonly NSView _contentHost;

    public string PanelId { get; }
    public NSView ContentHost => _contentHost;

    public DockPanelView(CGRect frame, string panelId, string title) : base(frame)
    {
        PanelId = panelId;
        WantsLayer = true;
        Layer!.BackgroundColor = NSColor.FromRgb(31, 31, 34).CGColor;

        _header = new NSView(new CGRect(0, Math.Max(0, frame.Height - HeaderHeight), frame.Width, HeaderHeight))
        {
            WantsLayer = true,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin
        };
        _header.Layer!.BackgroundColor = NSColor.FromRgb(42, 42, 46).CGColor;

        _title = new NSTextField(new CGRect(10, 5, Math.Max(0, frame.Width - 20), 18))
        {
            StringValue = title,
            Editable = false,
            Selectable = false,
            Bezeled = false,
            DrawsBackground = false,
            TextColor = NSColor.FromRgb(220, 220, 224),
            Font = NSFont.BoldSystemFontOfSize(12),
            AutoresizingMask = NSViewResizingMask.WidthSizable
        };

        _contentHost = new NSView(new CGRect(0, 0, frame.Width, Math.Max(0, frame.Height - HeaderHeight)))
        {
            WantsLayer = true,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
        };
        _contentHost.Layer!.BackgroundColor = NSColor.FromRgb(25, 25, 28).CGColor;

        _header.AddSubview(_title);
        AddSubview(_contentHost);
        AddSubview(_header);
    }

    public void SetContent(NSView view)
    {
        foreach (var existing in _contentHost.Subviews)
            existing.RemoveFromSuperview();

        view.Frame = _contentHost.Bounds;
        view.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        _contentHost.AddSubview(view);
    }
}
