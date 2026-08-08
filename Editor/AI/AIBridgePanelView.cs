using AppKit;
using CoreGraphics;
using TACTIX.Engine.AI;

namespace TACTIX.Editor.AI;

public sealed class AIBridgePanelView : NSView
{
    private readonly AIBridgeServer _bridge;
    private readonly NSTextField _status;
    private readonly NSButton _toggle;

    public AIBridgePanelView(CGRect frame, AIBridgeServer bridge) : base(frame)
    {
        _bridge = bridge;
        WantsLayer = true;
        Layer!.BackgroundColor = NSColor.FromRgb(25, 25, 28).CGColor;

        var title = Label("Local AI / MCP access", 14, 18, frame.Height - 42, 360, 22, NSColor.FromRgb(220,220,224));
        AddSubview(title);

        var endpoint = Label($"Bridge: {bridge.BaseUrl}", 12, 18, frame.Height - 72, 520, 20, NSColor.FromRgb(150,150,158));
        endpoint.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        AddSubview(endpoint);

        var manifest = Label($"Credentials: .tactix/ai-bridge.json", 11, 18, frame.Height - 96, 520, 20, NSColor.FromRgb(125,125,132));
        manifest.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        AddSubview(manifest);

        _status = Label("", 12, 18, frame.Height - 132, 520, 20, NSColor.FromRgb(175,175,182));
        _status.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        AddSubview(_status);

        _toggle = new NSButton(new CGRect(18, frame.Height - 176, 150, 30))
        {
            Title = "Enable edits",
            BezelStyle = NSBezelStyle.Rounded,
            AutoresizingMask = NSViewResizingMask.MinYMargin
        };
        _toggle.Activated += (_, _) =>
        {
            _bridge.SetAllowEdits(!_bridge.AllowEdits);
            Refresh();
        };
        AddSubview(_toggle);

        var note = Label("Read access is always available while TACTIX is running. Writes are project-root scoped and require this toggle.", 11, 18, frame.Height - 212, (nfloat)Math.Max(100.0, (double)frame.Width - 36.0), 42, NSColor.FromRgb(120,120,128));
        note.LineBreakMode = NSLineBreakMode.ByWordWrapping;
        note.UsesSingleLineMode = false;
        note.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        AddSubview(note);

        var ollama = Label("Ollama agent: qwen2.5-coder:3b  •  ./ollama_agent.sh", 11, 18, frame.Height - 246, (nfloat)Math.Max(100.0, (double)frame.Width - 36.0), 20, NSColor.FromRgb(115,160,210));
        ollama.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        AddSubview(ollama);

        Refresh();
    }

    private void Refresh()
    {
        _status.StringValue = _bridge.AllowEdits ? "READ + WRITE enabled" : "READ ONLY";
        _status.TextColor = _bridge.AllowEdits ? NSColor.FromRgb(225, 150, 60) : NSColor.FromRgb(85, 190, 105);
        _toggle.Title = _bridge.AllowEdits ? "Disable edits" : "Enable edits";
    }

    private static NSTextField Label(string text, nfloat size, nfloat x, nfloat y, nfloat w, nfloat h, NSColor color)
        => new(new CGRect(x,y,w,h))
        {
            StringValue = text,
            Editable = false,
            Selectable = true,
            Bezeled = false,
            DrawsBackground = false,
            TextColor = color,
            Font = NSFont.SystemFontOfSize(size)
        };
}
