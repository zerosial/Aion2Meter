using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using A2Meter.Core;
using Vortice.DirectWrite;

namespace A2Meter.Forms;

internal sealed class SettingsPanelForm : Form
{
	private sealed class DarkDropdown : Control
	{
		private readonly List<string> _items;

		private int _selectedIndex;

		private bool _hover;

		private bool _open;

		private DropdownPopup? _popup;

		private static Color BgNormal => AppSettings.Instance.Theme.HeaderColor;

		private static Color BgHover => Color.FromArgb(Math.Min(255, AppSettings.Instance.Theme.HeaderColor.R + 14), Math.Min(255, AppSettings.Instance.Theme.HeaderColor.G + 14), Math.Min(255, AppSettings.Instance.Theme.HeaderColor.B + 14));

		private static Color Border => AppSettings.Instance.Theme.BorderColor;

		private static Color FgNormal => AppSettings.Instance.Theme.TextColor;

		private static Color Arrow => AppSettings.Instance.Theme.TextDimColor;

		public int SelectedIndex => _selectedIndex;

		public string? SelectedItem
		{
			get
			{
				if (_selectedIndex < 0 || _selectedIndex >= _items.Count)
				{
					return null;
				}
				return _items[_selectedIndex];
			}
		}

		public event Action<int>? SelectionChanged;

		public DarkDropdown(List<string> items, int selectedIndex)
		{
			_items = items;
			_selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, items.Count - 1));
			base.Height = 28;
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

		protected override void OnClick(EventArgs e)
		{
			base.OnClick(e);
			if (_open)
			{
				ClosePopup();
			}
			else
			{
				ShowPopup();
			}
		}

		private void ShowPopup()
		{
			_open = true;
			_popup = new DropdownPopup(_items, _selectedIndex, base.Width);
			_popup.ItemSelected += delegate(int idx)
			{
				_selectedIndex = idx;
				Invalidate();
				this.SelectionChanged?.Invoke(idx);
			};
			_popup.Closed += delegate
			{
				_open = false;
				Invalidate();
			};
			Point location = PointToScreen(new Point(0, base.Height));
			_popup.Location = location;
			_popup.Show();
			Invalidate();
		}

		private void ClosePopup()
		{
			_popup?.Close();
			_popup = null;
			_open = false;
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using (SolidBrush brush = new SolidBrush(_open ? BgHover : (_hover ? BgHover : BgNormal)))
			{
				using GraphicsPath path = RoundRect(0, 0, base.Width, base.Height, 6);
				graphics.FillPath(brush, path);
			}
			using (Pen pen = new Pen(_open ? AppSettings.Instance.Theme.AccentColor : Border))
			{
				using GraphicsPath path2 = RoundRect(0, 0, base.Width, base.Height, 6);
				graphics.DrawPath(pen, path2);
			}
			string text = SelectedItem ?? "";
			using Font font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize);
			Rectangle bounds = new Rectangle(10, 0, base.Width - 30, base.Height);
			TextRenderer.DrawText(graphics, text, font, bounds, FgNormal, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
			int num = base.Width - 18;
			int num2 = base.Height / 2;
			using Pen pen2 = new Pen(Arrow, 1.5f)
			{
				StartCap = LineCap.Round,
				EndCap = LineCap.Round
			};
			graphics.DrawLine(pen2, num - 3, num2 - 2, num, num2 + 1);
			graphics.DrawLine(pen2, num, num2 + 1, num + 3, num2 - 2);
		}

