using AppKit;
using CoreGraphics;
using Metal;
using System.Numerics;
using TACTIX.Editor.Scene;
using TACTIX.Engine.Rendering.Metal;
using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Editor.UI.Viewport;

public enum TransformTool
{
    Select,
    Translate,
    Rotate,
    Scale
}

public enum TransformSpace
{
    World,
    Local
}

/// <summary>
/// Interactive editor viewport. Camera navigation, picking, transform-tool state,
/// and gizmo interaction are editor concerns. The Metal renderer only consumes
/// camera data and selected entity identity.
/// </summary>
public sealed class ViewportPanelView : NSView
{
    private enum GizmoAxis { None = -1, X = 0, Y = 1, Z = 2 }

    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly EditorCommandStack _commands;
    private readonly NSTextField _help;
    private readonly InteractionSurface _interaction;
    private readonly NSButton[] _toolButtons;
    private readonly NSButton _spaceButton;

    private CGPoint _lastDragPoint;
    private MetalRenderer? _renderer;

    private bool _transforming;
    private Entity? _transformEntity;
    private TransformComponent _transformStart;
    private GizmoAxis _activeAxis = GizmoAxis.None;
    private CGPoint _transformMouseStart;
    private float _rotationMouseAngleStart;

    public MetalView MetalView { get; }
    public EditorCamera Camera { get; } = new();
    public TransformTool ActiveTool { get; private set; } = TransformTool.Translate;
    public TransformSpace ActiveSpace { get; private set; } = TransformSpace.World;

    public ViewportPanelView(CGRect frame, IMTLDevice device, World world, EditorSelection selection, EditorCommandStack commands) : base(frame)
    {
        _world = world;
        _selection = selection;
        _commands = commands;

        WantsLayer = true;
        Layer!.BackgroundColor = NSColor.Black.CGColor;

        MetalView = new MetalView(Bounds, device)
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
        };
        AddSubview(MetalView);

