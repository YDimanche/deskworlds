using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace Deskworlds;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ProbeMode = args.Contains("--probe");
        using var mutex = new Mutex(true, @"Local\Deskworlds.Wallpaper", out bool first);
        if (!first) return;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var app = new DeskworldsApp();
        app.Start();
        Application.Run();
    }

    /// <summary>Diagnostic run: force full rate, sample the scene canvas, dump a window capture, exit.</summary>
    public static bool ProbeMode { get; private set; }
}

/// <summary>
/// The Windows host, mirroring the macOS agent: one window per screen on the wallpaper
/// layer, a tray menu to pick worlds, feed or pause, and rate control from occlusion,
/// battery power, session lock and monitor power.
/// </summary>
internal sealed class DeskworldsApp : IDisposable
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Deskworlds");
    public static readonly string LogPath = Path.Combine(DataDir, "log.txt");
    private const string RegKey = @"Software\Deskworlds";

    private readonly List<WallpaperForm> forms = new();
    private NotifyIcon tray = null!;
    private ToolStripMenuItem statusItem = null!, feedItem = null!, pauseItem = null!, coverItem = null!;
    private readonly List<ToolStripMenuItem> worldItems = new();
    private System.Windows.Forms.Timer cursorTimer = null!, occlusionTimer = null!;
    private CoreWebView2Environment? env;
    private WorldDef world = LoadWorld();
    private bool stopped = LoadPaused();
    private bool animateCovered = LoadAnimateCovered();
    private bool awake = true;
    private int applied;
    private int cursorRate = -1;
    private string? screensSignature;
    private InputSink? inputSink;
    private IntPtr rawInputBuffer;
    private bool rawInputSeen;
    private long lastDown;
    private Native.POINT lastDownPt;
    private DateTime activityUntil = DateTime.MinValue;

    public void Start()
    {
        Directory.CreateDirectory(DataDir);
        Log($"Deskworlds starting, exe={Environment.ProcessPath}, world={world.Key}, paused={stopped}");
        InitTray();
        InstallRawInput();
        cursorTimer = new System.Windows.Forms.Timer { Interval = 33 };
        cursorTimer.Tick += (_, _) => TrackCursor();
        occlusionTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        occlusionTimer.Tick += (_, _) => ApplyRate();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions
            {
                // Chromium would suspend rendering when it thinks the window is fully
                // covered by ordinary windows; this wallpaper is always covered, and the
                // host alone decides when the scene may rest.
                AdditionalBrowserArguments = "--disable-features=CalculateNativeWinOcclusion,WindowOcclusionCalculation",
                Language = "zh-CN",
            };
            env = await CoreWebView2Environment.CreateAsync(
                null, Path.Combine(DataDir, "WebView2"), options);
        }
        catch (Exception ex)
        {
            Log($"WebView2 unavailable: {ex.Message}");
            MessageBox.Show(
                "Deskworlds 需要 Microsoft Edge WebView2 运行时。\n\n请从以下地址安装后重试：\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
                "Deskworlds", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Application.Exit();
            return;
        }
        Rebuild();
    }

    private void InitTray()
    {
        var menu = new ContextMenuStrip();
        statusItem = new ToolStripMenuItem("Deskworlds") { Enabled = false };
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        var worldMenu = new ToolStripMenuItem("世界");
        foreach (var w in WorldDef.All)
        {
            var item = new ToolStripMenuItem(w.Title) { Tag = w };
            item.Click += (sender, _) => SelectWorld((WorldDef)((ToolStripMenuItem)sender!).Tag!);
            worldMenu.DropDownItems.Add(item);
            worldItems.Add(item);
        }
        menu.Items.Add(worldMenu);
        menu.Items.Add(new ToolStripSeparator());
        feedItem = new ToolStripMenuItem("投喂");
        feedItem.Click += (_, _) => forms.ForEach(f => f.Feed());
        menu.Items.Add(feedItem);
        pauseItem = new ToolStripMenuItem("暂停");
        pauseItem.Click += (_, _) => TogglePause();
        menu.Items.Add(pauseItem);
        coverItem = new ToolStripMenuItem("被覆盖时动画");
        coverItem.Click += (_, _) => ToggleAnimateCovered();
        menu.Items.Add(coverItem);
        menu.Items.Add(new ToolStripSeparator());
        var quit = new ToolStripMenuItem("退出");
        quit.Click += (_, _) =>
        {
            tray.Visible = false;
            Application.Exit();
        };
        menu.Items.Add(quit);
        menu.Opening += (_, _) => UpdateMenu();

        tray = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
            Text = $"Deskworlds · {world.Title}",
        };
        try
        {
            var exeIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
            tray.Icon = exeIcon ?? SystemIcons.Application;
        }
        catch
        {
            tray.Icon = SystemIcons.Application;
        }
    }

    private void UpdateMenu()
    {
        foreach (var item in worldItems) item.Checked = ReferenceEquals(item.Tag, world);
        statusItem.Text = stopped ? "已暂停"
            : !awake ? "静止中：屏幕已关闭或已锁定"
            : applied == 0 ? "静置于窗口之后"
            : $"运行中：每秒 {applied} 帧";
        pauseItem.Text = stopped ? "继续" : "暂停";
        coverItem.Checked = animateCovered;
        feedItem.Text = world.FeedTitle;
        feedItem.Enabled = applied > 0 && world.CanFeed;
    }

    private void SelectWorld(WorldDef next)
    {
        if (next == world) return;
        world = next;
        SaveWorld(next);
        tray.Text = $"Deskworlds · {next.Title}";
        Rebuild();
    }

    private void TogglePause()
    {
        stopped = !stopped;
        SavePaused(stopped);
        ApplyRate();
    }

    /// <summary>Serving users who keep windows open — or record the screen — with the wallpaper visible around them.</summary>
    private void ToggleAnimateCovered()
    {
        animateCovered = !animateCovered;
        SaveAnimateCovered(animateCovered);
        ApplyRate();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        string signature = ScreensSignature();
        if (signature == screensSignature) return;
        Rebuild();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange) ApplyRate();
    }

    public void ExplorerRestarted()
    {
        Log("explorer restarted; reattaching");
        screensSignature = null;
        Rebuild();
    }

    public void SessionChanged(bool locked)
    {
        bool nowAwake = !locked;
        if (awake == nowAwake) return;
        awake = nowAwake;
        Log(locked ? "session locked" : "session unlocked");
        ApplyRate();
    }

    public void MonitorPower(bool on)
    {
        if (awake == on) return;
        awake = on;
        Log(on ? "monitor on" : "monitor off");
        ApplyRate();
    }

    private static string ScreensSignature() => string.Join("|",
        Screen.AllScreens.Select(s => $"{s.Bounds.X},{s.Bounds.Y},{s.Bounds.Width},{s.Bounds.Height}"));

    /// <summary>One window per screen, each running its own world on the wallpaper layer.</summary>
    private void Rebuild()
    {
        screensSignature = ScreensSignature();
        foreach (var form in forms) form.Dispose();
        forms.Clear();
        IntPtr host = Native.FindWallpaperHost();
        if (host == IntPtr.Zero)
        {
            Log("wallpaper host window not found");
            return;
        }
        if (env == null) return;
        var hostRect = Native.WindowRect(host);
        Log($"host=0x{host:X} rect=({hostRect.Left},{hostRect.Top},{hostRect.Right},{hostRect.Bottom})");
        foreach (var screen in Screen.AllScreens)
        {
            var form = new WallpaperForm(this, world, screen);
            forms.Add(form);
            Native.SetParent(form.Handle, host);
            form.Bounds = new Rectangle(
                screen.Bounds.X - hostRect.Left, screen.Bounds.Y - hostRect.Top,
                screen.Bounds.Width, screen.Bounds.Height);
            form.Show();
            _ = form.InitAsync(env);
        }
        ApplyRate();
    }

    public void ApplyRate()
    {
        bool battery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
        bool still = !Program.ProbeMode && (stopped || !awake);
        int full = battery ? 30 : 60;
        var blockers = still ? new List<Rectangle>() : WindowBlockers();
        bool active = !still && DateTime.UtcNow < activityUntil;
        applied = 0;
        foreach (var form in forms)
        {
            double showing = still ? 0 : Exposure(form.ScreenBounds, blockers);
            int rate = Program.ProbeMode ? 60
                : still ? 0
                : showing < 0.15 ? (animateCovered ? (battery ? 20 : 30) : 0)
                : showing < 0.4 ? 20 : full;
            // A strike is playing out: keep the scene animated even behind windows.
            if (rate == 0 && active) rate = battery ? 20 : 30;
            form.SetPower(battery);
            form.SetRate(rate);
            applied = Math.Max(applied, form.Rate);
        }
        UpdateTimers(still);
    }

    private void UpdateTimers(bool still)
    {
        // Pointer sampling need not outrun the animation, nor wake a stopped wallpaper.
        int wanted = Math.Min(30, applied);
        if (wanted != cursorRate)
        {
            cursorRate = wanted;
            cursorTimer.Stop();
            if (wanted > 0)
            {
                cursorTimer.Interval = 1000 / wanted;
                cursorTimer.Start();
            }
        }
        if (still) occlusionTimer.Stop();
        else if (!occlusionTimer.Enabled) occlusionTimer.Start();
    }

    private void TrackCursor()
    {
        if (applied <= 0) return;
        if (!Native.GetCursorPos(out var point)) return;
        foreach (var form in forms)
        {
            var b = form.ScreenBounds;
            if (point.X >= b.Left && point.X < b.Right && point.Y >= b.Top && point.Y < b.Bottom)
            {
                var client = new Native.POINT { X = point.X, Y = point.Y };
                Native.ScreenToClient(form.Handle, ref client);
                double scale = form.DeviceDpi / 96.0;
                form.SetPointer(client.X / scale, client.Y / scale);
            }
            else
            {
                form.SetPointer(null, null);
            }
        }
    }

    /// <summary>
    /// Raw input with INPUTSINK watches double-clicks without consuming them: the shell
    /// still opens whatever icon was double-clicked, and we add the special effect.
    /// </summary>
    private void InstallRawInput()
    {
        try
        {
            inputSink = new InputSink(this);
            var devices = new[] { new Native.RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x02,   // generic mouse
                dwFlags = Native.RIDEV_INPUTSINK,
                hwndTarget = inputSink.Handle,
            } };
            bool ok = Native.RegisterRawInputDevices(devices, 1,
                (uint)Marshal.SizeOf<Native.RAWINPUTDEVICE>());
            Log(ok ? "raw input registered"
                : $"raw input registration failed (error {Marshal.GetLastWin32Error()})");
        }
        catch (Exception ex)
        {
            Log($"raw input setup failed: {ex.Message}");
        }
    }

    /// <summary>WM_INPUT on the UI thread: pick left-button presses out of the input stream.</summary>
    public void OnRawInput(IntPtr handle)
    {
        const uint bufferSize = 64;   // RAWINPUTHEADER + RAWMOUSE is 48 bytes on x64
        if (rawInputBuffer == IntPtr.Zero) rawInputBuffer = Marshal.AllocHGlobal((int)bufferSize);
        uint size = bufferSize;
        uint copied = Native.GetRawInputData(handle, Native.RID_INPUT, rawInputBuffer, ref size,
            (uint)Marshal.SizeOf<Native.RAWINPUTHEADER>());
        if (copied == 0 || copied == 0xFFFFFFFF || copied > bufferSize) return;
        if (Marshal.PtrToStructure<Native.RAWINPUTHEADER>(rawInputBuffer).dwType
            != Native.RIM_TYPEMOUSE) return;
        var mouse = Marshal.PtrToStructure<Native.RAWMOUSE>(
            rawInputBuffer + Marshal.SizeOf<Native.RAWINPUTHEADER>());
        if ((mouse.usButtonFlags & Native.RI_MOUSE_LEFT_BUTTON_DOWN) == 0) return;
        if (!rawInputSeen)
        {
            rawInputSeen = true;
            Log("raw input flowing");
        }
        if (!Native.GetCursorPos(out var point)) return;
        long now = Environment.TickCount64;
        bool doubleClick = lastDown > 0
            && now - lastDown <= Native.GetDoubleClickTime()
            && Math.Abs(point.X - lastDownPt.X) <= Native.GetSystemMetrics(Native.SM_CXDOUBLECLK)
            && Math.Abs(point.Y - lastDownPt.Y) <= Native.GetSystemMetrics(Native.SM_CYDOUBLECLK);
        if (doubleClick)
        {
            lastDown = 0;   // the next click starts a fresh pair
            OnDesktopDoubleClick(new Point(point.X, point.Y));
        }
        else
        {
            lastDown = now;
            lastDownPt = point;
        }
    }

    /// <summary>A double-click landed: electrocute the desktop unless it hit an ordinary window.</summary>
    private void OnDesktopDoubleClick(Point pt)
    {
        if (Program.ProbeMode) return;
        string? reject = stopped ? "paused"
            : !awake ? "screen off"
            : !SystemInformation.VirtualScreen.Contains(pt) ? "off-screen"
            : OverTaskbar(pt) ? "taskbar"
            : WindowBlockers().Any(b => b.Contains(pt)) ? "window"
            : null;
        if (reject != null)
        {
            Log($"double-click at ({pt.X},{pt.Y}) ignored: {reject}");
            return;
        }
        Log($"strike at ({pt.X},{pt.Y})");
        activityUntil = DateTime.UtcNow + TimeSpan.FromSeconds(2.5);
        ApplyRate();
        foreach (var form in forms)
            if (form.ScreenBounds.Contains(pt))
                form.Strike(pt);
    }

    private static bool OverTaskbar(Point pt)
    {
        bool hit = false;
        Native.EnumWindows((hwnd, _) =>
        {
            if (Native.ClassIs(hwnd, "Shell_TrayWnd") || Native.ClassIs(hwnd, "Shell_SecondaryTrayWnd"))
            {
                var r = Native.WindowRect(hwnd);
                if (pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom) hit = true;
            }
            return !hit;
        }, IntPtr.Zero);
        return hit;
    }

    /// <summary>How much of a screen ordinary windows leave uncovered, from none to all of it.</summary>
    private static double Exposure(Rectangle screen, List<Rectangle> blockers)
    {
        if (blockers.Count == 0) return 1;
        const int columns = 16, rows = 10;
        int free = 0;
        for (int column = 0; column < columns; column++)
        {
            for (int row = 0; row < rows; row++)
            {
                double x = screen.Left + screen.Width * (column + 0.5) / columns;
                double y = screen.Top + screen.Height * (row + 0.5) / rows;
                if (!blockers.Any(b => x >= b.Left && x < b.Right && y >= b.Top && y < b.Bottom)) free++;
            }
        }
        return free / (double)(columns * rows);
    }

    /// <summary>Visible, non-cloaked windows of other processes that could hide the wallpaper.</summary>
    private static List<Rectangle> WindowBlockers()
    {
        var list = new List<Rectangle>();
        uint me = (uint)Environment.ProcessId;
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == me) return true;
            if (!Native.IsWindowVisible(hwnd)) return true;
            var name = new System.Text.StringBuilder(64);
            Native.GetClassName(hwnd, name, 64);
            string cls = name.ToString();
            if (cls is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "Shell_TrayWnd"
                or "Shell_SecondaryTrayWnd" or "XamlExplorerHostIslandWindow") return true;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                && cloaked != 0) return true;
            uint exStyle = (uint)Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            if ((exStyle & Native.WS_EX_TOOLWINDOW) != 0) return true;
            if ((exStyle & 0x00080000) != 0) // WS_EX_LAYERED
            {
                // Click-through overlays (screen-recorder frames, capture banners) hide nothing.
                if ((exStyle & 0x00000020) != 0) return true; // WS_EX_TRANSPARENT
                if (Native.GetLayeredWindowAttributes(hwnd, IntPtr.Zero, out byte alpha, out uint flags)
                    && (flags & Native.LWA_ALPHA) != 0 && alpha < 243) return true;
            }
            Native.RECT bounds;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS,
                out bounds, System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) != 0)
            {
                bounds = Native.WindowRect(hwnd);
            }
            if (bounds.Right - bounds.Left <= 1 || bounds.Bottom - bounds.Top <= 1) return true;
            list.Add(Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public void OnPageMessage(WallpaperForm form, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("handler", out var handler)) return;
            switch (handler.GetString())
            {
                case "ready":
                    form.MarkLoaded();
                    form.SendHostState();
                    Log($"scene ready on {form.Screen.DeviceName}");
                    if (Program.ProbeMode) _ = form.RunProbeAsync();
                    break;
                case "report":
                    string value = root.TryGetProperty("value", out var v) ? v.ToString() : string.Empty;
                    Log($"page: {value}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"bad page message: {ex.Message}");
        }
    }

    public void Log(string line)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}\n");
        }
        catch { }
    }

    private static WorldDef LoadWorld()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey);
            return WorldDef.FromKey(key?.GetValue("world") as string);
        }
        catch { return WorldDef.All[0]; }
    }

    private static void SaveWorld(WorldDef world)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegKey);
            key.SetValue("world", world.Key);
        }
        catch { }
    }

    private static bool LoadPaused()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey);
            return key?.GetValue("paused") is int i && i != 0;
        }
        catch { return false; }
    }

    private static void SavePaused(bool paused)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegKey);
            key.SetValue("paused", paused ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static bool LoadAnimateCovered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey);
            return key?.GetValue("animateCovered") is not int i || i != 0;
        }
        catch { return true; }
    }

    private static void SaveAnimateCovered(bool value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegKey);
            key.SetValue("animateCovered", value ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { }
    }

    public void Dispose()
    {
        if (inputSink != null)
        {
            try
            {
                var devices = new[] { new Native.RAWINPUTDEVICE
                {
                    usUsagePage = 0x01,
                    usUsage = 0x02,
                    dwFlags = Native.RIDEV_REMOVE,
                    hwndTarget = IntPtr.Zero,
                } };
                Native.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<Native.RAWINPUTDEVICE>());
            }
            catch { }
            inputSink.DestroyHandle();
            inputSink = null;
        }
        if (rawInputBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(rawInputBuffer);
            rawInputBuffer = IntPtr.Zero;
        }
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        cursorTimer?.Dispose();
        occlusionTimer?.Dispose();
        foreach (var form in forms) form.Dispose();
        forms.Clear();
        tray?.Dispose();
    }
}
