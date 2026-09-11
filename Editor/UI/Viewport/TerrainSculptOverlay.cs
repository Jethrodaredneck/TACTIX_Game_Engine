using AppKit;
using CoreGraphics;
using System.Numerics;
using TACTIX.Editor.Scene;
using TACTIX.Editor.Terrain;
using TACTIX.Editor.UI.Theme;
using TACTIX.Engine.Assets.Database;
using TACTIX.Engine.Assets.Formats;
using TACTIX.Engine.Core.Logging;
using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Editor.UI.Viewport;

/// <summary>
/// Editor-only terrain sculpt surface layered above the regular scene viewport.
/// When inactive it only receives input over its Terrain button, so the existing
/// transform/picking surface remains untouched. When active it owns left-drag sculpting
/// while forwarding camera navigation to the normal viewport.
/// </summary>
public sealed class TerrainSculptOverlay : NSView
{
    private static readonly long PreviewIntervalTicks = TimeSpan.TicksPerMillisecond * 75;

    private readonly ViewportPanelView _viewport;
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly EditorCommandStack _commands;
    private readonly AssetDatabase _assets;

    private readonly NSButton _toggleButton;
    private readonly NSPopUpButton _modePopup;
    private readonly NSButton _radiusDownButton;
    private readonly NSButton _radiusUpButton;
    private readonly NSButton _strengthDownButton;
    private readonly NSButton _strengthUpButton;
    private readonly NSTextField _radiusLabel;
    private readonly NSTextField _strengthLabel;
    private readonly NSTextField _hintLabel;

    private bool _active;
    private bool _sculpting;
    private Entity? _strokeEntity;
    private TerrainAsset _strokeBefore;
    private TerrainAsset _strokePreview;
    private float _strokeFlattenHeight;
    private Vector3? _lastStampWorld;
    private TerrainHit? _lastHit;
    private long _nextPreviewWriteTicks;

    private float _radiusWorld = 4f;
    private float _strength = 0.45f;

    public TerrainSculptOverlay(
        CGRect frame,
        ViewportPanelView viewport,
        World world,
        EditorSelection selection,
        EditorCommandStack commands,
        AssetDatabase assets) : base(frame)
    {
        _viewport = viewport;
        _world = world;
        _selection = selection;
        _commands = commands;
        _assets = assets;

        AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;

        _toggleButton = new NSButton(new CGRect(0, 0, 78, 24))
        {
            Title = "Terrain",
            ToolTip = "Toggle terrain sculpt mode"
        };
        EditorTheme.StyleButton(_toggleButton, true);
        _toggleButton.Activated += (_, _) => SetActive(!_active);
        AddSubview(_toggleButton);

        _modePopup = new NSPopUpButton(new CGRect(0, 0, 132, 24), false);
        _modePopup.AddItems(new[] { "Raise / Lower", "Smooth", "Flatten" });
        _modePopup.ToolTip = "Terrain brush mode";
        _modePopup.Activated += (_, _) =>
        {
            if (_sculpting) CommitStroke();
            UpdateControlLabels();
        };
        AddSubview(_modePopup);

        _radiusDownButton = MakeAdjustButton("−", "Decrease brush radius", () => AdjustRadius(1f / 1.25f));
        _radiusUpButton = MakeAdjustButton("+", "Increase brush radius", () => AdjustRadius(1.25f));
        _strengthDownButton = MakeAdjustButton("−", "Decrease brush strength", () => AdjustStrength(-0.1f));
        _strengthUpButton = MakeAdjustButton("+", "Increase brush strength", () => AdjustStrength(0.1f));

        _radiusLabel = EditorTheme.Label("", 10, true, true);
        _strengthLabel = EditorTheme.Label("", 10, true, true);
        _hintLabel = EditorTheme.Label("", 9, true);
        AddSubview(_radiusLabel);
        AddSubview(_strengthLabel);
        AddSubview(_hintLabel);

        SetActive(false);
    }

    public override bool AcceptsFirstResponder() => true;

    public override void Layout()
    {
        base.Layout();
        var topY = (float)Math.Max(8, Bounds.Height - 38);
        const float startX = 246f;

        _toggleButton.Frame = new CGRect(startX, topY, 78, 24);
        _modePopup.Frame = new CGRect(startX + 84, topY, 132, 24);
        _hintLabel.Frame = new CGRect(startX + 222, topY + 4, Math.Max(80, Bounds.Width - startX - 232), 17);

        var rowY = topY - 29;
        _radiusDownButton.Frame = new CGRect(startX, rowY, 24, 22);
        _radiusLabel.Frame = new CGRect(startX + 29, rowY + 3, 93, 17);
        _radiusUpButton.Frame = new CGRect(startX + 126, rowY, 24, 22);
        _strengthDownButton.Frame = new CGRect(startX + 160, rowY, 24, 22);
        _strengthLabel.Frame = new CGRect(startX + 189, rowY + 3, 103, 17);
        _strengthUpButton.Frame = new CGRect(startX + 296, rowY, 24, 22);
    }

