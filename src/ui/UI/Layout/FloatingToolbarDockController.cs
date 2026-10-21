/**
 * Encapsulates drag-and-drop and top/bottom edge-snapping behavior
 * for the floating control toolbar inside the HistoryWindow.
 */

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace LiveTranslateOverlay.UI.Layout;

public sealed class FloatingToolbarDockController
{
    private enum DockState
    {
        Top,
        Bottom,
        Floating
    }

    private const double EdgeSnapThreshold = 75.0;
    private const double DockMargin = 12.0;

    private readonly Canvas _overlayCanvas;
    private readonly Border _toolbar;
    private readonly Control _dragHandle;
    private readonly Border _topPreview;
    private readonly Border _bottomPreview;

    private DockState _dockState = DockState.Top;
    private bool _isDragging;
    private Point _dragPointerOffset;

    public FloatingToolbarDockController(
        Canvas overlayCanvas,
        Border toolbar,
        Control dragHandle,
        Border topPreview,
        Border bottomPreview)
    {
        _overlayCanvas = overlayCanvas;
        _toolbar = toolbar;
        _dragHandle = dragHandle;
        _topPreview = topPreview;
        _bottomPreview = bottomPreview;

        _dragHandle.PointerPressed += OnPointerPressed;
        _dragHandle.PointerMoved += OnPointerMoved;
        _dragHandle.PointerReleased += OnPointerReleased;
        _dragHandle.PointerCaptureLost += OnPointerCaptureLost;
        _overlayCanvas.SizeChanged += (_, _) => ApplyDockPosition();
    }

    public void ApplyDockPosition()
    {
        double canvasW = _overlayCanvas.Bounds.Width;
        double canvasH = _overlayCanvas.Bounds.Height;
        double toolbarW = _toolbar.Bounds.Width;
        double toolbarH = _toolbar.Bounds.Height;

        if (canvasW <= 0 || canvasH <= 0) return;

        if (toolbarW <= 0 || toolbarH <= 0)
        {
            _toolbar.Measure(Size.Infinity);
            toolbarW = _toolbar.DesiredSize.Width;
            toolbarH = _toolbar.DesiredSize.Height;
        }

        double centeredLeft = Math.Max(6, (canvasW - toolbarW) / 2.0);

        switch (_dockState)
        {
            case DockState.Top:
                Canvas.SetLeft(_toolbar, centeredLeft);
                Canvas.SetTop(_toolbar, DockMargin);
                break;

            case DockState.Bottom:
                Canvas.SetLeft(_toolbar, centeredLeft);
                Canvas.SetTop(_toolbar, Math.Max(6, canvasH - toolbarH - DockMargin));
                break;

            case DockState.Floating:
                double curLeft = Canvas.GetLeft(_toolbar);
                double curTop = Canvas.GetTop(_toolbar);
                if (double.IsNaN(curLeft)) curLeft = centeredLeft;
                if (double.IsNaN(curTop)) curTop = DockMargin;

                Canvas.SetLeft(_toolbar, Math.Clamp(curLeft, 6, Math.Max(6, canvasW - toolbarW - 6)));
                Canvas.SetTop(_toolbar, Math.Clamp(curTop, 6, Math.Max(6, canvasH - toolbarH - 6)));
                break;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_overlayCanvas).Properties.IsLeftButtonPressed) return;

        _isDragging = true;
        _dockState = DockState.Floating;

        Point pointerPos = e.GetPosition(_overlayCanvas);
        double curLeft = Canvas.GetLeft(_toolbar);
        double curTop = Canvas.GetTop(_toolbar);

        if (double.IsNaN(curLeft)) curLeft = 0;
        if (double.IsNaN(curTop)) curTop = 0;

        _dragPointerOffset = new Point(pointerPos.X - curLeft, pointerPos.Y - curTop);
        e.Pointer.Capture(_dragHandle);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;

        Point pointerPos = e.GetPosition(_overlayCanvas);
        double canvasW = _overlayCanvas.Bounds.Width;
        double canvasH = _overlayCanvas.Bounds.Height;
        double toolbarW = _toolbar.Bounds.Width;
        double toolbarH = _toolbar.Bounds.Height;

        double newLeft = Math.Clamp(pointerPos.X - _dragPointerOffset.X, 6, Math.Max(6, canvasW - toolbarW - 6));
        double newTop = Math.Clamp(pointerPos.Y - _dragPointerOffset.Y, 6, Math.Max(6, canvasH - toolbarH - 6));

        Canvas.SetLeft(_toolbar, newLeft);
        Canvas.SetTop(_toolbar, newTop);

        UpdateSnapPreviews(newTop, canvasW, canvasH, toolbarW, toolbarH);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        e.Pointer.Capture(null);
        CompleteDrag();
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDragging) CompleteDrag();
    }

    private void UpdateSnapPreviews(double toolbarTop, double canvasW, double canvasH, double toolbarW, double toolbarH)
    {
        double centeredLeft = Math.Max(6, (canvasW - toolbarW) / 2.0);
        bool nearTop = toolbarTop <= EdgeSnapThreshold;
        bool nearBottom = (toolbarTop + toolbarH) >= (canvasH - EdgeSnapThreshold);

        if (nearTop)
        {
            _topPreview.Width = toolbarW;
            _topPreview.Height = toolbarH;
            Canvas.SetLeft(_topPreview, centeredLeft);
            Canvas.SetTop(_topPreview, DockMargin);

            _topPreview.IsVisible = true;
            _bottomPreview.IsVisible = false;
        }
        else if (nearBottom)
        {
            _bottomPreview.Width = toolbarW;
            _bottomPreview.Height = toolbarH;
            Canvas.SetLeft(_bottomPreview, centeredLeft);
            Canvas.SetTop(_bottomPreview, Math.Max(6, canvasH - toolbarH - DockMargin));

            _bottomPreview.IsVisible = true;
            _topPreview.IsVisible = false;
        }
        else
        {
            _topPreview.IsVisible = false;
            _bottomPreview.IsVisible = false;
        }
    }

    private void CompleteDrag()
    {
        _isDragging = false;

        if (_topPreview.IsVisible)
        {
            _dockState = DockState.Top;
            ApplyDockPosition();
        }
        else if (_bottomPreview.IsVisible)
        {
            _dockState = DockState.Bottom;
            ApplyDockPosition();
        }

        _topPreview.IsVisible = false;
        _bottomPreview.IsVisible = false;
    }
}