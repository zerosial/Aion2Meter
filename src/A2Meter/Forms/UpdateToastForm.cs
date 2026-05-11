using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using A2Meter.Core;

namespace A2Meter.Forms;

internal sealed class UpdateToastForm : Form
{
	private sealed class ToastButton : Control
	{
		private readonly Color _fgColor;

		private readonly bool _isAccent;

		private bool _hover;

		private bool _pressed;

		public ToastButton(string text, Color fgColor, bool isAccent)
		{
			_fgColor = fgColor;
			_isAccent = isAccent;
			Text = text;
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Color.Transparent;
			Cursor = Cursors.Hand;
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			_hover = true;
			base.OnMouseEnter(e);
			Invalidate();
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hover = false;
			_pressed = false;
			base.OnMouseLeave(e);
			Invalidate();
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			_pressed = true;
			base.OnMouseDown(e);
			Invalidate();
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			_pressed = false;
			base.OnMouseUp(e);
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
			_ = AppSettings.Instance.Theme;
			if (_isAccent)
			{
				using SolidBrush brush = new SolidBrush(Color.FromArgb(_pressed ? 255 : (_hover ? 220 : 180), _fgColor));
				using GraphicsPath path = RoundRectF(new RectangleF(0f, 0f, base.Width, base.Height), 4f);
				graphics.FillPath(brush, path);
			}
			else if (_hover)
			{
				using SolidBrush brush2 = new SolidBrush(Color.FromArgb(_pressed ? 110 : 70, 60, 100, 160));
				using GraphicsPath path2 = RoundRectF(new RectangleF(0f, 0f, base.Width, base.Height), 4f);
				graphics.FillPath(brush2, path2);
			}
			Color color = ((!_isAccent) ? (_hover ? Color.FromArgb(235, 240, 250) : _fgColor) : Color.FromArgb(20, 24, 36));
			FontStyle fontStyle = ((AppSettings.Instance.FontWeight >= 600) ? FontStyle.Bold : FontStyle.Regular);
			FontStyle style = (_isAccent ? FontStyle.Bold : fontStyle);
			using Font font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f, style);
			StringFormat format = new StringFormat
			{
				Alignment = StringAlignment.Center,
				LineAlignment = StringAlignment.Center
			};
			using SolidBrush brush3 = new SolidBrush(color);
			graphics.DrawString(Text, font, brush3, new RectangleF(0f, 0f, base.Width, base.Height), format);
		}

		private static GraphicsPath RoundRectF(RectangleF rect, float r)
		{
			GraphicsPath graphicsPath = new GraphicsPath();
			float num = r * 2f;
			graphicsPath.AddArc(rect.X, rect.Y, num, num, 180f, 90f);
			graphicsPath.AddArc(rect.Right - num, rect.Y, num, num, 270f, 90f);
			graphicsPath.AddArc(rect.Right - num, rect.Bottom - num, num, num, 0f, 90f);
			graphicsPath.AddArc(rect.X, rect.Bottom - num, num, num, 90f, 90f);
			graphicsPath.CloseFigure();
			return graphicsPath;
		}
	}

	private readonly string _downloadUrl;

	private readonly string _releaseNotes;

	private readonly Version _version;

	private readonly Form _parent;

	private readonly ToastButton _btnUpdate;

	private readonly ToastButton _btnClose;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle |= 136;
			return createParams;
		}
	}

	public UpdateToastForm(Form parent, Version version, string downloadUrl, string releaseNotes)
	{
		_parent = parent;
		_version = version;
		_downloadUrl = downloadUrl;
		_releaseNotes = releaseNotes;
		AppSettings.ThemeColors theme = AppSettings.Instance.Theme;
		base.FormBorderStyle = FormBorderStyle.None;
		base.ShowInTaskbar = false;
		base.TopMost = true;
		base.StartPosition = FormStartPosition.Manual;
		base.Size = new Size(260, 36);
		BackColor = theme.HeaderColor;
		base.Opacity = 0.96;
		DoubleBuffered = true;
		_btnUpdate = new ToastButton("업데이트", theme.AccentColor, isAccent: true)
		{
			Size = new Size(64, 22),
			Location = new Point(168, 7)
		};
		_btnUpdate.Click += OnUpdateClick;
		base.Controls.Add(_btnUpdate);
		_btnClose = new ToastButton("✕", theme.TextDimColor, isAccent: false)
		{
			Size = new Size(22, 22),
			Location = new Point(base.Width - 30, 7)
		};
		_btnClose.Click += delegate
		{
			Close();
		};
		base.Controls.Add(_btnClose);
		base.Paint += OnPaint;
		PlaceAtBottom();
		parent.Move += delegate
		{
			PlaceAtBottom();
		};
		parent.Resize += delegate
		{
			PlaceAtBottom();
		};
	}

	private void OnPaint(object? sender, PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
		AppSettings.ThemeColors theme = AppSettings.Instance.Theme;
		using Pen pen = new Pen(theme.BorderColor);
		using GraphicsPath path = RoundRect(0, 0, base.Width - 1, base.Height - 1, 6);
		graphics.DrawPath(pen, path);
		using SolidBrush brush = new SolidBrush(theme.AccentColor);
		graphics.FillRectangle(brush, 0, 8, 3, base.Height - 16);
		string s = $"v{_version} 업데이트 가능";
		FontStyle style = ((AppSettings.Instance.FontWeight >= 600) ? FontStyle.Bold : FontStyle.Regular);
		using Font font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize, style);
		using SolidBrush brush2 = new SolidBrush(theme.TextColor);
		RectangleF layoutRectangle = new RectangleF(12f, 0f, 150f, base.Height);
		StringFormat format = new StringFormat
		{
			Alignment = StringAlignment.Near,
			LineAlignment = StringAlignment.Center
		};
		graphics.DrawString(s, font, brush2, layoutRectangle, format);
	}

	private void PlaceAtBottom()
	{
		if (!_parent.IsDisposed)
		{
			int x = _parent.Left + (_parent.Width - base.Width) / 2;
			int y = _parent.Bottom - base.Height - 6;
			base.Location = new Point(x, y);
		}
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using SolidBrush brush = new SolidBrush(BackColor);
		using GraphicsPath path = RoundRect(0, 0, base.Width - 1, base.Height - 1, 6);
		graphics.FillPath(brush, path);
	}

	private void OnUpdateClick(object? sender, EventArgs e)
	{
		new UpdateDetailForm(_version, _downloadUrl, _releaseNotes).Show();
		Close();
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
