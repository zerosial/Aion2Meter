using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Api;
using A2Meter.Core;
using A2Meter.Direct2D;
using D2DColor = Vortice.Mathematics.Color4;

namespace A2Meter.Forms;

internal sealed class DpsDetailForm : Form
{
    private const int ResizeMargin = 14;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private readonly Label _lblName;
    private readonly Label _lblJob;
    private readonly FlowLayoutPanel _infoPanel;
    private readonly FlowLayoutPanel _statsPanel;
    private readonly VirtualListPanel _list;
    private readonly Panel _titleBar;
    private Color _accentColor = Color.FromArgb(100, 160, 220);

    private readonly List<SkillRow> _rows = new();

    private record struct SkillRow(
        string Name, int[]? Specs, string Hits,
        string Crit, string Back, string Strong, string Perfect, string Multi, string Dodge, string Block,
        string Max, string Dps, string Avg, string Damage, double Percent);

    public DpsDetailForm()
    {
        Text = "전투 상세";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        MinimumSize = new Size(700, 300);
        var _s = AppSettings.Instance;
        Size = new Size(
            Math.Max(MinimumSize.Width, _s.DetailPanelWidth),
            Math.Max(MinimumSize.Height, _s.DetailPanelHeight));
        if (_s.DetailPanelX >= 0 && _s.DetailPanelY >= 0)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(_s.DetailPanelX, _s.DetailPanelY);
        }
        else StartPosition = FormStartPosition.CenterScreen;

        var _t = _s.Theme;
        string _fn = _s.FontName;
        float _fs = _s.FontSize;
        BackColor = _t.BgColor;
        ForeColor = _t.TextColor;
        Font = new Font(_fn, _fs);
        Padding = new Padding(3);
        DoubleBuffered = true;

