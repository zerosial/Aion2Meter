using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using A2Meter.Core;

namespace A2Meter.Forms;

internal sealed class VirtualListPanel : Control
{
	private readonly AppSettings.ThemeColors _t = AppSettings.Instance.Theme;

	private int _rowCount;

	private int _scrollOffset;

	private int _selectedIndex = -1;

	private int _rowHeight = 24;

	private int _headerHeight = 26;

	private const int ScrollBarW = 6;

	private const int ScrollBarPad = 3;

	public int RowCount
	{
		get
		{
			return _rowCount;
		}
		set
		{
			_rowCount = value;
			_scrollOffset = 0;
			_selectedIndex = -1;
			Invalidate();
		}
	}

	public int RowHeight
	{
		get
		{
			return _rowHeight;
		}
		set
		{
			_rowHeight = value;
			Invalidate();
		}
	}

	public int HeaderHeight
	{
		get
		{
			return _headerHeight;
		}
		set
		{
			_headerHeight = value;
			Invalidate();
		}
	}

	public int SelectedIndex => _selectedIndex;

	public (string Text, float Weight, HorizontalAlignment Align)[] Columns { get; set; } = Array.Empty<(string, float, HorizontalAlignment)>();

	private int VisibleRows => Math.Max(1, (base.Height - _headerHeight) / _rowHeight);

	private int MaxScroll => Math.Max(0, _rowCount - VisibleRows);

	public event Action<Graphics, Rectangle, Rectangle[], int, bool>? PaintRow;

	public event Action<int>? RowDoubleClicked;

