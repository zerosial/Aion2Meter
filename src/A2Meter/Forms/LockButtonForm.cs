using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace A2Meter.Forms;

internal sealed class LockButtonForm : Form
{
	private const int BtnSize = 28;

	private readonly OverlayForm _owner;

	private bool _hover;

	private bool _pressed;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle |= 134742152;
			return createParams;
		}
	}

	protected override bool ShowWithoutActivation => true;

	public LockButtonForm(OverlayForm owner)
	{
		_owner = owner;
		base.FormBorderStyle = FormBorderStyle.None;
		base.ShowInTaskbar = false;
		base.TopMost = true;
		base.StartPosition = FormStartPosition.Manual;
		base.Size = new Size(28, 28);
		BackColor = Color.FromArgb(12, 18, 30);
		DoubleBuffered = true;
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		Cursor = Cursors.Hand;
		base.Opacity = 0.85;
	}

	public void PlaceNear(Form overlay)
	{
		base.Location = new Point(overlay.Right - 28 - 8, overlay.Top + 4);
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		_hover = true;
		Invalidate();
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hover = false;
		_pressed = false;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		_pressed = true;
		Invalidate();
		base.OnMouseDown(e);
	}

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
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using (SolidBrush brush = new SolidBrush(Color.FromArgb(_pressed ? 140 : (_hover ? 100 : 60), 60, 100, 160)))
		{
			using GraphicsPath path = RoundRect(0, 0, base.Width, base.Height, 5);
			graphics.FillPath(brush, path);
		}
		using (Pen pen = new Pen(Color.FromArgb(80, 100, 160, 240), 1f))
		{
			using GraphicsPath path2 = RoundRect(0, 0, base.Width, base.Height, 5);
			graphics.DrawPath(pen, path2);
		}
		using Pen pen2 = new Pen(_hover ? Color.FromArgb(235, 240, 250) : Color.FromArgb(170, 195, 230), 1.6f)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round
		};
		int num = base.Width / 2;
		int num2 = base.Height / 2;
		Rectangle rect = new Rectangle(num - 5, num2 - 1, 10, 8);
		graphics.DrawRectangle(pen2, rect);
		graphics.DrawArc(pen2, num - 4, num2 - 8, 8, 8, 180, 180);
	}

	private static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddArc(x, y, r * 2, r * 2, 180f, 90f);
		graphicsPath.AddArc(x + w - r * 2, y, r * 2, r * 2, 270f, 90f);
		graphicsPath.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0f, 90f);
		graphicsPath.AddArc(x, y + h - r * 2, r * 2, r * 2, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