        // ── title bar ──
        _titleBar = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = _t.HeaderColor };
        _titleBar.Paint += (_, e) =>
        {
            using var pen = new Pen(_t.BorderColor);
            e.Graphics.DrawLine(pen, 0, _titleBar.Height - 1, _titleBar.Width, _titleBar.Height - 1);
        };
        _lblName = new Label
        {
            Text = "", Font = new Font(_fn, _fs + 0.5f, FontStyle.Bold),
            ForeColor = _t.TextColor, AutoSize = true, Location = new Point(10, 9), BackColor = Color.Transparent,
        };
        _lblJob = new Label
        {
            Text = "", Font = new Font(_fn, _fs),
            ForeColor = _t.TextDimColor, AutoSize = true, Location = new Point(200, 11), BackColor = Color.Transparent,
        };
        var btnClose = new HeaderCloseButton();
        _titleBar.Controls.Add(_lblName);
        _titleBar.Controls.Add(_lblJob);
        _titleBar.Controls.Add(btnClose);
        _titleBar.Resize += (_, _) => btnClose.Location = new Point(_titleBar.Width - btnClose.Width - 8, (_titleBar.Height - btnClose.Height) / 2);
        btnClose.Click += (_, _) => Close();
        _titleBar.MouseDown += (_, e) => Drag(e);
        _lblName.MouseDown += (_, e) => Drag(e);
        _lblJob.MouseDown += (_, e) => Drag(e);

        // ── info / stats badges ──
        _infoPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 34, BackColor = _t.BgColor,
            Padding = new Padding(8, 6, 8, 4), WrapContents = false,
        };
        _statsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 34, BackColor = _t.BgColor,
            Padding = new Padding(8, 2, 8, 6), WrapContents = false,
        };

        // ── skill list ──
        _list = new VirtualListPanel
        {
            Dock = DockStyle.Fill,
            Font = new Font(_fn, _fs),
            RowHeight = (int)(20 + _fs),
            Columns = new[]
            {
                ("스킬",   120f, HorizontalAlignment.Left),
                ("특화",    50f, HorizontalAlignment.Center),
                ("타수",    35f, HorizontalAlignment.Right),
                ("치명타",  48f, HorizontalAlignment.Right),
                ("후방",    48f, HorizontalAlignment.Right),
                ("강타",    48f, HorizontalAlignment.Right),
                ("완벽",    48f, HorizontalAlignment.Right),
                ("다단",    48f, HorizontalAlignment.Right),
                ("회피",    48f, HorizontalAlignment.Right),
                ("막기",    48f, HorizontalAlignment.Right),
                ("MAX",     60f, HorizontalAlignment.Right),
                ("초당",    55f, HorizontalAlignment.Right),
                ("평균",    55f, HorizontalAlignment.Right),
                ("피해",   100f, HorizontalAlignment.Right),
            },
        };
        _list.PaintRow += OnPaintRow;

        Controls.Add(_list);
        Controls.Add(_statsPanel);
        Controls.Add(_infoPanel);
        Controls.Add(_titleBar);
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

    public void SetData(DpsCanvas.PlayerRow row)
    {
        _lblName.Text = row.Name;
        _lblJob.Text = row.JobIconKey;
        _accentColor = ToGdi(row.AccentColor);
        using (var g = CreateGraphics())
        {
            var sz = g.MeasureString(row.Name, _lblName.Font);
            _lblJob.Location = new Point((int)(16 + sz.Width), 11);
        }

        double elapsed = row.DpsValue > 0 ? (double)row.Damage / row.DpsValue : 0;

        _infoPanel.Controls.Clear();
        if (row.CombatPower > 0)
            _infoPanel.Controls.Add(Badge($"전투력 {row.CombatPower:N0}"));
        if (row.Skills is { Count: > 0 })
            _infoPanel.Controls.Add(Badge($"스킬 {row.Skills.Count}개"));

        _statsPanel.Controls.Clear();
        _statsPanel.Controls.Add(Badge($"누적피해 {row.Damage:N0}"));
        _statsPanel.Controls.Add(Badge($"치명타 {row.CritRate * 100:0.#}%"));
        if (row.DpsValue > 0) _statsPanel.Controls.Add(Badge($"DPS {row.DpsValue:N0}"));
        if (row.HealTotal > 0) _statsPanel.Controls.Add(Badge($"힐 {row.HealTotal:N0}"));
        if (row.DotDamage > 0 && row.Damage > 0)
            _statsPanel.Controls.Add(Badge($"DoT {(double)row.DotDamage / row.Damage * 100:0.#}%"));

        _rows.Clear();
        if (row.Skills is { Count: > 0 })
        {
            // Prefer SkillLevels carried on the row (saved in history JSON);
            // fall back to the live API cache.
            var lvMap = row.SkillLevels;
            if (lvMap is not { Count: > 0 })
            {
                string cleanName = row.Name;
                int idx = cleanName.IndexOf('[');
                if (idx > 0) cleanName = cleanName[..idx];
                lvMap = SkillLevelCache.Instance.Get(cleanName, row.ServerId)?.SkillLevels;
            }

            foreach (var s in row.Skills)
            {
                long avg = s.Hits > 0 ? s.Total / s.Hits : 0;
                long dps = elapsed > 0 ? (long)(s.Total / elapsed) : 0;

                // Append skill level if available.
                string displayName = s.Name;
                if (lvMap != null
                    && lvMap.TryGetValue(s.Name, out var lv) && lv > 0)
                {
                    displayName = $"{s.Name} Lv{lv}";
                }

                _rows.Add(new SkillRow(
                    displayName, s.Specs, $"{s.Hits}",
                    Pct(s.CritRate), Pct(s.BackRate), Pct(s.StrongRate),
                    Pct(s.PerfectRate), Pct(s.MultiHitRate), Pct(s.DodgeRate), Pct(s.BlockRate),
                    s.MaxHit > 0 ? $"{s.MaxHit:N0}" : "-",
                    dps > 0 ? $"{dps:N0}" : "-",
                    avg > 0 ? $"{avg:N0}" : "-",
                    $"{s.Total:N0} ({s.PercentOfActor * 100:0.#}%)",
                    s.PercentOfActor));
            }
        }
        _list.RowCount = _rows.Count;
    }

    private void OnPaintRow(Graphics g, Rectangle rowRect, Rectangle[] cells, int idx, bool sel)
    {
        if (idx < 0 || idx >= _rows.Count || cells.Length < 14) return;
        var r = _rows[idx];
        var theme = AppSettings.Instance.Theme;
        var fg = sel ? Color.White : theme.TextColor;

        // Col 0: skill name with damage bar
        double maxPct = _rows.Count > 0 ? _rows[0].Percent : 1;
        if (maxPct <= 0) maxPct = 1;
        double rel = r.Percent / maxPct;
        if (rel > 0)
        {
            int barW = (int)(cells[0].Width * rel);
            if (barW > 0)
            {
                var barRect = new Rectangle(cells[0].X, cells[0].Y + 2, barW, cells[0].Height - 4);
                using var brush = new SolidBrush(Color.FromArgb(80, _accentColor));
                g.FillRectangle(brush, barRect);
            }
        }
        DrawCell(g, cells[0], r.Name, fg, TextFormatFlags.Left);

        // Col 1: spec boxes
        PaintSpecBoxes(g, cells[1], r.Specs);

        // Cols 2..13: text
        string[] texts = { r.Hits, r.Crit, r.Back, r.Strong, r.Perfect, r.Multi, r.Dodge, r.Block, r.Max, r.Dps, r.Avg, r.Damage };
        for (int i = 0; i < texts.Length; i++)
            DrawCell(g, cells[i + 2], texts[i], fg, TextFormatFlags.Right);
    }

    private void DrawCell(Graphics g, Rectangle cell, string text, Color fg, TextFormatFlags align)
    {
        var tr = new Rectangle(cell.X + 3, cell.Y, cell.Width - 6, cell.Height);
        TextRenderer.DrawText(g, text, _list.Font, tr, fg, align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static void PaintSpecBoxes(Graphics g, Rectangle cell, int[]? specs)
    {
        const int boxSize = 7, gap = 2, maxTiers = 5;
        int totalW = maxTiers * boxSize + (maxTiers - 1) * gap;
        int x0 = cell.X + (cell.Width - totalW) / 2;
        int y0 = cell.Y + (cell.Height - boxSize) / 2;

        using var inBrush = new SolidBrush(Color.FromArgb(60, 70, 90));
        using var actBrush = new SolidBrush(Color.FromArgb(46, 204, 113));
        using var inPen = new Pen(Color.FromArgb(80, 100, 130), 1f);
        using var actPen = new Pen(Color.FromArgb(46, 204, 113), 1f);

        for (int t = 1; t <= maxTiers; t++)
        {
            int x = x0 + (t - 1) * (boxSize + gap);
            var rect = new Rectangle(x, y0, boxSize, boxSize);
            bool active = specs != null && Array.IndexOf(specs, t) >= 0;
            g.FillRectangle(active ? actBrush : inBrush, rect);
            g.DrawRectangle(active ? actPen : inPen, rect);
        }
    }

    private static Color ToGdi(D2DColor c) =>
        Color.FromArgb(
            (int)(Math.Clamp(c.A, 0f, 1f) * 255),
            (int)(Math.Clamp(c.R, 0f, 1f) * 255),
            (int)(Math.Clamp(c.G, 0f, 1f) * 255),
            (int)(Math.Clamp(c.B, 0f, 1f) * 255));

    private static string Pct(double rate) => $"{rate * 100:0.#}%";

    private static Label Badge(string text) => new()
    {
        Text = text,
        Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f),
        ForeColor = AppSettings.Instance.Theme.TextColor,
        BackColor = Color.FromArgb(20, 35, 50),
        AutoSize = true,
        Padding = new Padding(6, 3, 6, 3),
        Margin = new Padding(3, 0, 3, 0),
    };

    private void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
    }

    private void PersistBounds()
    {
        if (WindowState != FormWindowState.Normal) return;
        var s = AppSettings.Instance;
        s.DetailPanelX = Location.X;
        s.DetailPanelY = Location.Y;
        s.DetailPanelWidth = Size.Width;
        s.DetailPanelHeight = Size.Height;
        s.SaveDebounced();
    }

    protected override void OnMove(EventArgs e) { base.OnMove(e); PersistBounds(); }
    protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); PersistBounds(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(AppSettings.Instance.Theme.BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32Native.WM_NCHITTEST)
        {
            int lp = unchecked((int)(long)m.LParam);
            var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            int hit = HitTestEdges(pt);
            if (hit != Win32Native.HTCLIENT) { m.Result = (IntPtr)hit; return; }
            if (pt.Y < 36 && pt.X < Width - 40) { m.Result = (IntPtr)Win32Native.HTCAPTION; return; }
        }
        base.WndProc(ref m);
    }

    private int HitTestEdges(Point pt)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        bool L = pt.X < ResizeMargin, R = pt.X >= w - ResizeMargin;
        bool T = pt.Y < ResizeMargin, B = pt.Y >= h - ResizeMargin;
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

    private sealed class HeaderCloseButton : Control
    {
        private bool _hover, _pressed;
        public HeaderCloseButton()
        {
            Size = new Size(26, 26); DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_hover)
            {
                using var bg = new SolidBrush(Color.FromArgb(_pressed ? 110 : 70, 220, 70, 70));
                using var path = RoundRect(0, 0, Width, Height, 4);
                g.FillPath(bg, path);
            }
            var fg = _hover ? Color.FromArgb(235, 240, 250) : AppSettings.Instance.Theme.TextColor;
            using var pen = new Pen(fg, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            int cx = Width / 2, cy = Height / 2;
            g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
            g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
        }
        private static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
        {
            var p = new GraphicsPath();
            p.AddArc(x, y, r * 2, r * 2, 180, 90);
            p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            p.CloseFigure(); return p;
        }
    }
}
