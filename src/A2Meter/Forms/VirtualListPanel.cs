using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using A2Meter.Core;

namespace A2Meter.Forms;

/// Pure owner-drawn virtual list. No ListView, no DataGridView.
/// Caller sets RowCount, handles PaintRow event, done.
internal sealed class VirtualListPanel : Control
{
    private readonly AppSettings.ThemeColors _t = AppSettings.Instance.Theme;

    private int _rowCount;
    private int _scrollOffset;  // first visible row index
    private int _selectedIndex = -1;
    private int _rowHeight = 24;
    private int _headerHeight = 26;

    public int RowCount
    {
        get => _rowCount;
        set { _rowCount = value; _scrollOffset = 0; _selectedIndex = -1; Invalidate(); }
    }

    public int RowHeight { get => _rowHeight; set { _rowHeight = value; Invalidate(); } }
    public int HeaderHeight { get => _headerHeight; set { _headerHeight = value; Invalidate(); } }
    public int SelectedIndex => _selectedIndex;

    /// Columns: (header text, width weight, alignment).
    public (string Text, float Weight, HorizontalAlignment Align)[] Columns { get; set; } = Array.Empty<(string, float, HorizontalAlignment)>();

    /// Paint a single row. Args: (Graphics, rowBounds, columnBounds[], rowIndex, isSelected).
    public event Action<Graphics, Rectangle, Rectangle[], int, bool>? PaintRow;

    /// Row double-clicked.
    public event Action<int>? RowDoubleClicked;

