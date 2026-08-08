using AppKit;
using CoreGraphics;

namespace TACTIX.Editor.UI.Panels;

/// <summary>
/// Temporary content used while the editor shell is being brought online.
/// Each placeholder is replaced in-place by its real editor system later.
/// </summary>
public sealed class PlaceholderPanelView : NSView
{
    public PlaceholderPanelView(CGRect frame, string message) : base(frame)
    {
        WantsLayer = true;
        Layer!.BackgroundColor = NSColor.FromRgb(25, 25, 28).CGColor;

        var label = new NSTextField(new CGRect(12, Math.Max(10, frame.Height - 38), Math.Max(0, frame.Width - 24), 22))
        {
            StringValue = message,
            Editable = false,
            Selectable = false,
            Bezeled = false,
            DrawsBackground = false,
            TextColor = NSColor.FromRgb(145, 145, 152),
            Font = NSFont.SystemFontOfSize(12),
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin
        };

        AddSubview(label);
    }
}
