using System;
using System.Drawing;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Direct2D;
using A2Meter.Dps;

namespace A2Meter.Forms;

/// Frameless, topmost overlay shell.
/// Layout (native + D2D, no WebView2 in the overlay process anymore):
///   [OverlayHeaderPanel: WinForms ~36px]   ← brand + lock/settings/close buttons
///   [DpsCanvas:          Direct2D fills]   ← bars/numbers (high-frequency redraw)
///
/// The secondary windows (Settings/DpsDetail/CombatRecords/PlayerNote/Consent)
/// still use WebView2 because they're complex HTML and only opened on demand.
internal sealed class OverlayForm : Form
{
    private const int HeaderHeight = 36;
    private const int ResizeMargin = 10;

    private readonly OverlayHeaderPanel _header;
    private readonly DpsCanvas _dps;

    private IPacketSource? _source;
    private readonly DpsMeter      _meter   = new();
    private readonly PartyTracker  _party   = new();
    private DpsPipeline? _pipeline;
    private ProtocolPipeline? _protocol;
    private ForegroundWatcher? _fgWatcher;
    private CompactOverlayForm? _compactForm;

    /// Optional override: when set before Load fires, OverlayForm uses this packet source
    /// instead of constructing a live PacketSniffer. Used for replay mode.
    public IPacketSource? PacketSourceOverride { get; set; }

    private bool _locked;          // when locked, click-through + ignore drag/resize
    private bool _compactMode;
    private bool _appCloseRequested;
    private bool _loaded;          // true after Load — prevents saving during init

    public HotkeyManager? Hotkeys { get; set; }
    public SecondaryWindows Windows { get; }
    public event EventHandler? AppCloseRequested;

    private void PersistWindowState()
    {
        if (!_loaded) return;
        if (WindowState != FormWindowState.Normal) return;
        var s = AppSettings.Instance;
        s.WindowState.X = Location.X;
        s.WindowState.Y = Location.Y;
        s.WindowState.Width  = Size.Width;
        s.WindowState.Height = Size.Height;
        s.SaveDebounced();
    }