        _interaction = new InteractionSurface(Bounds, this)
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
        };
        AddSubview(_interaction);

        _help = new NSTextField(new CGRect(10, 8, 720, 20))
        {
            StringValue = "LMB Select/Gizmo   •   RMB Orbit   •   MMB Pan   •   Scroll Dolly   •   F Frame   •   Q/W/E/R Tools   •   X World/Local   •   Shift Snap",
            Editable = false,
            Selectable = false,
            Bezeled = false,
            DrawsBackground = false,
            TextColor = NSColor.FromWhite(0.86f, 0.92f),
            Font = NSFont.SystemFontOfSize(11),
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MaxYMargin
        };
        AddSubview(_help);

        _toolButtons =
        [
            MakeToolButton("Q Select", TransformTool.Select),
            MakeToolButton("W Move", TransformTool.Translate),
            MakeToolButton("E Rotate", TransformTool.Rotate),
            MakeToolButton("R Scale", TransformTool.Scale)
        ];
        foreach (var button in _toolButtons) AddSubview(button);

        _spaceButton = new NSButton(new CGRect(0, 0, 74, 24))
        {
            Title = "World",
            BezelStyle = NSBezelStyle.Rounded
        };
        _spaceButton.Activated += (_, _) =>
        {
            ActiveSpace = ActiveSpace == TransformSpace.World ? TransformSpace.Local : TransformSpace.World;
            _spaceButton.Title = ActiveSpace.ToString();
            _interaction.NeedsDisplay = true;
        };
        AddSubview(_spaceButton);

        _selection.Changed += OnEditorStateChanged;
        _world.Changed += OnEditorStateChanged;
        UpdateToolButtons();
    }

    public override void Layout()
    {
        base.Layout();
        const float gap = 6f;
        var x = 10f;
        var y = (float)Math.Max(8, Bounds.Height - 34);
        foreach (var button in _toolButtons)
        {
            button.Frame = new CGRect(x, y, 78, 24);
            x += 78 + gap;
        }
        _spaceButton.Frame = new CGRect(x + 4, y, 74, 24);
    }

    public void AttachRenderer(MetalRenderer renderer)
    {
        _renderer = renderer;
        SyncRendererView();
    }

    private NSButton MakeToolButton(string title, TransformTool tool)
    {
        var button = new NSButton(new CGRect(0, 0, 78, 24))
        {
            Title = title,
            BezelStyle = NSBezelStyle.Rounded
        };
        button.Activated += (_, _) => SetTool(tool);
        return button;
    }

    private void SetTool(TransformTool tool)
    {
        if (_transforming)
            CancelTransform();
        ActiveTool = tool;
        UpdateToolButtons();
        _interaction.NeedsDisplay = true;
    }

    private void UpdateToolButtons()
    {
        var labels = new[] { "Q Select", "W Move", "E Rotate", "R Scale" };
        for (var i = 0; i < _toolButtons.Length; i++)
            _toolButtons[i].Title = ((int)ActiveTool == i ? "● " : "") + labels[i];
    }

    private void OnEditorStateChanged()
    {
        if (_selection.ActiveEntity.HasValue && !_world.Exists(_selection.ActiveEntity.Value))
            _selection.Select(null);
        SyncRendererView();
        _interaction.NeedsDisplay = true;
    }

    private void SyncRendererView()
    {
        _renderer?.SetEditorView(Camera.Position, Camera.Right, Camera.Up, Camera.Forward, Camera.ProjectionScale, _selection.ActiveEntity);
    }

    public override bool AcceptsFirstResponder() => true;

    public override void MouseDown(NSEvent theEvent)
    {
        Window?.MakeFirstResponder(_interaction);
        var p = ConvertPointFromView(theEvent.LocationInWindow, null);
        _lastDragPoint = p;

        if (ActiveTool != TransformTool.Select && TryGetSelectedTransform(out var selectedEntity, out var selectedTransform))
        {
            var axis = HitTestGizmo(p, selectedTransform);
            if (axis != GizmoAxis.None)
            {
                BeginTransform(selectedEntity, selectedTransform, axis, p);
                return;
            }
        }

        _selection.Select(PickEntity(p));
    }

    public override void MouseDragged(NSEvent theEvent)
    {
        if (!_transforming || !_transformEntity.HasValue || !_world.Exists(_transformEntity.Value))
            return;

        var p = ConvertPointFromView(theEvent.LocationInWindow, null);
        ApplyTransformPreview(p, (theEvent.ModifierFlags & NSEventModifierMask.ShiftKeyMask) != 0);
        _lastDragPoint = p;
    }

    public override void MouseUp(NSEvent theEvent)
    {
        CommitTransform();
    }

    public override void RightMouseDown(NSEvent theEvent)
    {
        Window?.MakeFirstResponder(_interaction);
        _lastDragPoint = ConvertPointFromView(theEvent.LocationInWindow, null);
    }

    public override void RightMouseDragged(NSEvent theEvent)
    {
        var p = ConvertPointFromView(theEvent.LocationInWindow, null);
        Camera.Orbit((float)(p.X - _lastDragPoint.X), (float)(p.Y - _lastDragPoint.Y));
        _lastDragPoint = p;
        SyncRendererView();
        _interaction.NeedsDisplay = true;
    }

    public override void OtherMouseDown(NSEvent theEvent)
    {
        Window?.MakeFirstResponder(_interaction);
        _lastDragPoint = ConvertPointFromView(theEvent.LocationInWindow, null);
    }

    public override void OtherMouseDragged(NSEvent theEvent)
    {
        var p = ConvertPointFromView(theEvent.LocationInWindow, null);
        Camera.Pan((float)(p.X - _lastDragPoint.X), (float)(p.Y - _lastDragPoint.Y));
        _lastDragPoint = p;
        SyncRendererView();
        _interaction.NeedsDisplay = true;
    }

    public override void ScrollWheel(NSEvent theEvent)
    {
        Camera.Dolly((float)theEvent.ScrollingDeltaY);
        SyncRendererView();
        _interaction.NeedsDisplay = true;
    }

    public override void KeyDown(NSEvent theEvent)
    {
        var key = theEvent.CharactersIgnoringModifiers?.ToLowerInvariant();
        var command = (theEvent.ModifierFlags & NSEventModifierMask.CommandKeyMask) != 0;
        var shift = (theEvent.ModifierFlags & NSEventModifierMask.ShiftKeyMask) != 0;

        if (command && key == "z")
        {
            if (shift) _commands.Redo(); else _commands.Undo();
            return;
        }

        switch (key)
        {
            case "q": SetTool(TransformTool.Select); return;
            case "w": SetTool(TransformTool.Translate); return;
            case "e": SetTool(TransformTool.Rotate); return;
            case "r": SetTool(TransformTool.Scale); return;
            case "f": FrameSelection(); return;
            case "x":
                ActiveSpace = ActiveSpace == TransformSpace.World ? TransformSpace.Local : TransformSpace.World;
                _spaceButton.Title = ActiveSpace.ToString();
                _interaction.NeedsDisplay = true;
                return;
            case "\u001b":
                CancelTransform();
                return;
        }

        base.KeyDown(theEvent);
    }

    public void FrameSelection()
    {
        if (TryGetSelectedTransform(out _, out var transform))
        {
            var radius = MathF.Max(MathF.Abs(transform.Scale.X), MathF.Max(MathF.Abs(transform.Scale.Y), MathF.Abs(transform.Scale.Z)));
            Camera.Frame(transform.Position, MathF.Max(radius, 0.5f));
            SyncRendererView();
            _interaction.NeedsDisplay = true;
        }
    }

    private void BeginTransform(Entity entity, TransformComponent transform, GizmoAxis axis, CGPoint mouse)
    {
        _transforming = true;
        _transformEntity = entity;
        _transformStart = transform;
        _activeAxis = axis;
        _transformMouseStart = mouse;

        if (Camera.TryProject(transform.Position, (float)Bounds.Width, (float)Bounds.Height, out var center, out _))
            _rotationMouseAngleStart = MathF.Atan2((float)mouse.Y - center.Y, (float)mouse.X - center.X);
        else
            _rotationMouseAngleStart = 0f;

        _interaction.NeedsDisplay = true;
    }

    private void ApplyTransformPreview(CGPoint mouse, bool snap)
    {
        if (!_transformEntity.HasValue)
            return;

        var after = _transformStart;
        var axis = AxisVector(_activeAxis, _transformStart);
        var width = (float)Math.Max(Bounds.Width, 1);
        var height = (float)Math.Max(Bounds.Height, 1);

        switch (ActiveTool)
        {
            case TransformTool.Translate:
            {
                var scalar = AxisDragWorldDelta(_transformStart.Position, axis, _transformMouseStart, mouse, width, height);
                var applied = snap ? MathF.Round(scalar / 0.25f) * 0.25f : scalar;
                after.Position = _transformStart.Position + axis * applied;
                break;
            }
            case TransformTool.Rotate:
            {
                if (Camera.TryProject(_transformStart.Position, width, height, out var center, out _))
                {
                    var current = MathF.Atan2((float)mouse.Y - center.Y, (float)mouse.X - center.X);
                    var delta = NormalizeRadians(current - _rotationMouseAngleStart) * (180f / MathF.PI);
                    if (snap) delta = MathF.Round(delta / 15f) * 15f;
                    SetAxisComponent(ref after.Rotation, _activeAxis, GetAxisComponent(_transformStart.Rotation, _activeAxis) + delta);
                }
                break;
            }
            case TransformTool.Scale:
            {
                var screenDelta = AxisDragPixels(_transformStart.Position, axis, _transformMouseStart, mouse, width, height);
                var start = GetAxisComponent(_transformStart.Scale, _activeAxis);
                var magnitude = MathF.Max(MathF.Abs(start), 1f);
                var value = start + screenDelta * 0.01f * magnitude;
                if (snap) value = MathF.Round(value / 0.1f) * 0.1f;
                if (MathF.Abs(value) < 0.01f) value = MathF.CopySign(0.01f, value == 0f ? start : value);
                SetAxisComponent(ref after.Scale, _activeAxis, value);
                break;
            }
        }

        _world.Set(_transformEntity.Value, after);
        _interaction.NeedsDisplay = true;
    }

    private void CommitTransform()
    {
        if (!_transforming || !_transformEntity.HasValue)
        {
            ResetTransformState();
            return;
        }

        var entity = _transformEntity.Value;
        if (_world.Exists(entity) && _world.Has<TransformComponent>(entity))
        {
            var after = _world.Get<TransformComponent>(entity);
            if (!TransformsEqual(_transformStart, after))
            {
                // Preview is immediate, but the command stack owns the durable mutation.
                _world.Set(entity, _transformStart);
                _commands.Execute(new SetTransformCommand(_world, entity, _transformStart, after));
            }
        }
        ResetTransformState();
    }

    private void CancelTransform()
    {
        if (_transforming && _transformEntity.HasValue && _world.Exists(_transformEntity.Value))
            _world.Set(_transformEntity.Value, _transformStart);
        ResetTransformState();
    }

    private void ResetTransformState()
    {
        _transforming = false;
        _transformEntity = null;
        _activeAxis = GizmoAxis.None;
        _interaction.NeedsDisplay = true;
    }

    private Entity? PickEntity(CGPoint point)
    {
        var (origin, direction) = Camera.ScreenPointToRay((float)point.X, (float)point.Y, (float)Bounds.Width, (float)Bounds.Height);
        Entity? best = null;
        var bestT = float.PositiveInfinity;

        foreach (var (entity, mesh) in _world.Query<MeshRendererComponent>())
        {
            if (!_world.Has<TransformComponent>(entity))
                continue;

            var transform = _world.Get<TransformComponent>(entity);
            var scale = MathF.Max(MathF.Abs(transform.Scale.X), MathF.Max(MathF.Abs(transform.Scale.Y), MathF.Abs(transform.Scale.Z)));
            var radius = BaseRadius(mesh.Mesh) * MathF.Max(scale, 0.01f);
            if (RaySphere(origin, direction, transform.Position, radius, out var t) && t < bestT)
            {
                bestT = t;
                best = entity;
            }
        }
        return best;
    }

    private GizmoAxis HitTestGizmo(CGPoint point, TransformComponent transform)
    {
        return ActiveTool switch
        {
            TransformTool.Translate => HitTestLinearGizmo(point, transform),
            TransformTool.Scale => HitTestLinearGizmo(point, transform),
            TransformTool.Rotate => HitTestRotationGizmo(point, transform),
            _ => GizmoAxis.None
        };
    }

    private GizmoAxis HitTestLinearGizmo(CGPoint point, TransformComponent transform)
    {
        var width = (float)Bounds.Width;
        var height = (float)Bounds.Height;
        if (!Camera.TryProject(transform.Position, width, height, out var center, out _))
            return GizmoAxis.None;

        var length = Camera.WorldUnitsPerPixel(transform.Position, height) * 76f;
        var mouse = new Vector2((float)point.X, (float)point.Y);
        var bestAxis = GizmoAxis.None;
        var bestDistance = 10f;

        foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
        {
            var endWorld = transform.Position + AxisVector(axis, transform) * length;
            if (!Camera.TryProject(endWorld, width, height, out var end, out _))
                continue;
            var distance = DistanceToSegment(mouse, center, end);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestAxis = axis;
            }
        }
        return bestAxis;
    }

    private GizmoAxis HitTestRotationGizmo(CGPoint point, TransformComponent transform)
    {
        var mouse = new Vector2((float)point.X, (float)point.Y);
        var bestAxis = GizmoAxis.None;
        var bestDistance = 9f;
        foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
        {
            var points = GetRotationRingPoints(transform, axis, 48);
            for (var i = 1; i < points.Count; i++)
            {
                var d = DistanceToSegment(mouse, points[i - 1], points[i]);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestAxis = axis;
                }
            }
        }
        return bestAxis;
    }

    internal void DrawGizmo()
    {
        if (ActiveTool == TransformTool.Select || !TryGetSelectedTransform(out _, out var transform))
            return;

        switch (ActiveTool)
        {
            case TransformTool.Translate:
                DrawLinearGizmo(transform, scaleHandles: false);
                break;
            case TransformTool.Scale:
                DrawLinearGizmo(transform, scaleHandles: true);
                break;
            case TransformTool.Rotate:
                DrawRotationGizmo(transform);
                break;
        }
    }

    private void DrawLinearGizmo(TransformComponent transform, bool scaleHandles)
    {
        var width = (float)Bounds.Width;
        var height = (float)Bounds.Height;
        if (!Camera.TryProject(transform.Position, width, height, out var center, out _))
            return;

        var length = Camera.WorldUnitsPerPixel(transform.Position, height) * 76f;
        foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
        {
            var endWorld = transform.Position + AxisVector(axis, transform) * length;
            if (!Camera.TryProject(endWorld, width, height, out var end, out _))
                continue;

            AxisColor(axis, axis == _activeAxis).SetStroke();
            var path = new NSBezierPath { LineWidth = axis == _activeAxis ? 4f : 3f };
            path.MoveTo(new CGPoint(center.X, center.Y));
            path.LineTo(new CGPoint(end.X, end.Y));
            path.Stroke();

            if (scaleHandles)
            {
                AxisColor(axis, axis == _activeAxis).SetFill();
                var size = axis == _activeAxis ? 11f : 9f;
                NSBezierPath.FillRect(new CGRect(end.X - size / 2f, end.Y - size / 2f, size, size));
            }
            else
            {
                DrawArrowHead(center, end, AxisColor(axis, axis == _activeAxis));
            }
        }
    }

    private void DrawRotationGizmo(TransformComponent transform)
    {
        foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
        {
            var points = GetRotationRingPoints(transform, axis, 64);
            if (points.Count < 2) continue;
            AxisColor(axis, axis == _activeAxis).SetStroke();
            var path = new NSBezierPath { LineWidth = axis == _activeAxis ? 4f : 2.5f };
            path.MoveTo(new CGPoint(points[0].X, points[0].Y));
            for (var i = 1; i < points.Count; i++)
                path.LineTo(new CGPoint(points[i].X, points[i].Y));
            path.Stroke();
        }
    }

    private List<Vector2> GetRotationRingPoints(TransformComponent transform, GizmoAxis axis, int segments)
    {
        var points = new List<Vector2>(segments + 1);
        var width = (float)Bounds.Width;
        var height = (float)Bounds.Height;
        var radius = Camera.WorldUnitsPerPixel(transform.Position, height) * 62f;
        var axisDir = AxisVector(axis, transform);
        var reference = MathF.Abs(Vector3.Dot(axisDir, Vector3.UnitY)) < 0.92f ? Vector3.UnitY : Vector3.UnitX;
        var tangent = Vector3.Normalize(Vector3.Cross(axisDir, reference));
        var bitangent = Vector3.Normalize(Vector3.Cross(axisDir, tangent));

        for (var i = 0; i <= segments; i++)
        {
            var angle = (float)i / segments * MathF.PI * 2f;
            var world = transform.Position + (tangent * MathF.Cos(angle) + bitangent * MathF.Sin(angle)) * radius;
            if (Camera.TryProject(world, width, height, out var screen, out _))
                points.Add(screen);
        }
        return points;
    }

    private Vector3 AxisVector(GizmoAxis axis, TransformComponent transform)
    {
        var vector = axis switch
        {
            GizmoAxis.X => Vector3.UnitX,
            GizmoAxis.Y => Vector3.UnitY,
            GizmoAxis.Z => Vector3.UnitZ,
            _ => Vector3.Zero
        };
        return ActiveSpace == TransformSpace.Local ? RotateEuler(vector, transform.Rotation) : vector;
    }

    private static Vector3 RotateEuler(Vector3 vector, Vector3 degrees)
    {
        var d2r = MathF.PI / 180f;
        var x = degrees.X * d2r;
        var y = degrees.Y * d2r;
        var z = degrees.Z * d2r;

        var sx = MathF.Sin(x); var cx = MathF.Cos(x);
        var sy = MathF.Sin(y); var cy = MathF.Cos(y);
        var sz = MathF.Sin(z); var cz = MathF.Cos(z);

        vector = new Vector3(vector.X, cx * vector.Y - sx * vector.Z, sx * vector.Y + cx * vector.Z);
        vector = new Vector3(cy * vector.X + sy * vector.Z, vector.Y, -sy * vector.X + cy * vector.Z);
        vector = new Vector3(cz * vector.X - sz * vector.Y, sz * vector.X + cz * vector.Y, vector.Z);
        return Vector3.Normalize(vector);
    }

    private float AxisDragWorldDelta(Vector3 origin, Vector3 axis, CGPoint start, CGPoint current, float width, float height)
    {
        var pixels = AxisDragPixels(origin, axis, start, current, width, height);
        var unit = Camera.WorldUnitsPerPixel(origin, height);

        // Correct for foreshortening by comparing the projected axis against a
        // one-world-unit step. This keeps manipulation stable at oblique angles.
        if (Camera.TryProject(origin, width, height, out var a, out _) &&
            Camera.TryProject(origin + axis, width, height, out var b, out _))
        {
            var projectedPerUnit = Vector2.Distance(a, b);
            if (projectedPerUnit > 0.5f)
                return pixels / projectedPerUnit;
        }
        return pixels * unit;
    }

    private float AxisDragPixels(Vector3 origin, Vector3 axis, CGPoint start, CGPoint current, float width, float height)
    {
        if (!Camera.TryProject(origin, width, height, out var a, out _) ||
            !Camera.TryProject(origin + axis, width, height, out var b, out _))
            return 0f;

        var direction = b - a;
        if (direction.LengthSquared() < 0.01f)
            return 0f;
        direction = Vector2.Normalize(direction);
        var mouseDelta = new Vector2((float)(current.X - start.X), (float)(current.Y - start.Y));
        return Vector2.Dot(mouseDelta, direction);
    }

    private static void DrawArrowHead(Vector2 center, Vector2 end, NSColor color)
    {
        var direction = end - center;
        if (direction.LengthSquared() < 1f) return;
        direction = Vector2.Normalize(direction);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var basePoint = end - direction * 11f;
        var p1 = basePoint + perpendicular * 5f;
        var p2 = basePoint - perpendicular * 5f;
        color.SetFill();
        var path = new NSBezierPath();
        path.MoveTo(new CGPoint(end.X, end.Y));
        path.LineTo(new CGPoint(p1.X, p1.Y));
        path.LineTo(new CGPoint(p2.X, p2.Y));
        path.ClosePath();
        path.Fill();
    }

    private static NSColor AxisColor(GizmoAxis axis, bool active)
    {
        if (active) return NSColor.White;
        return axis switch
        {
            GizmoAxis.X => NSColor.FromRgb(0.93f, 0.24f, 0.22f),
            GizmoAxis.Y => NSColor.FromRgb(0.30f, 0.82f, 0.30f),
            GizmoAxis.Z => NSColor.FromRgb(0.25f, 0.48f, 0.96f),
            _ => NSColor.White
        };
    }

    private bool TryGetSelectedTransform(out Entity entity, out TransformComponent transform)
    {
        entity = default;
        transform = default;
        if (!_selection.ActiveEntity.HasValue)
            return false;
        entity = _selection.ActiveEntity.Value;
        if (!_world.Exists(entity) || !_world.Has<TransformComponent>(entity))
            return false;
        transform = _world.Get<TransformComponent>(entity);
        return true;
    }

    private static bool RaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius, out float t)
    {
        var oc = origin - center;
        var b = Vector3.Dot(oc, direction);
        var c = Vector3.Dot(oc, oc) - radius * radius;
        var h = b * b - c;
        if (h < 0f) { t = 0f; return false; }
        h = MathF.Sqrt(h);
        var t0 = -b - h;
        var t1 = -b + h;
        t = t0 > 0f ? t0 : t1;
        return t > 0f;
    }

    private static float BaseRadius(BuiltInMesh mesh) => mesh switch
    {
        BuiltInMesh.Cube => 1.75f,
        BuiltInMesh.Plane => 1.45f,
        BuiltInMesh.Capsule => 1.9f,
        _ => 1.35f
    };

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        if (lengthSquared <= 0.0001f) return Vector2.Distance(point, a);
        var t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + ab * t);
    }

    private static float NormalizeRadians(float radians)
    {
        while (radians > MathF.PI) radians -= MathF.PI * 2f;
        while (radians < -MathF.PI) radians += MathF.PI * 2f;
        return radians;
    }

    private static float GetAxisComponent(Vector3 v, GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => v.X,
        GizmoAxis.Y => v.Y,
        GizmoAxis.Z => v.Z,
        _ => 0f
    };

    private static void SetAxisComponent(ref Vector3 v, GizmoAxis axis, float value)
    {
        switch (axis)
        {
            case GizmoAxis.X: v.X = value; break;
            case GizmoAxis.Y: v.Y = value; break;
            case GizmoAxis.Z: v.Z = value; break;
        }
    }

    private static bool TransformsEqual(TransformComponent a, TransformComponent b)
        => a.Position == b.Position && a.Rotation == b.Rotation && a.Scale == b.Scale;

    private sealed class InteractionSurface : NSView
    {
        private readonly ViewportPanelView _owner;

        public InteractionSurface(CGRect frame, ViewportPanelView owner) : base(frame)
        {
            _owner = owner;
        }

        public override bool AcceptsFirstResponder() => true;

        public override void DrawRect(CGRect dirtyRect)
        {
            base.DrawRect(dirtyRect);
            _owner.DrawGizmo();
        }

        public override void MouseDown(NSEvent e) => _owner.MouseDown(e);
        public override void MouseDragged(NSEvent e) => _owner.MouseDragged(e);
        public override void MouseUp(NSEvent e) => _owner.MouseUp(e);
        public override void RightMouseDown(NSEvent e) => _owner.RightMouseDown(e);
        public override void RightMouseDragged(NSEvent e) => _owner.RightMouseDragged(e);
        public override void RightMouseUp(NSEvent e) => _owner.RightMouseUp(e);
        public override void OtherMouseDown(NSEvent e) => _owner.OtherMouseDown(e);
        public override void OtherMouseDragged(NSEvent e) => _owner.OtherMouseDragged(e);
        public override void OtherMouseUp(NSEvent e) => _owner.OtherMouseUp(e);
        public override void ScrollWheel(NSEvent e) => _owner.ScrollWheel(e);
        public override void KeyDown(NSEvent e) => _owner.KeyDown(e);
    }
}
