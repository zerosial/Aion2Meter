using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Dps;

namespace A2Meter.Forms;

internal sealed class CombatHistoryForm : Form
{
	private sealed class HeaderCloseButton : Control
	{
		private bool _hover;

		private bool _pressed;

		public HeaderCloseButton()
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
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hover = false;
			_pressed = false;
			Invalidate();
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			_pressed = true;
			Invalidate();
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			_pressed = false;
			Invalidate();
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

	private const int ResizeMargin = 14;

	private readonly VirtualListPanel _list;

	private readonly Panel _headerPanel;

	private CombatHistory? _history;

	private Action<CombatRecord>? _onRecordSelected;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle |= 136;
			return createParams;
		}
	}

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern nint SendMessage(nint hWnd, int Msg, nint wParam, nint lParam);

	public CombatHistoryForm()
	{
		Text = "전투 기록";
		base.FormBorderStyle = FormBorderStyle.None;
		base.ShowInTaskbar = false;
		MinimumSize = new Size(500, 300);
		AppSettings instance = AppSettings.Instance;
		AppSettings.ThemeColors _t = instance.Theme;
		BackColor = _t.BgColor;
		base.Padding = new Padding(3);
		DoubleBuffered = true;
		base.Size = new Size(Math.Max(MinimumSize.Width, instance.CombatRecordsPanelWidth), Math.Max(MinimumSize.Height, instance.CombatRecordsPanelHeight));
		if (instance.CombatRecordsPanelX >= 0 && instance.CombatRecordsPanelY >= 0)
		{
			base.StartPosition = FormStartPosition.Manual;
			base.Location = new Point(instance.CombatRecordsPanelX, instance.CombatRecordsPanelY);
		}
		else
		{
			base.StartPosition = FormStartPosition.CenterScreen;
		}
		_headerPanel = new Panel
		{
			Dock = DockStyle.Top,
			Height = 36,
			BackColor = _t.HeaderColor
		};
		_headerPanel.Paint += delegate(object? _, PaintEventArgs e)
		{
			using Pen pen = new Pen(_t.BorderColor);
			e.Graphics.DrawLine(pen, 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);
		};
		Label label = new Label
		{
			Text = "전투 기록",
			ForeColor = _t.TextColor,
			Font = new Font(instance.FontName, instance.FontSize + 0.5f, FontStyle.Bold),
			AutoSize = true,
			Location = new Point(10, 9),
			BackColor = Color.Transparent
		};
		_headerPanel.Controls.Add(label);
		HeaderCloseButton btnClose = new HeaderCloseButton();
		_headerPanel.Controls.Add(btnClose);
		_headerPanel.Resize += delegate
		{
			btnClose.Location = new Point(_headerPanel.Width - btnClose.Width - 8, (_headerPanel.Height - btnClose.Height) / 2);
		};
		btnClose.Click += delegate
		{
			Close();
		};
		_headerPanel.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			Drag(e);
		};
		label.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			Drag(e);
		};
		_list = new VirtualListPanel
		{
			Dock = DockStyle.Fill,
			Font = new Font(instance.FontName, instance.FontSize),
			RowHeight = (int)(24f + instance.FontSize),
			Columns = new(string, float, HorizontalAlignment)[6]
			{
				("시간", 130f, HorizontalAlignment.Left),
				("보스", 140f, HorizontalAlignment.Left),
				("시간(초)", 70f, HorizontalAlignment.Right),
				("총 대미지", 110f, HorizontalAlignment.Right),
				("평균 DPS", 100f, HorizontalAlignment.Right),
				("최대 DPS", 100f, HorizontalAlignment.Right)
			}
		};
		_list.PaintRow += OnPaintRow;
		_list.RowDoubleClicked += delegate(int idx)
		{
			if (_history != null && idx >= 0 && idx < _history.Records.Count)
			{
				_onRecordSelected?.Invoke(_history.Records[idx]);
			}
		};
		base.Controls.Add(_list);
		base.Controls.Add(_headerPanel);
	}

	public void SetData(CombatHistory history, Action<CombatRecord> onSelect)
	{
		_history = history;
		_onRecordSelected = onSelect;
		_list.RowCount = history.Records.Count;
	}

	private void OnPaintRow(Graphics g, Rectangle rowRect, Rectangle[] cells, int idx, bool sel)
	{
		if (_history != null && idx >= 0 && idx < _history.Records.Count)
		{
			CombatRecord combatRecord = _history.Records[idx];
			Color foreColor = (sel ? Color.White : AppSettings.Instance.Theme.TextColor);
			string[] array = new string[6]
			{
				combatRecord.Timestamp.ToString("MM/dd HH:mm:ss"),
				combatRecord.BossName ?? "-",
				$"{combatRecord.DurationSec:0.0}",
				$"{combatRecord.TotalDamage:N0}",
				$"{combatRecord.AverageDps:N0}",
				$"{combatRecord.PeakDps:N0}"
			};
			for (int i = 0; i < cells.Length && i < array.Length; i++)
			{
				Rectangle rectangle = cells[i];
				TextFormatFlags flags = VirtualListPanel_AlignFlag(_list.Columns[i].Align) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
				TextRenderer.DrawText(bounds: new Rectangle(rectangle.X + 4, rectangle.Y, rectangle.Width - 8, rectangle.Height), dc: g, text: array[i], font: _list.Font, foreColor: foreColor, flags: flags);
			}
		}
	}

	private static TextFormatFlags VirtualListPanel_AlignFlag(HorizontalAlignment a)
	{
		return a switch
		{
			HorizontalAlignment.Right => TextFormatFlags.Right, 
			HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter, 
			_ => TextFormatFlags.Default, 
		};
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		AppSettings instance = AppSettings.Instance;
		instance.CombatRecordsPanelWidth = base.Width;
		instance.CombatRecordsPanelHeight = base.Height;
		instance.SaveDebounced();
		base.OnFormClosing(e);
	}

	private void Drag(MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			ReleaseCapture();
			SendMessage(base.Handle, 161, 2, IntPtr.Zero);
		}
	}

	private void PersistBounds()
	{
		if (base.WindowState == FormWindowState.Normal)
		{
			AppSettings instance = AppSettings.Instance;
			instance.CombatRecordsPanelX = base.Location.X;
			instance.CombatRecordsPanelY = base.Location.Y;
			instance.CombatRecordsPanelWidth = base.Size.Width;
			instance.CombatRecordsPanelHeight = base.Size.Height;
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
			if (pt.Y < 36 && pt.X < base.Width - 40)
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
