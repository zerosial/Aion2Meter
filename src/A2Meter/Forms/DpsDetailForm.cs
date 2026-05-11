using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Api;
using A2Meter.Core;
using A2Meter.Direct2D;
using A2Meter.Dps;
using Vortice.Mathematics;

namespace A2Meter.Forms;

internal sealed class DpsDetailForm : Form
{
	private record struct BuffRow(string Name, string Uptime, double Percent);

	private record struct SkillRow(string Name, string IconKey, int[]? Specs, string Hits, string Crit, string Back, string Strong, string Perfect, string Multi, string Dodge, string Block, string Max, string Dps, string Avg, string Damage, double Percent);

	private sealed class HeaderCloseButton : Control
	{
		private bool _hover;

		private bool _pressed;

		public HeaderCloseButton()
		{
			base.Size = new System.Drawing.Size(26, 26);
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = System.Drawing.Color.Transparent;
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
				using SolidBrush brush = new SolidBrush(System.Drawing.Color.FromArgb(_pressed ? 110 : 70, 220, 70, 70));
				using GraphicsPath path = RoundRect(0, 0, base.Width, base.Height, 4);
				graphics.FillPath(brush, path);
			}
			using Pen pen = new Pen(_hover ? System.Drawing.Color.FromArgb(235, 240, 250) : AppSettings.Instance.Theme.TextColor, 1.6f)
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

	private readonly Label _lblName;

	private readonly Label _lblJob;

	private readonly FlowLayoutPanel _infoPanel;

	private readonly FlowLayoutPanel _statsPanel;

	private readonly VirtualListPanel _list;

	private readonly VirtualListPanel _buffList;

	private readonly SplitContainer _split;

	private readonly Panel _titleBar;

	private System.Drawing.Color _accentColor = System.Drawing.Color.FromArgb(100, 160, 220);

	private readonly List<SkillRow> _rows = new List<SkillRow>();

	private readonly List<DpsCanvas.SkillBar> _skillBars = new List<DpsCanvas.SkillBar>();

	private readonly List<BuffRow> _buffRows = new List<BuffRow>();

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