    public override NSView HitTest(CGPoint aPoint)
    {
        var hit = base.HitTest(aPoint);
        if (_active)
            return hit;
        return ReferenceEquals(hit, _toggleButton) ? hit : null!;
    }

    public override void DrawRect(CGRect dirtyRect)
    {
        base.DrawRect(dirtyRect);
        if (!_active || !_lastHit.HasValue)
            return;

        var hit = _lastHit.Value;
        var height = (float)Math.Max(Bounds.Height, 1);
        if (!_viewport.Camera.TryProject(hit.WorldPosition, (float)Bounds.Width, height, out var center, out _))
            return;

        var unitsPerPixel = _viewport.Camera.WorldUnitsPerPixel(hit.WorldPosition, height);
        var radiusPixels = Math.Clamp(_radiusWorld / MathF.Max(unitsPerPixel, 0.00001f), 4f, 4096f);

        EditorTheme.Accent.ColorWithAlphaComponent((nfloat)0.90).SetStroke();
        var path = new NSBezierPath { LineWidth = 2f };
        const int segments = 48;
        for (var i = 0; i <= segments; i++)
        {
            var angle = (float)i / segments * MathF.PI * 2f;
            var point = new CGPoint(
                center.X + MathF.Cos(angle) * radiusPixels,
                center.Y + MathF.Sin(angle) * radiusPixels);
            if (i == 0) path.MoveTo(point); else path.LineTo(point);
        }
        path.ClosePath();
        path.Stroke();
    }

    public override void MouseDown(NSEvent theEvent)
    {
        if (!_active)
        {
            base.MouseDown(theEvent);
            return;
        }

        Window?.MakeFirstResponder(this);
        var point = ConvertPointFromView(theEvent.LocationInWindow, null);
        if (!TryPick(point, out var hit))
        {
            _lastHit = null;
            NeedsDisplay = true;
            return;
        }

        _selection.Select(hit.Entity);
        try
        {
            BeginStroke(hit, theEvent.ModifierFlags);
        }
        catch (Exception ex)
        {
            HandleSculptFailure(ex);
        }
    }

    public override void MouseDragged(NSEvent theEvent)
    {
        if (!_active || !_sculpting || !_strokeEntity.HasValue)
            return;

        var point = ConvertPointFromView(theEvent.LocationInWindow, null);
        if (!TryPick(point, out var hit, _strokeEntity))
            return;

        _lastHit = hit;
        var minimumSpacing = MathF.Max(0.05f, _radiusWorld * 0.12f);
        if (_lastStampWorld.HasValue && Vector3.Distance(_lastStampWorld.Value, hit.WorldPosition) < minimumSpacing)
        {
            NeedsDisplay = true;
            return;
        }

        try
        {
            ApplyStamp(hit, theEvent.ModifierFlags);
        }
        catch (Exception ex)
        {
            HandleSculptFailure(ex);
        }
    }

    public override void MouseUp(NSEvent theEvent)
    {
        if (_sculpting)
            CommitStroke();
    }

    public override void RightMouseDown(NSEvent theEvent)
    {
        _lastHit = null;
        _viewport.RightMouseDown(theEvent);
        Window?.MakeFirstResponder(this);
        NeedsDisplay = true;
    }

    public override void RightMouseDragged(NSEvent theEvent)
    {
        _viewport.RightMouseDragged(theEvent);
        NeedsDisplay = true;
    }

    public override void RightMouseUp(NSEvent theEvent) => _viewport.RightMouseUp(theEvent);

    public override void OtherMouseDown(NSEvent theEvent)
    {
        _lastHit = null;
        _viewport.OtherMouseDown(theEvent);
        Window?.MakeFirstResponder(this);
        NeedsDisplay = true;
    }

    public override void OtherMouseDragged(NSEvent theEvent)
    {
        _viewport.OtherMouseDragged(theEvent);
        NeedsDisplay = true;
    }

    public override void OtherMouseUp(NSEvent theEvent) => _viewport.OtherMouseUp(theEvent);

    public override void ScrollWheel(NSEvent theEvent)
    {
        _lastHit = null;
        _viewport.ScrollWheel(theEvent);
        NeedsDisplay = true;
    }

