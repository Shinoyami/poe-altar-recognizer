using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace PoeAltarGuard;

public partial class SelectionWindow : Window
{
    private Point _start;
    public Rect SelectedArea { get; private set; }

    public SelectionWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Surface);
        SelectionRect.Visibility = Visibility.Visible;
        Surface.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!Surface.IsMouseCaptured) return;
        var p = e.GetPosition(Surface);
        var rect = Normalize(_start, p);
        Canvas.SetLeft(SelectionRect, rect.Left);
        Canvas.SetTop(SelectionRect, rect.Top);
        SelectionRect.Width = rect.Width;
        SelectionRect.Height = rect.Height;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!Surface.IsMouseCaptured) return;
        Surface.ReleaseMouseCapture();
        var local = Normalize(_start, e.GetPosition(Surface));
        if (local.Width < 40 || local.Height < 20) return;
        SelectedArea = new Rect(local.X + Left, local.Y + Top, local.Width, local.Height);
        DialogResult = true;
    }

    private static Rect Normalize(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
