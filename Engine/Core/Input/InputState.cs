using AppKit;
using CoreGraphics;

namespace TACTIX.Engine.Core.Input;

public sealed class InputState
{
    private readonly HashSet<ushort> _keysDown = new();
    private readonly HashSet<int> _mouseButtonsDown = new();

    public CGPoint MousePosition { get; private set; }
    public CGPoint MouseDelta { get; private set; }
    private CGPoint _lastMouse;

    internal void BeginFrame()
    {
        MouseDelta = new CGPoint(0, 0);
    }

    internal void OnMouseMoved(CGPoint pos)
    {
        MousePosition = pos;
        MouseDelta = new CGPoint(pos.X - _lastMouse.X, pos.Y - _lastMouse.Y);
        _lastMouse = pos;
    }

    internal void OnKeyDown(NSEvent e) => _keysDown.Add(e.KeyCode);
    internal void OnKeyUp(NSEvent e) => _keysDown.Remove(e.KeyCode);

    internal void OnMouseDown(int button) => _mouseButtonsDown.Add(button);
    internal void OnMouseUp(int button) => _mouseButtonsDown.Remove(button);

    public bool IsKeyDown(ushort keyCode) => _keysDown.Contains(keyCode);
    public bool IsMouseButtonDown(int button) => _mouseButtonsDown.Contains(button);
}