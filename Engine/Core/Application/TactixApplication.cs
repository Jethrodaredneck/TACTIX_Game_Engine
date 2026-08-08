using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppKit;
using CoreGraphics;
using Foundation;
using Metal;
using TACTIX.Engine.Core.Input;
using TACTIX.Engine.Core.Logging;
using TACTIX.Engine.Core.Time;
using TACTIX.Engine.Core.Windowing;
using TACTIX.Engine.Rendering.Metal;
using TACTIX.Editor.UI.Docking;
using TACTIX.Engine.Runtime.Scene;
using TACTIX.Engine.AI;
using TACTIX.Engine.Runtime.ECS;
using TACTIX.Editor.Scene;
using TACTIX.Engine.Assets.Database;

namespace TACTIX.Engine.Core.Application;

public sealed class TactixApplication
{
    private readonly InputState _input = new();
    private readonly FixedStepClock _clock = new(1.0 / 60.0);

    private TactixWindow? _window;
    private DockHostView? _dockHost;
    private MetalView? _view;
    private MetalRenderer? _renderer;
    private NSTimer? _timer;
    private AIBridgeServer? _aiBridge;
    private AssetDatabase? _assetDatabase;

    private readonly SceneManager _sceneManager = new();
    private readonly EditorSelection _selection = new();
    private readonly EditorCommandStack _commands = new();

    public void Start()
    {
        try { File.Delete("/tmp/tactix_boottrace.txt"); } catch { }
        Log.Info("Starting TACTIX...");
        Trace("A: Start() entered");

        var device = MTLDevice.SystemDefault;
        if (device == null)
            throw new InvalidOperationException("Metal is not supported on this machine.");
        Trace("B: got SystemDefault device");

        var frame = new CGRect(200, 200, 1280, 720);
        _window = new TactixWindow(frame, "TACTIX", _input);
        Trace("C: window created");

        var host = _window.ContentView;
        if (host == null)
        {
            host = new NSView(frame);
            _window.ContentView = host;
        }

        var bounds = host.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            bounds = new CGRect(0, 0, frame.Width, frame.Height);

        Trace($"C2: host bounds = {bounds.Width}x{bounds.Height}");

        try
        {
            var projectRoot = FindProjectRoot();
            Trace($"C2.1: project root = {projectRoot}");

            _assetDatabase = new AssetDatabase(projectRoot);
            _assetDatabase.Initialize();
            Trace("C2.2: asset database initialized");

            var scene = new Scene("Main");
            var cube = scene.World.CreateEntity();
            scene.World.Add(cube, new NameComponent("TACTIX Cube"));
            var cubeTransform = TransformComponent.Identity;
            cubeTransform.Rotation = new System.Numerics.Vector3(18, 28, 0);
            cubeTransform.Scale = new System.Numerics.Vector3(0.85f);
            scene.World.Add(cube, cubeTransform);
            scene.World.Add(cube, new MeshRendererComponent(BuiltInMesh.Cube));
            _sceneManager.Load(scene);
            _selection.Select(cube);
            Trace("C2.3: default scene created");

            // Construct the bridge so the AI panel has a stable object, but do not let
            // listener/manifest startup block the editor shell from appearing.
            _aiBridge = new AIBridgeServer(projectRoot, () => new
            {
                engine = "TACTIX",
                projectRoot,
                activeScene = _sceneManager.ActiveScene?.Name,
                permissions = new { read = true, write = _aiBridge?.AllowEdits ?? false }
            });

            _dockHost = new DockHostView(bounds, device, _aiBridge, scene, _selection, _commands, _assetDatabase)
            {
                Frame = host.Bounds,
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
            };

            host.AddSubview(_dockHost);
            _view = _dockHost.Viewport.MetalView;
            Trace("D: DockHostView created + attached");

            try
            {
                _aiBridge.Start();
                Trace($"D2: AI Bridge started at {_aiBridge.BaseUrl}");
            }
            catch (Exception aiEx)
            {
                Log.Warn($"AI Bridge unavailable: {aiEx.Message}");
                Trace("D2_WARN: AI Bridge startup failed but editor continues");
                Trace(aiEx.ToString());
            }
        }
        catch (Exception ex)
        {
            Trace("D_FAIL: editor shell initialization threw");
            Trace(ex.ToString());
            ShowStartupFailure(host, ex);
            return;
        }

        Trace("E: about to load shader");
        string shaderSource;
        try
        {
            shaderSource = LoadShaderSource("Triangle.metal");
            Trace($"F: shader loaded (len={shaderSource.Length})");
        }
        catch (Exception ex)
        {
            Trace("F_FAIL: shader load threw");
            Trace(ex.ToString());
            ShowStartupFailure(host, ex, "Renderer shader could not be loaded. The editor shell is still available.");
            return;
        }

        try
        {
            _renderer = new MetalRenderer(device, _view!.MetalLayer, shaderSource);
            _renderer.BindScene(_sceneManager.ActiveScene!.World);
            _renderer.BindAssets(_assetDatabase!);
            _dockHost!.Viewport.AttachRenderer(_renderer);
            Trace("G: renderer created + scene/assets bound");

            _view.SetRenderer(_renderer);
            Trace("H: renderer attached to view");
        }
        catch (Exception ex)
        {
            Trace("H_FAIL: renderer setup threw");
            Trace(ex.ToString());
            ShowStartupFailure(host, ex, "Metal renderer setup failed. The editor shell is still available.");
            return;
        }

        _clock.Start();
        _timer = NSTimer.CreateRepeatingScheduledTimer(1.0 / 60.0, _ => Tick());
        NSRunLoop.Main.AddTimer(_timer, NSRunLoopMode.Common);

        Trace("Z: Start() completed");
    }

