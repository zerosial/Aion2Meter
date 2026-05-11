using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;

namespace A2Meter.Forms;

internal sealed class UpdateDetailForm : Form
{
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
				graphics.FillEllipse(brush, 0, 0, base.Width, base.Height);
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
	}

	private readonly Version _version;

	private readonly string _downloadUrl;

	private readonly Button _btnDownload;

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

	public UpdateDetailForm(Version version, string downloadUrl, string releaseNotes)
	{
		_version = version;
		_downloadUrl = downloadUrl;
		AppSettings.ThemeColors theme = AppSettings.Instance.Theme;
		string fontName = AppSettings.Instance.FontName;
		float fontSize = AppSettings.Instance.FontSize;
		Text = "업데이트";
		base.FormBorderStyle = FormBorderStyle.None;
		base.ShowInTaskbar = false;
		base.TopMost = true;
		base.StartPosition = FormStartPosition.CenterScreen;
		base.Size = new Size(420, 360);
		BackColor = theme.BgColor;
		DoubleBuffered = true;
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
		Label label = new Label
		{
			Text = $"v{version} 업데이트",
			ForeColor = theme.TextColor,
			Font = new Font(fontName, fontSize + 0.5f, FontStyle.Bold),
			AutoSize = true,
			Location = new Point(12, 9),
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
		CloseButton btnClose = new CloseButton
		{
			Location = new Point(titleBar.Width - 34, 5)
		};
		titleBar.Controls.Add(btnClose);
		titleBar.Resize += delegate
		{
			btnClose.Location = new Point(titleBar.Width - 34, 5);
		};
		btnClose.Click += delegate
		{
			Close();
		};
		Label value = new Label
		{
			Text = $"현재: v{AutoUpdater.CurrentVersion}  →  새 버전: v{version}",
			ForeColor = theme.AccentColor,
			Font = new Font(fontName, fontSize, FontStyle.Bold),
			AutoSize = true,
			Location = new Point(16, 48),
			BackColor = Color.Transparent
		};
		Label value2 = new Label
		{
			Text = "릴리즈 노트",
			ForeColor = theme.TextDimColor,
			Font = new Font(fontName, fontSize - 0.5f, FontStyle.Bold),
			AutoSize = true,
			Location = new Point(16, 76),
			BackColor = Color.Transparent
		};
		RichTextBox txtNotes = new RichTextBox
		{
			Text = (string.IsNullOrWhiteSpace(releaseNotes) ? "(릴리즈 노트 없음)" : releaseNotes),
			ReadOnly = true,
			BorderStyle = BorderStyle.None,
			BackColor = theme.HeaderColor,
			ForeColor = theme.TextColor,
			Font = new Font(fontName, fontSize),
			Location = new Point(16, 98),
			Size = new Size(388, 190),
			ScrollBars = RichTextBoxScrollBars.Vertical
		};
		_btnDownload = new Button
		{
			Text = "다운로드 및 업데이트",
			FlatStyle = FlatStyle.Flat,
			BackColor = theme.AccentColor,
			ForeColor = Color.FromArgb(20, 24, 36),
			Font = new Font(fontName, fontSize + 0.5f, FontStyle.Bold),
			Size = new Size(200, 34),
			Location = new Point(110, 310),
			Cursor = Cursors.Hand
		};
		_btnDownload.FlatAppearance.BorderSize = 0;
		_btnDownload.Click += OnDownloadClick;
		base.Controls.Add(_btnDownload);
		base.Controls.Add(txtNotes);
		base.Controls.Add(value2);
		base.Controls.Add(value);
		base.Controls.Add(titleBar);
		base.Resize += delegate
		{
			txtNotes.Size = new Size(base.ClientSize.Width - 32, base.ClientSize.Height - 170);
			_btnDownload.Location = new Point((base.ClientSize.Width - _btnDownload.Width) / 2, base.ClientSize.Height - 50);
		};
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		using Pen pen = new Pen(AppSettings.Instance.Theme.BorderColor);
		e.Graphics.DrawRectangle(pen, 0, 0, base.Width - 1, base.Height - 1);
	}

	private void Drag(MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left)
		{
			ReleaseCapture();
			SendMessage(base.Handle, 161, 2, IntPtr.Zero);
		}
	}

	private async void OnDownloadClick(object? sender, EventArgs e)
	{
		try
		{
			_btnDownload.Enabled = false;
			_btnDownload.Text = "다운로드 중...";
			await AutoUpdater.ApplyAsync(_downloadUrl, _version, delegate(string msg)
			{
				Console.Error.WriteLine(msg);
			});
			AppSettings.Instance.Save();
			Environment.Exit(0);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("[updater] apply failed: " + ex.Message);
			_btnDownload.Text = "실패 — 다시 시도";
			_btnDownload.Enabled = true;
		}
	}
}
