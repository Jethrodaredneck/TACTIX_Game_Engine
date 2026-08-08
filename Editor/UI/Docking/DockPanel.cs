using AppKit;

namespace TACTIX.Editor.UI.Docking;

/// <summary>
/// Registered editor panel. Content is created lazily so a panel can later move
/// between tab groups or floating windows without coupling the tool to AppKit layout.
/// </summary>
public sealed class DockPanel
{
    private readonly Func<NSView> _contentFactory;
    private NSView? _content;

    public string Id { get; }
    public string Title { get; }

    public DockPanel(string id, string title, Func<NSView> contentFactory)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        _contentFactory = contentFactory ?? throw new ArgumentNullException(nameof(contentFactory));
    }

    public NSView GetContent()
    {
        _content ??= _contentFactory();
        return _content;
    }
}
