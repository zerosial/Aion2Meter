using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Direct2D;
using D2DColor = Vortice.Mathematics.Color4;

namespace A2Meter.Forms;

/// Per-player combat detail popup. Shows skill breakdown table with damage bars.
/// Opened when the user clicks a DPS bar in DpsCanvas.
internal sealed class DpsDetailForm : Form
{
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private readonly Label _lblName;
    private readonly Label _lblJob;
    private readonly FlowLayoutPanel _infoPanel;
    private readonly FlowLayoutPanel _statsPanel;
    private readonly DataGridView _grid;
    private Color _accentColor = Color.FromArgb(100, 160, 220);

    /// Per-row damage fraction (0..1) for the skill bar overlay.
    private readonly System.Collections.Generic.List<double> _rowPercents = new();
    /// Per-row spec tiers for the 특화 column.
    private readonly System.Collections.Generic.List<int[]?> _rowSpecs = new();

    private const int ColSkill = 0;
    private const int ColSpec  = 1;

    public DpsDetailForm()
    {
        Text = "전투 상세";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 300);
        Size = new Size(1100, 540);
        BackColor = Color.FromArgb(16, 20, 42);
        ForeColor = Color.FromArgb(220, 230, 245);
        Font = new Font("Malgun Gothic", 9f);

