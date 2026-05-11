using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;

namespace A2Meter.Forms;

internal sealed class OverlayHeaderPanel : Panel
{
	public enum IconKind
	{
		Lock,
		Unlock,
		Eye,
		EyeOff,
		History,
		Settings,
		Close,
		CpOn,
		CpOff,
		ScoreOn,
		ScoreOff
	}

	private sealed class IconButton : Control
	{
		private bool _hover;

		private bool _pressed;

		public IconKind Kind { get; set; }

		public Color HoverColor { get; set; } = Color.FromArgb(60, 100, 160);

		public IconButton(IconKind kind)
		{
			Kind = kind;
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
			if (_hover)
			{
				using SolidBrush brush = new SolidBrush(Color.FromArgb(_pressed ? 110 : 70, HoverColor));
				using GraphicsPath path = RoundRect(0, 0, base.Width, base.Height, 4);
				graphics.FillPath(brush, path);
			}
			using Pen pen = new Pen(_hover ? Color.FromArgb(235, 240, 250) : AppSettings.Instance.Theme.TextColor, 1.6f)
			{
				StartCap = LineCap.Round,
				EndCap = LineCap.Round
			};
			int cx = base.Width / 2;
			int cy = base.Height / 2;
			switch (Kind)
			{
			case IconKind.Lock:
				DrawLock(graphics, pen, cx, cy, closed: true);
				break;
			case IconKind.Unlock:
				DrawLock(graphics, pen, cx, cy, closed: false);
				break;
			case IconKind.Eye:
				DrawEye(graphics, pen, cx, cy, off: false);
				break;
			case IconKind.EyeOff:
				DrawEye(graphics, pen, cx, cy, off: true);
				break;
			case IconKind.History:
				DrawHistory(graphics, pen, cx, cy);
				break;
			case IconKind.Settings:
				DrawGear(graphics, pen, cx, cy);
				break;
			case IconKind.Close:
				DrawCross(graphics, pen, cx, cy);
				break;
			case IconKind.CpOn:
				DrawCpLabel(graphics, cx, cy, on: true);
				break;
			case IconKind.CpOff:
				DrawCpLabel(graphics, cx, cy, on: false);
				break;
			case IconKind.ScoreOn:
				DrawScoreLabel(graphics, cx, cy, on: true);
				break;
			case IconKind.ScoreOff:
				DrawScoreLabel(graphics, cx, cy, on: false);
				break;
			}
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

		private static void DrawLock(Graphics g, Pen pen, int cx, int cy, bool closed)
		{
			Rectangle rect = new Rectangle(cx - 5, cy - 1, 10, 8);
			g.DrawRectangle(pen, rect);
			if (closed)
			{
				g.DrawArc(pen, cx - 4, cy - 8, 8, 8, 180, 180);
			}
			else
			{
				g.DrawArc(pen, cx - 6, cy - 8, 8, 8, 180, 180);
			}
		}

		private static void DrawEye(Graphics g, Pen pen, int cx, int cy, bool off)
		{
			g.DrawArc(pen, cx - 7, cy - 3, 14, 10, 200, 140);
			g.DrawArc(pen, cx - 7, cy - 7, 14, 10, 20, 140);
			g.DrawEllipse(pen, cx - 2, cy - 2, 4, 4);
			if (off)
			{
				g.DrawLine(pen, cx - 6, cy + 5, cx + 6, cy - 5);
			}
		}

		private static void DrawHistory(Graphics g, Pen pen, int cx, int cy)
		{
			g.DrawEllipse(pen, cx - 6, cy - 6, 12, 12);
			g.DrawLine(pen, cx, cy, cx, cy - 4);
			g.DrawLine(pen, cx, cy, cx + 3, cy + 1);
		}

		private static void DrawGear(Graphics g, Pen pen, int cx, int cy)
		{
			g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
			for (int i = 0; i < 6; i++)
			{
				double num = (double)i * Math.PI / 3.0;
				int x = cx + (int)(4.0 * Math.Cos(num));
				int y = cy + (int)(4.0 * Math.Sin(num));
				int x2 = cx + (int)(6.0 * Math.Cos(num));
				int y2 = cy + (int)(6.0 * Math.Sin(num));
				g.DrawLine(pen, x, y, x2, y2);
			}
		}

		private static void DrawCross(Graphics g, Pen pen, int cx, int cy)
		{
			g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
			g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
		}

		private static void DrawCpLabel(Graphics g, int cx, int cy, bool on)
		{
			Color color = (on ? Color.FromArgb(100, 180, 255) : Color.FromArgb(90, 90, 100));
			using Font font = new Font("Malgun Gothic", 6.5f, FontStyle.Bold);
			using SolidBrush brush = new SolidBrush(color);
			StringFormat format = new StringFormat
			{
				Alignment = StringAlignment.Center,
				LineAlignment = StringAlignment.Center
			};
			g.DrawString("전투력", font, brush, cx, cy, format);
		}

		private static void DrawScoreLabel(Graphics g, int cx, int cy, bool on)
		{
			Color color = (on ? Color.FromArgb(232, 200, 77) : Color.FromArgb(90, 90, 100));
			using Font font = new Font("Malgun Gothic", 6.5f, FontStyle.Bold);
			using SolidBrush brush = new SolidBrush(color);
			StringFormat format = new StringFormat
			{
				Alignment = StringAlignment.Center,
				LineAlignment = StringAlignment.Center
			};
			g.DrawString("아툴", font, brush, cx, cy, format);
		}
	}

