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
    private const int ResizeMargin = 6;

    private readonly OverlayHeaderPanel _header;
    private readonly DpsCanvas _dps;

    private IPacketSource? _source;
    private readonly DpsMeter      _meter   = new();
    private readonly PartyTracker  _party   = new();
    private DpsPipeline? _pipeline;
    private ProtocolPipeline? _protocol;

    /// Optional override: when set before Load fires, OverlayForm uses this packet source
    /// instead of constructing a live PacketSniffer. Used for replay mode.
    public IPacketSource? PacketSourceOverride { get; set; }

    private bool _locked;          // when locked, click-through + ignore drag/resize
    private bool _compactMode;
    private bool _appCloseRequested;

    public HotkeyManager? Hotkeys { get; set; }
    public SecondaryWindows Windows { get; }
    public event EventHandler? AppCloseRequested;

    private void PersistWindowState()
    {
        if (WindowState != FormWindowState.Normal) return;
        var s = AppSettings.Instance;
        s.WindowState.X = Location.X;
        s.WindowState.Y = Location.Y;
        s.WindowState.Width  = Size.Width;
        s.WindowState.Height = Size.Height;
        s.SaveDebounced();
    }

    protected override void OnMove(EventArgs e)   { base.OnMove(e);   PersistWindowState(); }
    protected override void OnResizeEnd(EventArgs e){ base.OnResizeEnd(e); PersistWindowState(); }

    public OverlayForm()
    {
        Text = "A2Meter";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(380, 360);

        var ws = AppSettings.Instance.WindowState;
        Location = new Point(ws.X, ws.Y);
        int wantW = ws.Width  >= MinimumSize.Width  ? ws.Width  : 460;
        int wantH = ws.Height >= MinimumSize.Height ? ws.Height : 500;
        Size = new Size(wantW, wantH);
        BackColor = Color.FromArgb(8, 11, 20);
        Windows = new SecondaryWindows(this);

        _header = new OverlayHeaderPanel();
        _header.LockToggled    += SetLocked;
        _header.HistoryClicked += OpenHistory;
        _header.OpacityChanged += OnOpacitySlider;
        _header.CloseClicked   += RequestAppClose;

        _dps = new DpsCanvas { Dock = DockStyle.Fill };
        _dps.PlayerRowClicked += row =>
        {
            var detail = Windows.Open<DpsDetailForm>();
            detail.SetData(row);
        };

        Controls.Add(_dps);
        Controls.Add(_header);

        Load += (_, _) => InitOverlay();
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
        // Bridge is null now — DpsPipeline still accepts one but the secondary
        // windows manage their own when opened, so the overlay no longer needs it.
        _pipeline = new DpsPipeline(_source, _meter, _party, _dps);
        try { _pipeline.Start(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[overlay] packet source failed to start: " + ex.Message);
        }

    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
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

    private void OnOpacitySlider(int value)
    {
        AppSettings.Instance.Opacity = value;
        AppSettings.Instance.SaveDebounced();
        ApplyOpacity();
    }

    private void SetLocked(bool locked)
    {
        _locked = locked;
        var ex = Win32Native.GetWindowLong(Handle, Win32Native.GWL_EXSTYLE);
        if (locked) ex |=  Win32Native.WS_EX_TRANSPARENT;
        else        ex &= ~Win32Native.WS_EX_TRANSPARENT;
        Win32Native.SetWindowLong(Handle, Win32Native.GWL_EXSTYLE, ex);
    }

    public void Unlock()
    {
        SetLocked(false);
        _header.ForceUnlock();
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
        // TODO: tell DpsCanvas to redraw in compact layout
    }

    public void TriggerClearShortcut()  => _pipeline?.Reset();
    public void TriggerSwitchTab()      { /* compact/full toggle is the only "tab" we have */ }

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
            var screenPt = new Point(m.LParam.ToInt32());
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