    protected override void OnMove(EventArgs e)      { base.OnMove(e);      PersistWindowState(); }
    protected override void OnResize(EventArgs e)    { base.OnResize(e);    PersistWindowState(); }
    protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); PersistWindowState(); }

    public OverlayForm()
    {
        Text = "A2Meter";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(100, 100);

        var ws = AppSettings.Instance.WindowState;
        Location = new Point(ws.X, ws.Y);
        int wantW = ws.Width  >= MinimumSize.Width  ? ws.Width  : 460;
        int wantH = ws.Height >= MinimumSize.Height ? ws.Height : 500;
        Size = new Size(wantW, wantH);
        BackColor = AppSettings.Instance.Theme.BgColor;
        Windows = new SecondaryWindows(this);

        _header = new OverlayHeaderPanel();
        _header.LockToggled      += SetLocked;
        _header.AnonymousToggled += anon => { _dps.AnonymousMode = anon; _dps.Invalidate(); };
        _header.HistoryClicked   += OpenHistory;
        _header.SettingsClicked  += OpenSettings;
        _header.OpacityChanged   += OnOpacitySlider;
        _header.CloseClicked     += RequestAppClose;

        _dps = new DpsCanvas { Dock = DockStyle.Fill };
        _dps.PlayerRowClicked += row =>
        {
            var detail = Windows.Open<DpsDetailForm>();
            detail.SetData(row);
        };
        _dps.CountdownClicked += OnCountdownClicked;

        Controls.Add(_dps);
        Controls.Add(_header);

        Load += (_, _) => { _loaded = true; InitOverlay(); };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32Native.WS_EX_LAYERED
                        | Win32Native.WS_EX_TOOLWINDOW
                        | Win32Native.WS_EX_NOACTIVATE
                        | Win32Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    private void InitOverlay()
    {
        var alpha = (byte)Math.Clamp(AppSettings.Instance.Opacity * 255 / 100, 30, 255);
        Win32Native.SetLayeredWindowAttributes(Handle, 0, alpha, Win32Native.LWA_ALPHA);

        _source ??= PacketSourceOverride ?? new PacketSniffer();
        _protocol = new ProtocolPipeline(_source, log: msg => Console.Error.WriteLine(msg));
        _pipeline = new DpsPipeline(_source, _meter, _party, _dps);
        try { _pipeline.Start(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[overlay] packet source failed to start: " + ex.Message);
        }

        // Foreground watcher: hide overlay when Aion 2 is not active.
        _fgWatcher = new ForegroundWatcher("aion2");
        _fgWatcher.ActiveChanged += OnAionActiveChanged;
        if (AppSettings.Instance.OverlayOnlyWhenAion)
            _fgWatcher.Start();
    }

    private void OnAionActiveChanged(bool active)
    {
        if (!AppSettings.Instance.OverlayOnlyWhenAion) return;
        if (active) ShowOverlay();
        else        HideOverlay();
    }

    /// Called from TrayManager when the setting is toggled.
    public void SetOverlayOnlyWhenAion(bool enabled)
    {
        if (enabled)
        {
            _fgWatcher?.Start();
            // If Aion 2 is not currently active, hide immediately.
            if (_fgWatcher != null && !_fgWatcher.IsActive)
                HideOverlay();
        }
        else
        {
            _fgWatcher?.Stop();
            ShowOverlay();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _fgWatcher?.Dispose();
        _compactForm?.Close();
        _lockBtn?.Close();
        _pipeline?.Dispose();
        _protocol?.Dispose();
        base.OnFormClosed(e);
    }

    private void ApplyOpacity()
    {
        var alpha = (byte)Math.Clamp(AppSettings.Instance.Opacity * 255 / 100, 30, 255);
        Win32Native.SetLayeredWindowAttributes(Handle, 0, alpha, Win32Native.LWA_ALPHA);
    }

    private void OpenHistory()
    {
        if (_pipeline == null) return;
        var form = Windows.Open<CombatHistoryForm>();
        form.FormClosed += (_, _) => _pipeline.ExitHistoryView();
        form.SetData(_pipeline.History, record =>
        {
            // Pause live updates and show the historical snapshot.
            _pipeline.EnterHistoryView();
            var rows = _pipeline.MapSnapshotForCanvas(record.Snapshot);
            var summary = new DpsCanvas.SessionSummary(
                DurationSec:    record.DurationSec,
                TotalDamage:    record.TotalDamage,
                AverageDps:     record.AverageDps,
                PeakDps:        record.PeakDps,
                TopActorName:   rows.Count > 0 ? rows[0].Name : "",
                TopActorDamage: rows.Count > 0 ? rows[0].Damage : 0,
                BossName:       record.BossName);
            string timer = $"{(int)record.DurationSec / 60}:{(int)record.DurationSec % 60:00}";
            _dps.SetData(rows, record.TotalDamage, timer, record.Snapshot.Target, summary);
        });
    }

    private void OpenSettings()
    {
        var form = Windows.Open<SettingsPanelForm>();
        form.SettingsChanged += () =>
        {
            _dps.ApplySettings();
            _compactForm?.ApplySettings();
            var t = AppSettings.Instance.Theme;
            BackColor = t.BgColor;
            _header.BackColor = t.HeaderColor;
            _header.Invalidate();
        };
    }

    private void OnOpacitySlider(int value)
    {
        AppSettings.Instance.Opacity = value;
        AppSettings.Instance.SaveDebounced();
        ApplyOpacity();
    }

    private LockButtonForm? _lockBtn;

    private void SetLocked(bool locked)
    {
        _locked = locked;
        var ex = Win32Native.GetWindowLong(Handle, Win32Native.GWL_EXSTYLE);
        if (locked) ex |=  Win32Native.WS_EX_TRANSPARENT;
        else        ex &= ~Win32Native.WS_EX_TRANSPARENT;
        Win32Native.SetWindowLong(Handle, Win32Native.GWL_EXSTYLE, ex);

        if (locked)
        {
            _lockBtn ??= new LockButtonForm(this);
            _lockBtn.PlaceNear(this);
            _lockBtn.Show();
        }
        else
        {
            _lockBtn?.Hide();
        }
    }

    public void Unlock()
    {
        SetLocked(false);
        _header.ForceUnlock();
        _lockBtn?.Hide();
    }

    public void ShowOverlay()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        BringToFront();
    }

    public void HideOverlay() => Hide();

    public void ToggleVisibility()
    {
        if (Visible) HideOverlay(); else ShowOverlay();
    }

    public void ToggleCompact()
    {
        _compactMode = !_compactMode;

        if (_compactMode)
        {
            _lockBtn?.Hide();
            // Create compact overlay at same position/size.
            _compactForm ??= new CompactOverlayForm();
            _compactForm.Location = Location;
            _compactForm.Size = Size;
            // Subscribe to data updates.
            if (_pipeline != null)
                _pipeline.DataPushed += _compactForm.PushData;
            _compactForm.Show();
            _compactForm.RenderFrame();
            Hide();
        }
        else
        {
            // Unsubscribe and hide compact form.
            if (_pipeline != null)
                _pipeline.DataPushed -= _compactForm!.PushData;
            // Sync position from compact form back to main overlay.
            if (_compactForm != null)
            {
                Location = _compactForm.Location;
                PersistWindowState();
            }
            _compactForm?.Hide();
            // Restore overlay.
            Show();
            if (_locked) _lockBtn?.Show();
        }
    }

    private void SetClickThrough(bool passThrough)
    {
        var ex = Win32Native.GetWindowLong(Handle, Win32Native.GWL_EXSTYLE);
        if (passThrough) ex |=  Win32Native.WS_EX_TRANSPARENT;
        else             ex &= ~Win32Native.WS_EX_TRANSPARENT;
        Win32Native.SetWindowLong(Handle, Win32Native.GWL_EXSTYLE, ex);
    }

    public void TriggerClearShortcut()  => _pipeline?.Reset();
    public void TriggerSwitchTab()      { /* compact/full toggle is the only "tab" we have */ }

    public void TriggerRestart()
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
            System.Diagnostics.Process.Start(exe);
        Environment.Exit(0);
    }

    public void TriggerAnonymousToggle()
    {
        _dps.AnonymousMode = !_dps.AnonymousMode;
        _header.SetAnonymous(_dps.AnonymousMode);
        _dps.Invalidate();
    }

    private void OnCountdownClicked()
    {
        if (_pipeline == null) return;
        _pipeline.CycleCountdown();
        _dps.CountdownSec = _pipeline.CountdownSeconds;
        _dps.CountdownExpired = _pipeline.CountdownExpired;
        _dps.Invalidate();
    }

    public void RequestAppClose()
    {
        _appCloseRequested = true;
        Windows.CloseAll();
        AppCloseRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == HotkeyManager.WM_HOTKEY)
        {
            Hotkeys?.ProcessHotkey(m.WParam.ToInt32());
            return;
        }

        if (m.Msg == Win32Native.WM_NCHITTEST && !_locked)
        {
            int lp = unchecked((int)(long)m.LParam);
            var screenPt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            var pt = PointToClient(screenPt);

            int hit = HitTestEdges(pt);
            if (hit != Win32Native.HTCLIENT)
            {
                m.Result = (IntPtr)hit;
                return;
            }

            // Drag from the header strip — but only outside of the header buttons.
            if (pt.Y >= 0 && pt.Y < HeaderHeight)
            {
                var headerPt = _header.PointToClient(screenPt);
                if (_header.IsDragArea(headerPt))
                {
                    m.Result = (IntPtr)Win32Native.HTCAPTION;
                    return;
                }
            }
        }
        base.WndProc(ref m);
    }

    private int HitTestEdges(Point pt)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        bool L = pt.X < ResizeMargin;
        bool R = pt.X >= w - ResizeMargin;
        bool T = pt.Y < ResizeMargin;
        bool B = pt.Y >= h - ResizeMargin;

        if (T && L) return Win32Native.HTTOPLEFT;
        if (T && R) return Win32Native.HTTOPRIGHT;
        if (B && L) return Win32Native.HTBOTTOMLEFT;
        if (B && R) return Win32Native.HTBOTTOMRIGHT;
        if (L) return Win32Native.HTLEFT;
        if (R) return Win32Native.HTRIGHT;
        if (T) return Win32Native.HTTOP;
        if (B) return Win32Native.HTBOTTOM;
        return Win32Native.HTCLIENT;
    }
}