    public override void KeyDown(NSEvent theEvent)
    {
        var key = theEvent.CharactersIgnoringModifiers?.ToLowerInvariant();
        var command = (theEvent.ModifierFlags & NSEventModifierMask.CommandKeyMask) != 0;
        var shift = (theEvent.ModifierFlags & NSEventModifierMask.ShiftKeyMask) != 0;

        if (command && key == "z")
        {
            if (_sculpting) CancelStroke();
            if (shift) _commands.Redo(); else _commands.Undo();
            return;
        }

        switch (key)
        {
            case "t": SetActive(false); return;
            case "\u001b":
                if (_sculpting) CancelStroke(); else SetActive(false);
                return;
            case "[": AdjustRadius(1f / 1.25f); return;
            case "]": AdjustRadius(1.25f); return;
            case "-": AdjustStrength(-0.1f); return;
            case "=": AdjustStrength(0.1f); return;
            case "f": _viewport.FrameSelection(); return;
            case "q":
            case "w":
            case "e":
            case "r":
                SetActive(false);
                _viewport.KeyDown(theEvent);
                return;
        }

        base.KeyDown(theEvent);
    }

    private NSButton MakeAdjustButton(string title, string toolTip, Action action)
    {
        var button = new NSButton(new CGRect(0, 0, 24, 22)) { Title = title, ToolTip = toolTip };
        EditorTheme.StyleButton(button);
        button.Activated += (_, _) => action();
        AddSubview(button);
        return button;
    }

    private void SetActive(bool active)
    {
        if (_active == active)
        {
            UpdateControlVisibility();
            UpdateControlLabels();
            return;
        }

        if (!active && _sculpting)
            CancelStroke();

        _active = active;
        _lastHit = null;
        _toggleButton.Title = active ? "Terrain ✓" : "Terrain";
        _toggleButton.ContentTintColor = active ? EditorTheme.Accent : EditorTheme.Text;
        UpdateControlVisibility();
        UpdateControlLabels();
        if (active)
            Window?.MakeFirstResponder(this);
        NeedsDisplay = true;
    }

    private void UpdateControlVisibility()
    {
        _modePopup.Hidden = !_active;
        _radiusDownButton.Hidden = !_active;
        _radiusUpButton.Hidden = !_active;
        _strengthDownButton.Hidden = !_active;
        _strengthUpButton.Hidden = !_active;
        _radiusLabel.Hidden = !_active;
        _strengthLabel.Hidden = !_active;
        _hintLabel.Hidden = !_active;
    }

    private void UpdateControlLabels()
    {
        _radiusLabel.StringValue = $"Radius {_radiusWorld:0.##}";
        _strengthLabel.StringValue = $"Strength {_strength:0.##}";
        _hintLabel.StringValue = _active ? "⌥ lower  ⇧ smooth  ⌃ flatten  [ ] radius" : "";
    }

    private void AdjustRadius(float multiplier)
    {
        _radiusWorld = Math.Clamp(_radiusWorld * multiplier, 0.25f, 128f);
        UpdateControlLabels();
        NeedsDisplay = true;
    }

    private void AdjustStrength(float delta)
    {
        _strength = Math.Clamp(_strength + delta, 0.05f, 1f);
        UpdateControlLabels();
    }

    private bool TryPick(CGPoint point, out TerrainHit hit, Entity? onlyEntity = null)
    {
        var (origin, direction) = _viewport.Camera.ScreenPointToRay(
            (float)point.X,
            (float)point.Y,
            (float)Math.Max(Bounds.Width, 1),
            (float)Math.Max(Bounds.Height, 1));
        return TerrainRaycaster.TryPick(_world, _assets, origin, direction, out hit, onlyEntity);
    }

    private void BeginStroke(TerrainHit hit, NSEventModifierMask modifiers)
    {
        if (_sculpting)
            CommitStroke();

        var terrain = _assets.LoadTerrain(hit.TerrainAssetGuid);
        _strokeEntity = hit.Entity;
        _strokeBefore = Clone(terrain);
        _strokePreview = Clone(terrain);
        _strokeFlattenHeight = TerrainBrush.SampleHeightNormalized(terrain, hit.NormalizedX, hit.NormalizedZ);
        _lastStampWorld = null;
        _lastHit = hit;
        _nextPreviewWriteTicks = 0;
        _sculpting = true;
        ApplyStamp(hit, modifiers, forcePreview: true);
    }

