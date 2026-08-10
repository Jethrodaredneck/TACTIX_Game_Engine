using AppKit;
using CoreGraphics;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Importing;
using TACTIX.Engine.Assets.Serialization;
using TACTIX.Engine.Runtime.ECS;
using TACTIX.Editor.Scene;
using TACTIX.Editor.UI.Theme;

namespace TACTIX.Editor.UI.Content;

public sealed class ContentBrowserPanelView : NSView
{
    private readonly AssetDatabase _assets;
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly EditorCommandStack _commands;
    private readonly AssetImportPipeline _importPipeline = AssetImportPipeline.CreateDefault();
    private readonly string _projectRoot;
    private readonly string _assetsRoot;
    private readonly NSSearchField _search;
    private readonly NSPopUpButton _category;
    private readonly NSPopUpButton _importMenu;
    private readonly NSTextField _path;
    private readonly NSTextField _status;
    private readonly NSButton _upButton;
    private readonly NSButton _refreshButton;
    private readonly NSStackView _rows;
    private readonly NSScrollView _scroll;
    private string _relativeFolder = "";

    public ContentBrowserPanelView(CGRect frame, AssetDatabase assets, World world, EditorSelection selection, EditorCommandStack commands) : base(frame)
    {
        _assets = assets;
        _world = world;
        _selection = selection;
        _commands = commands;
        _projectRoot = assets.ProjectRoot;
        _assetsRoot = Path.Combine(_projectRoot, "Assets");
        Directory.CreateDirectory(_assetsRoot);
        EditorTheme.ApplyPanel(this);

        _upButton = new NSButton(new CGRect(0, 0, 30, 24)) { Title = "↑", ToolTip = "Up one folder" };
        EditorTheme.StyleButton(_upButton);
        _upButton.Activated += (_, _) => NavigateUp();
        AddSubview(_upButton);

        _refreshButton = new NSButton(new CGRect(0, 0, 30, 24)) { Title = "↻", ToolTip = "Refresh assets" };
        EditorTheme.StyleButton(_refreshButton);
        _refreshButton.Activated += (_, _) => RefreshFromDisk();
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

        _importMenu = new NSPopUpButton(new CGRect(0, 0, 150, 24), false);
        _importMenu.AddItems(new[] { "Import ▾", "Model / Asset File…", "Blender .blend…", "Import Pending" });
        _importMenu.SelectItem(0);
        _importMenu.Activated += (_, _) => HandleImportMenu();
        AddSubview(_importMenu);

        _status = EditorTheme.Label("Choose Import ▾ to bring source assets into this project", 9, true);
        _status.LineBreakMode = NSLineBreakMode.TruncatingTail;
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
        nfloat toolbarHeight = 64;
        nfloat statusHeight = 22;
        var w = Bounds.Width;
        var h = Bounds.Height;
        var topY = h - 31;
        var importY = h - 58;
        var searchWidth = Math.Min(210, Math.Max(120, w * 0.22));
        var categoryWidth = Math.Min(125, Math.Max(95, w * 0.14));
        var searchX = Math.Max(margin, w - searchWidth - margin);
        var categoryX = Math.Max(margin, searchX - categoryWidth - 6);

        _upButton.Frame = new CGRect(margin, topY, 30, 24);
        _refreshButton.Frame = new CGRect(margin + 34, topY, 30, 24);
        _search.Frame = new CGRect(searchX, topY, searchWidth, 24);
        _category.Frame = new CGRect(categoryX, topY, categoryWidth, 24);
        var pathX = margin + 72;
        var pathRight = Math.Max(pathX + 90, categoryX - 8);
        _path.Frame = new CGRect(pathX, h - 28, Math.Max(90, pathRight - pathX), 20);
        _importMenu.Frame = new CGRect(margin, importY, 160, 24);
        _status.Frame = new CGRect(margin + 170, importY + 3, Math.Max(100, w - margin * 2 - 170), 18);
        _scroll.Frame = new CGRect(0, statusHeight, w, Math.Max(0, h - toolbarHeight - statusHeight));
        _rows.Frame = new CGRect(0, 0, Math.Max(1, _scroll.ContentSize.Width), Math.Max(_scroll.ContentSize.Height, _rows.FittingSize.Height));
    }

