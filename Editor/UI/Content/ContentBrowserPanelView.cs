using AppKit;
using CoreGraphics;
using TACTIX.Editor.UI.Theme;

namespace TACTIX.Editor.UI.Content;

public sealed class ContentBrowserPanelView : NSView
{
    private readonly string _projectRoot;
    private readonly string _assetsRoot;
    private readonly NSSearchField _search;
    private readonly NSTextField _path;
    private readonly NSStackView _rows;
    private readonly NSScrollView _scroll;
    private string _relativeFolder = "";

    public ContentBrowserPanelView(CGRect frame, string projectRoot) : base(frame)
    {
        _projectRoot = projectRoot;
        _assetsRoot = Path.Combine(projectRoot, "Assets");
        Directory.CreateDirectory(_assetsRoot);
        EditorTheme.ApplyPanel(this);

        _path = EditorTheme.Label("Assets", 12, false, true);
        AddSubview(_path);

        _search = new NSSearchField { PlaceholderString = "Search Assets" };
        _search.Changed += (_, _) => Refresh();
        AddSubview(_search);

        _rows = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 2,
            EdgeInsets = new NSEdgeInsets(6, 6, 6, 6)
        };

        _scroll = new NSScrollView
        {
            HasVerticalScroller = true,
            DrawsBackground = true,
            BackgroundColor = EditorTheme.Panel,
            DocumentView = _rows
        };
        AddSubview(_scroll);
        Refresh();
    }

    public override void Layout()
    {
        base.Layout();
        const nfloat margin = 10;
        const nfloat header = 34;
        var w = Bounds.Width;
        var h = Bounds.Height;
        _path.Frame = new CGRect(margin, h - 28, Math.Max(80, w * 0.5 - margin), 20);
        _search.Frame = new CGRect(Math.Max(margin, w - 230), h - 31, Math.Min(220, Math.Max(100, w - 2 * margin)), 24);
        _scroll.Frame = new CGRect(0, 0, w, Math.Max(0, h - header));
        _rows.Frame = new CGRect(0, 0, Math.Max(1, _scroll.ContentSize.Width), Math.Max(_scroll.ContentSize.Height, _rows.FittingSize.Height));
    }

    private void Refresh()
    {
        foreach (var view in _rows.ArrangedSubviews.ToArray())
        {
            _rows.RemoveArrangedSubview(view);
            view.RemoveFromSuperview();
            view.Dispose();
        }

        var folder = Path.GetFullPath(Path.Combine(_assetsRoot, _relativeFolder));
        if (!folder.StartsWith(Path.GetFullPath(_assetsRoot), StringComparison.Ordinal))
        {
            _relativeFolder = "";
            folder = _assetsRoot;
        }

        _path.StringValue = string.IsNullOrEmpty(_relativeFolder) ? "Assets" : "Assets / " + _relativeFolder.Replace(Path.DirectorySeparatorChar, '/');
        var filter = _search.StringValue.Trim();

        if (!string.IsNullOrEmpty(_relativeFolder))
            AddRow("←  ..", true, () => { _relativeFolder = Path.GetDirectoryName(_relativeFolder) ?? ""; Refresh(); });

        foreach (var dir in Directory.EnumerateDirectories(folder).OrderBy(Path.GetFileName))
        {
            var name = Path.GetFileName(dir);
            if (!Matches(name, filter)) continue;
            AddRow("▸  " + name, true, () =>
            {
                _relativeFolder = Path.GetRelativePath(_assetsRoot, dir);
                Refresh();
            });
        }

        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(Path.GetFileName))
        {
            var name = Path.GetFileName(file);
            if (!Matches(name, filter)) continue;
            var ext = Path.GetExtension(name).ToLowerInvariant();
            var badge = ext switch
            {
                ".tasset" => "ASSET",
                ".tactixscene" => "SCENE",
                ".glb" or ".gltf" or ".fbx" or ".obj" or ".blend" => "MODEL",
                ".png" or ".jpg" or ".jpeg" or ".tga" or ".hdr" or ".exr" => "TEX",
                ".wav" or ".ogg" or ".flac" => "AUDIO",
                _ => ext.TrimStart('.').ToUpperInvariant()
            };
            AddFileRow(name, badge);
        }

        NeedsLayout = true;
    }

    private static bool Matches(string value, string filter) =>
        string.IsNullOrWhiteSpace(filter) || value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private void AddRow(string title, bool folder, Action action)
    {
        var button = new NSButton(new CGRect(0, 0, 520, 24))
        {
            Title = title,
            BezelStyle = NSBezelStyle.Inline,
            Alignment = NSTextAlignment.Left,
            Font = NSFont.SystemFontOfSize(11),
            ContentTintColor = folder ? EditorTheme.Accent : EditorTheme.Text
        };
        button.Activated += (_, _) => action();
        _rows.AddArrangedSubview(button);
    }

    private void AddFileRow(string name, string badge)
    {
        var row = new NSView(new CGRect(0, 0, 620, 24));
        var nameLabel = EditorTheme.Label(name, 11);
        nameLabel.Frame = new CGRect(8, 3, 410, 18);
        row.AddSubview(nameLabel);

        var typeLabel = EditorTheme.Label(badge, 9, true, true);
        typeLabel.Alignment = NSTextAlignment.Right;
        typeLabel.Frame = new CGRect(430, 4, 90, 16);
        row.AddSubview(typeLabel);
        _rows.AddArrangedSubview(row);
    }
}
