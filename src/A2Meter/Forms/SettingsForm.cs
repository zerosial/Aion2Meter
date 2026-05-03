using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;

namespace A2Meter.Forms;

/// Native WinForms settings panel — replaces the WebView2 settings.html.
/// Each control mutates AppSettings live and persists via SaveDebounced.
internal sealed class SettingsForm : Form
{
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION        = 2;
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    public event Action<int>?              OpacityChanged;          // 0..100
    public event Action<bool>?             OverlayOnlyToggled;
    public event Action<ShortcutSettings>? ShortcutsChanged;

    private readonly AppSettings _s = AppSettings.Instance;

    public SettingsForm()
    {
        Text = "A2Meter — 설정";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(360, 480);
        Size = new Size(420, 560);
        BackColor = Color.FromArgb(16, 20, 42);
        ForeColor = Color.FromArgb(220, 230, 245);
        Font = new Font("Malgun Gothic", 9.0f);

        BuildLayout();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32Native.WS_EX_TOOLWINDOW | Win32Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    private void BuildLayout()
    {
        // ── title bar ───────────────────────────────────────────────────
        var title = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.FromArgb(12, 18, 30) };
        var titleLabel = new Label
        {
            Text = "설정",
            Font = new Font("Malgun Gothic", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(170, 195, 230),
            AutoSize = true,
            Location = new Point(12, 10),
            BackColor = Color.Transparent,
        };
        var btnClose = new Button
        {
            Text = "✕",
            Width = 36, Height = 28,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(190, 200, 220),
            BackColor = Color.FromArgb(12, 18, 30),
            Font = new Font("Segoe UI", 11f),
            TabStop = false,
        };
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(180, 60, 60);
        btnClose.Click += (_, _) => Close();
        btnClose.Location = new Point(Width - btnClose.Width - 4, 4);
        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        title.Controls.Add(titleLabel);
        title.Controls.Add(btnClose);
        // Drag the form by the title bar.
        title.MouseDown       += (_, e) => Drag(e);
        titleLabel.MouseDown  += (_, e) => Drag(e);

        // ── body (scrollable) ───────────────────────────────────────────
        var body = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(16, 12, 16, 12),
            BackColor = Color.FromArgb(16, 20, 42),
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Overlay-only-when-Aion checkbox
        var chkAion = new CheckBox
        {
            Text = "아이온2 활성화 시에만 오버레이 표시",
            ForeColor = ForeColor,
            BackColor = Color.Transparent,
            Checked = _s.OverlayOnlyWhenAion,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 12),
        };
        chkAion.CheckedChanged += (_, _) =>
        {
            _s.OverlayOnlyWhenAion = chkAion.Checked;
            _s.SaveDebounced();
            OverlayOnlyToggled?.Invoke(chkAion.Checked);
        };
        content.Controls.Add(chkAion);

        // Opacity slider
        content.Controls.Add(SectionHeader("오버레이 투명도"));
        var opacityRow = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.Transparent };
        var sliderOpacity = new TrackBar
        {
            Minimum = 30, Maximum = 100, TickStyle = TickStyle.None,
            Value = Math.Clamp(_s.Opacity, 30, 100),
            Width = 280, Location = new Point(0, 0),
        };
        var lblOpacity = new Label
        {
            Text = $"{_s.Opacity}%",
            ForeColor = Color.FromArgb(170, 195, 230),
            AutoSize = true, Location = new Point(290, 8),
            BackColor = Color.Transparent,
        };
        sliderOpacity.ValueChanged += (_, _) =>
        {
            _s.Opacity = sliderOpacity.Value;
            lblOpacity.Text = $"{_s.Opacity}%";
            _s.SaveDebounced();
            OpacityChanged?.Invoke(_s.Opacity);
        };
        opacityRow.Controls.Add(sliderOpacity);
        opacityRow.Controls.Add(lblOpacity);
        content.Controls.Add(opacityRow);