    private void HandleImportMenu()
    {
        var index = _importMenu.IndexOfSelectedItem;
        _importMenu.SelectItem(0);
        switch (index)
        {
            case 1: ImportFromFileDialog(false); break;
            case 2: ImportFromFileDialog(true); break;
            case 3: ImportPending(); break;
        }
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
            AddRow("▸  " + name, true, () => { _relativeFolder = Path.GetRelativePath(_assetsRoot, dir); Refresh(); });
            shown++;
        }

        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(Path.GetFileName))
        {
            var name = Path.GetFileName(file);
            if (!Matches(name, filter) || !MatchesCategory(file, name, category)) continue;
            var ext = Path.GetExtension(name).ToLowerInvariant();
            AddFileRow(name, BadgeForFile(file, ext), InstantiateActionFor(file, ext), ReimportActionFor(file, ext));
            shown++;
        }

        if (string.IsNullOrWhiteSpace(_status.StringValue) || _status.StringValue.Contains("item"))
            _status.StringValue = $"{shown} item{(shown == 1 ? "" : "s")} · {category}";
        NeedsLayout = true;
    }

    private static bool Matches(string value, string filter) => string.IsNullOrWhiteSpace(filter) || value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesCategory(string file, string name, string category)
    {
        if (category == "All Assets") return true;
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext == ".tasset" && TryReadAssetType(file, out var type))
        {
            return category switch
            {
                "Models" => type is AssetType.Model or AssetType.Mesh,
                "Textures" => type == AssetType.Texture,
                "Audio" => type == AssetType.Audio,
                "Terrain" => type == AssetType.Terrain,
                _ => true
            };
        }
        return category switch
        {
            "Models" => ext is ".glb" or ".gltf" or ".fbx" or ".obj" or ".blend" or ".usd" or ".usda" or ".usdc" or ".usdz",
            "Textures" => ext is ".png" or ".jpg" or ".jpeg" or ".tga" or ".hdr" or ".exr" or ".dds",
            "Audio" => ext is ".wav" or ".ogg" or ".flac",
            "Scenes" => ext == ".tactixscene",
            "Terrain" => ext == ".tasset" && name.Contains("terrain", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private void AddRow(string title, bool folder, Action action)
    {
        var button = new NSButton(new CGRect(0, 0, 520, 26)) { Title = title, BezelStyle = NSBezelStyle.Inline, Alignment = NSTextAlignment.Left, Font = NSFont.SystemFontOfSize(11), ContentTintColor = folder ? EditorTheme.Accent : EditorTheme.Text };
        button.Activated += (_, _) => action();
        _rows.AddArrangedSubview(button);
    }

    private void AddFileRow(string name, string badge, Action? instantiate, Action? reimport)
    {
        var row = new NSView(new CGRect(0, 0, 760, 26));
        var nameLabel = EditorTheme.Label(name, 11); nameLabel.Frame = new CGRect(8, 4, 390, 18); row.AddSubview(nameLabel);
        var typeLabel = EditorTheme.Label(badge, 9, true, true); typeLabel.Alignment = NSTextAlignment.Right; typeLabel.Frame = new CGRect(400, 5, 80, 16); row.AddSubview(typeLabel);
        var x = 492;
        if (instantiate != null)
        {
            var button = new NSButton(new CGRect(x, 3, 96, 20)) { Title = "Add to Scene" };
            EditorTheme.StyleButton(button, true);
            button.Activated += (_, _) => instantiate();
            row.AddSubview(button);
            x += 102;
        }
        if (reimport != null)
        {
            var button = new NSButton(new CGRect(x, 3, 84, 20)) { Title = "Reimport" };
            EditorTheme.StyleButton(button);
            button.Activated += (_, _) => reimport();
            row.AddSubview(button);
        }
        _rows.AddArrangedSubview(row);
    }

    private static string BadgeForFile(string file, string extension)
    {
        if (extension == ".tasset")
        {
            return TryReadAssetType(file, out var type) ? type switch
            {
                AssetType.Model => "MODEL", AssetType.Mesh => "MESH", AssetType.Material => "MAT", AssetType.Texture => "TEXTURE",
                AssetType.Terrain => "TERRAIN", AssetType.Animation => "ANIM", AssetType.Skeleton => "SKEL", _ => "ASSET"
            } : "ASSET";
        }
        return extension switch
        {
            ".tactixscene" => "SCENE",
            ".glb" or ".gltf" or ".fbx" or ".obj" or ".blend" or ".usd" or ".usda" or ".usdc" or ".usdz" => "MODEL",
            ".tactiximport.json" => "META",
            ".png" or ".jpg" or ".jpeg" or ".tga" or ".hdr" or ".exr" or ".dds" => "TEXTURE",
            ".wav" or ".ogg" or ".flac" => "AUDIO",
            _ => extension.TrimStart('.').ToUpperInvariant()
        };
    }

    private static bool TryReadAssetType(string file, out AssetType type)
    {
        try { type = JsonAssetSerializer.Load(file).Meta.Type; return true; }
        catch { type = AssetType.Unknown; return false; }
    }

    private Action? InstantiateActionFor(string file, string extension)
    {
        if (extension != ".tasset") return null;
        try
        {
            var assetFile = JsonAssetSerializer.Load(file);
            if (assetFile.Meta.Type != AssetType.Mesh) return null;
            var guid = assetFile.Meta.Guid;
            var name = assetFile.Meta.Name;
            return () =>
            {
                _commands.Execute(new CreateAssetMeshCommand(_world, _selection, guid, name));
                _status.StringValue = $"Added {name} to Scene";
            };
        }
        catch { return null; }
    }

    private Action? ReimportActionFor(string file, string extension)
    {
        if (extension != ".tasset") return null;
        try
        {
            var assetFile = JsonAssetSerializer.Load(file);
            if (assetFile.Meta.Type != AssetType.Model || string.IsNullOrWhiteSpace(assetFile.Meta.SourcePath)) return null;
            return () => ReimportAsset(file);
        }
        catch { return null; }
    }

    private void ImportFromFileDialog(bool blenderOnly)
    {
        using var panel = NSOpenPanel.OpenPanel;
        panel.Title = blenderOnly ? "Import Blender Source (.blend)" : "Import Model / Asset File";
        panel.Prompt = "Import";
        panel.Message = blenderOnly
            ? "Choose a .blend file. TACTIX will launch Blender in the background, convert it to GLB, then create a TACTIX model asset."
            : "Choose a supported source file. OBJ files become native TACTIX meshes that can be added directly to the Scene.";
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.AllowsMultipleSelection = !blenderOnly;

        if (panel.RunModal() != (nint)NSModalResponse.OK) return;

        var paths = panel.Urls.Select(u => u.Path).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if (blenderOnly && paths.Any(p => !string.Equals(Path.GetExtension(p), ".blend", StringComparison.OrdinalIgnoreCase)))
        {
            _status.StringValue = "Blender import requires a .blend file";
            return;
        }

        _status.StringValue = blenderOnly ? "Launching Blender and converting source…" : $"Importing {paths.Length} source file{(paths.Length == 1 ? "" : "s")}…";
        var results = new List<AssetImportResult>();
        foreach (var path in paths) results.Add(ImportPath(path));
        ReportResults(results);
    }

    private void ImportPending()
    {
        try
        {
            _status.StringValue = "Scanning Imports for pending assets…";
            var results = _importPipeline.ImportPending(_assets, "Assets/Models");
            if (results.Count == 0) { _status.StringValue = "No pending imports in Imports"; return; }
            ReportResults(results);
        }
        catch (Exception ex) { _status.StringValue = "Import pending failed: " + ex.Message; }
    }

    private void ReimportAsset(string assetFilePath)
    {
        try
        {
            var assetFile = JsonAssetSerializer.Load(assetFilePath);
            var sourcePath = ResolveSourcePath(assetFile.Meta.SourcePath);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) { _status.StringValue = "Reimport source missing: " + assetFile.Meta.SourcePath; return; }
            _status.StringValue = Path.GetExtension(sourcePath).Equals(".blend", StringComparison.OrdinalIgnoreCase) ? "Reimporting through Blender…" : "Reimporting asset…";
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["assetProjectPath"] = assetFile.Meta.ProjectPath };
            ReportResults([_importPipeline.Import(_assets, new AssetImportRequest(sourcePath, "Assets/Models", settings))]);
        }
        catch (Exception ex) { _status.StringValue = "Reimport failed: " + ex.Message; }
    }

    private AssetImportResult ImportPath(string sourcePath) => _importPipeline.Import(_assets, new AssetImportRequest(sourcePath, "Assets/Models"));

    private void ReportResults(IReadOnlyList<AssetImportResult> results)
    {
        _assets.ScanAssetsFolder();
        RevealLastImported(results);
        Refresh();
        var failures = results.Where(r => !r.Success).ToArray();
        if (failures.Length > 0) { _status.StringValue = "Import failed: " + failures[0].Message; return; }
        var importedCount = results.Sum(r => r.Assets.Count);
        _status.StringValue = importedCount switch
        {
            0 => "No assets imported",
            1 => "Import complete: " + results.First(r => r.Assets.Count > 0).Assets[0].ProjectPath,
            _ => $"Import complete: {importedCount} assets"
        };
    }

    private void RevealLastImported(IReadOnlyList<AssetImportResult> results)
    {
        var asset = results.SelectMany(r => r.Assets).LastOrDefault();
        if (asset == null) return;
        var folder = Path.GetDirectoryName(asset.ProjectPath)?.Replace('\\', '/') ?? "Assets";
        _relativeFolder = folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? folder["Assets/".Length..] : "";
    }

    private void RefreshFromDisk()
    {
        _assets.ScanAssetsFolder();
        Refresh();
        _status.StringValue = "Project browser refreshed";
    }

    private string ResolveSourcePath(string sourcePath)
    {
        if (Path.IsPathRooted(sourcePath)) return sourcePath;
        return Path.GetFullPath(Path.Combine(_projectRoot, sourcePath));
    }
}
