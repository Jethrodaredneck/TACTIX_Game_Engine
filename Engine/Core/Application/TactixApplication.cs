

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

        // Build the editor shell around the existing Metal viewport. The renderer
        // stays unchanged; only the view hierarchy changes so editor panels can dock
        // around it.
        var host = _window.ContentView;
        if (host == null)
        {
            host = new NSView(frame);
            _window.ContentView = host;
        }

        var bounds = host.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            bounds = frame;

        Trace($"C2: host bounds = {bounds.Width}x{bounds.Height}");

        try
        {
            var projectRoot = FindProjectRoot();
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
            _aiBridge = new AIBridgeServer(projectRoot, () => new
            {
                engine = "TACTIX",
                projectRoot,
                activeScene = _sceneManager.ActiveScene?.Name,
                permissions = new { read = true, write = _aiBridge?.AllowEdits ?? false }
            });
            _aiBridge.Start();
            Trace($"C3: AI Bridge started at {_aiBridge.BaseUrl}");

            _dockHost = new DockHostView(bounds, device, _aiBridge, scene, _selection, _commands, projectRoot)
            {
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
            };

            host.AddSubview(_dockHost);
            _view = _dockHost.Viewport.MetalView;
            Trace("D: DockHostView created + MetalView docked in Viewport panel");
        }
        catch (Exception ex)
        {
            Trace("D_FAIL: DockHostView/MetalView create threw");
            Trace(ex.ToString());
            throw;
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
            throw;
        }

        try
        {
            _renderer = new MetalRenderer(device, _view!.MetalLayer, shaderSource);
            _renderer.BindScene(_sceneManager.ActiveScene!.World, _selection);
            Trace("G: renderer created + scene bound");

            _view.SetRenderer(_renderer);
            Trace("H: renderer attached to view");
        }
        catch (Exception ex)
        {
            Trace("H_FAIL: renderer setup threw");
            Trace(ex.ToString());
            throw;
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
        var starts = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var start in starts)
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TACTIX.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        // Published app should still have a writable container-local fallback.
        return Directory.GetCurrentDirectory();
    }

    private static void Trace(string msg)
    {
        try
        {
            File.AppendAllText("/tmp/tactix_boottrace.txt", msg + "\n");
        }
        catch
        {
            // swallow
        }
    }

    private static string LoadShaderSource(string fileName)
    {
        // Prefer app bundle Resources/Shaders (publish output).
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
        catch
        {
            // ignore
        }

        // Fallbacks for running from output folders.
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