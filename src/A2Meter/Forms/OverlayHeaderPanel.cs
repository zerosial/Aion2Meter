using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;

namespace A2Meter.Forms;

/// Native WinForms replacement for the WebView2 header strip.
/// Three icon buttons on the right (lock / settings / close), a brand label on
/// the left. Owner-drawn buttons keep the look consistent with the dark D2D
/// canvas below and avoid the OS theme bleeding through.
internal sealed class OverlayHeaderPanel : Panel
{
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION        = 2;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    public event Action<bool>? LockToggled;
    public event Action?       HistoryClicked;
    public event Action?       CloseClicked;
    public event Action<int>?  OpacityChanged;

    private readonly IconButton _btnLock;
    private readonly IconButton _btnHistory;
    private readonly IconButton _btnClose;
    private readonly TrackBar   _sliderOpacity;
    private readonly Label      _brand;

    private bool _locked;

    public OverlayHeaderPanel()
    {
        Dock      = DockStyle.Top;
        Height    = 36;
        BackColor = Color.FromArgb(12, 18, 30);
        DoubleBuffered = true;

        _brand = new Label
        {
            Text      = "A2Meter",
            ForeColor = Color.FromArgb(170, 195, 230),
            Font      = new Font("Malgun Gothic", 9.5f, FontStyle.Bold),
            AutoSize  = true,
            Location  = new Point(10, 9),
            BackColor = Color.Transparent,
        };
        _brand.MouseDown += (_, e) => BeginParentDrag(e);
        this.MouseDown   += (_, e) => OnHeaderMouseDown(e);
        this.MouseMove   += (_, e) => OnHeaderMouseMove(e);

        _btnLock     = new IconButton(IconKind.Unlock)   { TabStop = false };
        _btnHistory  = new IconButton(IconKind.History)  { TabStop = false };
        _btnClose    = new IconButton(IconKind.Close)    { TabStop = false, HoverColor = Color.FromArgb(220, 70, 70) };

        _sliderOpacity = new TrackBar
        {
            Minimum = 20,
            Maximum = 100,
            Value = Math.Clamp(Core.AppSettings.Instance.Opacity, 20, 100),
            TickStyle = TickStyle.None,
            AutoSize = false,
            Height = 22,
            Width = 80,
            BackColor = Color.FromArgb(12, 18, 30),
            Cursor = Cursors.Hand,
        };
        _sliderOpacity.ValueChanged += (_, _) => OpacityChanged?.Invoke(_sliderOpacity.Value);

        _btnLock.Click     += (_, _) => { _locked = !_locked; _btnLock.Kind = _locked ? IconKind.Lock : IconKind.Unlock; _btnLock.Invalidate(); LockToggled?.Invoke(_locked); };
        _btnHistory.Click  += (_, _) => HistoryClicked?.Invoke();
        _btnClose.Click    += (_, _) => CloseClicked?.Invoke();

        Controls.Add(_btnLock);
        Controls.Add(_btnHistory);
        Controls.Add(_sliderOpacity);
        Controls.Add(_btnClose);
        Controls.Add(_brand);

        Resize += (_, _) => LayoutButtons();
        LayoutButtons();
    }

    /// Externally force unlock state (e.g. from tray menu).
    public void ForceUnlock()
    {
        _locked = false;
        _btnLock.Kind = IconKind.Unlock;
        _btnLock.Invalidate();
    }

    /// Returns true if a hit at the given client point should be treated as a
    /// title-bar drag region (so the OverlayForm can move on click-drag).
    public bool IsDragArea(Point clientPt)
    {
        if (_locked) return false;
        var hit = GetChildAtPoint(clientPt);
        return hit is null || hit == _brand;
    }

