using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Deskworlds;

/// <summary>
/// One screen's worth of world: a borderless window parented to the wallpaper WorkerW,
/// hosting a WebView2 that renders the scene. The window never takes focus; the host
/// injects the global cursor position as pointer events instead.
/// </summary>
internal sealed class WallpaperForm : Form
{
    private static readonly int TaskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

    // Injected before any page script: bridges the WebKit-style host contract the scenes
    // already speak (webkit.messageHandlers.ready / scenePointer / sceneRate / ...) onto
    // WebView2's window.chrome.webview message channel.
    private const string HostBridge = """
        (() => {
          const bridge = window.chrome && window.chrome.webview;
          if (!bridge) return;
          window.webkit = window.webkit || {};
          window.webkit.messageHandlers = window.webkit.messageHandlers || {};
          const make = (name) => ({
            postMessage: (value) => {
              try { bridge.postMessage({ handler: name, value: value === undefined ? null : value }); } catch {}
            },
          });
          if (!window.webkit.messageHandlers.ready) window.webkit.messageHandlers.ready = make('ready');
          if (!window.webkit.messageHandlers.report) window.webkit.messageHandlers.report = make('report');
          for (const level of ['error', 'warn']) {
            const original = console[level];
            console[level] = (...parts) => {
              try {
                bridge.postMessage({ handler: 'report',
                  value: parts.map((part) => part && part.stack ? part.stack : String(part)).join(' ') });
              } catch {}
              original.apply(console, parts);
            };
          }
          window.addEventListener('error', (event) => {
            try { bridge.postMessage({ handler: 'report', value: event.message + ' at ' + event.filename + ':' + event.lineno }); } catch {}
          });
          window.addEventListener('unhandledrejection', (event) => {
            try { bridge.postMessage({ handler: 'report', value: String(event.reason) }); } catch {}
          });
          window.scenePointerCount = 0;
          window.scenePointer = (x, y) => {
            const canvas = document.querySelector('#scene');
            window.scenePointerCount++;
            if (canvas)
              canvas.dispatchEvent(new PointerEvent('pointermove', { clientX: x, clientY: y, bubbles: true }));
          };
          window.scenePointerOut = () => {
            const canvas = document.querySelector('#scene');
            if (canvas) canvas.dispatchEvent(new PointerEvent('pointerleave'));
          };
        })();
        """;

    // Probe mode only: copies each rendered WebGL frame into a 2D canvas inside the same
    // JS task (the drawing buffer is only guaranteed valid there), so the host can read
    // back pixels at any time regardless of preserveDrawingBuffer.
    private const string ProbeBridge = """
        (() => {
          const off = document.createElement('canvas');
          off.width = 16; off.height = 10;
          const ctx = off.getContext('2d', { willReadFrequently: true });
          window.__dwProbe = { ctx: ctx, frames: 0 };
          const orig = window.requestAnimationFrame;
          if (!orig) return;
          window.requestAnimationFrame = (cb) => orig((t) => {
            try { cb(t); } finally {
              window.__dwProbe.frames++;
              const c = document.querySelector('#scene');
              if (c) { try { ctx.drawImage(c, 0, 0, 16, 10); } catch {} }
            }
          });
        })();
        """;

    private readonly DeskworldsApp app;
    private readonly WorldDef world;
    private CoreWebView2Controller? controller;
    private CoreWebView2? core;
    private bool wtsRegistered;
    private IntPtr powerNotification = IntPtr.Zero;
    private double lastX = -1e4;
    private double lastY = -1e4;

    public bool Loaded { get; private set; }
    public int Rate { get; private set; }
    public bool OnBattery { get; private set; }
    public bool Inside { get; private set; }
    public Screen Screen { get; }
    public Rectangle ScreenBounds => Screen.Bounds;

    public WallpaperForm(DeskworldsApp app, WorldDef world, Screen screen)
    {
        this.app = app;
        this.world = world;
        Screen = screen;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = world.BackgroundColor;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { wtsRegistered = Native.WTSRegisterSessionNotification(Handle, 0); } catch { }
        try
        {
            var guid = Native.GUID_MONITOR_POWER_ON;
            powerNotification = Native.RegisterPowerSettingNotification(Handle, ref guid, 0);
        }
        catch { }
    }

    public async Task InitAsync(CoreWebView2Environment env)
    {
        try
        {
            controller = await env.CreateCoreWebView2ControllerAsync(Handle);
            if (IsDisposed)
            {
                controller.Close();
                controller = null;
                return;
            }
            core = controller.CoreWebView2;
            var settings = core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsPinchZoomEnabled = false;
            controller.DefaultBackgroundColor = world.BackgroundColor;
            controller.ShouldDetectMonitorScaleChanges = true;
            core.SetVirtualHostNameToFolderMapping(
                "deskworlds.local", AppContext.BaseDirectory, CoreWebView2HostResourceAccessKind.Allow);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(HostBridge);
            if (Program.ProbeMode) await core.AddScriptToExecuteOnDocumentCreatedAsync(ProbeBridge);
            core.WebMessageReceived += (sender, e) => app.OnPageMessage(this, e);
            core.NavigationCompleted += (sender, e) =>
            {
                if (!e.IsSuccess) app.Log($"navigation failed on {Screen.DeviceName}: {e.WebErrorStatus}");
            };
            UpdateControllerBounds();
            core.Navigate($"https://deskworlds.local/scenes/{world.Key}/wallpaper.html?quality=native");
        }
        catch (Exception ex)
        {
            app.Log($"webview init failed on {Screen.DeviceName}: {ex.Message}");
        }
    }

    private void UpdateControllerBounds()
    {
        if (controller != null) controller.Bounds = new Rectangle(Point.Empty, ClientSize);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateControllerBounds();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateControllerBounds();
    }

