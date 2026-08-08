using AppKit;
using CoreGraphics;
using Metal;
using TACTIX.Engine.Rendering.Metal;

namespace TACTIX.Editor.UI.Viewport;

/// <summary>
/// Editor-facing wrapper around the existing MetalView. The renderer remains
/// unchanged; docking only changes where the Metal view lives in the AppKit tree.
/// </summary>
public sealed class ViewportPanelView : NSView
{
    public MetalView MetalView { get; }

    public ViewportPanelView(CGRect frame, IMTLDevice device) : base(frame)
    {
        WantsLayer = true;
        Layer!.BackgroundColor = NSColor.Black.CGColor;

        MetalView = new MetalView(Bounds, device)
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
        };

        AddSubview(MetalView);
    }
}
