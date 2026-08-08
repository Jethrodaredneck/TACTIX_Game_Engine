using AppKit;
using CoreGraphics;
using Foundation;
using TACTIX.Engine.Core.Input;

namespace TACTIX.Engine.Core.Windowing;

public sealed class TactixWindow : NSWindow
{
    private readonly InputState _input;

    public TactixWindow(CGRect frame, string title, InputState input)
        : base(frame,
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable,
            NSBackingStore.Buffered,
            deferCreation: false)
    {
        _input = input;
        Title = title;
        AcceptsMouseMovedEvents = true;
        ReleasedWhenClosed = false;

        Center();
        MakeKeyAndOrderFront(null);
    }

    public override void KeyDown(NSEvent theEvent) => _input.OnKeyDown(theEvent);
    public override void KeyUp(NSEvent theEvent) => _input.OnKeyUp(theEvent);

    public override void MouseMoved(NSEvent theEvent)
    {
        var p = theEvent.LocationInWindow;
        _input.OnMouseMoved(new CGPoint(p.X, p.Y));
    }

    public override void MouseDown(NSEvent theEvent) => _input.OnMouseDown(0);
    public override void MouseUp(NSEvent theEvent) => _input.OnMouseUp(0);
    public override void RightMouseDown(NSEvent theEvent) => _input.OnMouseDown(1);
    public override void RightMouseUp(NSEvent theEvent) => _input.OnMouseUp(1);

    public void RequestClose()
    {
        base.Close();
        NSApplication.SharedApplication.Terminate(this);
    }
}