	public DpsDetailForm()
	{
		Text = "전투 상세";
		base.FormBorderStyle = FormBorderStyle.None;
		base.ShowInTaskbar = false;
		base.TopMost = true;
		MinimumSize = new System.Drawing.Size(700, 300);
		AppSettings instance = AppSettings.Instance;
		base.Size = new System.Drawing.Size(Math.Max(MinimumSize.Width, instance.DetailPanelWidth), Math.Max(MinimumSize.Height, instance.DetailPanelHeight));
		if (instance.DetailPanelX >= 0 && instance.DetailPanelY >= 0)
		{
			base.StartPosition = FormStartPosition.Manual;
			base.Location = new Point(instance.DetailPanelX, instance.DetailPanelY);
		}
		else
		{
			base.StartPosition = FormStartPosition.CenterScreen;
		}
		AppSettings.ThemeColors _t = instance.Theme;
		string fontName = instance.FontName;
		float fontSize = instance.FontSize;
		BackColor = _t.BgColor;
		ForeColor = _t.TextColor;
		Font = new Font(fontName, fontSize);
		base.Padding = new Padding(3);
		DoubleBuffered = true;
		_titleBar = new Panel
		{
			Dock = DockStyle.Top,
			Height = 36,
			BackColor = _t.HeaderColor
		};
		_titleBar.Paint += delegate(object? _, PaintEventArgs e)
		{
			using Pen pen = new Pen(_t.BorderColor);
			e.Graphics.DrawLine(pen, 0, _titleBar.Height - 1, _titleBar.Width, _titleBar.Height - 1);
		};
		_lblName = new Label
		{
			Text = "",
			Font = new Font(fontName, fontSize + 0.5f, FontStyle.Bold),
			ForeColor = _t.TextColor,
			AutoSize = true,
			Location = new Point(10, 9),
			BackColor = System.Drawing.Color.Transparent
		};
		_lblJob = new Label
		{
			Text = "",
			Font = new Font(fontName, fontSize),
			ForeColor = _t.TextDimColor,
			AutoSize = true,
			Location = new Point(200, 11),
			BackColor = System.Drawing.Color.Transparent
		};
		HeaderCloseButton btnClose = new HeaderCloseButton();
		_titleBar.Controls.Add(_lblName);
		_titleBar.Controls.Add(_lblJob);
		_titleBar.Controls.Add(btnClose);
		_titleBar.Resize += delegate
		{
			btnClose.Location = new Point(_titleBar.Width - btnClose.Width - 8, (_titleBar.Height - btnClose.Height) / 2);
		};
		btnClose.Click += delegate
		{
			Close();
		};
		_titleBar.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			Drag(e);
		};
		_lblName.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			Drag(e);
		};
		_lblJob.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			Drag(e);
		};
		_infoPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 34,
			BackColor = _t.BgColor,
			Padding = new Padding(8, 6, 8, 4),
			WrapContents = false
		};
		_statsPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 34,
			BackColor = _t.BgColor,
			Padding = new Padding(8, 2, 8, 6),
			WrapContents = false
		};
		_list = new VirtualListPanel
		{
			Dock = DockStyle.Fill,
			Font = new Font(fontName, fontSize),
			RowHeight = (int)(20f + fontSize),
			Columns = new(string, float, HorizontalAlignment)[14]
			{
				("스킬", 145f, HorizontalAlignment.Left),
				("특화", 50f, HorizontalAlignment.Center),
				("타수", 35f, HorizontalAlignment.Right),
				("치명타", 48f, HorizontalAlignment.Right),
				("후방", 48f, HorizontalAlignment.Right),
				("강타", 48f, HorizontalAlignment.Right),
				("완벽", 48f, HorizontalAlignment.Right),
				("다단", 48f, HorizontalAlignment.Right),
				("회피", 48f, HorizontalAlignment.Right),
				("막기", 48f, HorizontalAlignment.Right),
				("MAX", 60f, HorizontalAlignment.Right),
				("초당", 55f, HorizontalAlignment.Right),
				("평균", 55f, HorizontalAlignment.Right),
				("피해", 100f, HorizontalAlignment.Right)
			}
		};
		_list.PaintRow += OnPaintRow;
		_list.RowDoubleClicked += OnSkillDoubleClicked;
		_buffList = new VirtualListPanel
		{
			Dock = DockStyle.Fill,
			Font = new Font(fontName, fontSize),
			RowHeight = (int)(18f + fontSize),
			Columns = new(string, float, HorizontalAlignment)[2]
			{
				("버프", 180f, HorizontalAlignment.Left),
				("업타임", 70f, HorizontalAlignment.Right)
			}
		};
		_buffList.PaintRow += OnPaintBuffRow;
		_split = new SplitContainer
		{
			Dock = DockStyle.Fill,
			Orientation = Orientation.Horizontal,
			BackColor = _t.BorderColor,
			SplitterWidth = 4,
			FixedPanel = FixedPanel.Panel2
		};
		_split.Panel1.BackColor = _t.BgColor;
		_split.Panel2.BackColor = _t.BgColor;
		_split.Panel1.Controls.Add(_list);
		_split.Panel2.Controls.Add(_buffList);
		base.Controls.Add(_split);
		base.Controls.Add(_statsPanel);
		base.Controls.Add(_infoPanel);
		base.Controls.Add(_titleBar);
		base.Load += delegate
		{
			_split.SplitterDistance = Math.Max(100, _split.Height - 120);
		};
		SkillIconCache.Instance.IconReady += delegate
		{
			if (base.IsHandleCreated && !base.IsDisposed)
			{
				BeginInvoke(delegate
				{
					_list.Invalidate();
				});
			}
		};
	}

	public void SetData(DpsCanvas.PlayerRow row)
	{
		_lblName.Text = row.Name;
		_lblJob.Text = row.JobIconKey;
		_accentColor = ToGdi(row.AccentColor);
		using (Graphics graphics = CreateGraphics())
		{
			SizeF sizeF = graphics.MeasureString(row.Name, _lblName.Font);
			_lblJob.Location = new Point((int)(16f + sizeF.Width), 11);
		}
		double num = ((row.DpsValue > 0) ? ((double)row.Damage / (double)row.DpsValue) : 0.0);
		_infoPanel.Controls.Clear();
		if (row.CombatPower > 0)
		{
			_infoPanel.Controls.Add(Badge($"전투력 {row.CombatPower:N0}"));
		}
		IReadOnlyList<DpsCanvas.SkillBar> skills = row.Skills;
		if (skills != null && skills.Count > 0)
		{
			_infoPanel.Controls.Add(Badge($"스킬 {row.Skills.Count}개"));
		}
		_statsPanel.Controls.Clear();
		_statsPanel.Controls.Add(Badge($"누적피해 {row.Damage:N0}"));
		_statsPanel.Controls.Add(Badge($"치명타 {row.CritRate * 100.0:0.#}%"));
		if (row.DpsValue > 0)
		{
			_statsPanel.Controls.Add(Badge($"DPS {row.DpsValue:N0}"));
		}
		if (row.HealTotal > 0)
		{
			_statsPanel.Controls.Add(Badge($"힐 {row.HealTotal:N0}"));
		}
		if (row.DotDamage > 0 && row.Damage > 0)
		{
			_statsPanel.Controls.Add(Badge($"DoT {(double)row.DotDamage / (double)row.Damage * 100.0:0.#}%"));
		}
		_rows.Clear();
		_skillBars.Clear();
		skills = row.Skills;
		if (skills != null && skills.Count > 0)
		{
			Dictionary<string, int> dictionary = row.SkillLevels;
			if (dictionary == null || dictionary.Count <= 0)
			{
				string text = row.Name;
				int num2 = text.IndexOf('[');
				if (num2 > 0)
				{
					text = text.Substring(0, num2);
				}
				dictionary = SkillLevelCache.Instance.Get(text, row.ServerId)?.SkillLevels;
			}
			foreach (DpsCanvas.SkillBar skill in row.Skills)
			{
				long num3 = ((skill.Hits > 0) ? (skill.Total / skill.Hits) : 0);
				long num4 = ((num > 0.0) ? ((long)((double)skill.Total / num)) : 0);
				string name = skill.Name;
				if (dictionary != null && dictionary.TryGetValue(skill.Name, out var value) && value > 0)
				{
					name = $"{skill.Name} Lv{value}";
				}
				_rows.Add(new SkillRow(name, skill.Name, skill.Specs, $"{skill.Hits}", Pct(skill.CritRate), Pct(skill.BackRate), Pct(skill.StrongRate), Pct(skill.PerfectRate), Pct(skill.MultiHitRate), Pct(skill.DodgeRate), Pct(skill.BlockRate), (skill.MaxHit > 0) ? $"{skill.MaxHit:N0}" : "-", (num4 > 0) ? $"{num4:N0}" : "-", (num3 > 0) ? $"{num3:N0}" : "-", $"{skill.Total:N0} ({skill.PercentOfActor * 100.0:0.#}%)", skill.PercentOfActor));
				_skillBars.Add(skill);
			}
		}
		_list.RowCount = _rows.Count;
		_buffRows.Clear();
		IReadOnlyList<BuffUptime> buffs = row.Buffs;
		if (buffs != null && buffs.Count > 0)
		{
			foreach (BuffUptime buff in row.Buffs)
			{
				_buffRows.Add(new BuffRow(buff.Name, $"{buff.Uptime * 100.0:0.#}%", buff.Uptime));
			}
		}
		_buffList.RowCount = _buffRows.Count;
	}

	private void OnPaintRow(Graphics g, Rectangle rowRect, Rectangle[] cells, int idx, bool sel)
	{
		if (idx < 0 || idx >= _rows.Count || cells.Length < 14)
		{
			return;
		}
		SkillRow skillRow = _rows[idx];
		AppSettings.ThemeColors theme = AppSettings.Instance.Theme;
		System.Drawing.Color fg = (sel ? System.Drawing.Color.White : theme.TextColor);
		double num = ((_rows.Count > 0) ? _rows[0].Percent : 1.0);
		if (num <= 0.0)
		{
			num = 1.0;
		}
		double num2 = skillRow.Percent / num;
		if (num2 > 0.0)
		{
			int num3 = (int)((double)cells[0].Width * num2);
			if (num3 > 0)
			{
				Rectangle rect = new Rectangle(cells[0].X, cells[0].Y + 2, num3, cells[0].Height - 4);
				using SolidBrush brush = new SolidBrush(System.Drawing.Color.FromArgb(80, _accentColor));
				g.FillRectangle(brush, rect);
			}
		}
		int num4 = cells[0].Height - 4;
		int num5 = 0;
		Image image = SkillIconCache.Instance.Get(skillRow.IconKey);
		if (image != null)
		{
			int y = cells[0].Y + (cells[0].Height - num4) / 2;
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			g.DrawImage(image, cells[0].X + 3, y, num4, num4);
			num5 = num4 + 3;
		}
		Rectangle cell = new Rectangle(cells[0].X + num5, cells[0].Y, cells[0].Width - num5, cells[0].Height);
		DrawCell(g, cell, skillRow.Name, fg, TextFormatFlags.Default);
		PaintSpecBoxes(g, cells[1], skillRow.Specs);
		string[] array = new string[12]
		{
			skillRow.Hits, skillRow.Crit, skillRow.Back, skillRow.Strong, skillRow.Perfect, skillRow.Multi, skillRow.Dodge, skillRow.Block, skillRow.Max, skillRow.Dps,
			skillRow.Avg, skillRow.Damage
		};
		for (int i = 0; i < array.Length; i++)
		{
			DrawCell(g, cells[i + 2], array[i], fg, TextFormatFlags.Right);
		}
	}

	private void OnSkillDoubleClicked(int idx)
	{
		if (idx >= 0 && idx < _skillBars.Count)
		{
			DpsCanvas.SkillBar skillBar = _skillBars[idx];
			IReadOnlyList<long> hitLog = skillBar.HitLog;
			if (hitLog != null && hitLog.Count > 0)
			{
				new SkillHitDetailForm(skillBar, _accentColor).Show(this);
			}
		}
	}

	private void OnPaintBuffRow(Graphics g, Rectangle rowRect, Rectangle[] cells, int idx, bool sel)
	{
		if (idx < 0 || idx >= _buffRows.Count || cells.Length < 2)
		{
			return;
		}
		BuffRow buffRow = _buffRows[idx];
		AppSettings.ThemeColors theme = AppSettings.Instance.Theme;
		System.Drawing.Color fg = (sel ? System.Drawing.Color.White : theme.TextColor);
		int num = (int)((double)cells[0].Width * buffRow.Percent);
		if (num > 0)
		{
			Rectangle rect = new Rectangle(cells[0].X, cells[0].Y + 2, num, cells[0].Height - 4);
			using SolidBrush brush = new SolidBrush(System.Drawing.Color.FromArgb(60, _accentColor));
			g.FillRectangle(brush, rect);
		}
		DrawCell(g, cells[0], buffRow.Name, fg, TextFormatFlags.Default);
		DrawCell(g, cells[1], buffRow.Uptime, fg, TextFormatFlags.Right);
	}

	private void DrawCell(Graphics g, Rectangle cell, string text, System.Drawing.Color fg, TextFormatFlags align)
	{
		TextRenderer.DrawText(bounds: new Rectangle(cell.X + 3, cell.Y, cell.Width - 6, cell.Height), dc: g, text: text, font: _list.Font, foreColor: fg, flags: align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
	}

	private static void PaintSpecBoxes(Graphics g, Rectangle cell, int[]? specs)
	{
		int num = 43;
		int num2 = cell.X + (cell.Width - num) / 2;
		int y = cell.Y + (cell.Height - 7) / 2;
		using SolidBrush solidBrush = new SolidBrush(System.Drawing.Color.FromArgb(60, 70, 90));
		using SolidBrush solidBrush2 = new SolidBrush(System.Drawing.Color.FromArgb(46, 204, 113));
		using Pen pen = new Pen(System.Drawing.Color.FromArgb(80, 100, 130), 1f);
		using Pen pen2 = new Pen(System.Drawing.Color.FromArgb(46, 204, 113), 1f);
		for (int i = 1; i <= 5; i++)
		{
			int x = num2 + (i - 1) * 9;
			Rectangle rect = new Rectangle(x, y, 7, 7);
			bool flag = specs != null && Array.IndexOf(specs, i) >= 0;
			g.FillRectangle(flag ? solidBrush2 : solidBrush, rect);
			g.DrawRectangle(flag ? pen2 : pen, rect);
		}
	}

	private static System.Drawing.Color ToGdi(Color4 c)
	{
		return System.Drawing.Color.FromArgb((int)(Math.Clamp(c.A, 0f, 1f) * 255f), (int)(Math.Clamp(c.R, 0f, 1f) * 255f), (int)(Math.Clamp(c.G, 0f, 1f) * 255f), (int)(Math.Clamp(c.B, 0f, 1f) * 255f));
	}

	private static string Pct(double rate)
	{
		return $"{rate * 100.0:0.#}%";
	}

	private static Label Badge(string text)
	{
		return new Label
		{
			Text = text,
			Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f),
			ForeColor = AppSettings.Instance.Theme.TextColor,
			BackColor = System.Drawing.Color.FromArgb(20, 35, 50),
			AutoSize = true,
			Padding = new Padding(6, 3, 6, 3),
			Margin = new Padding(3, 0, 3, 0)
		};
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
			instance.DetailPanelX = base.Location.X;
			instance.DetailPanelY = base.Location.Y;
			instance.DetailPanelWidth = base.Size.Width;
			instance.DetailPanelHeight = base.Size.Height;
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
