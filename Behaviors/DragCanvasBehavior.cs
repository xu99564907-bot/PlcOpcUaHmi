using System;
using System.Windows;
using System.Windows.Input;
using PlcOpcUaHmi.Models;

namespace PlcOpcUaHmi.Behaviors;

public static class DragCanvasBehavior
{
    public static readonly DependencyProperty EnableDragProperty = DependencyProperty.RegisterAttached(
        "EnableDrag",
        typeof(bool),
        typeof(DragCanvasBehavior),
        new PropertyMetadata(false, OnEnableDragChanged));

    public static bool GetEnableDrag(DependencyObject obj) => (bool)obj.GetValue(EnableDragProperty);
    public static void SetEnableDrag(DependencyObject obj, bool value) => obj.SetValue(EnableDragProperty, value);

    private static Point _startMouse;
    private static double _startLeft;
    private static double _startTop;
    private static FrameworkElement? _draggingElement;

    private static void OnEnableDragChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.MouseLeftButtonDown += Element_MouseLeftButtonDown;
            element.MouseMove += Element_MouseMove;
            element.MouseLeftButtonUp += Element_MouseLeftButtonUp;
        }
        else
        {
            element.MouseLeftButtonDown -= Element_MouseLeftButtonDown;
            element.MouseMove -= Element_MouseMove;
            element.MouseLeftButtonUp -= Element_MouseLeftButtonUp;
        }
    }

    private static void Element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not DesignerElement model)
        {
            return;
        }

        _draggingElement = element;
        _startMouse = e.GetPosition(element.Parent as IInputElement);
        _startLeft = model.Left;
        _startTop = model.Top;
        element.CaptureMouse();
        e.Handled = true;
    }

    private static void Element_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingElement is null || sender is not FrameworkElement element || element != _draggingElement || element.DataContext is not DesignerElement model)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(element.Parent as IInputElement);
        var offsetX = current.X - _startMouse.X;
        var offsetY = current.Y - _startMouse.Y;
        model.Left = Math.Max(0, _startLeft + offsetX);
        model.Top = Math.Max(0, _startTop + offsetY);
    }

    private static void Element_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingElement is not FrameworkElement element)
        {
            return;
        }

        element.ReleaseMouseCapture();
        _draggingElement = null;
    }
}
