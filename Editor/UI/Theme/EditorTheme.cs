using AppKit;
using CoreGraphics;

namespace TACTIX.Editor.UI.Theme;

public static class EditorTheme
{
    public static readonly NSColor Window = NSColor.FromRgb(18, 19, 22);
    public static readonly NSColor Panel = NSColor.FromRgb(24, 25, 29);
    public static readonly NSColor PanelRaised = NSColor.FromRgb(30, 31, 36);
    public static readonly NSColor TabBar = NSColor.FromRgb(27, 28, 32);
    public static readonly NSColor TabSelected = NSColor.FromRgb(38, 40, 46);
    public static readonly NSColor Field = NSColor.FromRgb(34, 36, 41);
    public static readonly NSColor Border = NSColor.FromRgb(49, 51, 58);
    public static readonly NSColor Text = NSColor.FromRgb(224, 225, 230);
    public static readonly NSColor TextMuted = NSColor.FromRgb(145, 148, 158);
    public static readonly NSColor Accent = NSColor.FromRgb(226, 146, 54);
    public static readonly NSColor AccentSoft = NSColor.FromRgb(91, 63, 36);

    public static void ApplyPanel(NSView view, NSColor? color = null)
    {
        view.WantsLayer = true;
        view.Layer!.BackgroundColor = (color ?? Panel).CGColor;
    }

    public static NSTextField Label(string text, bool muted = false, bool bold = false)
        => Label(text, (nfloat)12, muted, bold);

    public static NSTextField Label(string text, nfloat size, bool muted = false, bool bold = false)
    {
        return new NSTextField
        {
            StringValue = text,
            Editable = false,
            Selectable = false,
            Bezeled = false,
            DrawsBackground = false,
            TextColor = muted ? TextMuted : Text,
            Font = bold ? NSFont.BoldSystemFontOfSize(size) : NSFont.SystemFontOfSize(size)
        };
    }

    public static void StyleField(NSTextField field)
    {
        field.Bezeled = true;
        field.BezelStyle = NSTextFieldBezelStyle.Rounded;
        field.DrawsBackground = true;
        field.BackgroundColor = Field;
        field.TextColor = Text;
        field.Font = NSFont.MonospacedDigitSystemFontOfSize(11, NSFontWeight.Regular);
    }

    public static void StyleButton(NSButton button, bool accent = false)
    {
        button.BezelStyle = NSBezelStyle.TexturedRounded;
        button.Font = NSFont.SystemFontOfSize(11);
        button.ContentTintColor = accent ? Accent : Text;
    }

    public static void Card(NSView view)
    {
        ApplyPanel(view, PanelRaised);
        view.Layer!.CornerRadius = 5;
        view.Layer!.BorderWidth = 1;
        view.Layer!.BorderColor = Border.CGColor;
    }
}
