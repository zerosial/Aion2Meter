using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Dps;
using A2Meter.Dps.Protocol;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace A2Meter.Direct2D;

internal sealed class DpsCanvas : Control
{
	public sealed record PlayerRow(string Name, string JobIconKey, long Damage, double Percent, long DpsValue, double CritRate, long HealTotal, Color4 AccentColor, IReadOnlyList<SkillBar>? Skills = null, int CombatPower = 0, int CombatScore = 0, long PeakDps = 0L, long AvgDps = 0L, long DotDamage = 0L, int ServerId = 0, string ServerName = "", Dictionary<string, int>? SkillLevels = null, IReadOnlyList<BuffUptime>? Buffs = null);

	public sealed record SkillBar(string Name, long Total, long Hits, double CritRate, double PercentOfActor, double BackRate = 0.0, double StrongRate = 0.0, double PerfectRate = 0.0, double MultiHitRate = 0.0, double DodgeRate = 0.0, double BlockRate = 0.0, long MaxHit = 0L, int[]? Specs = null, IReadOnlyList<long>? HitLog = null);

	public sealed record TargetInfo(string Name, long CurrentHp, long MaxHp);

	public sealed record SessionSummary(double DurationSec, long TotalDamage, long AverageDps, long PeakDps, string TopActorName, long TopActorDamage, string? BossName);

	private const float PadX = 10f;

	private const float PadBottom = 6f;

	private const float HeaderHeight = 28f;

	private const float TargetBarH = 20f;

	private const float RowGap = 3f;

	private const float BarRadius = 5f;

	private const float IconSize = 22f;

	private D2DContext? _ctx;

	private D2DFontProvider? _fonts;

	private JobIconAtlas? _icons;

	private ID2D1SolidColorBrush? _brushBarBg;

	private ID2D1SolidColorBrush? _brushBarFill;

	private ID2D1SolidColorBrush? _brushBarBorder;

	private ID2D1SolidColorBrush? _brushText;

	private ID2D1SolidColorBrush? _brushTextBright;

	private ID2D1SolidColorBrush? _brushTextDim;

	private ID2D1SolidColorBrush? _brushGold;

	private ID2D1SolidColorBrush? _brushAccent;

	private IDWriteTextFormat? _fontName;

	private IDWriteTextFormat? _fontNumber;

	private IDWriteTextFormat? _fontSmall;

	private IDWriteTextFormat? _fontCpScore;

	private IDWriteTextFormat? _fontTotal;

	private readonly List<PlayerRow> _rows = new List<PlayerRow>();

	private readonly List<(float Top, float Bottom, int Index)> _rowHitAreas = new List<(float, float, int)>();

	private string _timerText = "0:00";

	private long _totalDamage;

	private TargetInfo? _target;

	private SessionSummary? _summary;

	private ID2D1SolidColorBrush? _brushHpBg;

	private ID2D1SolidColorBrush? _brushHpFill;

	private ID2D1SolidColorBrush? _brushNameElyos;

	private ID2D1SolidColorBrush? _brushNameAsmo;

	private readonly Queue<(string Text, DateTime Expires)> _toasts = new Queue<(string, DateTime)>();

	private const double ToastDurationSec = 3.0;

	private const int EdgeMargin = 6;

	private const int WM_NCLBUTTONDOWN = 161;

	private float RowH => 30f * (float)AppSettings.Instance.RowHeight / 90f;

	public int CountdownSec { get; set; }

	public bool CountdownExpired { get; set; }

	public bool AnonymousMode { get; set; }

	public int PingMs { get; set; }

	public bool CompactMode { get; set; }

	public event Action<PlayerRow>? PlayerRowClicked;

	public event Action? CountdownClicked;

	public void ShowToast(string message)
	{
		lock (_toasts)
		{
			_toasts.Enqueue((message, DateTime.UtcNow.AddSeconds(3.0)));
			while (_toasts.Count > 4)
			{
				_toasts.Dequeue();
			}
		}
		Invalidate();
	}