	public VirtualListPanel()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = _t.BgColor;
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		int num = ((e.Delta > 0) ? (-3) : 3);
		_scrollOffset = Math.Clamp(_scrollOffset + num, 0, MaxScroll);
		Invalidate();
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		Focus();
		if (e.Button != MouseButtons.Left)
		{
			return;
		}
		if (e.X >= base.Width - 6 - 6 && MaxScroll > 0)
		{
			var (num, num2) = ThumbRect();
			if (e.Y < num)
			{
				_scrollOffset = Math.Max(0, _scrollOffset - VisibleRows);
			}
			else if (e.Y > num + num2)
			{
				_scrollOffset = Math.Min(MaxScroll, _scrollOffset + VisibleRows);
			}
			Invalidate();
		}
		else
		{
			int num3 = HitTestRow(e.Y);
			if (num3 >= 0 && num3 < _rowCount)
			{
				_selectedIndex = num3;
				Invalidate();
			}
			base.OnMouseDown(e);
		}
	}

	protected override void OnMouseDoubleClick(MouseEventArgs e)
	{
		int num = HitTestRow(e.Y);
		if (num >= 0 && num < _rowCount)
		{
			this.RowDoubleClicked?.Invoke(num);
		}
		base.OnMouseDoubleClick(e);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Up && _selectedIndex > 0)
		{
			_selectedIndex--;
			EnsureVisible(_selectedIndex);
			Invalidate();
		}
		else if (e.KeyCode == Keys.Down && _selectedIndex < _rowCount - 1)
		{
			_selectedIndex++;
			EnsureVisible(_selectedIndex);
			Invalidate();
		}
		else if (e.KeyCode == Keys.Return && _selectedIndex >= 0)
		{
			this.RowDoubleClicked?.Invoke(_selectedIndex);
		}
		base.OnKeyDown(e);
	}

	private int HitTestRow(int y)
	{
		if (y < _headerHeight)
		{
			return -1;
		}
		return _scrollOffset + (y - _headerHeight) / _rowHeight;
	}

	private void EnsureVisible(int index)
	{
		if (index < _scrollOffset)
		{
			_scrollOffset = index;
		}
		else if (index >= _scrollOffset + VisibleRows)
		{
			_scrollOffset = index - VisibleRows + 1;
		}
		_scrollOffset = Math.Clamp(_scrollOffset, 0, MaxScroll);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		int width = base.Width;
		using (SolidBrush brush = new SolidBrush(_t.HeaderColor))
		{
			graphics.FillRectangle(brush, 0, 0, width, _headerHeight);
		}
		using (Pen pen = new Pen(_t.BorderColor))
		{
			graphics.DrawLine(pen, 0, _headerHeight - 1, width, _headerHeight - 1);
		}
		Rectangle[] array = ComputeColumnRects(0, 0, width - 6 - 6, _headerHeight);
		for (int i = 0; i < Columns.Length && i < array.Length; i++)
		{
			Rectangle rectangle = array[i];
			TextFormatFlags flags = AlignFlag(Columns[i].Align) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
			TextRenderer.DrawText(bounds: new Rectangle(rectangle.X + 4, rectangle.Y, rectangle.Width - 8, rectangle.Height), dc: graphics, text: Columns[i].Text, font: Font, foreColor: _t.TextColor, flags: flags);
		}
		int num = _headerHeight;
		int visibleRows = VisibleRows;
		for (int j = 0; j < visibleRows; j++)
		{
			int num2 = _scrollOffset + j;
			if (num2 >= _rowCount)
			{
				break;
			}
			bool flag = num2 == _selectedIndex;
			Rectangle rectangle2 = new Rectangle(0, num, width - 6 - 6, _rowHeight);
			Color color = (flag ? Color.FromArgb(Math.Min(255, _t.AccentColor.R / 4 + _t.BgColor.R), Math.Min(255, _t.AccentColor.G / 4 + _t.BgColor.G), Math.Min(255, _t.AccentColor.B / 4 + _t.BgColor.B)) : ((num2 % 2 != 1) ? _t.BgColor : Color.FromArgb(Math.Min(255, _t.HeaderColor.R + 3), Math.Min(255, _t.HeaderColor.G + 3), Math.Min(255, _t.HeaderColor.B + 3))));
			using (SolidBrush brush2 = new SolidBrush(color))
			{
				graphics.FillRectangle(brush2, rectangle2);
			}
			using (Pen pen2 = new Pen(Color.FromArgb(40, _t.BorderColor)))
			{
				graphics.DrawLine(pen2, 0, num + _rowHeight - 1, rectangle2.Right, num + _rowHeight - 1);
			}
			Rectangle[] arg = ComputeColumnRects(0, num, rectangle2.Width, _rowHeight);
			this.PaintRow?.Invoke(graphics, rectangle2, arg, num2, flag);
			num += _rowHeight;
		}
		if (MaxScroll <= 0)
		{
			return;
		}
		(int y, int h) tuple = ThumbRect();
		int item = tuple.y;
		int item2 = tuple.h;
		int num3 = width - 6 - 3;
		using SolidBrush brush3 = new SolidBrush(Color.FromArgb(140, _t.TextDimColor));
		float num4 = 3f;
		RectangleF rect = new RectangleF(num3, item, 6f, item2);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using GraphicsPath graphicsPath = new GraphicsPath();
		float num5 = num4 * 2f;
		if (rect.Height < num5)
		{
			graphicsPath.AddEllipse(rect);
		}
		else
		{
			graphicsPath.AddArc(rect.X, rect.Y, num5, num5, 180f, 90f);
			graphicsPath.AddArc(rect.Right - num5, rect.Y, num5, num5, 270f, 90f);
			graphicsPath.AddArc(rect.Right - num5, rect.Bottom - num5, num5, num5, 0f, 90f);
			graphicsPath.AddArc(rect.X, rect.Bottom - num5, num5, num5, 90f, 90f);
			graphicsPath.CloseFigure();
		}
		graphics.FillPath(brush3, graphicsPath);
	}

	private (int y, int h) ThumbRect()
	{
		int num = base.Height - _headerHeight;
		float num2 = (float)VisibleRows / (float)Math.Max(1, _rowCount);
		int num3 = Math.Max(20, (int)((float)num * num2));
		float num4 = (float)_scrollOffset / (float)Math.Max(1, MaxScroll);
		return (y: _headerHeight + (int)(num4 * (float)(num - num3)), h: num3);
	}

	private Rectangle[] ComputeColumnRects(int x0, int y, int totalW, int h)
	{
		if (Columns.Length == 0)
		{
			return Array.Empty<Rectangle>();
		}
		float num = 0f;
		(string, float, HorizontalAlignment)[] columns = Columns;
		for (int i = 0; i < columns.Length; i++)
		{
			(string, float, HorizontalAlignment) tuple = columns[i];
			num += tuple.Item2;
		}
		if (num <= 0f)
		{
			num = 1f;
		}
		Rectangle[] array = new Rectangle[Columns.Length];
		float num2 = x0;
		for (int j = 0; j < Columns.Length; j++)
		{
			float num3 = Columns[j].Weight / num * (float)totalW;
			array[j] = new Rectangle((int)num2, y, (int)num3, h);
			num2 += num3;
		}
		return array;
	}

	private static TextFormatFlags AlignFlag(HorizontalAlignment a)
	{
		return a switch
		{
			HorizontalAlignment.Right => TextFormatFlags.Right, 
			HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter, 
			_ => TextFormatFlags.Default, 
		};
	}
}
