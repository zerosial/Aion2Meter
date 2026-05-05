using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using A2Meter.Core;

namespace A2Meter.Forms;

/// Tiny always-on-top borderless form that shows a lock icon.
/// Appears only while the main overlay is in click-through (locked) mode,
/// giving the user a way to unlock without the tray menu.
internal sealed class LockButtonForm : Form
{
    private const int BtnSize = 28;
    private readonly OverlayForm _owner;
    private bool _hover;
    private bool _pressed;

    public LockButtonForm(OverlayForm owner)
    {
        _owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(BtnSize, BtnSize);
        BackColor = Color.FromArgb(12, 18, 30);
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        Opacity = 0.85;
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

    public void PlaceNear(Form overlay)
    {
        // Position at top-right corner of the overlay, offset slightly inside.
        Location = new Point(overlay.Right - BtnSize - 8, overlay.Top + 4);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _owner.Unlock();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Background rounded rect.
        int alpha = _pressed ? 140 : _hover ? 100 : 60;
        using (var bg = new SolidBrush(Color.FromArgb(alpha, 60, 100, 160)))
        using (var path = RoundRect(0, 0, Width, Height, 5))
            g.FillPath(bg, path);

        // Border.
        using (var pen = new Pen(Color.FromArgb(80, 100, 160, 240), 1f))
        using (var path = RoundRect(0, 0, Width, Height, 5))
            g.DrawPath(pen, path);

        // Lock icon (closed).
        var fg = _hover ? Color.FromArgb(235, 240, 250) : Color.FromArgb(170, 195, 230);
        using var iconPen = new Pen(fg, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int cx = Width / 2, cy = Height / 2;
        var body = new Rectangle(cx - 5, cy - 1, 10, 8);
        g.DrawRectangle(iconPen, body);
        g.DrawArc(iconPen, cx - 4, cy - 8, 8, 8, 180, 180);
    }

    private static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
    {
        var p = new GraphicsPath();
        p.AddArc(x, y, r * 2, r * 2, 180, 90);
        p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        p.CloseFigure();
        return p;
    }
}
