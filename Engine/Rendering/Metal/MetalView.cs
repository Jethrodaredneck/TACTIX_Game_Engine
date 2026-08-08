using System.Runtime.InteropServices;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Metal;
using TACTIX.Engine.Core.Logging;

namespace TACTIX.Engine.Rendering.Metal;

public sealed class MetalView : NSView
{
    private readonly CAMetalLayer _metalLayer;
    private MetalRenderer? _renderer;
    private NSTimer? _frameTimer;
    private int _tick;
    private CGSize _lastDrawableSize;
    private bool _initialized;

    public CAMetalLayer MetalLayer => _metalLayer;

    public MetalView(CGRect frame, IMTLDevice device) : base(frame)
    {
        WantsLayer = true;

        AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;

        _metalLayer = new CAMetalLayer
        {
            Device = device,
            PixelFormat = MTLPixelFormat.BGRA8Unorm,
            FramebufferOnly = true,
            PresentsWithTransaction = false
        };

        Layer = _metalLayer;
        _initialized = true;
        ResizeDrawable();
    }

    public void SetRenderer(MetalRenderer renderer)
    {
        _renderer = renderer;

        if (Window != null)
        {
            StartRenderLoop();
        }
    }

    private void StartRenderLoop()
    {
        if (_renderer == null)
            return;

        _frameTimer?.Invalidate();
        _frameTimer = NSTimer.CreateRepeatingScheduledTimer(1.0 / 60.0, _ =>
        {
            if (_tick++ == 0)
                Log.Info("MetalView: render loop started");

            ResizeDrawable();

            _renderer.Draw();
        });
        NSRunLoop.Main.AddTimer(_frameTimer, NSRunLoopMode.Common);
    }

    public override bool WantsUpdateLayer => true;

    public override void UpdateLayer()
    {
        base.UpdateLayer();
        if (_initialized)
            ResizeDrawable();
    }

    public override void SetFrameSize(CGSize newSize)
    {
        base.SetFrameSize(newSize);
        if (_initialized)
            ResizeDrawable();
    }

    private void ResizeDrawable()
    {
        if (!_initialized)
            return;

        var scale = Window?.BackingScaleFactor ?? NSScreen.MainScreen?.BackingScaleFactor ?? 1.0;
        _metalLayer.ContentsScale = (System.Runtime.InteropServices.NFloat)scale;
        _metalLayer.DrawableSize = new CGSize(Bounds.Width * scale, Bounds.Height * scale);
        if (_metalLayer.DrawableSize.Width != _lastDrawableSize.Width || _metalLayer.DrawableSize.Height != _lastDrawableSize.Height)
        {
            _lastDrawableSize = _metalLayer.DrawableSize;
            Log.Info($"DrawableSize: {_metalLayer.DrawableSize.Width}x{_metalLayer.DrawableSize.Height} scale:{scale:0.00}");
        }
    }

    public override void ViewDidMoveToWindow()
    {
        base.ViewDidMoveToWindow();

        if (Window == null)
        {
            _frameTimer?.Invalidate();
            _frameTimer = null;
            return;
        }

        StartRenderLoop();
    }

    public override void RemoveFromSuperview()
    {
        _frameTimer?.Invalidate();
        _frameTimer = null;
        base.RemoveFromSuperview();
    }
}
