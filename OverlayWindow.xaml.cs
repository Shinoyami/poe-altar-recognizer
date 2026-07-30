using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PoeAltarGuard;

public partial class OverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolwindow = 0x80;
    private const int WsExNoactivate = 0x08000000;
    private const uint WdaExcludeFromCapture = 0x00000011;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, GwlExstyle);
            SetWindowLong(hwnd, GwlExstyle, style | WsExTransparent | WsExToolwindow | WsExNoactivate);
            SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
        };
    }

    public void ShowAt(Rect rect, bool isGood)
    {
        var color = isGood ? Color.FromRgb(30, 210, 75) : Color.FromRgb(255, 32, 32);
        MatchBorder.BorderBrush = new SolidColorBrush(color);
        MatchBorder.Background = new SolidColorBrush(Color.FromArgb(52, color.R, color.G, color.B));
        Left = rect.Left;
        Top = rect.Top;
        Width = rect.Width;
        Height = rect.Height;
        if (!IsVisible) Show();
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
}
