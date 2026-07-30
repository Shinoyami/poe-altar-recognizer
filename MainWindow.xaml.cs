using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Navigation;
using System.Windows.Controls;

namespace PoeAltarGuard;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(75) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly List<OverlayWindow> _overlays = new();
    private readonly OcrWatcher _watcher = new();
    private Rect? _area;
    private bool _running;
    private bool _scanning;
    private int _consecutiveMisses;
    private const int HotkeyId = 4108;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkF8 = 0x77;
    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    private readonly string _legacySettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PoeAltarGuard", "settings.json");

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += Scan;
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveSettings();
        };
        LoadSettings();
        UpdateLineNumbers();
        GoodModifiersText.TextChanged += (_, _) => { UpdateLineNumbers(); ScheduleSave(); };
        BadModifiersText.TextChanged += (_, _) => { UpdateLineNumbers(); ScheduleSave(); };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessage);
            RegisterHotKey(hwnd, HotkeyId, ModControl | ModShift, VkF8);
        };
        Closed += (_, _) =>
        {
            _saveTimer.Stop();
            SaveSettings();
            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HotkeyId);
            CloseOverlays();
            _watcher.Dispose();
        };
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_running) Stop();
        else Start();
    }

    private void PoEDbLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Start()
    {
        if (Rules(GoodModifiersText.Text).Count == 0 && Rules(BadModifiersText.Text).Count == 0)
        {
            StatusText.Text = "Add at least one good or bad modifier first.";
            return;
        }
        _area = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        SaveSettings();
        _running = true;
        _consecutiveMisses = 0;
        _timer.Start();
        ToggleButton.Content = "Stop watching";
        StatusText.Text = "Watching the entire screen…";
        WindowState = WindowState.Minimized;
    }

    private void Stop()
    {
        _running = false;
        _timer.Stop();
        HideOverlays();
        ToggleButton.Content = "Start watching the entire screen";
        StatusText.Text = "Stopped.";
    }

    private async void Scan(object? sender, EventArgs e)
    {
        if (_scanning || !_running || _area is null) return;
        if (!TryGetPathOfExileForegroundArea(out var poeArea))
        {
            HideOverlays();
            StatusText.Text = "Paused — Path of Exile is not focused.";
            return;
        }
        _scanning = true;
        try
        {
            var scanTimer = Stopwatch.StartNew();
            var matches = await _watcher.ScanAsync(
                poeArea, Rules(GoodModifiersText.Text), Rules(BadModifiersText.Text));
            scanTimer.Stop();
            if (matches.Count > 0)
            {
                _consecutiveMisses = 0;
                for (var i = 0; i < matches.Count; i++)
                {
                    while (_overlays.Count <= i) _overlays.Add(new OverlayWindow());
                    _overlays[i].ShowAt(matches[i].Bounds, matches[i].IsGood);
                }
                for (var i = matches.Count; i < _overlays.Count; i++) _overlays[i].Hide();
                StatusText.Text =
                    $"{matches.Count} configured modifier{(matches.Count == 1 ? "" : "s")} detected in {scanTimer.ElapsedMilliseconds} ms.";
            }
            else
            {
                _consecutiveMisses++;
                if (_consecutiveMisses >= 1) HideOverlays();
                StatusText.Text = $"Watching the entire screen… Last scan: {scanTimer.ElapsedMilliseconds} ms.";
            }
        }
        catch (Exception ex)
        {
            Stop();
            StatusText.Text = $"Could not read screen: {ex.Message}";
            WindowState = WindowState.Normal;
        }
        finally { _scanning = false; }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath) && File.Exists(_legacySettingsPath))
                File.Copy(_legacySettingsPath, _settingsPath);
            if (!File.Exists(_settingsPath))
            {
                BadModifiersText.Text =
                    "reduced recovery rate of life, mana and energy shield per endurance charge";
                return;
            }
            var json = File.ReadAllText(_settingsPath);
            if (json.TrimStart().StartsWith("["))
            {
                var old = JsonSerializer.Deserialize<double[]>(json);
                if (old?.Length == 4) _area = new Rect(old[0], old[1], old[2], old[3]);
                BadModifiersText.Text =
                    "reduced recovery rate of life, mana and energy shield per endurance charge";
            }
            else
            {
                var settings = JsonSerializer.Deserialize<Settings>(json);
                if (settings?.Area?.Length == 4)
                    _area = new Rect(settings.Area[0], settings.Area[1], settings.Area[2], settings.Area[3]);
                GoodModifiersText.Text = string.Join(Environment.NewLine, settings?.Good ?? []);
                BadModifiersText.Text = string.Join(Environment.NewLine, settings?.Bad ?? []);
            }
            StatusText.Text = "Saved modifier lists loaded. Ready to watch the entire screen.";
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            _area ??= new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var settings = new Settings
            {
                Area = [_area.Value.X, _area.Value.Y, _area.Value.Width, _area.Value.Height],
                Good = Rules(GoodModifiersText.Text),
                Bad = Rules(BadModifiersText.Text)
            };
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not save settings beside the app: {ex.Message}";
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
        StatusText.Text = "Modifier lists changed — saving…";
    }

    private void UpdateLineNumbers()
    {
        GoodLineNumbers.Text = BuildLineNumbers(GoodModifiersText.Text);
        BadLineNumbers.Text = BuildLineNumbers(BadModifiersText.Text);
    }

    private static string BuildLineNumbers(string text)
    {
        var count = Math.Max(1, text.Replace("\r\n", "\n").Split('\n').Length);
        return string.Join(Environment.NewLine, Enumerable.Range(1, count));
    }

    private void ModifierText_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender == GoodModifiersText)
            GoodLineNumbers.ScrollToLine(Math.Max(0, GoodModifiersText.GetFirstVisibleLineIndex()));
        else if (sender == BadModifiersText)
            BadLineNumbers.ScrollToLine(Math.Max(0, BadModifiersText.GetFirstVisibleLineIndex()));
    }

    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Stop();
            WindowState = WindowState.Normal;
            Activate();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static List<string> Rules(string text) =>
        text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private void HideOverlays()
    {
        foreach (var overlay in _overlays) overlay.Hide();
    }

    private void CloseOverlays()
    {
        foreach (var overlay in _overlays) overlay.Close();
        _overlays.Clear();
    }

    private sealed class Settings
    {
        public double[]? Area { get; set; }
        public List<string> Good { get; set; } = [];
        public List<string> Bad { get; set; } = [];
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(
        IntPtr hWnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(
        IntPtr hWnd, out uint processId);

    private static bool TryGetPathOfExileForegroundArea(out Rect area)
    {
        area = Rect.Empty;
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0) return false;
            using var process = Process.GetProcessById((int)processId);
            if (!process.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!GetWindowRect(hwnd, out var bounds)) return false;
            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            if (width < 100 || height < 100) return false;
            area = new Rect(bounds.Left, bounds.Top, width, height);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
}