	private sealed class SlimSlider : Control
	{
		private int _min;

		private int _max;

		private int _value;

		private bool _dragging;

		private bool _hover;

		private const int TrackHeight = 4;

		private const int ThumbRadius = 6;

		public int Value
		{
			get
			{
				return _value;
			}
			set
			{
				_value = Math.Clamp(value, _min, _max);
				Invalidate();
			}
		}

		private int TrackLeft => 6;

		private int TrackRight => base.Width - 6;

		private float Ratio => (float)(_value - _min) / (float)Math.Max(1, _max - _min);

		private int ThumbX => TrackLeft + (int)(Ratio * (float)(TrackRight - TrackLeft));

		public event Action<int>? ValueChanged;

		public SlimSlider(int min, int max, int value)
		{
			_min = min;
			_max = max;
			_value = Math.Clamp(value, min, max);
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Color.Transparent;
			Cursor = Cursors.Hand;
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
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				_dragging = true;
				base.Capture = true;
				UpdateValue(e.X);
			}
			base.OnMouseDown(e);
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			if (_dragging)
			{
				UpdateValue(e.X);
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			_dragging = false;
			base.Capture = false;
			base.OnMouseUp(e);
		}

		private void UpdateValue(int x)
		{
			float num = (float)(x - TrackLeft) / (float)Math.Max(1, TrackRight - TrackLeft);
			int value = _min + (int)Math.Round(num * (float)(_max - _min));
			value = Math.Clamp(value, _min, _max);
			if (value != _value)
			{
				_value = value;
				Invalidate();
				this.ValueChanged?.Invoke(_value);
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			int num = base.Height / 2;
			int trackLeft = TrackLeft;
			int trackRight = TrackRight;
			int thumbX = ThumbX;
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(30, 40, 60)))
			{
				using GraphicsPath path = RoundRectF(new RectangleF(trackLeft, (float)num - 2f, trackRight - trackLeft, 4f), 2f);
				graphics.FillPath(brush, path);
			}
			if (thumbX > trackLeft)
			{
				Color accentColor = AppSettings.Instance.Theme.AccentColor;
				using SolidBrush brush2 = new SolidBrush((_hover || _dragging) ? ControlPaint.Light(accentColor, 0.3f) : accentColor);
				using GraphicsPath path2 = RoundRectF(new RectangleF(trackLeft, (float)num - 2f, thumbX - trackLeft, 4f), 2f);
				graphics.FillPath(brush2, path2);
			}
			using (SolidBrush brush3 = new SolidBrush(_dragging ? Color.FromArgb(220, 235, 255) : (_hover ? Color.FromArgb(200, 220, 250) : AppSettings.Instance.Theme.TextColor)))
			{
				int num2 = (_dragging ? 7 : 6);
				graphics.FillEllipse(brush3, thumbX - num2, num - num2, num2 * 2, num2 * 2);
			}
			using Pen pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1f);
			graphics.DrawEllipse(pen, thumbX - 6, num - 6, 12, 12);
		}

