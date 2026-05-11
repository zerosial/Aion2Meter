using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Direct2D;

namespace A2Meter.Forms;

internal sealed class SkillHitDetailForm : Form
{
	private readonly VirtualListPanel _list;

	private readonly IReadOnlyList<long> _hits;

	private readonly long _maxHit;

	private readonly Color _accent;

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

	public SkillHitDetailForm(DpsCanvas.SkillBar skill, Color accent)
	{
		IReadOnlyList<long> readOnlyList = (_hits = skill.HitLog ?? Array.Empty<long>());
		_accent = accent;
		long num = 0L;
		long num2 = long.MaxValue;
		long num3 = 0L;
		foreach (long item in readOnlyList)
		{
			if (item > num)
			{
				num = item;
			}
			if (item < num2)
			{
				num2 = item;
			}
			num3 += item;
		}
		if (readOnlyList.Count == 0)
		{
			num2 = 0L;
		}
		_maxHit = ((num > 0) ? num : 1);
		AppSettings instance = AppSettings.Instance;
		AppSettings.ThemeColors theme = instance.Theme;
		string fontName = instance.FontName;
		float fontSize = instance.FontSize;
		Text = skill.Name + " - 피해 히스토리";
		base.FormBorderStyle = FormBorderStyle.None;
		base.ShowInTaskbar = false;
		base.TopMost = true;
		base.Size = new Size(340, 400);
		base.StartPosition = FormStartPosition.CenterParent;
		BackColor = theme.BgColor;
		ForeColor = theme.TextColor;
		Font = new Font(fontName, fontSize);
		DoubleBuffered = true;
		Panel titleBar = new Panel
		{
			Dock = DockStyle.Top,
			Height = 32,
			BackColor = theme.HeaderColor
		};
		Label label = new Label
		{
			Text = skill.Name,
			Font = new Font(fontName, fontSize + 0.5f, FontStyle.Bold),
			ForeColor = theme.TextColor,
			AutoSize = true,
			Location = new Point(10, 7),
			BackColor = Color.Transparent
		};
		Label btnClose = new Label
		{
			Text = "✕",
			Font = new Font(fontName, fontSize),
			ForeColor = theme.TextDimColor,
			AutoSize = true,
			Cursor = Cursors.Hand,
			BackColor = Color.Transparent
		};
		titleBar.Controls.Add(label);
		titleBar.Controls.Add(btnClose);
		titleBar.Resize += delegate
		{
			btnClose.Location = new Point(titleBar.Width - btnClose.Width - 10, 7);
		};
		btnClose.Click += delegate
		{
			Close();
		};
		titleBar.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			Drag(e);
		};
		label.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			Drag(e);
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 30,
			BackColor = theme.BgColor,
			Padding = new Padding(8, 4, 8, 4),
			WrapContents = false
		};
		long value = ((readOnlyList.Count > 0) ? (num3 / readOnlyList.Count) : 0);
		flowLayoutPanel.Controls.Add(Badge($"타수 {readOnlyList.Count}"));
		flowLayoutPanel.Controls.Add(Badge($"평균 {value:N0}"));
		flowLayoutPanel.Controls.Add(Badge($"최대 {num:N0}"));
		flowLayoutPanel.Controls.Add(Badge($"최소 {num2:N0}"));
		_list = new VirtualListPanel
		{
			Dock = DockStyle.Fill,
			Font = new Font(fontName, fontSize),
			RowHeight = (int)(18f + fontSize),
			Columns = new(string, float, HorizontalAlignment)[2]
			{
				("#", 30f, HorizontalAlignment.Right),
				("피해량", 100f, HorizontalAlignment.Right)
			}
		};
		_list.RowCount = readOnlyList.Count;
		_list.PaintRow += OnPaintRow;
		base.Controls.Add(_list);
		base.Controls.Add(flowLayoutPanel);
		base.Controls.Add(titleBar);
	}

	private void OnPaintRow(Graphics g, Rectangle rowRect, Rectangle[] cells, int idx, bool sel)
	{
		if (idx < 0 || idx >= _hits.Count || cells.Length < 2)
		{
			return;
		}
		AppSettings.ThemeColors theme = AppSettings.Instance.Theme;
		Color foreColor = (sel ? Color.White : theme.TextColor);
		long num = _hits[idx];
		double num2 = (double)num / (double)_maxHit;
		if (num2 > 0.0)
		{
			int num3 = (int)((double)cells[1].Width * num2);
			if (num3 > 0)
			{
				Rectangle rect = new Rectangle(cells[1].X, cells[1].Y + 2, num3, cells[1].Height - 4);
				using SolidBrush brush = new SolidBrush(Color.FromArgb(70, _accent));
				g.FillRectangle(brush, rect);
			}
		}
		TextRenderer.DrawText(bounds: new Rectangle(cells[0].X + 3, cells[0].Y, cells[0].Width - 6, cells[0].Height), dc: g, text: $"{idx + 1}", font: _list.Font, foreColor: theme.TextDimColor, flags: TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
		TextRenderer.DrawText(bounds: new Rectangle(cells[1].X + 3, cells[1].Y, cells[1].Width - 6, cells[1].Height), dc: g, text: $"{num:N0}", font: _list.Font, foreColor: foreColor, flags: TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
	}

	private static Label Badge(string text)
	{
		return new Label
		{
			Text = text,
			Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f),
			ForeColor = AppSettings.Instance.Theme.TextColor,
			BackColor = Color.FromArgb(20, 35, 50),
			AutoSize = true,
			Padding = new Padding(4, 2, 4, 2),
			Margin = new Padding(2, 0, 2, 0)
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

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		using Pen pen = new Pen(AppSettings.Instance.Theme.BorderColor);
		e.Graphics.DrawRectangle(pen, 0, 0, base.Width - 1, base.Height - 1);
	}

	protected override void OnDeactivate(EventArgs e)
	{
		base.OnDeactivate(e);
		Close();
	}
}
