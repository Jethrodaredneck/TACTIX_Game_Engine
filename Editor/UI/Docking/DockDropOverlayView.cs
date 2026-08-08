using AppKit;
using CoreGraphics;

namespace TACTIX.Editor.UI.Docking;

/// <summary>
/// Lightweight native overlay shown while a dock tab is being dragged.
/// Five targets mirror conventional editor docking: center/tab, left, right,
/// top and bottom split.
/// </summary>
internal sealed class DockDropOverlayView : NSView
{
    private readonly Dictionary<DockDropRegion, NSView> _targets = new();
    private DockDropRegion _active;

    public DockDropOverlayView(CGRect frame) : base(frame)
    {
        WantsLayer = true;
        Layer!.BackgroundColor = NSColor.Clear.CGColor;
        AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;

        foreach (var region in new[]
                 {
                     DockDropRegion.Center,
                     DockDropRegion.Left,
                     DockDropRegion.Right,
                     DockDropRegion.Top,
                     DockDropRegion.Bottom
                 })
        {
            var target = new NSView(CGRect.Empty)
            {
                WantsLayer = true
            };
            target.Layer!.BackgroundColor = NSColor.FromRgb(65, 125, 210).CGColor;
            target.AlphaValue = 0.24f;
            _targets[region] = target;
            AddSubview(target);
        }

        LayoutTargets();
    }

    public DockDropRegion RegionAt(CGPoint localPoint)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 1 || h <= 1)
            return DockDropRegion.None;

        var nx = localPoint.X / w;
        var ny = localPoint.Y / h;

        // Center is deliberately generous so ordinary tab-to-tab moves are easy.
        if (nx >= 0.25 && nx <= 0.75 && ny >= 0.25 && ny <= 0.75)
            return DockDropRegion.Center;

        var left = nx;
        var right = 1.0 - nx;
        var bottom = ny;
        var top = 1.0 - ny;
        var min = Math.Min(Math.Min(left, right), Math.Min(top, bottom));

        if (min == left) return DockDropRegion.Left;
        if (min == right) return DockDropRegion.Right;
        if (min == top) return DockDropRegion.Top;
        return DockDropRegion.Bottom;
    }

    public void SetActive(DockDropRegion region)
    {
        _active = region;
        foreach (var pair in _targets)
        {
            pair.Value.AlphaValue = pair.Key == region ? 0.58f : 0.24f;
        }
    }

    public override void ResizeSubviewsWithOldSize(CGSize oldSize)
    {
        base.ResizeSubviewsWithOldSize(oldSize);
        LayoutTargets();
    }

    private void LayoutTargets()
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var edgeW = Math.Max(44, Math.Min(96, w * 0.18));
        var edgeH = Math.Max(44, Math.Min(86, h * 0.18));
        var centerW = Math.Max(80, Math.Min(180, w * 0.34));
        var centerH = Math.Max(56, Math.Min(120, h * 0.30));

        _targets[DockDropRegion.Center].Frame = new CGRect((w - centerW) * 0.5, (h - centerH) * 0.5, centerW, centerH);
        _targets[DockDropRegion.Left].Frame = new CGRect(10, (h - edgeH) * 0.5, edgeW, edgeH);
        _targets[DockDropRegion.Right].Frame = new CGRect(Math.Max(10, w - edgeW - 10), (h - edgeH) * 0.5, edgeW, edgeH);
        _targets[DockDropRegion.Top].Frame = new CGRect((w - centerW) * 0.5, Math.Max(10, h - edgeH - 10), centerW, edgeH);
        _targets[DockDropRegion.Bottom].Frame = new CGRect((w - centerW) * 0.5, 10, centerW, edgeH);
        SetActive(_active);
    }
}