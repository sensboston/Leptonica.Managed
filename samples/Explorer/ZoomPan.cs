using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Explorer;

/// <summary>
/// Zoom with the mouse wheel and pan by dragging, applied in the same way to several images, so
/// that the original and the result always show the same part of the page. Double click resets.
/// </summary>
public sealed class ZoomPan
{
    private readonly List<Control> _targets = new();
    private double _scale = 1;
    private Vector _offset;
    private Point? _dragStart;
    private Vector _dragOffset;

    /// <summary>Adds an image: the mouse works on its surface (the border around it).</summary>
    public void Add(Control image, Control surface)
    {
        _targets.Add(image);
        image.RenderTransformOrigin = RelativePoint.TopLeft;
        surface.ClipToBounds = true;
        surface.PointerWheelChanged += (_, e) => ZoomAt(Local(e.GetPosition(surface), image), e.Delta.Y);
        surface.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(surface).Properties.IsLeftButtonPressed)
                return;
            if (e.ClickCount == 2)
            {
                Reset();
                return;
            }
            _dragStart = e.GetPosition(surface);
            _dragOffset = _offset;
            e.Pointer.Capture(surface);
        };
        surface.PointerMoved += (_, e) =>
        {
            if (_dragStart is Point start)
            {
                _offset = _dragOffset + (e.GetPosition(surface) - start);
                Apply();
            }
        };
        surface.PointerReleased += (_, e) =>
        {
            _dragStart = null;
            e.Pointer.Capture(null);
        };
    }

    public void Reset()
    {
        _scale = 1;
        _offset = default;
        Apply();
    }

    // The mouse position relative to the top left corner of the image before the transform
    // (the image sits in its surface with a small margin).
    private static Point Local(Point p, Control image) => new(p.X - image.Bounds.X, p.Y - image.Bounds.Y);

    private void ZoomAt(Point p, double wheel)
    {
        double factor = Math.Pow(1.2, wheel);
        double scale = Math.Clamp(_scale * factor, 1, 32);
        factor = scale / _scale;
        // Keep the point under the mouse where it is.
        _offset = new Vector(p.X - (p.X - _offset.X) * factor, p.Y - (p.Y - _offset.Y) * factor);
        _scale = scale;
        if (_scale == 1)
            _offset = default;
        Apply();
    }

    private void Apply()
    {
        var transform = new MatrixTransform(Matrix.CreateScale(_scale, _scale) * Matrix.CreateTranslation(_offset.X, _offset.Y));
        foreach (Control target in _targets)
            target.RenderTransform = transform;
    }
}