        // Text scale slider
        content.Controls.Add(SectionHeader("폰트 크기 (%)"));
        var (fontSlider, fontLabel) = ScalePair(_s.FontScale, 70, 200, v => { _s.FontScale = v; _s.SaveDebounced(); });
        var fontRow = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.Transparent };
        fontRow.Controls.Add(fontSlider);
        fontRow.Controls.Add(fontLabel);
        content.Controls.Add(fontRow);

        // UI scale slider
        content.Controls.Add(SectionHeader("UI 크기 (%)"));
        var (uiSlider, uiLabel) = ScalePair(_s.TextScale, 70, 200, v => { _s.TextScale = v; _s.SaveDebounced(); });
        var uiRow = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.Transparent };
        uiRow.Controls.Add(uiSlider);
        uiRow.Controls.Add(uiLabel);
        content.Controls.Add(uiRow);

        // DPS preferences
        content.Controls.Add(SectionHeader("DPS 표시"));
        content.Controls.Add(BoolToggle("새로고침 시 파티 유지", _s.KeepPartyOnRefresh, v => { _s.KeepPartyOnRefresh = v; _s.SaveDebounced(); }));
        content.Controls.Add(BoolToggle("새로고침 시 본인 유지", _s.KeepSelfOnRefresh, v => { _s.KeepSelfOnRefresh = v; _s.SaveDebounced(); }));
        content.Controls.Add(BoolToggle("자동 탭 전환", _s.AutoTabSwitch, v => { _s.AutoTabSwitch = v; _s.SaveDebounced(); }));

        // GPU mode
        content.Controls.Add(SectionHeader("GPU 가속"));
        var radioGpuOn  = new RadioButton { Text = "켜기 (디폴트)", AutoSize = true, ForeColor = ForeColor, BackColor = Color.Transparent, Checked = _s.GpuMode == "on" };
        var radioGpuOff = new RadioButton { Text = "끄기 (게임 프레임 우선)", AutoSize = true, ForeColor = ForeColor, BackColor = Color.Transparent, Checked = _s.GpuMode == "off" };
        radioGpuOn.CheckedChanged  += (_, _) => { if (radioGpuOn.Checked)  { _s.GpuMode = "on";  _s.GpuModeUserOverride = true; _s.SaveDebounced(); } };
        radioGpuOff.CheckedChanged += (_, _) => { if (radioGpuOff.Checked) { _s.GpuMode = "off"; _s.GpuModeUserOverride = true; _s.SaveDebounced(); } };
        var gpuRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent };
        gpuRow.Controls.Add(radioGpuOn);
        gpuRow.Controls.Add(radioGpuOff);
        content.Controls.Add(gpuRow);

        // Shortcuts
        content.Controls.Add(SectionHeader("단축키"));
        content.Controls.Add(ShortcutRow("표시 토글",   _s.Shortcuts.Toggle,    s => { _s.Shortcuts.Toggle    = s; PersistShortcuts(); }));
        content.Controls.Add(ShortcutRow("리셋",        _s.Shortcuts.Refresh,   s => { _s.Shortcuts.Refresh   = s; PersistShortcuts(); }));
        content.Controls.Add(ShortcutRow("컴팩트 모드", _s.Shortcuts.Compact,   s => { _s.Shortcuts.Compact   = s; PersistShortcuts(); }));
        content.Controls.Add(ShortcutRow("탭 전환",     _s.Shortcuts.SwitchTab, s => { _s.Shortcuts.SwitchTab = s; PersistShortcuts(); }));

        body.Controls.Add(content);

        Controls.Add(body);
        Controls.Add(title);
    }

    // ── helpers ────────────────────────────────────────────────────────

    private void PersistShortcuts()
    {
        _s.SaveDebounced();
        ShortcutsChanged?.Invoke(_s.Shortcuts);
    }

    private Label SectionHeader(string text) => new()
    {
        Text = text,
        Font = new Font("Malgun Gothic", 9.0f, FontStyle.Bold),
        ForeColor = Color.FromArgb(120, 200, 255),
        AutoSize = true,
        Margin = new Padding(0, 12, 0, 4),
        BackColor = Color.Transparent,
    };

    private CheckBox BoolToggle(string label, bool initial, Action<bool> onChange)
    {
        var cb = new CheckBox
        {
            Text = label,
            ForeColor = ForeColor,
            BackColor = Color.Transparent,
            AutoSize = true,
            Checked = initial,
            Margin = new Padding(0, 2, 0, 2),
        };
        cb.CheckedChanged += (_, _) => onChange(cb.Checked);
        return cb;
    }

    private (TrackBar slider, Label label) ScalePair(int initial, int min, int max, Action<int> onChange)
    {
        var slider = new TrackBar
        {
            Minimum = min, Maximum = max, TickStyle = TickStyle.None,
            Value = Math.Clamp(initial, min, max),
            Width = 280, Location = new Point(0, 0),
        };
        var label = new Label
        {
            Text = $"{initial}%",
            ForeColor = Color.FromArgb(170, 195, 230),
            AutoSize = true, Location = new Point(290, 8),
            BackColor = Color.Transparent,
        };
        slider.ValueChanged += (_, _) =>
        {
            label.Text = $"{slider.Value}%";
            onChange(slider.Value);
        };
        return (slider, label);
    }

    private Panel ShortcutRow(string label, string accelerator, Action<string> onChange)
    {
        var row = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 2) };
        var l = new Label
        {
            Text = label,
            ForeColor = ForeColor,
            BackColor = Color.Transparent,
            AutoSize = false,
            Width = 110, Height = 22,
            Location = new Point(0, 4),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var capture = new ShortcutBox
        {
            Text = accelerator,
            Width = 200, Height = 24,
            Location = new Point(120, 3),
            BackColor = Color.FromArgb(28, 36, 56),
            ForeColor = Color.FromArgb(220, 230, 245),
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            TextAlign = HorizontalAlignment.Center,
        };
        capture.AcceleratorChanged += s =>
        {
            capture.Text = s;
            onChange(s);
        };
        row.Controls.Add(l);
        row.Controls.Add(capture);
        return row;
    }

    private void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    /// One-shot keyboard capture textbox: focus it, press a chord, the chord
    /// becomes the new accelerator string ("Ctrl+Shift+1", "Alt+`", etc).
    private sealed class ShortcutBox : TextBox
    {
        public event Action<string>? AcceleratorChanged;

        public ShortcutBox()
        {
            KeyDown += OnKeyDown;
            GotFocus += (_, _) => { BackColor = Color.FromArgb(50, 70, 110); };
            LostFocus += (_, _) => { BackColor = Color.FromArgb(28, 36, 56); };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Focused && msg.Msg == 0x100 /* WM_KEYDOWN */)
            {
                if (TryFormat(keyData, out var s)) AcceleratorChanged?.Invoke(s);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (TryFormat(e.KeyData, out var s)) { AcceleratorChanged?.Invoke(s); e.SuppressKeyPress = true; }
            e.Handled = true;
        }

        private static bool TryFormat(Keys data, out string s)
        {
            s = "";
            var key = data & Keys.KeyCode;
            if (key == Keys.None || key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu) return false;
            var parts = new System.Collections.Generic.List<string>(3);
            if ((data & Keys.Control) != 0) parts.Add("Ctrl");
            if ((data & Keys.Shift)   != 0) parts.Add("Shift");
            if ((data & Keys.Alt)     != 0) parts.Add("Alt");
            parts.Add(KeyName(key));
            s = string.Join("+", parts);
            return true;
        }

        private static string KeyName(Keys key) => key switch
        {
            Keys.D0 or Keys.NumPad0 => "0",
            Keys.D1 or Keys.NumPad1 => "1",
            Keys.D2 or Keys.NumPad2 => "2",
            Keys.D3 or Keys.NumPad3 => "3",
            Keys.D4 or Keys.NumPad4 => "4",
            Keys.D5 or Keys.NumPad5 => "5",
            Keys.D6 or Keys.NumPad6 => "6",
            Keys.D7 or Keys.NumPad7 => "7",
            Keys.D8 or Keys.NumPad8 => "8",
            Keys.D9 or Keys.NumPad9 => "9",
            Keys.Oemtilde            => "`",
            _ => key.ToString(),
        };
    }
}
