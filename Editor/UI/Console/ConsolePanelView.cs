using AppKit;
using CoreGraphics;
using Foundation;
using TACTIX.Editor.UI.Theme;
using TACTIX.Engine.Core.Logging;

namespace TACTIX.Editor.UI.Console;

public sealed class ConsolePanelView : NSView
{
    private readonly NSTextView _text;
    private readonly NSButton _info;
    private readonly NSButton _warnings;
    private readonly NSButton _errors;
    private bool _showInfo = true;
    private bool _showWarnings = true;
    private bool _showErrors = true;

    public ConsolePanelView(CGRect frame) : base(frame)
    {
        EditorTheme.ApplyPanel(this);

        var toolbar = new NSView(new CGRect(0, Math.Max(0, frame.Height - 34), frame.Width, 34))
        {
            WantsLayer = true,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin
        };
        toolbar.Layer!.BackgroundColor = EditorTheme.TabBar.CGColor;
        AddSubview(toolbar);

        _info = MakeFilter("Info", 10, true, () => { _showInfo = !_showInfo; Refresh(); });
        _warnings = MakeFilter("Warnings", 70, true, () => { _showWarnings = !_showWarnings; Refresh(); });
        _errors = MakeFilter("Errors", 158, true, () => { _showErrors = !_showErrors; Refresh(); });
        toolbar.AddSubview(_info);
        toolbar.AddSubview(_warnings);
        toolbar.AddSubview(_errors);

        var clear = new NSButton(new CGRect(226, 5, 58, 24)) { Title = "Clear" };
        EditorTheme.StyleButton(clear);
        clear.Activated += (_, _) => _text.String = string.Empty;
        toolbar.AddSubview(clear);

        var scroll = new NSScrollView(new CGRect(0, 0, frame.Width, Math.Max(0, frame.Height - 34)))
        {
            HasVerticalScroller = true,
            HasHorizontalScroller = true,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
            BorderType = NSBorderType.NoBorder,
            DrawsBackground = true,
            BackgroundColor = EditorTheme.Panel
        };

        _text = new NSTextView(scroll.Bounds)
        {
            Editable = false,
            Selectable = true,
            DrawsBackground = true,
            BackgroundColor = EditorTheme.Panel,
            TextColor = EditorTheme.Text,
            Font = NSFont.MonospacedSystemFontOfSize(11, NSFontWeight.Regular),
            AutoresizingMask = NSViewResizingMask.WidthSizable
        };
        scroll.DocumentView = _text;
        AddSubview(scroll);

        Log.EntryWritten += OnLogEntry;
        Refresh();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Log.EntryWritten -= OnLogEntry;
        base.Dispose(disposing);
    }

    private NSButton MakeFilter(string title, nfloat x, bool enabled, Action changed)
    {
        var button = new NSButton(new CGRect(x, 5, title == "Warnings" ? 82 : 56, 24))
        {
            Title = title,
            ToolTip = $"Show or hide {title.ToLowerInvariant()}"
        };
        EditorTheme.StyleButton(button);
        button.Activated += (_, _) => changed();
        return button;
    }

    private void OnLogEntry(LogEntry entry)
    {
        if (!NSThread.IsMain)
        {
            BeginInvokeOnMainThread(Refresh);
            return;
        }
        Refresh();
    }

    private void Refresh()
    {
        UpdateButton(_info, _showInfo);
        UpdateButton(_warnings, _showWarnings);
        UpdateButton(_errors, _showErrors);

        var lines = Log.Snapshot()
            .Where(Visible)
            .Select(Format);
        _text.String = string.Join("\n", lines);
        _text.ScrollRangeToVisible(new NSRange(_text.String.Length, 0));
    }

    private bool Visible(LogEntry entry) => entry.Level switch
    {
        LogLevel.Info => _showInfo,
        LogLevel.Warning => _showWarnings,
        LogLevel.Error => _showErrors,
        _ => true
    };

    private static string Format(LogEntry entry)
    {
        var tag = entry.Level switch
        {
            LogLevel.Info => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            _ => "LOG"
        };
        return $"{entry.Timestamp:HH:mm:ss.fff}  {tag,-5}  {entry.Member}  {entry.Message}";
    }

    private static void UpdateButton(NSButton button, bool active)
    {
        button.ContentTintColor = active ? EditorTheme.Accent : EditorTheme.TextMuted;
    }
}