    private void LayoutButtons()
    {
        const int btnSize = 26;
        const int gap     = 4;
        int y  = (Height - btnSize) / 2;
        int x  = Width - 8 - btnSize;
        _btnClose.SetBounds(x, y, btnSize, btnSize);
        x -= btnSize + gap;
        _btnHistory.SetBounds(x, y, btnSize, btnSize);
        x -= btnSize + gap;
        _btnLock.SetBounds(x, y, btnSize, btnSize);
        // Opacity slider sits left of the icon buttons.
        x -= _sliderOpacity.Width + gap;
        _sliderOpacity.SetBounds(x, (Height - _sliderOpacity.Height) / 2, _sliderOpacity.Width, _sliderOpacity.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // 1px subtle bottom border.
        using var pen = new Pen(Color.FromArgb(28, 36, 56));
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }

    /// Standard WinForms trick to make a child control drag its top-level form:
    /// release the local mouse capture and forward an NCLBUTTONDOWN/CAPTION to
    /// the parent so Windows runs its built-in window-move loop.
    private void BeginParentDrag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var top = FindForm();
        if (top is null) return;
        ReleaseCapture();
        SendMessage(top.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    private const int EdgeMargin = 6;

    /// Header sits at the top of the form, so it owns the top edge + the top
    /// 6px of the left/right edges. Anywhere else on the panel acts as drag.
    private int EdgeHitOnHeader(System.Drawing.Point pt)
    {
        int w = Width, h = Height;
        bool L = pt.X < EdgeMargin;
        bool R = pt.X >= w - EdgeMargin;
        bool T = pt.Y < EdgeMargin;
        if (T && L) return 13; // HTTOPLEFT
        if (T && R) return 14; // HTTOPRIGHT
        if (T)      return 12; // HTTOP
        if (L)      return 10; // HTLEFT
        if (R)      return 11; // HTRIGHT
        return 0;
    }

    private void OnHeaderMouseMove(MouseEventArgs e)
    {
        int code = EdgeHitOnHeader(e.Location);
        Cursor = code switch
        {
            10 or 11 => Cursors.SizeWE,
            12       => Cursors.SizeNS,
            13       => Cursors.SizeNWSE,
            14       => Cursors.SizeNESW,
            _        => Cursors.Default,
        };
    }

    private void OnHeaderMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int code = EdgeHitOnHeader(e.Location);
        if (code != 0)
        {
            var top = FindForm();
            if (top is null) return;
            ReleaseCapture();
            SendMessage(top.Handle, WM_NCLBUTTONDOWN, (IntPtr)code, IntPtr.Zero);
            return;
        }
        BeginParentDrag(e);
    }

    // ─── Owner-drawn icon button ─────────────────────────────────────

    public enum IconKind { Lock, Unlock, History, Close }

    private sealed class IconButton : Control
    {
        public IconKind Kind { get; set; }
        public Color HoverColor { get; set; } = Color.FromArgb(60, 100, 160);

        private bool _hover;
        private bool _pressed;

        public IconButton(IconKind kind)
        {
            Kind = kind;
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true;  base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true;  base.OnMouseDown(e); Invalidate(); }
        protected override void OnMouseUp  (MouseEventArgs e) { _pressed = false; base.OnMouseUp(e);   Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Hover / press background.
            if (_hover)
            {
                int alpha = _pressed ? 110 : 70;
                using var bg = new SolidBrush(Color.FromArgb(alpha, HoverColor));
                using var path = RoundRect(0, 0, Width, Height, 4);
                g.FillPath(bg, path);
            }

            // Foreground glyph.
            var fg = _hover ? Color.FromArgb(235, 240, 250) : Color.FromArgb(170, 195, 230);
            using var pen = new Pen(fg, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            int cx = Width / 2, cy = Height / 2;

            switch (Kind)
            {
                case IconKind.Lock:    DrawLock(g, pen, cx, cy, closed: true);  break;
                case IconKind.Unlock:  DrawLock(g, pen, cx, cy, closed: false); break;
                case IconKind.History:  DrawHistory(g, pen, cx, cy); break;
                case IconKind.Close:    DrawCross(g, pen, cx, cy); break;
            }
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

        private static void DrawLock(Graphics g, Pen pen, int cx, int cy, bool closed)
        {
            var body = new Rectangle(cx - 5, cy - 1, 10, 8);
            g.DrawRectangle(pen, body);
            // Shackle: circle on top, rotated open if unlocked.
            if (closed)
                g.DrawArc(pen, cx - 4, cy - 8, 8, 8, 180, 180);
            else
                g.DrawArc(pen, cx - 6, cy - 8, 8, 8, 180, 180);
        }


        private static void DrawHistory(Graphics g, Pen pen, int cx, int cy)
        {
            // Clock icon: circle + two hands.
            g.DrawEllipse(pen, cx - 6, cy - 6, 12, 12);
            g.DrawLine(pen, cx, cy, cx, cy - 4);
            g.DrawLine(pen, cx, cy, cx + 3, cy + 1);
        }

        private static void DrawCross(Graphics g, Pen pen, int cx, int cy)
        {
            g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
            g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
        }
    }
}