		private static GraphicsPath RoundRectF(RectangleF rect, float radius)
		{
			GraphicsPath graphicsPath = new GraphicsPath();
			float num = radius * 2f;
			if (rect.Width < num)
			{
				graphicsPath.AddEllipse(rect);
				return graphicsPath;
			}
			graphicsPath.AddArc(rect.X, rect.Y, num, num, 180f, 90f);
			graphicsPath.AddArc(rect.Right - num, rect.Y, num, num, 270f, 90f);
			graphicsPath.AddArc(rect.Right - num, rect.Bottom - num, num, num, 0f, 90f);
			graphicsPath.AddArc(rect.X, rect.Bottom - num, num, num, 90f, 90f);
			graphicsPath.CloseFigure();
			return graphicsPath;
		}
	}

	private const int WM_NCLBUTTONDOWN = 161;

	private const int HTCAPTION = 2;

	private readonly IconButton _btnLock;

	private readonly IconButton _btnAnon;

	private readonly IconButton _btnHistory;

	private readonly IconButton _btnSettings;

	private readonly IconButton _btnClose;

	private readonly SlimSlider _sliderOpacity;

	private readonly Label _brand;

	private bool _locked;

	private bool _anonymous;

	private const int EdgeMargin = 10;

	public event Action<bool>? LockToggled;

	public event Action<bool>? AnonymousToggled;

	public event Action? HistoryClicked;

	public event Action? SettingsClicked;

	public event Action? CloseClicked;

	public event Action<int>? OpacityChanged;

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern nint SendMessage(nint hWnd, int Msg, nint wParam, nint lParam);

	public OverlayHeaderPanel()
	{
		Dock = DockStyle.Top;
		base.Height = 36;
		BackColor = AppSettings.Instance.Theme.HeaderColor;
		DoubleBuffered = true;
		_brand = new Label
		{
			Text = "A2Meter v" + AutoUpdater.CurrentVersion.ToString(3),
			ForeColor = AppSettings.Instance.Theme.TextColor,
			Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize + 0.5f, FontStyle.Bold),
			AutoSize = true,
			Location = new Point(10, 9),
			BackColor = Color.Transparent
		};
		_brand.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			BeginParentDrag(e);
		};
		base.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			OnHeaderMouseDown(e);
		};
		base.MouseMove += delegate(object? _, MouseEventArgs e)
		{
			OnHeaderMouseMove(e);
		};
		_btnLock = new IconButton(IconKind.Unlock)
		{
			TabStop = false
		};
		_btnAnon = new IconButton(IconKind.Eye)
		{
			TabStop = false
		};
		_btnHistory = new IconButton(IconKind.History)
		{
			TabStop = false
		};
		_btnSettings = new IconButton(IconKind.Settings)
		{
			TabStop = false
		};
		_btnClose = new IconButton(IconKind.Close)
		{
			TabStop = false,
			HoverColor = Color.FromArgb(220, 70, 70)
		};
		_sliderOpacity = new SlimSlider(20, 100, Math.Clamp(AppSettings.Instance.Opacity, 20, 100))
		{
			Width = 72,
			Height = 20,
			TabStop = false
		};
		_sliderOpacity.ValueChanged += delegate(int v)
		{
			this.OpacityChanged?.Invoke(v);
		};
		_btnLock.Click += delegate
		{
			_locked = !_locked;
			_btnLock.Kind = ((!_locked) ? IconKind.Unlock : IconKind.Lock);
			_btnLock.Invalidate();
			this.LockToggled?.Invoke(_locked);
		};
		_btnAnon.Click += delegate
		{
			_anonymous = !_anonymous;
			_btnAnon.Kind = (_anonymous ? IconKind.EyeOff : IconKind.Eye);
			_btnAnon.Invalidate();
			this.AnonymousToggled?.Invoke(_anonymous);
		};
		_btnHistory.Click += delegate
		{
			this.HistoryClicked?.Invoke();
		};
		_btnSettings.Click += delegate
		{
			this.SettingsClicked?.Invoke();
		};
		_btnClose.Click += delegate
		{
			this.CloseClicked?.Invoke();
		};
		base.Controls.Add(_btnLock);
		base.Controls.Add(_btnAnon);
		base.Controls.Add(_btnHistory);
		base.Controls.Add(_btnSettings);
		base.Controls.Add(_sliderOpacity);
		base.Controls.Add(_btnClose);
		base.Controls.Add(_brand);
		base.Resize += delegate
		{
			LayoutButtons();
		};
		LayoutButtons();
	}

	public void ForceUnlock()
	{
		_locked = false;
		_btnLock.Kind = IconKind.Unlock;
		_btnLock.Invalidate();
	}

	public void SetAnonymous(bool anon)
	{
		_anonymous = anon;
		_btnAnon.Kind = (anon ? IconKind.EyeOff : IconKind.Eye);
		_btnAnon.Invalidate();
	}

	public bool IsLockButtonArea(Point clientPt)
	{
		return _btnLock.Bounds.Contains(clientPt);
	}

	public bool IsDragArea(Point clientPt)
	{
		if (_locked)
		{
			return false;
		}
		Control childAtPoint = GetChildAtPoint(clientPt);
		if (childAtPoint != null)
		{
			return childAtPoint == _brand;
		}
		return true;
	}

	private void LayoutButtons()
	{
		int y = (base.Height - 26) / 2;
		int num = base.Width - 8 - 26;
		_btnClose.SetBounds(num, y, 26, 26);
		num -= 30;
		_btnSettings.SetBounds(num, y, 26, 26);
		num -= 30;
		_btnHistory.SetBounds(num, y, 26, 26);
		num -= 30;
		_btnAnon.SetBounds(num, y, 26, 26);
		num -= 30;
		_btnLock.SetBounds(num, y, 26, 26);
		num -= _sliderOpacity.Width + 4;
		_sliderOpacity.SetBounds(num, (base.Height - _sliderOpacity.Height) / 2, _sliderOpacity.Width, _sliderOpacity.Height);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		using Pen pen = new Pen(AppSettings.Instance.Theme.BorderColor);
		e.Graphics.DrawLine(pen, 0, base.Height - 1, base.Width, base.Height - 1);
	}

	private void BeginParentDrag(MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			Form form = FindForm();
			if (form != null)
			{
				ReleaseCapture();
				SendMessage(form.Handle, 161, 2, IntPtr.Zero);
			}
		}
	}

	private int EdgeHitOnHeader(Point pt)
	{
		int width = base.Width;
		_ = base.Height;
		bool flag = pt.X < 10;
		bool flag2 = pt.X >= width - 10;
		bool flag3 = pt.Y < 10;
		if (flag3 && flag)
		{
			return 13;
		}
		if (flag3 && flag2)
		{
			return 14;
		}
		if (flag3)
		{
			return 12;
		}
		if (flag)
		{
			return 10;
		}
		if (flag2)
		{
			return 11;
		}
		return 0;
	}

	private void OnHeaderMouseMove(MouseEventArgs e)
	{
		Cursor cursor;
		switch (EdgeHitOnHeader(e.Location))
		{
		case 10:
		case 11:
			cursor = Cursors.SizeWE;
			break;
		case 12:
			cursor = Cursors.SizeNS;
			break;
		case 13:
			cursor = Cursors.SizeNWSE;
			break;
		case 14:
			cursor = Cursors.SizeNESW;
			break;
		default:
			cursor = Cursors.Default;
			break;
		}
		Cursor = cursor;
	}

	private void OnHeaderMouseDown(MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Left)
		{
			return;
		}
		int num = EdgeHitOnHeader(e.Location);
		if (num != 0)
		{
			Form form = FindForm();
			if (form != null)
			{
				ReleaseCapture();
				SendMessage(form.Handle, 161, num, IntPtr.Zero);
			}
		}
		else
		{
			BeginParentDrag(e);
		}
	}
}