    public VirtualListPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = _t.BgColor;
    }

    private int VisibleRows => Math.Max(1, (Height - _headerHeight) / _rowHeight);
    private int MaxScroll => Math.Max(0, _rowCount - VisibleRows);
    private const int ScrollBarW = 6;
    private const int ScrollBarPad = 3;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int delta = e.Delta > 0 ? -3 : 3;
        _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, MaxScroll);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button != MouseButtons.Left) return;

        // Scrollbar thumb drag start?
        if (e.X >= Width - ScrollBarW - ScrollBarPad * 2 && MaxScroll > 0)
        {
            // Page up/down by click position
            var (ty, th) = ThumbRect();
            if (e.Y < ty) _scrollOffset = Math.Max(0, _scrollOffset - VisibleRows);
            else if (e.Y > ty + th) _scrollOffset = Math.Min(MaxScroll, _scrollOffset + VisibleRows);
            Invalidate();
            return;
        }

        int row = HitTestRow(e.Y);
        if (row >= 0 && row < _rowCount)
        {
            _selectedIndex = row;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        int row = HitTestRow(e.Y);
        if (row >= 0 && row < _rowCount)
            RowDoubleClicked?.Invoke(row);
        base.OnMouseDoubleClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up && _selectedIndex > 0) { _selectedIndex--; EnsureVisible(_selectedIndex); Invalidate(); }
        else if (e.KeyCode == Keys.Down && _selectedIndex < _rowCount - 1) { _selectedIndex++; EnsureVisible(_selectedIndex); Invalidate(); }
        else if (e.KeyCode == Keys.Enter && _selectedIndex >= 0) RowDoubleClicked?.Invoke(_selectedIndex);
        base.OnKeyDown(e);
    }

    private int HitTestRow(int y)
    {
        if (y < _headerHeight) return -1;
        return _scrollOffset + (y - _headerHeight) / _rowHeight;
    }

    private void EnsureVisible(int index)
    {
        if (index < _scrollOffset) _scrollOffset = index;
        else if (index >= _scrollOffset + VisibleRows) _scrollOffset = index - VisibleRows + 1;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, MaxScroll);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int w = Width;

        // ── Header ──
        using (var hBrush = new SolidBrush(_t.HeaderColor))
            g.FillRectangle(hBrush, 0, 0, w, _headerHeight);
        using (var bPen = new Pen(_t.BorderColor))
            g.DrawLine(bPen, 0, _headerHeight - 1, w, _headerHeight - 1);

        var colRects = ComputeColumnRects(0, 0, w - ScrollBarW - ScrollBarPad * 2, _headerHeight);
        for (int i = 0; i < Columns.Length && i < colRects.Length; i++)
        {
            var cr = colRects[i];
            var flags = AlignFlag(Columns[i].Align) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            var tr = new Rectangle(cr.X + 4, cr.Y, cr.Width - 8, cr.Height);
            TextRenderer.DrawText(g, Columns[i].Text, Font, tr, _t.TextColor, flags);
        }

        // ── Rows ──
        int y = _headerHeight;
        int visible = VisibleRows;
        for (int vi = 0; vi < visible; vi++)
        {
            int idx = _scrollOffset + vi;
            if (idx >= _rowCount) break;

            bool sel = idx == _selectedIndex;
            var rowRect = new Rectangle(0, y, w - ScrollBarW - ScrollBarPad * 2, _rowHeight);

            // Row bg
            Color bg;
            if (sel)
                bg = Color.FromArgb(
                    Math.Min(255, _t.AccentColor.R / 4 + _t.BgColor.R),
                    Math.Min(255, _t.AccentColor.G / 4 + _t.BgColor.G),
                    Math.Min(255, _t.AccentColor.B / 4 + _t.BgColor.B));
            else if (idx % 2 == 1)
                bg = Color.FromArgb(
                    Math.Min(255, _t.HeaderColor.R + 3),
                    Math.Min(255, _t.HeaderColor.G + 3),
                    Math.Min(255, _t.HeaderColor.B + 3));
            else
                bg = _t.BgColor;

            using (var brush = new SolidBrush(bg))
                g.FillRectangle(brush, rowRect);

            using (var pen = new Pen(Color.FromArgb(40, _t.BorderColor)))
                g.DrawLine(pen, 0, y + _rowHeight - 1, rowRect.Right, y + _rowHeight - 1);

            var cellRects = ComputeColumnRects(0, y, rowRect.Width, _rowHeight);
            PaintRow?.Invoke(g, rowRect, cellRects, idx, sel);

            y += _rowHeight;
        }

        // ── Scrollbar ──
        if (MaxScroll > 0)
        {
            var (ty, th) = ThumbRect();
            int sx = w - ScrollBarW - ScrollBarPad;
            using var brush = new SolidBrush(Color.FromArgb(140, _t.TextDimColor));
            float r = ScrollBarW / 2f;
            var rect = new RectangleF(sx, ty, ScrollBarW, th);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = new GraphicsPath();
            float d = r * 2;
            if (rect.Height < d) { path.AddEllipse(rect); }
            else
            {
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
            }
            g.FillPath(brush, path);
        }
    }

    private (int y, int h) ThumbRect()
    {
        int trackH = Height - _headerHeight;
        float visRatio = (float)VisibleRows / Math.Max(1, _rowCount);
        int th = Math.Max(20, (int)(trackH * visRatio));
        float scrollRatio = (float)_scrollOffset / Math.Max(1, MaxScroll);
        int ty = _headerHeight + (int)(scrollRatio * (trackH - th));
        return (ty, th);
    }

    private Rectangle[] ComputeColumnRects(int x0, int y, int totalW, int h)
    {
        if (Columns.Length == 0) return Array.Empty<Rectangle>();
        float totalWeight = 0;
        foreach (var c in Columns) totalWeight += c.Weight;
        if (totalWeight <= 0) totalWeight = 1;

        var rects = new Rectangle[Columns.Length];
        float xf = x0;
        for (int i = 0; i < Columns.Length; i++)
        {
            float cw = Columns[i].Weight / totalWeight * totalW;
            rects[i] = new Rectangle((int)xf, y, (int)cw, h);
            xf += cw;
        }
        return rects;
    }

    private static TextFormatFlags AlignFlag(HorizontalAlignment a) => a switch
    {
        HorizontalAlignment.Right => TextFormatFlags.Right,
        HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
        _ => TextFormatFlags.Left,
    };
}