    public void Stop()
    {
        _dockHost?.SaveLayout();

        _timer?.Invalidate();
        _timer?.Dispose();
        _timer = null;

        _aiBridge?.Dispose();
        _aiBridge = null;

        _renderer?.Dispose();
        _renderer = null;
        _assetDatabase = null;

        Log.Info("Stopped.");
    }

    private void Tick()
    {
        _input.BeginFrame();

        var steps = _clock.Pump();
        for (int i = 0; i < steps; i++)
            _sceneManager.FixedUpdate(_clock.FixedDeltaSeconds, _input);

        _sceneManager.Update(_clock.UnscaledDeltaSeconds, _input);
    }

    private static string FindProjectRoot()
    {
        // Development/source-tree runs keep using the checked-out TACTIX project.
        var starts = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var start in starts)
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;

            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TACTIX.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        // A published .app does not contain TACTIX.csproj and Finder may launch it
        // with '/' as cwd. Never use cwd as the packaged project root; it can be
        // unwritable and previously caused the editor to fail before DockHostView.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var packagedProjectRoot = Path.Combine(appData, "TACTIX", "DefaultProject");
        Directory.CreateDirectory(packagedProjectRoot);
        return packagedProjectRoot;
    }

    private static void ShowStartupFailure(NSView host, Exception ex, string? summary = null)
    {
        try
        {
            var panel = new NSView(host.Bounds)
            {
                WantsLayer = true,
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
            };
            panel.Layer!.BackgroundColor = NSColor.FromRgb(28, 29, 33).CGColor;

            var title = new NSTextField(new CGRect(28, Math.Max(80, host.Bounds.Height - 74), Math.Max(300, host.Bounds.Width - 56), 28))
            {
                StringValue = "TACTIX startup problem",
                Editable = false,
                Selectable = false,
                Bezeled = false,
                DrawsBackground = false,
                TextColor = NSColor.FromRgb(230, 180, 95),
                Font = NSFont.BoldSystemFontOfSize(18),
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin
            };

            var details = new NSTextField(new CGRect(28, 28, Math.Max(300, host.Bounds.Width - 56), Math.Max(40, host.Bounds.Height - 118)))
            {
                StringValue = (summary ?? "The editor could not finish startup.") + "\n\n" + ex.Message + "\n\nDetails: /tmp/tactix_boottrace.txt",
                Editable = false,
                Selectable = true,
                Bezeled = false,
                DrawsBackground = false,
                TextColor = NSColor.FromRgb(205, 207, 214),
                Font = NSFont.SystemFontOfSize(13),
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
                LineBreakMode = NSLineBreakMode.ByWordWrapping,
                UsesSingleLineMode = false
            };

            panel.AddSubview(title);
            panel.AddSubview(details);
            host.AddSubview(panel);
        }
        catch
        {
            // Boot trace remains available even if AppKit cannot render the panel.
        }
    }

    private static void Trace(string msg)
    {
        try
        {
            File.AppendAllText("/tmp/tactix_boottrace.txt", msg + "\n");
        }
        catch { }
    }

    private static string LoadShaderSource(string fileName)
    {
        var candidates = new List<string>();

        try
        {
            var res = NSBundle.MainBundle?.ResourcePath;
            if (!string.IsNullOrWhiteSpace(res))
            {
                candidates.Add(Path.Combine(res!, "Shaders", fileName));
                candidates.Add(Path.Combine(res!, fileName));
            }
        }
        catch { }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Shaders", fileName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, fileName));

        var cwd = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(cwd, "Shaders", fileName));
        candidates.Add(Path.Combine(cwd, fileName));

        foreach (var p in candidates.Distinct())
        {
            if (File.Exists(p))
                return File.ReadAllText(p);
        }

        var tried = string.Join("\n", candidates.Distinct());
        throw new FileNotFoundException($"Shader not found: {fileName}\nTried:\n{tried}");
    }
}