		private static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
		{
			GraphicsPath graphicsPath = new GraphicsPath();
			graphicsPath.AddArc(x, y, r * 2, r * 2, 180f, 90f);
			graphicsPath.AddArc(x + w - r * 2 - 1, y, r * 2, r * 2, 270f, 90f);
			graphicsPath.AddArc(x + w - r * 2 - 1, y + h - r * 2 - 1, r * 2, r * 2, 0f, 90f);
			graphicsPath.AddArc(x, y + h - r * 2 - 1, r * 2, r * 2, 90f, 90f);
			graphicsPath.CloseFigure();
			return graphicsPath;
		}
	}

	private sealed class DropdownPopup : Form
	{
		private readonly List<string> _items;

		private int _hoverIndex = -1;

		private int _selectedIndex;

		private const int ItemHeight = 26;

		private const int MaxVisible = 10;

		private int _scrollOffset;

		protected override CreateParams CreateParams
		{
			get
			{
				CreateParams createParams = base.CreateParams;
				createParams.ExStyle |= 136;
				createParams.ClassStyle |= 8;
				return createParams;
			}
		}

		protected override bool ShowWithoutActivation => true;

		public event Action<int>? ItemSelected;

		public DropdownPopup(List<string> items, int selectedIndex, int width)
		{
			_items = items;
			_selectedIndex = selectedIndex;
			base.FormBorderStyle = FormBorderStyle.None;
			base.ShowInTaskbar = false;
			base.TopMost = true;
			base.StartPosition = FormStartPosition.Manual;
			BackColor = AppSettings.Instance.Theme.BgColor;
			DoubleBuffered = true;
			int num = Math.Min(items.Count, 10);
			base.Size = new Size(Math.Max(width, 120), num * 26 + 4);
			if (selectedIndex > 7)
			{
				_scrollOffset = Math.Min(selectedIndex - 3, Math.Max(0, items.Count - 10));
			}
		}

		protected override void OnDeactivate(EventArgs e)
		{
			base.OnDeactivate(e);
			Close();
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = (e.Y - 2) / 26 + _scrollOffset;
			if (num != _hoverIndex)
			{
				_hoverIndex = num;
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseClick(MouseEventArgs e)
		{
			int num = (e.Y - 2) / 26 + _scrollOffset;
			if (num >= 0 && num < _items.Count)
			{
				_selectedIndex = num;
				this.ItemSelected?.Invoke(num);
			}
			Close();
			base.OnMouseClick(e);
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			int max = Math.Max(0, _items.Count - 10);
			_scrollOffset = Math.Clamp(_scrollOffset - ((e.Delta > 0) ? 2 : (-2)), 0, max);
			Invalidate();
			base.OnMouseWheel(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hoverIndex = -1;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			AppSettings.ThemeColors theme = AppSettings.Instance.Theme;
			using (Pen pen = new Pen(theme.BorderColor))
			{
				using GraphicsPath path = RoundRect(0, 0, base.Width, base.Height, 6);
				using SolidBrush brush = new SolidBrush(theme.BgColor);
				graphics.FillPath(brush, path);
				graphics.DrawPath(pen, path);
			}
			using Font font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize);
			int num = Math.Min(_items.Count - _scrollOffset, 10);
			for (int i = 0; i < num; i++)
			{
				int num2 = i + _scrollOffset;
				int y = 2 + i * 26;
				Rectangle rectangle = new Rectangle(3, y, base.Width - 6, 26);
				bool flag = num2 == _hoverIndex;
				bool flag2 = num2 == _selectedIndex;
				if (flag || flag2)
				{
					using SolidBrush brush2 = new SolidBrush(flag ? theme.HeaderColor : Color.FromArgb(Math.Min(255, theme.HeaderColor.R + 10), Math.Min(255, theme.HeaderColor.G + 10), Math.Min(255, theme.HeaderColor.B + 10)));
					using GraphicsPath path2 = RoundRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, 4);
					graphics.FillPath(brush2, path2);
				}
				Color foreColor = (flag2 ? theme.AccentColor : theme.TextColor);
				TextRenderer.DrawText(bounds: new Rectangle(rectangle.X + 10, rectangle.Y, rectangle.Width - 14, rectangle.Height), dc: graphics, text: _items[num2], font: font, foreColor: foreColor, flags: TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
			}
			if (_items.Count > 10)
			{
				int num3 = base.Height - 8;
				float num4 = 10f / (float)_items.Count;
				int num5 = Math.Max(12, (int)((float)num3 * num4));
				float num6 = (float)_scrollOffset / (float)Math.Max(1, _items.Count - 10);
				int y2 = 4 + (int)(num6 * (float)(num3 - num5));
				using SolidBrush brush3 = new SolidBrush(theme.TextDimColor);
				graphics.FillRectangle(brush3, base.Width - 6, y2, 3, num5);
				return;
			}
		}

		private static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
		{
			GraphicsPath graphicsPath = new GraphicsPath();
			graphicsPath.AddArc(x, y, r * 2, r * 2, 180f, 90f);
			graphicsPath.AddArc(x + w - r * 2 - 1, y, r * 2, r * 2, 270f, 90f);
			graphicsPath.AddArc(x + w - r * 2 - 1, y + h - r * 2 - 1, r * 2, r * 2, 0f, 90f);
			graphicsPath.AddArc(x, y + h - r * 2 - 1, r * 2, r * 2, 90f, 90f);
			graphicsPath.CloseFigure();
			return graphicsPath;
		}
	}

	private sealed class CloseButton : Control
	{
		private bool _hover;

		private bool _pressed;

		public CloseButton()
		{
			base.Size = new Size(26, 26);
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

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			if (_hover)
			{
				using SolidBrush brush = new SolidBrush(Color.FromArgb(_pressed ? 110 : 70, 220, 70, 70));
				using GraphicsPath path = RoundRect(0, 0, base.Width, base.Height, 4);
				graphics.FillPath(brush, path);
			}
			using Pen pen = new Pen(_hover ? Color.FromArgb(235, 240, 250) : AppSettings.Instance.Theme.TextColor, 1.6f)
			{
				StartCap = LineCap.Round,
				EndCap = LineCap.Round
			};
			int num = base.Width / 2;
			int num2 = base.Height / 2;
			graphics.DrawLine(pen, num - 5, num2 - 5, num + 5, num2 + 5);
			graphics.DrawLine(pen, num + 5, num2 - 5, num - 5, num2 + 5);
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

	private sealed class StyledSlider : Control
	{
		private int _min;

		private int _max;

		private int _value;

		private bool _dragging;

		private bool _hover;

		private const int ThumbR = 7;

		private const int TrackH = 4;

		private int TL => 7;

		private int TR => base.Width - 7 - 44;

		private float Ratio => (float)(_value - _min) / (float)Math.Max(1, _max - _min);

		private int ThumbX => TL + (int)(Ratio * (float)(TR - TL));

		public event Action<int>? ValueChanged;

		public StyledSlider(int min, int max, int value)
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
				Upd(e.X);
			}
			base.OnMouseDown(e);
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			if (_dragging)
			{
				Upd(e.X);
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			_dragging = false;
			base.Capture = false;
			base.OnMouseUp(e);
		}

		private void Upd(int x)
		{
			float num = (float)(x - TL) / (float)Math.Max(1, TR - TL);
			int value = _min + (int)Math.Round(num * (float)(_max - _min));
			value = Math.Clamp(value, _min, _max);
			if (value != _value)
			{
				_value = value;
				Invalidate();
				this.ValueChanged?.Invoke(value);
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			int num = base.Height / 2;
			int thumbX = ThumbX;
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(30, 40, 60)))
			{
				using GraphicsPath path = RoundRectF(new RectangleF(TL, (float)num - 2f, TR - TL, 4f), 2f);
				graphics.FillPath(brush, path);
			}
			if (thumbX > TL)
			{
				Color accentColor = AppSettings.Instance.Theme.AccentColor;
				using SolidBrush brush2 = new SolidBrush((_hover || _dragging) ? ControlPaint.Light(accentColor, 0.3f) : accentColor);
				using GraphicsPath path2 = RoundRectF(new RectangleF(TL, (float)num - 2f, thumbX - TL, 4f), 2f);
				graphics.FillPath(brush2, path2);
			}
			using (SolidBrush brush3 = new SolidBrush(_dragging ? Color.FromArgb(220, 235, 255) : (_hover ? Color.FromArgb(200, 220, 250) : AppSettings.Instance.Theme.TextColor)))
			{
				graphics.FillEllipse(brush3, thumbX - 7, num - 7, 14, 14);
			}
			using (Pen pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1f))
			{
				graphics.DrawEllipse(pen, thumbX - 7, num - 7, 14, 14);
			}
			using Font font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 1f);
			using SolidBrush brush4 = new SolidBrush(Color.FromArgb(140, 165, 200));
			graphics.DrawString($"{_value}%", font, brush4, TR + 10, num - 7);
		}

		private static GraphicsPath RoundRectF(RectangleF rect, float r)
		{
			GraphicsPath graphicsPath = new GraphicsPath();
			float num = r * 2f;
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

	private sealed class ColorSwatch : Control
	{
		private Color _color;

		private readonly int _index;

		private bool _hover;

		public Color SwatchColor
		{
			get
			{
				return _color;
			}
			set
			{
				_color = value;
				Invalidate();
			}
		}

		public event Action<int, Color>? ColorPicked;

		public ColorSwatch(Color color, int index)
		{
			_color = color;
			_index = index;
			base.Size = new Size(18, 18);
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

		protected override void OnClick(EventArgs e)
		{
			base.OnClick(e);
			using ColorDialog colorDialog = new ColorDialog
			{
				Color = _color,
				FullOpen = true,
				AnyColor = true
			};
			if (colorDialog.ShowDialog() == DialogResult.OK)
			{
				_color = colorDialog.Color;
				Invalidate();
				this.ColorPicked?.Invoke(_index, _color);
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			int num = Math.Min(base.Width, base.Height) - 2;
			int x = (base.Width - num) / 2;
			int y = (base.Height - num) / 2;
			using (SolidBrush brush = new SolidBrush(_color))
			{
				graphics.FillEllipse(brush, x, y, num, num);
			}
			using Pen pen = new Pen(_hover ? Color.FromArgb(200, 220, 250) : Color.FromArgb(60, 80, 110), _hover ? 2f : 1.2f);
			graphics.DrawEllipse(pen, x, y, num, num);
		}
	}

	private sealed class DarkToggle : Control
	{
		private bool _checked;

		private bool _hover;

		private const int TrackW = 36;

		private const int TrackH = 18;

		private const int ThumbR = 7;

		public bool Checked
		{
			get
			{
				return _checked;
			}
			set
			{
				if (_checked != value)
				{
					_checked = value;
					Invalidate();
					this.CheckedChanged?.Invoke(this, EventArgs.Empty);
				}
			}
		}

		public event EventHandler? CheckedChanged;

		public DarkToggle(string text, bool isChecked)
		{
			_checked = isChecked;
			Text = text;
			base.Height = 22;
			base.Width = 44 + TextRenderer.MeasureText(text, new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize)).Width;
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

		protected override void OnClick(EventArgs e)
		{
			Checked = !_checked;
			base.OnClick(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			AppSettings.ThemeColors theme = AppSettings.Instance.Theme;
			int num = base.Height / 2;
			RectangleF rect = new RectangleF(0f, (float)num - 9f, 36f, 18f);
			using (SolidBrush brush = new SolidBrush(_checked ? theme.AccentColor : (_hover ? Color.FromArgb(45, 55, 80) : Color.FromArgb(30, 40, 60))))
			{
				using GraphicsPath path = RoundRectF(rect, 9f);
				graphics.FillPath(brush, path);
			}
			float num2 = (_checked ? 26 : 10);
			using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(240, 245, 255)))
			{
				graphics.FillEllipse(brush2, num2 - 7f, num - 7, 14f, 14f);
			}
			using Font font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize);
			Rectangle bounds = new Rectangle(44, 0, base.Width - 36 - 8, base.Height);
			TextRenderer.DrawText(graphics, Text, font, bounds, theme.TextColor, TextFormatFlags.VerticalCenter);
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

	private sealed class DarkScrollPanel : Panel
	{
		private int _scrollOffset;

		private int _contentHeight;

		private bool _thumbDrag;

		private int _thumbDragStartY;

		private int _thumbDragStartOffset;

		private const int ScrollBarW = 6;

		private const int ThumbMinH = 20;

		private int ViewH => base.ClientSize.Height;

		private bool NeedsScroll => _contentHeight > ViewH;

		private int MaxScroll => Math.Max(0, _contentHeight - ViewH);

		public DarkScrollPanel()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		}

		public void SetContentHeight(int h)
		{
			_contentHeight = h;
			ClampScroll();
			Invalidate();
		}

		private void ClampScroll()
		{
			_scrollOffset = Math.Clamp(_scrollOffset, 0, MaxScroll);
			if (base.Controls.Count > 0)
			{
				Control control = base.Controls[0];
				if (control != null)
				{
					control.Top = -_scrollOffset;
				}
			}
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			if (!NeedsScroll)
			{
				base.OnMouseWheel(e);
				return;
			}
			_scrollOffset -= e.Delta / 4;
			ClampScroll();
			Invalidate();
			base.OnMouseWheel(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left && NeedsScroll && e.X >= base.Width - 6 - 4)
			{
				var (num, num2) = GetThumbRect();
				if (e.Y >= num && e.Y <= num + num2)
				{
					_thumbDrag = true;
					_thumbDragStartY = e.Y;
					_thumbDragStartOffset = _scrollOffset;
					base.Capture = true;
				}
			}
			base.OnMouseDown(e);
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			if (_thumbDrag)
			{
				int num = e.Y - _thumbDragStartY;
				int item = GetThumbRect().h;
				int num2 = ViewH - 8;
				float num3 = (float)num / (float)Math.Max(1, num2 - item);
				_scrollOffset = _thumbDragStartOffset + (int)(num3 * (float)MaxScroll);
				ClampScroll();
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			_thumbDrag = false;
			base.Capture = false;
			base.OnMouseUp(e);
		}

		private (int y, int h) GetThumbRect()
		{
			int num = ViewH - 8;
			float num2 = (float)ViewH / (float)Math.Max(1, _contentHeight);
			int num3 = Math.Max(20, (int)((float)num * num2));
			float num4 = (float)_scrollOffset / (float)Math.Max(1, MaxScroll);
			return (y: 4 + (int)(num4 * (float)(num - num3)), h: num3);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			if (!NeedsScroll)
			{
				return;
			}
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			(int y, int h) thumbRect = GetThumbRect();
			int item = thumbRect.y;
			int item2 = thumbRect.h;
			int num = base.Width - 6 - 2;
			using SolidBrush brush = new SolidBrush(AppSettings.Instance.Theme.TextDimColor);
			using GraphicsPath path = RoundRectF(new RectangleF(num, item, 6f, item2), 3f);
			graphics.FillPath(brush, path);
		}

		private static GraphicsPath RoundRectF(RectangleF rect, float r)
		{
			GraphicsPath graphicsPath = new GraphicsPath();
			float num = r * 2f;
			if (rect.Width < num || rect.Height < num)
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

	private const int ResizeMargin = 14;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle |= 136;
			return createParams;
		}
	}

	public event Action? SettingsChanged;

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern nint SendMessage(nint hWnd, int Msg, nint wParam, nint lParam);

	public SettingsPanelForm()
	{
		Text = "설정";
		base.FormBorderStyle = FormBorderStyle.None;
		base.ShowInTaskbar = false;
		base.TopMost = true;
		MinimumSize = new Size(360, 340);
		base.Padding = new Padding(3);
		DoubleBuffered = true;
		AppSettings settings = AppSettings.Instance;
		base.Size = new Size(Math.Max(MinimumSize.Width, settings.SettingsPanelWidth), Math.Max(MinimumSize.Height, settings.SettingsPanelHeight));
		if (settings.SettingsPanelX >= 0 && settings.SettingsPanelY >= 0)
		{
			base.StartPosition = FormStartPosition.Manual;
			base.Location = new Point(settings.SettingsPanelX, settings.SettingsPanelY);
		}
		else
		{
			base.StartPosition = FormStartPosition.CenterScreen;
		}
		AppSettings.ThemeColors theme = settings.Theme;
		BackColor = theme.BgColor;
		Panel titleBar = new Panel
		{
			Dock = DockStyle.Top,
			Height = 36,
			BackColor = theme.HeaderColor
		};
		titleBar.Paint += delegate(object? _, PaintEventArgs e)
		{
			using Pen pen = new Pen(theme.BorderColor);
			e.Graphics.DrawLine(pen, 0, titleBar.Height - 1, titleBar.Width, titleBar.Height - 1);
		};
		string fontName = settings.FontName;
		float fontSize = settings.FontSize;
		Label label = new Label
		{
			Text = "설정",
			ForeColor = theme.TextColor,
			Font = new Font(fontName, fontSize + 0.5f, System.Drawing.FontStyle.Bold),
			AutoSize = true,
			Location = new Point(10, 9),
			BackColor = Color.Transparent
		};
		titleBar.Controls.Add(label);
		titleBar.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			Drag(e);
		};
		label.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			Drag(e);
		};
		CloseButton btnClose = new CloseButton();
		titleBar.Controls.Add(btnClose);
		titleBar.Resize += delegate
		{
			btnClose.Location = new Point(titleBar.Width - btnClose.Width - 8, (titleBar.Height - btnClose.Height) / 2);
		};
		btnClose.Click += delegate
		{
			Close();
		};
		DarkScrollPanel scrollPanel = new DarkScrollPanel
		{
			Dock = DockStyle.Fill,
			BackColor = theme.BgColor
		};
		Panel content = new Panel
		{
			BackColor = theme.BgColor,
			Location = Point.Empty
		};
		int num = 14;
		content.Controls.Add(SectionLabel("설정 저장", 24, num));
		num += 22;
		Button button = StyledButton("내보내기", 24, num, 90);
		button.Click += delegate
		{
			ExportSettings(settings);
		};
		content.Controls.Add(button);
		Button button2 = StyledButton("불러오기", 124, num, 90);
		button2.Click += delegate
		{
			if (ImportSettings(settings))
			{
				this.SettingsChanged?.Invoke();
				Close();
			}
		};
		content.Controls.Add(button2);
		Button button3 = StyledButton("초기화", 224, num, 76);
		button3.Click += delegate
		{
			if (MessageBox.Show("모든 설정을 초기화하시겠습니까?", "설정 초기화", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				ResetAllSettings(settings);
				this.SettingsChanged?.Invoke();
				Close();
			}
		};
		content.Controls.Add(button3);
		num += 36;
		content.Controls.Add(SectionLabel("테마 색상", 24, num));
		num += 22;
		string[] array = new string[8] { "배경", "헤더", "보더", "텍스트", "보조 텍스트", "강조", "천족", "마족" };
		Func<string>[] array2 = new Func<string>[8]
		{
			() => theme.Background,
			() => theme.Header,
			() => theme.Border,
			() => theme.TextPrimary,
			() => theme.TextSecondary,
			() => theme.Accent,
			() => theme.Elyos,
			() => theme.Asmodian
		};
		Action<string>[] themeSetters = new Action<string>[8]
		{
			delegate(string v)
			{
				theme.Background = v;
			},
			delegate(string v)
			{
				theme.Header = v;
			},
			delegate(string v)
			{
				theme.Border = v;
			},
			delegate(string v)
			{
				theme.TextPrimary = v;
			},
			delegate(string v)
			{
				theme.TextSecondary = v;
			},
			delegate(string v)
			{
				theme.Accent = v;
			},
			delegate(string v)
			{
				theme.Elyos = v;
			},
			delegate(string v)
			{
				theme.Asmodian = v;
			}
		};
		ColorSwatch[] array3 = new ColorSwatch[array.Length];
		for (int num2 = 0; num2 < array.Length; num2++)
		{
			int num3 = num2 % 3;
			int num4 = num2 / 3;
			int num5 = 24 + num3 * 110;
			int num6 = num + num4 * 32;
			Color color;
			try
			{
				color = ColorTranslator.FromHtml(array2[num2]());
			}
			catch
			{
				color = Color.Gray;
			}
			ColorSwatch colorSwatch = new ColorSwatch(color, num2)
			{
				Location = new Point(num5, num6 + 1)
			};
			int idx = num2;
			colorSwatch.ColorPicked += delegate(int _, Color newColor)
			{
				themeSetters[idx](ColorTranslator.ToHtml(newColor));
				settings.SaveDebounced();
				this.SettingsChanged?.Invoke();
			};
			array3[num2] = colorSwatch;
			content.Controls.Add(colorSwatch);
			content.Controls.Add(new Label
			{
				Text = array[num2],
				ForeColor = Color.FromArgb(140, 160, 190),
				Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 1.5f),
				AutoSize = true,
				Location = new Point(num5 + 22, num6 + 3),
				BackColor = Color.Transparent
			});
		}
		num += 102;
		content.Controls.Add(SectionLabel("폰트", 24, num));
		num += 22;
		List<string> fontItems = GetFontList();
		DarkDropdown darkDropdown = new DarkDropdown(fontItems, FindIndex(fontItems, settings.FontName))
		{
			Location = new Point(24, num),
			Width = 230
		};
		darkDropdown.SelectionChanged += delegate(int num8)
		{
			if (num8 >= 0 && num8 < fontItems.Count)
			{
				settings.FontName = fontItems[num8];
				settings.SaveDebounced();
				this.SettingsChanged?.Invoke();
			}
		};
		content.Controls.Add(darkDropdown);
		num += 34;
		content.Controls.Add(SectionLabel("굵기", 24, num));
		num += 22;
		List<string> items = new List<string> { "Thin (100)", "Light (300)", "Regular (400)", "Medium (500)", "SemiBold (600)", "Bold (700)", "Black (900)" };
		int[] weightValues = new int[7] { 100, 300, 400, 500, 600, 700, 900 };
		int selectedIndex = 2;
		for (int num7 = 0; num7 < weightValues.Length; num7++)
		{
			if (weightValues[num7] == settings.FontWeight)
			{
				selectedIndex = num7;
				break;
			}
		}
		DarkDropdown darkDropdown2 = new DarkDropdown(items, selectedIndex)
		{
			Location = new Point(24, num),
			Width = 160
		};
		darkDropdown2.SelectionChanged += delegate(int num8)
		{
			if (num8 >= 0 && num8 < weightValues.Length)
			{
				settings.FontWeight = weightValues[num8];
				settings.SaveDebounced();
				this.SettingsChanged?.Invoke();
			}
		};
		content.Controls.Add(darkDropdown2);
		num += 40;
		content.Controls.Add(SectionLabel("폰트 크기", 24, num));
		num += 22;
		List<string> sizeItems = new List<string>
		{
			"7", "7.5", "8", "8.5", "9", "9.5", "10", "10.5", "11", "12",
			"13", "14", "14.5", "15", "16", "17", "18", "19", "20", "21",
			"22", "23", "24"
		};
		DarkDropdown darkDropdown3 = new DarkDropdown(sizeItems, FindIndex(sizeItems, settings.FontSize.ToString("0.#")))
		{
			Location = new Point(24, num),
			Width = 90
		};
		darkDropdown3.SelectionChanged += delegate(int num8)
		{
			if (num8 >= 0 && num8 < sizeItems.Count && float.TryParse(sizeItems[num8], out var result))
			{
				settings.FontSize = result;
				settings.SaveDebounced();
				this.SettingsChanged?.Invoke();
			}
		};
		content.Controls.Add(darkDropdown3);
		num += 42;
		content.Controls.Add(SectionLabel("레이아웃 크기", 24, num));
		num += 22;
		StyledSlider styledSlider = new StyledSlider(50, 250, settings.RowHeight)
		{
			Location = new Point(24, num),
			Size = new Size(280, 26)
		};
		styledSlider.ValueChanged += delegate(int v)
		{
			settings.RowHeight = v;
			settings.SaveDebounced();
			this.SettingsChanged?.Invoke();
		};
		content.Controls.Add(styledSlider);
		num += 40;
		content.Controls.Add(SectionLabel("DPS바 투명도", 24, num));
		num += 22;
		StyledSlider styledSlider2 = new StyledSlider(5, 100, settings.BarOpacity)
		{
			Location = new Point(24, num),
			Size = new Size(280, 26)
		};
		styledSlider2.ValueChanged += delegate(int v)
		{
			settings.BarOpacity = v;
			settings.SaveDebounced();
			this.SettingsChanged?.Invoke();
		};
		content.Controls.Add(styledSlider2);
		num += 40;
		content.Controls.Add(SectionLabel("숫자 표기", 24, num));
		num += 22;
		List<string> items2 = new List<string> { "축약 (1.5M)", "그대로 (1,500,000)" };
		int selectedIndex2 = ((settings.NumberFormat == "full") ? 1 : 0);
		DarkDropdown darkDropdown4 = new DarkDropdown(items2, selectedIndex2)
		{
			Location = new Point(24, num),
			Width = 180
		};
		darkDropdown4.SelectionChanged += delegate(int num8)
		{
			settings.NumberFormat = ((num8 == 1) ? "full" : "abbreviated");
			settings.SaveDebounced();
			this.SettingsChanged?.Invoke();
		};
		content.Controls.Add(darkDropdown4);
		num += 42;
		content.Controls.Add(SectionLabel("기여도 표기", 24, num));
		num += 22;
		List<string> items3 = new List<string> { "총 딜량 대비", "보스 최대체력 대비" };
		int selectedIndex3 = (string.Equals(settings.DpsPercentMode, "boss", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
		DarkDropdown darkDropdown5 = new DarkDropdown(items3, selectedIndex3)
		{
			Location = new Point(24, num),
			Width = 180
		};
		darkDropdown5.SelectionChanged += delegate(int num8)
		{
			settings.DpsPercentMode = ((num8 == 1) ? "boss" : "party");
			settings.SaveDebounced();
			this.SettingsChanged?.Invoke();
		};
		content.Controls.Add(darkDropdown5);
		num += 42;
		content.Controls.Add(SectionLabel("DPS바 레이아웃", 24, num));
		num += 22;
		num = AddBarSlotRow(content, "슬롯 1 (이름 옆)", settings.BarSlot1, 24, num, settings);
		num = AddBarSlotRow(content, "슬롯 2 (오른쪽)", settings.BarSlot2, 24, num, settings);
		num = AddBarSlotRow(content, "슬롯 3 (맨 오른쪽)", settings.BarSlot3, 24, num, settings);
		num += 10;
		DarkToggle chkGpu = StyledCheckBox("GPU 가속 사용", 24, num, string.Equals(settings.GpuMode, "on", StringComparison.OrdinalIgnoreCase));
		chkGpu.CheckedChanged += delegate
		{
			settings.GpuMode = (chkGpu.Checked ? "on" : "off");
			settings.GpuModeUserOverride = true;
			settings.SaveDebounced();
			this.SettingsChanged?.Invoke();
		};
		content.Controls.Add(chkGpu);
		num += 32;
		content.Controls.Add(new Label
		{
			Text = "※ GPU 가속은 재시작 후 적용됩니다.",
			ForeColor = Color.FromArgb(80, 100, 130),
			Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 1f),
			AutoSize = true,
			Location = new Point(24, num),
			BackColor = Color.Transparent
		});
		num += 34;
		DarkToggle chkAionOnly = StyledCheckBox("아이온2 활성화 시에만 오버레이 표시", 24, num, settings.OverlayOnlyWhenAion);
		chkAionOnly.CheckedChanged += delegate
		{
			settings.OverlayOnlyWhenAion = chkAionOnly.Checked;
			settings.SaveDebounced();
			if (base.Owner is OverlayForm overlayForm)
			{
				overlayForm.SetOverlayOnlyWhenAion(chkAionOnly.Checked);
			}
		};
		content.Controls.Add(chkAionOnly);
		num += 34;
		content.Controls.Add(SectionLabel("단축키", 24, num));
		num += 22;
		ShortcutSettings shortcuts = settings.Shortcuts;
		num = AddShortcutRow(content, "리셋", shortcuts.Reset, 24, num, delegate(string v)
		{
			shortcuts.Reset = v;
			settings.SaveDebounced();
		});
		num = AddShortcutRow(content, "프로그램 재시작", shortcuts.Restart, 24, num, delegate(string v)
		{
			shortcuts.Restart = v;
			settings.SaveDebounced();
		});
		num = AddShortcutRow(content, "익명 모드", shortcuts.Anonymous, 24, num, delegate(string v)
		{
			shortcuts.Anonymous = v;
			settings.SaveDebounced();
		});
		num = AddShortcutRow(content, "컴팩트 모드", shortcuts.Compact, 24, num, delegate(string v)
		{
			shortcuts.Compact = v;
			settings.SaveDebounced();
		});
		num = AddShortcutRow(content, "숨기기", shortcuts.Hide, 24, num, delegate(string v)
		{
			shortcuts.Hide = v;
			settings.SaveDebounced();
		});
		num += 10;
		content.Size = new Size(base.ClientSize.Width, num);
		scrollPanel.Controls.Add(content);
		scrollPanel.SetContentHeight(num);
		base.Controls.Add(scrollPanel);
		base.Controls.Add(titleBar);
		base.Resize += delegate
		{
			content.Width = scrollPanel.ClientSize.Width;
			scrollPanel.SetContentHeight(content.Height);
		};
	}

	private static int FindIndex(List<string> items, string value)
	{
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i].Equals(value, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return 0;
	}

	private static List<string> GetFontList()
	{
		try
		{
			using IDWriteFactory iDWriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
			using IDWriteFontCollection iDWriteFontCollection = iDWriteFactory.GetSystemFontCollection(false);
			List<string> list = new List<string>();
			string[] obj = new string[5] { "Malgun Gothic", "Segoe UI", "Noto Sans KR", "Gmarket Sans", "D2Coding" };
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string[] array = obj;
			foreach (string item in array)
			{
				list.Add(item);
				hashSet.Add(item);
			}
			int fontFamilyCount = (int)iDWriteFontCollection.FontFamilyCount;
			for (int j = 0; j < fontFamilyCount; j++)
			{
				using IDWriteFontFamily iDWriteFontFamily = iDWriteFontCollection.GetFontFamily((uint)j);
				using IDWriteLocalizedStrings iDWriteLocalizedStrings = iDWriteFontFamily.FamilyNames;
				iDWriteLocalizedStrings.FindLocaleName("en-us", out var index);
				if (index == uint.MaxValue)
				{
					index = 0u;
				}
				string item2 = iDWriteLocalizedStrings.GetString(index);
				if (!hashSet.Contains(item2))
				{
					hashSet.Add(item2);
					list.Add(item2);
				}
			}
			return list;
		}
		catch
		{
			List<string> list2 = new List<string> { "Malgun Gothic", "Segoe UI" };
			using InstalledFontCollection installedFontCollection = new InstalledFontCollection();
			FontFamily[] families = installedFontCollection.Families;
			foreach (FontFamily fontFamily in families)
			{
				list2.Add(fontFamily.Name);
			}
			return list2;
		}
	}

	private static Label SectionLabel(string text, int x, int y)
	{
		return new Label
		{
			Text = text,
			ForeColor = AppSettings.Instance.Theme.TextDimColor,
			Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f, System.Drawing.FontStyle.Bold),
			AutoSize = true,
			Location = new Point(x, y),
			BackColor = Color.Transparent
		};
	}

	private static DarkToggle StyledCheckBox(string text, int x, int y, bool isChecked)
	{
		return new DarkToggle(text, isChecked)
		{
			Location = new Point(x, y)
		};
	}

	private static Button StyledButton(string text, int x, int y, int width)
	{
		AppSettings.ThemeColors theme = AppSettings.Instance.Theme;
		Button button = new Button();
		button.Text = text;
		button.Location = new Point(x, y);
		button.Width = width;
		button.Height = 26;
		button.FlatStyle = FlatStyle.Flat;
		button.BackColor = theme.HeaderColor;
		button.ForeColor = theme.TextColor;
		button.Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f);
		button.Cursor = Cursors.Hand;
		button.FlatAppearance.BorderColor = theme.BorderColor;
		button.FlatAppearance.MouseOverBackColor = Color.FromArgb(Math.Min(255, theme.HeaderColor.R + 18), Math.Min(255, theme.HeaderColor.G + 18), Math.Min(255, theme.HeaderColor.B + 18));
		return button;
	}

	private int AddShortcutRow(Panel content, string label, string currentValue, int left, int y, Action<string> onChanged)
	{
		AppSettings.ThemeColors theme = AppSettings.Instance.Theme;
		content.Controls.Add(new Label
		{
			Text = label,
			ForeColor = theme.TextColor,
			Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f),
			AutoSize = true,
			Location = new Point(left, y + 4),
			BackColor = Color.Transparent
		});
		TextBox txt = new TextBox
		{
			Text = currentValue,
			Location = new Point(left + 120, y),
			Width = 140,
			Height = 24,
			BackColor = theme.HeaderColor,
			ForeColor = theme.TextColor,
			BorderStyle = BorderStyle.FixedSingle,
			Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize)
		};
		txt.GotFocus += delegate
		{
			(base.Owner as OverlayForm)?.Hotkeys?.Suspend();
		};
		txt.LostFocus += delegate
		{
			((base.Owner as OverlayForm)?.Hotkeys)?.Resume(AppSettings.Instance.Shortcuts);
		};
		txt.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			e.SuppressKeyPress = true;
			if (e.KeyCode == Keys.Escape)
			{
				txt.Text = currentValue;
				content.Focus();
			}
			else if (e.KeyCode == Keys.Back)
			{
				currentValue = "";
				txt.Text = "";
				onChanged("");
				content.Focus();
			}
			else
			{
				List<string> list = new List<string>();
				if (e.Alt)
				{
					list.Add("Alt");
				}
				if (e.Control)
				{
					list.Add("Ctrl");
				}
				if (e.Shift)
				{
					list.Add("Shift");
				}
				Keys keyCode = e.KeyCode;
				string text;
				switch (keyCode)
				{
				case Keys.Oemtilde:
					text = "`";
					break;
				case Keys.OemMinus:
					text = "-";
					break;
				case Keys.OemQuestion:
					text = "/";
					break;
				default:
					text = keyCode.ToString();
					break;
				case Keys.ShiftKey:
				case Keys.ControlKey:
				case Keys.Menu:
					return;
				}
				string item = text;
				list.Add(item);
				txt.Text = string.Join("+", list);
				currentValue = txt.Text;
				onChanged(txt.Text);
			}
		};
		content.Controls.Add(txt);
		return y + 32;
	}

	private int AddBarSlotRow(Panel content, string label, BarSlotConfig slot, int left, int y, AppSettings settings)
	{
		AppSettings.ThemeColors theme = settings.Theme;
		content.Controls.Add(new Label
		{
			Text = label,
			ForeColor = theme.TextColor,
			Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f),
			AutoSize = true,
			Location = new Point(left, y + 4),
			BackColor = Color.Transparent
		});
		List<string> items = new List<string> { "없음", "기여도", "대미지", "DPS" };
		DarkDropdown darkDropdown = new DarkDropdown(items, slot.Content switch
		{
			"percent" => 1, 
			"damage" => 2, 
			"dps" => 3, 
			_ => 0, 
		})
		{
			Location = new Point(left + 120, y),
			Width = 90
		};
		darkDropdown.SelectionChanged += delegate(int idx)
		{
			BarSlotConfig barSlotConfig = slot;
			barSlotConfig.Content = idx switch
			{
				1 => "percent", 
				2 => "damage", 
				3 => "dps", 
				_ => "none", 
			};
			settings.SaveDebounced();
			this.SettingsChanged?.Invoke();
		};
		content.Controls.Add(darkDropdown);
		List<string> sizeItems = new List<string> { "7", "7.5", "8", "8.5", "9", "9.5", "10", "11" };
		DarkDropdown darkDropdown2 = new DarkDropdown(sizeItems, FindIndex(sizeItems, slot.FontSize.ToString("0.#")))
		{
			Location = new Point(left + 218, y),
			Width = 54
		};
		darkDropdown2.SelectionChanged += delegate(int idx)
		{
			if (idx >= 0 && idx < sizeItems.Count && float.TryParse(sizeItems[idx], out var result))
			{
				slot.FontSize = result;
				settings.SaveDebounced();
				this.SettingsChanged?.Invoke();
			}
		};
		content.Controls.Add(darkDropdown2);
		Color color;
		try
		{
			color = ColorTranslator.FromHtml(slot.Color);
		}
		catch
		{
			color = Color.Gray;
		}
		ColorSwatch colorSwatch = new ColorSwatch(color, 0)
		{
			Location = new Point(left + 280, y + 3)
		};
		colorSwatch.ColorPicked += delegate(int _, Color newColor)
		{
			slot.Color = ColorTranslator.ToHtml(newColor);
			settings.SaveDebounced();
			this.SettingsChanged?.Invoke();
		};
		content.Controls.Add(colorSwatch);
		return y + 34;
	}

	private static void ExportSettings(AppSettings settings)
	{
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = "설정 내보내기",
			Filter = "JSON 파일|*.json",
			FileName = "a2meter_settings.json"
		};
		if (saveFileDialog.ShowDialog() == DialogResult.OK)
		{
			string contents = JsonSerializer.Serialize(settings, new JsonSerializerOptions
			{
				WriteIndented = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
			});
			File.WriteAllText(saveFileDialog.FileName, contents);
		}
	}

	private static bool ImportSettings(AppSettings settings)
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "설정 불러오기",
			Filter = "JSON 파일|*.json"
		};
		if (openFileDialog.ShowDialog() != DialogResult.OK)
		{
			return false;
		}
		try
		{
			AppSettings appSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(openFileDialog.FileName), new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			});
			if (appSettings == null)
			{
				return false;
			}
			settings.OverlayOnlyWhenAion = appSettings.OverlayOnlyWhenAion;
			settings.GpuMode = appSettings.GpuMode;
			settings.GpuModeUserOverride = appSettings.GpuModeUserOverride;
			settings.Opacity = appSettings.Opacity;
			settings.BarOpacity = appSettings.BarOpacity;
			settings.FontName = appSettings.FontName;
			settings.FontWeight = appSettings.FontWeight;
			settings.FontSize = appSettings.FontSize;
			settings.Theme = appSettings.Theme;
			settings.FontScale = appSettings.FontScale;
			settings.RowHeight = appSettings.RowHeight;
			settings.Shortcuts = appSettings.Shortcuts ?? new ShortcutSettings();
			settings.DpsPercentMode = appSettings.DpsPercentMode;
			settings.NumberFormat = appSettings.NumberFormat;
			settings.ShowCombatPower = appSettings.ShowCombatPower;
			settings.ShowCombatScore = appSettings.ShowCombatScore;
			settings.BarSlot1 = appSettings.BarSlot1;
			settings.BarSlot2 = appSettings.BarSlot2;
			settings.BarSlot3 = appSettings.BarSlot3;
			settings.Save();
			return true;
		}
		catch
		{
		}
		return false;
	}

	private static void ResetAllSettings(AppSettings settings)
	{
		AppSettings appSettings = new AppSettings();
		settings.OverlayOnlyWhenAion = appSettings.OverlayOnlyWhenAion;
		settings.GpuMode = appSettings.GpuMode;
		settings.GpuModeUserOverride = appSettings.GpuModeUserOverride;
		settings.Opacity = appSettings.Opacity;
		settings.BarOpacity = appSettings.BarOpacity;
		settings.FontName = appSettings.FontName;
		settings.FontWeight = appSettings.FontWeight;
		settings.FontSize = appSettings.FontSize;
		settings.Theme = appSettings.Theme;
		settings.FontScale = appSettings.FontScale;
		settings.RowHeight = appSettings.RowHeight;
		settings.Shortcuts = appSettings.Shortcuts;
		settings.DpsPercentMode = appSettings.DpsPercentMode;
		settings.NumberFormat = appSettings.NumberFormat;
		settings.ShowCombatPower = appSettings.ShowCombatPower;
		settings.ShowCombatScore = appSettings.ShowCombatScore;
		settings.BarSlot1 = appSettings.BarSlot1;
		settings.BarSlot2 = appSettings.BarSlot2;
		settings.BarSlot3 = appSettings.BarSlot3;
		settings.Save();
	}

	private void PersistBounds()
	{
		if (base.WindowState == FormWindowState.Normal)
		{
			AppSettings instance = AppSettings.Instance;
			instance.SettingsPanelX = base.Location.X;
			instance.SettingsPanelY = base.Location.Y;
			instance.SettingsPanelWidth = base.Size.Width;
			instance.SettingsPanelHeight = base.Size.Height;
			instance.SaveDebounced();
		}
	}

	protected override void OnMove(EventArgs e)
	{
		base.OnMove(e);
		PersistBounds();
	}

	protected override void OnResizeEnd(EventArgs e)
	{
		base.OnResizeEnd(e);
		PersistBounds();
	}

	private void Drag(MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			ReleaseCapture();
			SendMessage(base.Handle, 161, 2, IntPtr.Zero);
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		using Pen pen = new Pen(AppSettings.Instance.Theme.BorderColor);
		e.Graphics.DrawRectangle(pen, 0, 0, base.Width - 1, base.Height - 1);
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == 132)
		{
			int num = (int)m.LParam;
			Point pt = PointToClient(new Point((short)(num & 0xFFFF), (short)((num >> 16) & 0xFFFF)));
			int num2 = HitTestEdges(pt);
			if (num2 != 1)
			{
				m.Result = num2;
				return;
			}
			if (pt.Y >= 0 && pt.Y < 36 && pt.X < base.Width - 40)
			{
				m.Result = 2;
				return;
			}
		}
		base.WndProc(ref m);
	}

	private int HitTestEdges(Point pt)
	{
		int width = base.ClientSize.Width;
		int height = base.ClientSize.Height;
		bool flag = pt.X < 14;
		bool flag2 = pt.X >= width - 14;
		bool flag3 = pt.Y < 14;
		bool flag4 = pt.Y >= height - 14;
		if (flag3 && flag)
		{
			return 13;
		}
		if (flag3 && flag2)
		{
			return 14;
		}
		if (flag4 && flag)
		{
			return 16;
		}
		if (flag4 && flag2)
		{
			return 17;
		}
		if (flag)
		{
			return 10;
		}
		if (flag2)
		{
			return 11;
		}
		if (flag3)
		{
			return 12;
		}
		if (flag4)
		{
			return 15;
		}
		return 1;
	}
}