    private void ApplyStamp(TerrainHit hit, NSEventModifierMask modifiers, bool forcePreview = false)
    {
        if (!_sculpting || !_strokeEntity.HasValue || hit.Entity != _strokeEntity.Value)
            return;

        var mode = SelectedMode();
        if ((modifiers & NSEventModifierMask.ShiftKeyMask) != 0)
            mode = TerrainBrushMode.Smooth;
        else if ((modifiers & NSEventModifierMask.ControlKeyMask) != 0)
            mode = TerrainBrushMode.Flatten;

        var strength = _strength;
        if (mode == TerrainBrushMode.RaiseLower && (modifiers & NSEventModifierMask.AlternateKeyMask) != 0)
            strength = -strength;

        var transform = _world.Get<TransformComponent>(_strokeEntity.Value);
        var horizontalScale = MathF.Max(
            0.0001f,
            MathF.Min(MathF.Abs(transform.Scale.X), MathF.Abs(transform.Scale.Z)));
        var localRadius = _radiusWorld / horizontalScale;
        var normalizedRadius = localRadius / MathF.Max(0.0001f, MathF.Min(_strokePreview.SizeX, _strokePreview.SizeZ));

        _strokePreview = TerrainBrush.Apply(
            _strokePreview,
            mode,
            hit.NormalizedX,
            hit.NormalizedZ,
            normalizedRadius,
            strength,
            _strokeFlattenHeight);

        // A 129x129 terrain expands to roughly 100k Metal vertices. Avoid rebuilding
        // that GPU buffer and rewriting JSON for every raw mouse event; preview at a
        // bounded cadence while retaining every in-memory brush stamp for the final commit.
        var nowTicks = DateTime.UtcNow.Ticks;
        if (forcePreview || nowTicks >= _nextPreviewWriteTicks)
        {
            SavePreview(_strokePreview);
            _nextPreviewWriteTicks = nowTicks + PreviewIntervalTicks;
        }

        _lastStampWorld = hit.WorldPosition;
        _lastHit = hit;
        NeedsDisplay = true;
    }

    private TerrainBrushMode SelectedMode()
        => (int)_modePopup.IndexOfSelectedItem switch
        {
            1 => TerrainBrushMode.Smooth,
            2 => TerrainBrushMode.Flatten,
            _ => TerrainBrushMode.RaiseLower
        };

    private void SavePreview(TerrainAsset terrain)
    {
        if (!_assets.Registry.TryGet(terrain.Guid, out var meta) || meta.Type != AssetType.Terrain)
            throw new InvalidOperationException($"Terrain asset is not registered: {terrain.Guid}");
        _assets.SaveTerrain(meta.ProjectPath, Clone(terrain), meta.Name);
    }

    private void CommitStroke()
    {
        if (!_sculpting)
            return;

        try
        {
            var before = Clone(_strokeBefore);
            var after = Clone(_strokePreview);

            // Restore the pre-stroke state first so the command stack is the sole owner
            // of the durable edit and one drag produces exactly one undo entry.
            SavePreview(before);
            if (!HeightsEqual(before, after))
                _commands.Execute(new SetTerrainAssetCommand(_assets, before, after));
        }
        catch (Exception ex)
        {
            Log.Warn($"Terrain sculpt commit failed: {ex.Message}");
            try { SavePreview(_strokeBefore); } catch { }
        }
        finally
        {
            ResetStroke();
        }
    }

    private void CancelStroke()
    {
        if (!_sculpting)
            return;

        try
        {
            SavePreview(_strokeBefore);
        }
        catch (Exception ex)
        {
            Log.Warn($"Terrain sculpt cancel failed: {ex.Message}");
        }
        finally
        {
            ResetStroke();
        }
    }

    private void ResetStroke()
    {
        _sculpting = false;
        _strokeEntity = null;
        _strokeBefore = default;
        _strokePreview = default;
        _lastStampWorld = null;
        _nextPreviewWriteTicks = 0;
        NeedsDisplay = true;
    }

    private void HandleSculptFailure(Exception ex)
    {
        Log.Warn($"Terrain sculpt failed: {ex.Message}");
        try { CancelStroke(); } catch { ResetStroke(); }
        _hintLabel.StringValue = "Terrain sculpt error — see Console/log";
    }

    private static TerrainAsset Clone(TerrainAsset terrain)
        => terrain with
        {
            Heights = terrain.Heights == null ? Array.Empty<float>() : (float[])terrain.Heights.Clone(),
            Layers = terrain.Layers == null ? Array.Empty<TerrainLayer>() : (TerrainLayer[])terrain.Layers.Clone()
        };

    private static bool HeightsEqual(TerrainAsset a, TerrainAsset b)
    {
        if (a.Heights.Length != b.Heights.Length)
            return false;
        for (var i = 0; i < a.Heights.Length; i++)
        {
            if (a.Heights[i] != b.Heights[i])
                return false;
        }
        return true;
    }
}