	public DpsCanvas()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint, value: true);
		DoubleBuffered = false;
		BackColor = AppSettings.Instance.Theme.BgColor;
		base.MouseMove += OnEdgeMouseMove;
		base.MouseDown += OnEdgeMouseDown;
		base.MouseClick += OnPlayerRowClick;
	}

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	private static extern nint SendMessage(nint hWnd, int Msg, nint wParam, nint lParam);

	private static int HitFromEdges(Point pt, int w, int h)
	{
		bool flag = pt.X < 6;
		bool flag2 = pt.X >= w - 6;
		bool flag3 = pt.Y < 6;
		bool flag4 = pt.Y >= h - 6;
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
		return 0;
	}

	private void OnEdgeMouseMove(object? sender, MouseEventArgs e)
	{
		Cursor cursor;
		switch (HitFromEdges(e.Location, base.ClientSize.Width, base.ClientSize.Height))
		{
		case 10:
		case 11:
			cursor = Cursors.SizeWE;
			break;
		case 12:
		case 15:
			cursor = Cursors.SizeNS;
			break;
		case 13:
		case 17:
			cursor = Cursors.SizeNWSE;
			break;
		case 14:
		case 16:
			cursor = Cursors.SizeNESW;
			break;
		default:
			cursor = Cursors.Default;
			break;
		}
		Cursor = cursor;
	}

	private void OnEdgeMouseDown(object? sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Left)
		{
			return;
		}
		int num = HitFromEdges(e.Location, base.ClientSize.Width, base.ClientSize.Height);
		if (num != 0)
		{
			Form form = FindForm();
			if (form != null)
			{
				ReleaseCapture();
				SendMessage(form.Handle, 161, num, IntPtr.Zero);
			}
		}
	}

	public void SetData(IReadOnlyList<PlayerRow> rows, long totalDamage, string timerText, MobTarget? target = null, SessionSummary? summary = null)
	{
		_rows.Clear();
		_rows.AddRange(rows);
		_totalDamage = totalDamage;
		_timerText = timerText;
		_target = ((target != null && target.IsBoss && target.MaxHp > 0) ? new TargetInfo(target.Name, target.CurrentHp, target.MaxHp) : null);
		_summary = summary;
		Invalidate();
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		InitD2D();
	}

	protected override void OnHandleDestroyed(EventArgs e)
	{
		DisposeD2D();
		base.OnHandleDestroyed(e);
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		_ctx?.Resize(Math.Max(1, base.ClientSize.Width), Math.Max(1, base.ClientSize.Height));
		Invalidate();
	}

	private void InitD2D()
	{
		if (!base.DesignMode && base.Width > 0 && base.Height > 0)
		{
			bool forceWarp = string.Equals(AppSettings.Instance.GpuMode, "off", StringComparison.OrdinalIgnoreCase);
			_ctx = new D2DContext(base.Handle, base.ClientSize.Width, base.ClientSize.Height, forceWarp);
			ID2D1DeviceContext dC = _ctx.DC;
			_brushBarBg = dC.CreateSolidColorBrush(new Color4(0.078f, 0.098f, 0.157f, 0.6f));
			_brushBarFill = dC.CreateSolidColorBrush(new Color4(0.44f, 0.78f, 1f));
			_brushBarBorder = dC.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 0.07f));
			_brushText = dC.CreateSolidColorBrush(new Color4(0.93f, 0.95f, 1f));
			_brushTextBright = dC.CreateSolidColorBrush(new Color4(1f, 1f, 1f));
			_brushTextDim = dC.CreateSolidColorBrush(new Color4(0.635f, 0.694f, 0.784f));
			_brushGold = dC.CreateSolidColorBrush(new Color4(1f, 0.82f, 0.4f));
			_brushAccent = dC.CreateSolidColorBrush(new Color4(0.455f, 0.753f, 0.988f));
			_brushHpBg = dC.CreateSolidColorBrush(new Color4(0.2f, 0.07f, 0.1f));
			_brushHpFill = dC.CreateSolidColorBrush(new Color4(0.85f, 0.2f, 0.25f));
			_brushNameElyos = dC.CreateSolidColorBrush(new Color4(0.55f, 0.82f, 1f));
			_brushNameAsmo = dC.CreateSolidColorBrush(new Color4(0.76f, 0.65f, 1f));
			ApplyThemeBrushes();
			_fonts = new D2DFontProvider(_ctx.DWriteFactory);
			RebuildFonts();
			_icons = new JobIconAtlas(dC);
		}
	}

	private static Color4 ColorToD2D(System.Drawing.Color c)
	{
		return new Color4((float)(int)c.R / 255f, (float)(int)c.G / 255f, (float)(int)c.B / 255f);
	}

	private void ApplyThemeBrushes()
	{
		AppSettings.ThemeColors theme = AppSettings.Instance.Theme;
		if (_brushBarBg != null)
		{
			_brushBarBg.Color = ColorToD2D(theme.HeaderColor);
		}
		if (_brushText != null)
		{
			_brushText.Color = ColorToD2D(theme.TextColor);
		}
		if (_brushTextDim != null)
		{
			_brushTextDim.Color = ColorToD2D(theme.TextDimColor);
		}
		if (_brushAccent != null)
		{
			_brushAccent.Color = ColorToD2D(theme.AccentColor);
		}
	}

	public void ApplySettings()
	{
		if (_fonts != null)
		{
			DisposeFonts();
			RebuildFonts();
			ApplyThemeBrushes();
			BackColor = AppSettings.Instance.Theme.BgColor;
			Invalidate();
		}
	}

	private void RebuildFonts()
	{
		if (_fonts != null)
		{
			AppSettings instance = AppSettings.Instance;
			float num = instance.FontSize * (float)instance.FontScale / 100f;
			_fontName = _fonts.CreateUi(num + 4f);
			_fontNumber = _fonts.CreateUi(num + 4f);
			_fontSmall = _fonts.CreateUi(num + 1f);
			_fontCpScore = _fonts.CreateUi(num);
			_fontTotal = _fonts.CreateUi(num + 6f);
		}
	}

	private void DisposeFonts()
	{
		_fontTotal?.Dispose();
		_fontTotal = null;
		_fontCpScore?.Dispose();
		_fontCpScore = null;
		_fontSmall?.Dispose();
		_fontSmall = null;
		_fontNumber?.Dispose();
		_fontNumber = null;
		_fontName?.Dispose();
		_fontName = null;
	}

	private void DisposeD2D()
	{
		_icons?.Dispose();
		_fontTotal?.Dispose();
		_fontCpScore?.Dispose();
		_fontSmall?.Dispose();
		_fontNumber?.Dispose();
		_fontName?.Dispose();
		_brushNameElyos?.Dispose();
		_brushNameAsmo?.Dispose();
		_brushHpFill?.Dispose();
		_brushHpBg?.Dispose();
		_brushAccent?.Dispose();
		_brushGold?.Dispose();
		_brushTextDim?.Dispose();
		_brushTextBright?.Dispose();
		_brushText?.Dispose();
		_brushBarBorder?.Dispose();
		_brushBarFill?.Dispose();
		_brushBarBg?.Dispose();
		_ctx?.Dispose();
		_ctx = null;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		if (_ctx == null)
		{
			base.OnPaint(e);
			return;
		}
		ID2D1DeviceContext dC = _ctx.DC;
		dC.BeginDraw();
		dC.Clear(ColorToD2D(AppSettings.Instance.Theme.BgColor));
		float y = 6f;
		if (!CompactMode)
		{
			y = DrawHeader(dC, y);
		}
		y = DrawTargetBar(dC, y);
		DrawRows(dC, y);
		dC.EndDraw();
		_ctx.Present();
	}

	private float DrawHeader(ID2D1DeviceContext dc, float y)
	{
		float num = (float)base.ClientSize.Width - 20f;
		dc.DrawText(_timerText, _fontTotal, new Rect(10f, y, 60f, 28f), _brushText, DrawTextOptions.None, MeasuringMode.Natural);
		string text = ((CountdownSec <= 0) ? "⏱off" : (CountdownExpired ? $"⏱{CountdownSec}s ■" : $"⏱{CountdownSec}s"));
		ID2D1SolidColorBrush defaultForegroundBrush = (CountdownExpired ? _brushGold : _brushTextDim);
		dc.DrawText(text, _fontSmall, new Rect(66f, y + 2f, 80f, 26f), defaultForegroundBrush, DrawTextOptions.None, MeasuringMode.Natural);
		AppSettings instance = AppSettings.Instance;
		Color4 color = (instance.ShowCombatPower ? new Color4(0.39f, 0.71f, 1f) : new Color4(0.35f, 0.35f, 0.4f));
		_brushBarFill.Color = color;
		dc.DrawText("전투력", _fontSmall, new Rect(150f, y + 2f, 56f, 26f), _brushBarFill, DrawTextOptions.None, MeasuringMode.Natural);
		Color4 color2 = (instance.ShowCombatScore ? new Color4(0.91f, 0.78f, 0.3f) : new Color4(0.35f, 0.35f, 0.4f));
		_brushBarFill.Color = color2;
		dc.DrawText("아툴", _fontSmall, new Rect(206f, y + 2f, 50f, 26f), _brushBarFill, DrawTextOptions.None, MeasuringMode.Natural);
		IDWriteTextLayout iDWriteTextLayout = _ctx.DWriteFactory.CreateTextLayout(FormatDamage(_totalDamage), _fontTotal, num * 0.45f, 28f);
		iDWriteTextLayout.TextAlignment = TextAlignment.Trailing;
		dc.DrawTextLayout(new Vector2(10f + num * 0.55f, y), iDWriteTextLayout, _brushAccent);
		iDWriteTextLayout.Dispose();
		if (PingMs > 0)
		{
			string text2 = $"{PingMs}ms";
			float num2 = 256f;
			float num3 = 10f + num * 0.55f - num2;
			if (num3 > 40f)
			{
				dc.DrawText(text2, _fontSmall, new Rect(num2, y + 2f, num3, 26f), _brushTextDim, DrawTextOptions.None, MeasuringMode.Natural);
			}
		}
		return y + 28f + 6f;
	}

	private float DrawTargetBar(ID2D1DeviceContext dc, float y)
	{
		if ((object)_target == null)
		{
			return y;
		}
		float num = (float)base.ClientSize.Width - 20f;
		Rect value = new Rect(10f, y, num, 20f);
		dc.FillRoundedRectangle(new RoundedRectangle
		{
			Rect = value,
			RadiusX = 3f,
			RadiusY = 3f
		}, _brushHpBg);
		double num2 = ((_target.MaxHp > 0) ? ((double)_target.CurrentHp / (double)_target.MaxHp) : 0.0);
		float num3 = (float)Math.Clamp(num2, 0.0, 1.0) * num;
		if (num3 > 1f)
		{
			dc.FillRoundedRectangle(new RoundedRectangle
			{
				Rect = new Rect(10f, y, num3, 20f),
				RadiusX = 3f,
				RadiusY = 3f
			}, _brushHpFill);
		}
		float x = 10f + num / 3f;
		float x2 = 10f + num * 2f / 3f;
		dc.DrawLine(new Vector2(x, y + 2f), new Vector2(x, y + 20f - 2f), _brushBarBorder, 1f);
		dc.DrawLine(new Vector2(x2, y + 2f), new Vector2(x2, y + 20f - 2f), _brushBarBorder, 1f);
		string text = $"{_target.Name}   {FormatDamage(_target.CurrentHp)} / {FormatDamage(_target.MaxHp)}   {num2 * 100.0:0.#}%";
		IDWriteTextLayout iDWriteTextLayout = _ctx.DWriteFactory.CreateTextLayout(text, _fontSmall, num, 20f);
		iDWriteTextLayout.TextAlignment = TextAlignment.Center;
		iDWriteTextLayout.ParagraphAlignment = ParagraphAlignment.Center;
		dc.DrawTextLayout(new Vector2(10f, y), iDWriteTextLayout, _brushText);
		iDWriteTextLayout.Dispose();
		return y + 20f + 6f;
	}

	private void DrawRows(ID2D1DeviceContext dc, float startY)
	{
		float num = startY;
		float num2 = (float)base.ClientSize.Width - 20f;
		float num3 = 40f;
		float num4 = num2 - 30f;
		float num5 = (float)base.ClientSize.Height - 6f;
		long num6 = ((_rows.Count > 0) ? _rows[0].Damage : 1);
		if (num6 <= 0)
		{
			num6 = 1L;
		}
		_rowHitAreas.Clear();
		int num7 = 0;
		foreach (PlayerRow row in _rows)
		{
			if (num + RowH > num5)
			{
				break;
			}
			float num8 = num + (RowH - 22f) / 2f;
			ID2D1Bitmap1 iD2D1Bitmap = _icons?.Get(row.JobIconKey);
			if (iD2D1Bitmap != null)
			{
				Matrix3x2 transform = dc.Transform;
				float scale = 22f / iD2D1Bitmap.Size.Width;
				dc.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(10f, num8);
				dc.DrawImage(iD2D1Bitmap, null, null, InterpolationMode.Linear, CompositeMode.SourceOver);
				dc.Transform = transform;
			}
			else
			{
				_brushAccent.Color = row.AccentColor;
				dc.FillEllipse(new Ellipse(new Vector2(21f, num8 + 11f), 9f, 9f), _brushAccent);
			}
			RoundedRectangle roundedRect = new RoundedRectangle
			{
				Rect = new Rect(num3, num, num4, RowH),
				RadiusX = 5f,
				RadiusY = 5f
			};
			dc.FillRoundedRectangle(roundedRect, _brushBarBg);
			float num9 = (float)Math.Clamp((double)row.Damage / (double)num6, 0.0, 1.0) * num4;
			if (num9 > 1f)
			{
				_brushBarFill.Color = new Color4(row.AccentColor.R, row.AccentColor.G, row.AccentColor.B, 0.35f);
				dc.FillRoundedRectangle(new RoundedRectangle
				{
					Rect = new Rect(num3, num, num9, RowH),
					RadiusX = 5f,
					RadiusY = 5f
				}, _brushBarFill);
			}
			dc.DrawRoundedRectangle(roundedRect, _brushBarBorder);
			float num10 = num3 + 8f;
			float num11 = num4 - 16f;
			int serverId = row.ServerId;
			ID2D1SolidColorBrush iD2D1SolidColorBrush;
			if (serverId < 2000)
			{
				if (serverId < 1000)
				{
					goto IL_02f6;
				}
				iD2D1SolidColorBrush = _brushNameElyos;
			}
			else
			{
				if (serverId >= 3000)
				{
					goto IL_02f6;
				}
				iD2D1SolidColorBrush = _brushNameAsmo;
			}
			goto IL_02fe;
			IL_02fe:
			ID2D1SolidColorBrush defaultForegroundBrush = iD2D1SolidColorBrush;
			string text = ((AnonymousMode && !string.IsNullOrEmpty(row.JobIconKey)) ? AnonName(row) : row.Name);
			AppSettings instance = AppSettings.Instance;
			IDWriteTextLayout iDWriteTextLayout = _ctx.DWriteFactory.CreateTextLayout(text, _fontName, num11 * 0.5f, 16f);
			dc.DrawTextLayout(new Vector2(num10, num + 5f), iDWriteTextLayout, defaultForegroundBrush);
			float widthIncludingTrailingWhitespace = iDWriteTextLayout.Metrics.WidthIncludingTrailingWhitespace;
			iDWriteTextLayout.Dispose();
			float num12 = num10 + widthIncludingTrailingWhitespace + 4f;
			if (instance.ShowCombatPower)
			{
				string text2 = ((row.CombatPower > 0) ? $"{row.CombatPower:N0}" : "—");
				_brushBarFill.Color = new Color4(0.39f, 0.71f, 1f);
				dc.DrawText(text2, _fontCpScore, new Rect(num12, num + 8f, (float)text2.Length * 7f + 4f, 14f), _brushBarFill, DrawTextOptions.None, MeasuringMode.Natural);
				num12 += (float)text2.Length * 7f + 3f;
			}
			if (instance.ShowCombatScore)
			{
				string text3 = ((row.CombatScore > 0) ? $"{row.CombatScore:N0}" : "—");
				_brushBarFill.Color = new Color4(0.91f, 0.78f, 0.3f);
				dc.DrawText(text3, _fontCpScore, new Rect(num12, num + 8f, (float)text3.Length * 7f + 4f, 14f), _brushBarFill, DrawTextOptions.None, MeasuringMode.Natural);
			}
			float num13 = 0f;
			string slotText = GetSlotText(instance.BarSlot3, row);
			if (slotText != null)
			{
				IDWriteTextFormat slotFont = GetSlotFont(instance.BarSlot3);
				IDWriteTextLayout iDWriteTextLayout2 = _ctx.DWriteFactory.CreateTextLayout(slotText, slotFont, num11, 16f);
				iDWriteTextLayout2.TextAlignment = TextAlignment.Trailing;
				num13 = iDWriteTextLayout2.Metrics.Width;
				using ID2D1SolidColorBrush defaultForegroundBrush2 = dc.CreateSolidColorBrush(ParseSlotColor(instance.BarSlot3.Color));
				dc.DrawTextLayout(new Vector2(num10, num + 4f), iDWriteTextLayout2, defaultForegroundBrush2);
				iDWriteTextLayout2.Dispose();
			}
			float num14 = 0f;
			string slotText2 = GetSlotText(instance.BarSlot2, row);
			if (slotText2 != null)
			{
				IDWriteTextFormat slotFont2 = GetSlotFont(instance.BarSlot2);
				IDWriteTextLayout iDWriteTextLayout3 = _ctx.DWriteFactory.CreateTextLayout(slotText2, slotFont2, num11 - num13 - 6f, 16f);
				iDWriteTextLayout3.TextAlignment = TextAlignment.Trailing;
				num14 = iDWriteTextLayout3.Metrics.Width;
				using ID2D1SolidColorBrush defaultForegroundBrush3 = dc.CreateSolidColorBrush(ParseSlotColor(instance.BarSlot2.Color));
				dc.DrawTextLayout(new Vector2(num10, num + 6f), iDWriteTextLayout3, defaultForegroundBrush3);
				iDWriteTextLayout3.Dispose();
			}
			string slotText3 = GetSlotText(instance.BarSlot1, row);
			if (slotText3 != null)
			{
				IDWriteTextFormat slotFont3 = GetSlotFont(instance.BarSlot1);
				IDWriteTextLayout iDWriteTextLayout4 = _ctx.DWriteFactory.CreateTextLayout(slotText3, slotFont3, num11 - num13 - num14 - 12f, 16f);
				iDWriteTextLayout4.TextAlignment = TextAlignment.Trailing;
				using ID2D1SolidColorBrush defaultForegroundBrush4 = dc.CreateSolidColorBrush(ParseSlotColor(instance.BarSlot1.Color));
				dc.DrawTextLayout(new Vector2(num10, num + 6f), iDWriteTextLayout4, defaultForegroundBrush4);
				iDWriteTextLayout4.Dispose();
			}
			_rowHitAreas.Add((num, num + RowH, num7));
			num += RowH + 3f;
			num7++;
			continue;
			IL_02f6:
			iD2D1SolidColorBrush = _brushTextBright;
			goto IL_02fe;
		}
		if (_rows.Count == 0 && _summary != null)
		{
			DrawSummary(dc, startY);
		}
	}

	private void OnPlayerRowClick(object? sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Left)
		{
			return;
		}
		float num = e.Location.X;
		float num2 = e.Location.Y;
		if (num2 >= 6f && num2 < 34f && num >= 70f && num < 150f)
		{
			this.CountdownClicked?.Invoke();
			return;
		}
		if (num2 >= 6f && num2 < 34f && num >= 150f && num < 206f)
		{
			AppSettings instance = AppSettings.Instance;
			instance.ShowCombatPower = !instance.ShowCombatPower;
			instance.SaveDebounced();
			Invalidate();
			return;
		}
		if (num2 >= 6f && num2 < 34f && num >= 206f && num < 256f)
		{
			AppSettings instance2 = AppSettings.Instance;
			instance2.ShowCombatScore = !instance2.ShowCombatScore;
			instance2.SaveDebounced();
			Invalidate();
			return;
		}
		foreach (var (num3, num4, num5) in _rowHitAreas)
		{
			if (num2 >= num3 && num2 < num4 && num5 < _rows.Count)
			{
				this.PlayerRowClicked?.Invoke(_rows[num5]);
				break;
			}
		}
	}

	private void DrawSummary(ID2D1DeviceContext dc, float startY)
	{
		if ((object)_summary != null)
		{
			float num = (float)base.ClientSize.Width - 20f;
			dc.FillRoundedRectangle(new RoundedRectangle
			{
				Rect = new Rect(10f, startY, num, 92f),
				RadiusX = 6f,
				RadiusY = 6f
			}, _brushBarBg);
			IDWriteTextLayout iDWriteTextLayout = _ctx.DWriteFactory.CreateTextLayout("Last fight", _fontName, num - 12f, 20f);
			dc.DrawTextLayout(new Vector2(18f, startY + 6f), iDWriteTextLayout, _brushText);
			iDWriteTextLayout.Dispose();
			IDWriteTextLayout iDWriteTextLayout2 = _ctx.DWriteFactory.CreateTextLayout($"{_summary.DurationSec:0}s", _fontSmall, num - 12f, 20f);
			iDWriteTextLayout2.TextAlignment = TextAlignment.Trailing;
			dc.DrawTextLayout(new Vector2(14f, startY + 8f), iDWriteTextLayout2, _brushTextDim);
			iDWriteTextLayout2.Dispose();
			string text = $"total {FormatDamage(_summary.TotalDamage)}   avg {FormatDamage(_summary.AverageDps)}/s   peak {FormatDamage(_summary.PeakDps)}/s";
			string text2 = ((!string.IsNullOrEmpty(_summary.TopActorName)) ? (_summary.TopActorName + "  " + FormatDamage(_summary.TopActorDamage)) : "");
			if (!string.IsNullOrEmpty(_summary.BossName))
			{
				text2 = ((text2.Length > 0) ? (text2 + "   ") : "") + "vs " + _summary.BossName;
			}
			IDWriteTextLayout iDWriteTextLayout3 = _ctx.DWriteFactory.CreateTextLayout(text, _fontSmall, num - 12f, 18f);
			dc.DrawTextLayout(new Vector2(18f, startY + 32f), iDWriteTextLayout3, _brushText);
			iDWriteTextLayout3.Dispose();
			if (text2.Length > 0)
			{
				IDWriteTextLayout iDWriteTextLayout4 = _ctx.DWriteFactory.CreateTextLayout(text2, _fontSmall, num - 12f, 18f);
				dc.DrawTextLayout(new Vector2(18f, startY + 52f), iDWriteTextLayout4, _brushTextDim);
				iDWriteTextLayout4.Dispose();
			}
			IDWriteTextLayout iDWriteTextLayout5 = _ctx.DWriteFactory.CreateTextLayout("waiting for next fight...", _fontSmall, num - 12f, 18f);
			iDWriteTextLayout5.TextAlignment = TextAlignment.Center;
			dc.DrawTextLayout(new Vector2(14f, startY + 72f), iDWriteTextLayout5, _brushTextDim);
			iDWriteTextLayout5.Dispose();
		}
	}

	private static string FormatDamage(long v)
	{
		if (AppSettings.Instance.NumberFormat == "full")
		{
			return v.ToString("N0");
		}
		if (v >= 1000000000)
		{
			return ((double)v / 1000000000.0).ToString("0.##") + "B";
		}
		if (v >= 1000000)
		{
			return ((double)v / 1000000.0).ToString("0.##") + "M";
		}
		if (v >= 1000)
		{
			return ((double)v / 1000.0).ToString("0.#") + "K";
		}
		return v.ToString();
	}

	private static string? GetSlotText(BarSlotConfig slot, PlayerRow row)
	{
		return slot.Content switch
		{
			"percent" => $" {row.Percent * 100.0:0.#}%", 
			"damage" => FormatDamage(row.Damage), 
			"dps" => FormatDamage(row.DpsValue) + "/s", 
			_ => null, 
		};
	}

	private IDWriteTextFormat GetSlotFont(BarSlotConfig slot)
	{
		if (slot.FontSize >= 9f)
		{
			return _fontNumber;
		}
		return _fontSmall;
	}

	private void DrawBarSlot(ID2D1DeviceContext dc, BarSlotConfig slot, PlayerRow row, float x, float y, float w, float h, bool rightAlign)
	{
		string slotText = GetSlotText(slot, row);
		if (slotText == null)
		{
			return;
		}
		IDWriteTextFormat slotFont = GetSlotFont(slot);
		using ID2D1SolidColorBrush defaultForegroundBrush = dc.CreateSolidColorBrush(ParseSlotColor(slot.Color));
		dc.DrawText(slotText, slotFont, new Rect(x, y, w, h), defaultForegroundBrush, DrawTextOptions.None, MeasuringMode.Natural);
	}

	private string AnonName(PlayerRow row)
	{
		if (CompactMode)
		{
			string text = ((row.JobIconKey.Length > 2) ? row.JobIconKey.Substring(0, 2) : row.JobIconKey);
			if (row.ServerId > 0)
			{
				return text + "[" + ServerMap.GetShortName(row.ServerId) + "]";
			}
			return text;
		}
		if (row.ServerId > 0)
		{
			return row.JobIconKey + "[" + row.ServerName + "]";
		}
		return row.JobIconKey;
	}

	private static Color4 ParseSlotColor(string hex)
	{
		try
		{
			System.Drawing.Color color = AppSettings.ThemeColors.ParseHex(hex);
			return new Color4((float)(int)color.R / 255f, (float)(int)color.G / 255f, (float)(int)color.B / 255f);
		}
		catch
		{
			return new Color4(0.43f, 0.43f, 0.5f);
		}
	}
}