    public void MarkLoaded() => Loaded = true;

    /// <summary>Probe mode: two canvas samples three seconds apart, then a window capture, then exit.</summary>
    public async Task RunProbeAsync()
    {
        try
        {
            for (int i = 1; i <= 2; i++)
            {
                await Task.Delay(3000);
                app.Log($"probe#{i}: {await ProbeCanvasAsync()}");
            }
            CaptureForm();
            app.Log("probe done");
            Application.Exit();
        }
        catch (Exception ex)
        {
            app.Log($"probe failed: {ex.Message}");
            Application.Exit();
        }
    }

    private async Task<string> ProbeCanvasAsync()
    {
        if (core == null) return "no core";
        const string script = """
            (function(){
              var p = window.__dwProbe;
              if (!p) return JSON.stringify({error:'no probe hook'});
              var d = p.ctx.getImageData(0, 0, 16, 10).data;
              var s = [0, 0, 0];
              for (var i = 0; i < d.length; i += 4) { s[0]+=d[i]; s[1]+=d[i+1]; s[2]+=d[i+2]; }
              var n = d.length / 4;
              var c = document.querySelector('#scene');
              return JSON.stringify({
                frames: p.frames, w: c ? c.width : 0, h: c ? c.height : 0,
                avg: [Math.round(s[0]/n), Math.round(s[1]/n), Math.round(s[2]/n)]
              });
            })()
            """;
        string raw = await core.ExecuteScriptAsync(script);
        return JsonDocument.Parse(raw).RootElement.GetString() ?? raw;
    }

    private void CaptureForm()
    {
        try
        {
            using var bmp = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                Native.PrintWindow(Handle, hdc, 2 /* PW_RENDERFULLCONTENT */);
                g.ReleaseHdc(hdc);
            }
            string path = Path.Combine(DeskworldsApp.DataDir, "probe-shot.png");
            bmp.Save(path);
            app.Log($"probe shot: {path} ({new FileInfo(path).Length} bytes)");
        }
        catch (Exception ex)
        {
            app.Log($"probe shot failed: {ex.Message}");
        }
    }

    public void SetRate(int rate)
    {
        if (rate == Rate) return;
        Rate = rate;
        if (Rate == 0 && Inside)
        {
            SendPointerOut();
            Inside = false;
        }
        app.Log($"rate on {Screen.DeviceName}: {Rate} fps");
        SendHostState();
    }

    public void SetPower(bool battery)
    {
        if (battery == OnBattery) return;
        OnBattery = battery;
        SendHostState();
    }

    public void SendHostState()
    {
        if (!Loaded || core == null) return;
        _ = core.ExecuteScriptAsync(
            $"typeof scenePower === 'function' && scenePower({(OnBattery ? "true" : "false")});" +
            $"typeof sceneRate === 'function' && sceneRate({Rate});");
    }

    public void Feed()
    {
        if (!Loaded || Rate <= 0 || core == null) return;
        _ = core.ExecuteScriptAsync("typeof sceneFeed === 'function' && sceneFeed()");
    }

    /// <summary>The host electrocuted the desktop: a bolt strikes this screen point.</summary>
    public void Strike(Point screenPoint)
    {
        if (!Loaded || core == null) return;
        var client = new Native.POINT { X = screenPoint.X, Y = screenPoint.Y };
        Native.ScreenToClient(Handle, ref client);
        double scale = DeviceDpi / 96.0;
        _ = core.ExecuteScriptAsync(string.Create(CultureInfo.InvariantCulture,
            $"typeof sceneStrike === 'function' && sceneStrike({client.X / scale:0.0},{client.Y / scale:0.0})"));
    }

    public void SetPointer(double? x, double? y)
    {
        if (!Loaded || Rate <= 0 || core == null) return;
        if (x is null || y is null)
        {
            if (Inside) SendPointerOut();
            Inside = false;
            lastX = lastY = -1e4;
            return;
        }
        if (Math.Abs(x.Value - lastX) < 0.2 && Math.Abs(y.Value - lastY) < 0.2) return;
        lastX = x.Value;
        lastY = y.Value;
        Inside = true;
        _ = core.ExecuteScriptAsync(string.Create(
            CultureInfo.InvariantCulture, $"scenePointer({x.Value:0.0},{y.Value:0.0})"));
    }

    private void SendPointerOut()
    {
        if (core != null) _ = core.ExecuteScriptAsync("scenePointerOut()");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_WTSSESSION_CHANGE)
        {
            long reason = m.WParam.ToInt64();
            if (reason == Native.WTS_SESSION_LOCK || reason == Native.WTS_SESSION_UNLOCK)
                app.SessionChanged(reason == Native.WTS_SESSION_LOCK);
        }
        else if (m.Msg == Native.WM_POWERBROADCAST && m.WParam.ToInt64() == Native.PBT_POWERSETTINGCHANGE)
        {
            try
            {
                var setting = Marshal.PtrToStructure<Native.POWERBROADCAST_SETTING>(m.LParam);
                if (setting.PowerSetting == Native.GUID_MONITOR_POWER_ON)
                    app.MonitorPower(setting.Data != 0);
            }
            catch { }
        }
        else if (m.Msg == TaskbarCreated)
        {
            app.ExplorerRestarted();
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (wtsRegistered) { try { Native.WTSUnRegisterSessionNotification(Handle); } catch { } }
            if (powerNotification != IntPtr.Zero)
            { try { Native.UnregisterPowerSettingNotification(powerNotification); } catch { } }
            controller?.Close();
            controller = null;
            core = null;
        }
        base.Dispose(disposing);
    }
}
