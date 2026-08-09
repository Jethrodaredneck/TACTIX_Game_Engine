using AppKit;
using CoreGraphics;
using TACTIX.Editor.UI.Theme;

namespace TACTIX.Editor.UI.Content;

public sealed class ContentBrowserPanelView : NSView
{
    private readonly string _projectRoot;
    private readonly string _assetsRoot;
    private readonly NSSearchField _search;
    private readonly NSPopUpButton _category;
    private readonly NSTextField _path;
    private readonly NSTextField _status;
    private readonly NSButton _upButton;
    private readonly NSButton _refreshButton;
    private readonly NSStackView _rows;
    private readonly NSScrollView _scroll;
    private string _relativeFolder = "";

    public ContentBrowserPanelView(CGRect frame, string projectRoot) : base(frame)
    {
        _projectRoot = projectRoot;
        _assetsRoot = Path.Combine(projectRoot, "Assets");
        Directory.CreateDirectory(_assetsRoot);
        EditorTheme.ApplyPanel(this);

        _upButton = new NSButton(new CGRect(0, 0, 30, 24)) { Title = "↑", ToolTip = "Up one folder" };
        EditorTheme.StyleButton(_upButton);
        _upButton.Activated += (_, _) => NavigateUp();
        AddSubview(_upButton);

        _refreshButton = new NSButton(new CGRect(0, 0, 30, 24)) { Title = "↻", ToolTip = "Refresh assets" };
        EditorTheme.StyleButton(_refreshButton);
        _refreshButton.Activated += (_, _) => Refresh();
        AddSubview(_refreshButton);

        _path = EditorTheme.Label("Assets", 12, false, true);
        AddSubview(_path);

        _category = new NSPopUpButton(new CGRect(0, 0, 120, 24), false);
        _category.AddItems(new[] { "All Assets", "Models", "Textures", "Audio", "Scenes", "Terrain" });
        _category.Activated += (_, _) => Refresh();
        AddSubview(_category);

        _search = new NSSearchField { PlaceholderString = "Search Assets" };
        _search.Changed += (_, _) => Refresh();
        AddSubview(_search);

        _status = EditorTheme.Label("", 9, true);
        AddSubview(_status);

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
        nfloat margin = 10;
        nfloat toolbarHeight = 38;
        nfloat statusHeight = 22;
        var w = Bounds.Width;
        var h = Bounds.Height;

        _upButton.Frame = new CGRect(margin, h - 31, 30, 24);
        _refreshButton.Frame = new CGRect(margin + 34, h - 31, 30, 24);
        _path.Frame = new CGRect(margin + 72, h - 28, Math.Max(90, w * 0.32), 20);

        var searchWidth = Math.Min(210, Math.Max(120, w * 0.22));
        var categoryWidth = Math.Min(125, Math.Max(95, w * 0.14));
        _search.Frame = new CGRect(Math.Max(margin, w - searchWidth - margin), h - 31, searchWidth, 24);
        _category.Frame = new CGRect(Math.Max(margin, w - searchWidth - categoryWidth - margin - 6), h - 31, categoryWidth, 24);

        _status.Frame = new CGRect(margin, 4, Math.Max(100, w - margin * 2), 16);
        _scroll.Frame = new CGRect(0, statusHeight, w, Math.Max(0, h - toolbarHeight - statusHeight));
        _rows.Frame = new CGRect(0, 0, Math.Max(1, _scroll.ContentSize.Width), Math.Max(_scroll.ContentSize.Height, _rows.FittingSize.Height));
    }

    private void NavigateUp()
    {
        if (string.IsNullOrEmpty(_relativeFolder)) return;
        _relativeFolder = Path.GetDirectoryName(_relativeFolder) ?? "";
        Refresh();
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
        _upButton.Enabled = !string.IsNullOrEmpty(_relativeFolder);
        var filter = _search.StringValue.Trim();
        var category = _category.TitleOfSelectedItem ?? "All Assets";
        var shown = 0;

        foreach (var dir in Directory.EnumerateDirectories(folder).OrderBy(Path.GetFileName))
        {
            var name = Path.GetFileName(dir);
            if (!Matches(name, filter)) continue;
            AddRow("▸  " + name, true, () =>
            {
                _relativeFolder = Path.GetRelativePath(_assetsRoot, dir);
                Refresh();
            });
            shown++;
        }

        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(Path.GetFileName))
        {
            var name = Path.GetFileName(file);
            if (!Matches(name, filter) || !MatchesCategory(name, category)) continue;
            var ext = Path.GetExtension(name).ToLowerInvariant();
            var badge = ext switch
            {
                ".tasset" => "ASSET",
                ".tactixscene" => "SCENE",
                ".glb" or ".gltf" or ".fbx" or ".obj" or ".blend" or ".usd" or ".usda" or ".usdc" => "MODEL",
                ".png" or ".jpg" or ".jpeg" or ".tga" or ".hdr" or ".exr" => "TEXTURE",
                ".wav" or ".ogg" or ".flac" => "AUDIO",
                _ => ext.TrimStart('.').ToUpperInvariant()
            };
            AddFileRow(name, badge);
            shown++;
        }

        _status.StringValue = $"{shown} item{(shown == 1 ? "" : "s")}  ·  {category}";
        NeedsLayout = true;
    }

    private static bool Matches(string value, string filter) =>
        string.IsNullOrWhiteSpace(filter) || value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesCategory(string name, string category)
    {
        if (category == "All Assets") return true;
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return category switch
        {
            "Models" => ext is ".glb" or ".gltf" or ".fbx" or ".obj" or ".blend" or ".usd" or ".usda" or ".usdc",
            "Textures" => ext is ".png" or ".jpg" or ".jpeg" or ".tga" or ".hdr" or ".exr",
            "Audio" => ext is ".wav" or ".ogg" or ".flac",
            "Scenes" => ext == ".tactixscene",
            "Terrain" => ext == ".tasset" && name.Contains("terrain", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private void AddRow(string title, bool folder, Action action)
    {
        var button = new NSButton(new CGRect(0, 0, 520, 26))
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
        var row = new NSView(new CGRect(0, 0, 620, 26));
        var nameLabel = EditorTheme.Label(name, 11);
        nameLabel.Frame = new CGRect(8, 4, 410, 18);
        row.AddSubview(nameLabel);

        var typeLabel = EditorTheme.Label(badge, 9, true, true);
        typeLabel.Alignment = NSTextAlignment.Right;
        typeLabel.Frame = new CGRect(430, 5, 90, 16);
        row.AddSubview(typeLabel);
        _rows.AddArrangedSubview(row);
    }
}