        // ── title bar ───────────────────────────────────────────────
        var title = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.FromArgb(12, 18, 30) };
        _lblName = new Label
        {
            Text = "",
            Font = new Font("Malgun Gothic", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 230, 245),
            AutoSize = true,
            Location = new Point(12, 8),
            BackColor = Color.Transparent,
        };
        _lblJob = new Label
        {
            Text = "",
            Font = new Font("Malgun Gothic", 9f),
            ForeColor = Color.FromArgb(140, 160, 190),
            AutoSize = true,
            Location = new Point(200, 11),
            BackColor = Color.Transparent,
        };
        var btnClose = new Button
        {
            Text = "X",
            Width = 36, Height = 28,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(190, 200, 220),
            BackColor = Color.FromArgb(12, 18, 30),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            TabStop = false,
            Cursor = Cursors.Hand,
        };
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(180, 60, 60);
        btnClose.Click += (_, _) => Close();
        btnClose.Dock = DockStyle.Right;

        title.Controls.Add(_lblName);
        title.Controls.Add(_lblJob);
        title.Controls.Add(btnClose);
        title.MouseDown += (_, e) => Drag(e);
        _lblName.MouseDown += (_, e) => Drag(e);
        _lblJob.MouseDown += (_, e) => Drag(e);

        // ── info badges (전투력, 스킬 수) ────────────────────────────
        _infoPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = Color.FromArgb(16, 20, 42),
            Padding = new Padding(8, 6, 8, 4),
            WrapContents = false,
        };

        // ── stats badges (누적피해, 치명타, ...) ─────────────────────
        _statsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = Color.FromArgb(16, 20, 42),
            Padding = new Padding(8, 2, 8, 6),
            WrapContents = false,
        };

        // ── skill table ─────────────────────────────────────────────
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.FromArgb(16, 20, 42),
            GridColor = Color.FromArgb(40, 48, 72),
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 30,
            RowTemplate = { Height = 28 },
            Font = new Font("Malgun Gothic", 9f),
        };

        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(24, 30, 56),
            ForeColor = Color.FromArgb(140, 160, 190),
            Font = new Font("Malgun Gothic", 8.5f),
            Alignment = DataGridViewContentAlignment.MiddleCenter,
            SelectionBackColor = Color.FromArgb(24, 30, 56),
            SelectionForeColor = Color.FromArgb(140, 160, 190),
        };

        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(16, 20, 42),
            ForeColor = Color.FromArgb(220, 230, 245),
            SelectionBackColor = Color.FromArgb(30, 40, 70),
            SelectionForeColor = Color.FromArgb(220, 230, 245),
            Alignment = DataGridViewContentAlignment.MiddleRight,
        };

        _grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(20, 26, 50),
            ForeColor = Color.FromArgb(220, 230, 245),
            SelectionBackColor = Color.FromArgb(30, 40, 70),
            SelectionForeColor = Color.FromArgb(220, 230, 245),
        };

        var leftStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleLeft };
        var centerStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "스킬",   HeaderText = "스킬",   FillWeight = 120, DefaultCellStyle = leftStyle });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "특화",   HeaderText = "특화",   FillWeight = 50, DefaultCellStyle = centerStyle });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "타수",   HeaderText = "타수",   FillWeight = 35 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "치명타", HeaderText = "치명타", FillWeight = 48 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "후방",   HeaderText = "후방",   FillWeight = 48 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "강타",   HeaderText = "강타",   FillWeight = 48 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "완벽",   HeaderText = "완벽",   FillWeight = 48 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "다단",   HeaderText = "다단",   FillWeight = 48 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "회피",   HeaderText = "회피",   FillWeight = 48 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "막기",   HeaderText = "막기",   FillWeight = 48 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "초당",   HeaderText = "초당",   FillWeight = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "평균",   HeaderText = "평균",   FillWeight = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "피해",   HeaderText = "피해",   FillWeight = 100 });

        _grid.CellPainting += OnCellPainting;

        // Dock order: last added fills remaining space.
        Controls.Add(_grid);
        Controls.Add(_statsPanel);
        Controls.Add(_infoPanel);
        Controls.Add(title);
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

        // ── info badges ─────────────────────────────────────────────
        _infoPanel.Controls.Clear();
        if (row.CombatPower > 0)
            _infoPanel.Controls.Add(Badge($"전투력 {row.CombatPower:N0}", Color.FromArgb(40, 55, 100)));
        if (row.Skills is { Count: > 0 })
            _infoPanel.Controls.Add(Badge($"스킬 {row.Skills.Count}개", Color.FromArgb(40, 55, 100)));

        // ── stats badges ────────────────────────────────────────────
        _statsPanel.Controls.Clear();
        _statsPanel.Controls.Add(Badge($"누적피해 {row.Damage:N0}", Color.FromArgb(35, 65, 85)));
        _statsPanel.Controls.Add(Badge($"치명타 {row.CritRate * 100:0.#}%", Color.FromArgb(35, 65, 85)));
        if (row.DpsValue > 0)
            _statsPanel.Controls.Add(Badge($"DPS {row.DpsValue:N0}", Color.FromArgb(35, 65, 85)));
        if (row.HealTotal > 0)
            _statsPanel.Controls.Add(Badge($"힐 {row.HealTotal:N0}", Color.FromArgb(35, 65, 85)));
        if (row.DotDamage > 0 && row.Damage > 0)
        {
            double dotPct = (double)row.DotDamage / row.Damage * 100;
            _statsPanel.Controls.Add(Badge($"DoT {dotPct:0.#}%", Color.FromArgb(35, 65, 85)));
        }

        // ── skill table ─────────────────────────────────────────────
        _grid.Rows.Clear();
        _rowPercents.Clear();
        _rowSpecs.Clear();
        if (row.Skills is { Count: > 0 })
        {
            foreach (var s in row.Skills)
            {
                long avg = s.Hits > 0 ? s.Total / s.Hits : 0;
                long dps = elapsed > 0 ? (long)(s.Total / elapsed) : 0;

                _grid.Rows.Add(
                    s.Name,
                    "",   // 특화 — painted via CellPainting
                    s.Hits,
                    Pct(s.CritRate),
                    Pct(s.BackRate),
                    Pct(s.StrongRate),
                    Pct(s.PerfectRate),
                    Pct(s.MultiHitRate),
                    Pct(s.DodgeRate),
                    Pct(s.BlockRate),
                    dps > 0 ? $"{dps:N0}" : "-",
                    avg > 0 ? $"{avg:N0}" : "-",
                    $"{s.Total:N0} ({s.PercentOfActor * 100:0.#}%)");
                _rowPercents.Add(s.PercentOfActor);
                _rowSpecs.Add(s.Specs);
            }
        }
        _grid.ClearSelection();
    }

    /// Custom-paint the "스킬" column (damage bar) and "특화" column (spec boxes).
    private void OnCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0) return;

        if (e.ColumnIndex == ColSkill)
        {
            e.PaintBackground(e.ClipBounds, false);

            double pct = e.RowIndex < _rowPercents.Count ? _rowPercents[e.RowIndex] : 0;
            double maxPct = _rowPercents.Count > 0 ? _rowPercents[0] : 1;
            if (maxPct <= 0) maxPct = 1;
            double relPct = pct / maxPct;
            if (relPct > 0)
            {
                int barW = (int)(e.CellBounds.Width * relPct);
                if (barW > 0)
                {
                    var barRect = new Rectangle(e.CellBounds.X, e.CellBounds.Y + 2, barW, e.CellBounds.Height - 4);
                    using var brush = new SolidBrush(Color.FromArgb(80, _accentColor));
                    e.Graphics!.FillRectangle(brush, barRect);
                }
            }

            var textRect = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y, e.CellBounds.Width - 4, e.CellBounds.Height);
            TextRenderer.DrawText(e.Graphics!, e.FormattedValue?.ToString() ?? "",
                e.CellStyle!.Font, textRect, e.CellStyle.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.Handled = true;
        }
        else if (e.ColumnIndex == ColSpec)
        {
            e.PaintBackground(e.ClipBounds, false);
            var specs = e.RowIndex < _rowSpecs.Count ? _rowSpecs[e.RowIndex] : null;
            PaintSpecBoxes(e.Graphics!, e.CellBounds, specs);
            e.Handled = true;
        }
    }

    /// Paint 5 small squares for spec tiers. Active tiers are green, inactive are dim.
    private static void PaintSpecBoxes(Graphics g, Rectangle cell, int[]? specs)
    {
        const int boxSize = 7;
        const int gap = 2;
        const int maxTiers = 5;
        int totalW = maxTiers * boxSize + (maxTiers - 1) * gap;
        int x0 = cell.X + (cell.Width - totalW) / 2;
        int y0 = cell.Y + (cell.Height - boxSize) / 2;

        using var inactiveBrush = new SolidBrush(Color.FromArgb(60, 70, 90));
        using var activeBrush   = new SolidBrush(Color.FromArgb(46, 204, 113));
        using var borderPen     = new Pen(Color.FromArgb(80, 100, 130), 1f);
        using var activePen     = new Pen(Color.FromArgb(46, 204, 113), 1f);

        for (int tier = 1; tier <= maxTiers; tier++)
        {
            int x = x0 + (tier - 1) * (boxSize + gap);
            var rect = new Rectangle(x, y0, boxSize, boxSize);
            bool active = specs != null && Array.IndexOf(specs, tier) >= 0;
            g.FillRectangle(active ? activeBrush : inactiveBrush, rect);
            g.DrawRectangle(active ? activePen : borderPen, rect);
        }
    }

    private static Color ToGdi(D2DColor c) =>
        Color.FromArgb(
            (int)(Math.Clamp(c.A, 0f, 1f) * 255),
            (int)(Math.Clamp(c.R, 0f, 1f) * 255),
            (int)(Math.Clamp(c.G, 0f, 1f) * 255),
            (int)(Math.Clamp(c.B, 0f, 1f) * 255));

    private static string Pct(double rate) => $"{rate * 100:0.#}%";

    private static Label Badge(string text, Color bg) => new()
    {
        Text = text,
        Font = new Font("Malgun Gothic", 8.5f),
        ForeColor = Color.FromArgb(200, 215, 240),
        BackColor = bg,
        AutoSize = true,
        Padding = new Padding(6, 3, 6, 3),
        Margin = new Padding(3, 0, 3, 0),
    };

    private void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }
}
