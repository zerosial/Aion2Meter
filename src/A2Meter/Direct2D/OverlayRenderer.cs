using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using A2Meter.Core;
using A2Meter.Dps;
using A2Meter.Dps.Protocol;
using Vortice.DCommon;
using Vortice.DXGI;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace A2Meter.Direct2D;

internal sealed class OverlayRenderer : IDisposable
{
	public enum TabId
	{
		Dps,
		Party
	}

	public sealed record PartyRow(string Name, string JobIconKey, int CombatPower, int CombatScore, int ServerId, string ServerName, bool IsSelf, int Level = 0);

	public enum ZoneId
	{
		None,
		Lock,
		Anon,
		History,
		Settings,
		Close,
		Slider,
		Countdown,
		CpToggle,
		ScoreToggle,
		TabDps,
		TabParty
	}

	private const float PadX = 5f;

	private const float PadBottom = 6f;

	private ID2D1Factory1? _d2dFactory;

	private IDWriteFactory? _dwFactory;

	private ID3D11Device? _d3dDevice;

	private ID2D1Device? _d2dDevice;

	private ID2D1DeviceContext? _dc;

	private ID3D11Texture2D? _rtTexture;

	private ID3D11Texture2D? _stagingTexture;

	private ID2D1Bitmap1? _targetBitmap;

	private D2DFontProvider? _fonts;

	private JobIconAtlas? _icons;

	private int _texW;

	private int _texH;

	private ID2D1SolidColorBrush? _brushBarBg;

	private ID2D1SolidColorBrush? _brushBarFill;

	private ID2D1SolidColorBrush? _brushBarBorder;

	private ID2D1SolidColorBrush? _brushText;

	private ID2D1SolidColorBrush? _brushTextBright;

	private ID2D1SolidColorBrush? _brushTextDim;

	private ID2D1SolidColorBrush? _brushGold;

	private ID2D1SolidColorBrush? _brushAccent;

	private ID2D1SolidColorBrush? _brushIconTint;

	private ID2D1SolidColorBrush? _brushHpBg;

	private ID2D1SolidColorBrush? _brushHpFill;

	private ID2D1SolidColorBrush? _brushNameAsmo;

	private ID2D1SolidColorBrush? _brushNameElyos;

	private ID2D1SolidColorBrush? _brushToolbarBg;

	private IDWriteTextFormat? _fontName;

	private IDWriteTextFormat? _fontNumber;

	private IDWriteTextFormat? _fontSmall;

	private IDWriteTextFormat? _fontCpScore;

	private IDWriteTextFormat? _fontTotal;

	private IDWriteTextFormat? _fontNameC;

	private IDWriteTextFormat? _fontNumberC;

	private IDWriteTextFormat? _fontSmallC;

	private IDWriteTextFormat? _fontCpScoreC;

	private IDWriteTextFormat? _fontTotalC;

	private readonly List<DpsCanvas.PlayerRow> _rows = new List<DpsCanvas.PlayerRow>();

	private readonly List<(float Top, float Bottom, int Index)> _rowHitAreas = new List<(float, float, int)>();

	private string _timerText = "0:00";

	private long _totalDamage;

	private DpsCanvas.TargetInfo? _target;

	private DpsCanvas.SessionSummary? _summary;

	private bool _locked;

	private bool _anonymous;

	private readonly List<PartyRow> _partyRows = new List<PartyRow>();

	private ZoneId _hoveredZone;

	private readonly Dictionary<ZoneId, RectangleF> _zones = new Dictionary<ZoneId, RectangleF>();

	private int _sliderMin = 20;

	private int _sliderMax = 100;

	private readonly Queue<(string Text, DateTime Expires)> _toasts = new Queue<(string, DateTime)>();

	private const double ToastDurationSec = 3.0;

	private ID2D1PathGeometry? _geoLockShackle;

	private ID2D1PathGeometry? _geoUnlockShackle;

	private ID2D1PathGeometry? _geoEyeTop;

	private ID2D1PathGeometry? _geoEyeBottom;

	private static readonly Dictionary<string, Color4> _jobAccentByName = new Dictionary<string, Color4>
	{
		["검성"] = new Color4(0.525f, 0.867f, 0.953f),
		["궁성"] = new Color4(0.384f, 0.694f, 0.561f),
		["마도성"] = new Color4(0.718f, 0.549f, 0.949f),
		["살성"] = new Color4(0.643f, 0.906f, 0.608f),
		["수호성"] = new Color4(0.49f, 0.627f, 0.976f),
		["정령성"] = new Color4(0.812f, 0.42f, 0.816f),
		["치유성"] = new Color4(0.906f, 0.812f, 0.49f),
		["호법성"] = new Color4(0.894f, 0.647f, 0.357f)
	};

	private float RowH => 30f * (float)AppSettings.Instance.RowHeight / 90f;

	private float ToolbarHeight => RowH * 1.2f;

	private float HeaderHeight => RowH * 0.93f;

	private float TargetBarH => RowH * 0.67f;

	private float IconSize => RowH * 0.73f;

	private float RowGap => Math.Max(2f, RowH * 0.1f);

	public bool CompactMode { get; set; }

	public int PingMs { get; set; }

	public int CountdownSec { get; set; }

	public bool CountdownExpired { get; set; }

	public TabId ActiveTab { get; set; } = TabId.Party;

	private IDWriteTextFormat FName
	{
		get
		{
			if (!CompactMode)
			{
				return _fontName;
			}
			return _fontNameC;
		}
	}

	private IDWriteTextFormat FNumber
	{
		get
		{
			if (!CompactMode)
			{
				return _fontNumber;
			}
			return _fontNumberC;
		}
	}

	private IDWriteTextFormat FSmall
	{
		get
		{
			if (!CompactMode)
			{
				return _fontSmall;
			}
			return _fontSmallC;
		}
	}

	private IDWriteTextFormat FCpScore
	{
		get
		{
			if (!CompactMode)
			{
				return _fontCpScore;
			}
			return _fontCpScoreC;
		}
	}

	private IDWriteTextFormat FTotal
	{
		get
		{
			if (!CompactMode)
			{
				return _fontTotal;
			}
			return _fontTotalC;
		}
	}

	public void SetPartyData(IReadOnlyList<PartyRow> rows)
	{
		_partyRows.Clear();
		_partyRows.AddRange(rows);
	}

	public void Init()
	{
		_d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
		_dwFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
		DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;
		Vortice.Direct3D.FeatureLevel[] featureLevels = new Vortice.Direct3D.FeatureLevel[4]
		{
			Vortice.Direct3D.FeatureLevel.Level_11_1,
			Vortice.Direct3D.FeatureLevel.Level_11_0,
			Vortice.Direct3D.FeatureLevel.Level_10_1,
			Vortice.Direct3D.FeatureLevel.Level_10_0
		};
		D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, featureLevels, out ID3D11Device device);
		if (device == null)
		{
			D3D11.D3D11CreateDevice(null, DriverType.Warp, flags, featureLevels, out device);
		}
		_d3dDevice = device;
		using IDXGIDevice1 dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice1>();
		_d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
		_dc = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
		_brushBarBg = _dc.CreateSolidColorBrush(new Color4(0.078f, 0.098f, 0.157f, 0.6f));
		_brushBarFill = _dc.CreateSolidColorBrush(new Color4(0.44f, 0.78f, 1f));
		_brushBarBorder = _dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 0.07f));
		_brushText = _dc.CreateSolidColorBrush(new Color4(0.93f, 0.95f, 1f));
		_brushTextBright = _dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f));
		_brushTextDim = _dc.CreateSolidColorBrush(new Color4(0.635f, 0.694f, 0.784f));
		_brushGold = _dc.CreateSolidColorBrush(new Color4(1f, 0.82f, 0.4f));
		_brushAccent = _dc.CreateSolidColorBrush(new Color4(0.455f, 0.753f, 0.988f));
		_brushIconTint = _dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f));
		_brushHpBg = _dc.CreateSolidColorBrush(new Color4(0.2f, 0.07f, 0.1f));
		_brushHpFill = _dc.CreateSolidColorBrush(new Color4(0.85f, 0.2f, 0.25f));
		_brushNameElyos = _dc.CreateSolidColorBrush(new Color4(0.55f, 0.82f, 1f));
		_brushNameAsmo = _dc.CreateSolidColorBrush(new Color4(0.76f, 0.65f, 1f));
		_brushToolbarBg = _dc.CreateSolidColorBrush(new Color4(0.145f, 0.145f, 0.208f, 0.85f));
		ApplyThemeBrushes();
		_fonts = new D2DFontProvider(_dwFactory);
		RebuildFonts();
		_icons = new JobIconAtlas(_dc);
		BuildIconGeometries();
	}

	public void SetData(IReadOnlyList<DpsCanvas.PlayerRow> rows, long totalDamage, string timer, MobTarget? target, DpsCanvas.SessionSummary? summary)
	{
		_rows.Clear();
		_rows.AddRange(rows);
		_totalDamage = totalDamage;
		_timerText = timer;
		_target = ((target != null && target.IsBoss && target.MaxHp > 0) ? new DpsCanvas.TargetInfo(target.Name, target.CurrentHp, target.MaxHp) : null);
		_summary = summary;
	}

	public void SetLocked(bool v)
	{
		_locked = v;
	}

	public void SetAnonymous(bool v)
	{
		_anonymous = v;
	}

	public void SetHoveredZone(ZoneId zone)
	{
		_hoveredZone = zone;
	}

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
	}

	public void RenderFrame(int width, int height)
	{
		if (_dc == null || width <= 0 || height <= 0)
		{
			return;
		}
		EnsureTextures(width, height);
		_dc.Target = _targetBitmap;
		_dc.BeginDraw();
		AppSettings instance = AppSettings.Instance;
		if (CompactMode)
		{
			_dc.Clear(new Color4(0f, 0f, 0f, 0f));
			_zones.Clear();
			float y = 0f;
			y = DrawCompactHeader(y, width);
			y = DrawTargetBar(y, width);
			DrawRows(y, width, height);
		}
		else
		{
			System.Drawing.Color bgColor = instance.Theme.BgColor;
			float alpha = Math.Max(0.12f, (float)instance.Opacity / 100f);
			_dc.Clear(new Color4((float)(int)bgColor.R / 255f, (float)(int)bgColor.G / 255f, (float)(int)bgColor.B / 255f, alpha));
			float y2 = 0f;
			y2 = DrawToolbar(y2, width);
			y2 = DrawHeader(y2, width);
			if (ActiveTab == TabId.Party)
			{
				DrawPartyRows(y2, width, height);
			}
			else
			{
				y2 = DrawTargetBar(y2, width);
				DrawRows(y2, width, height);
			}
			DrawToasts(width, height);
		}
		_dc.EndDraw();
		_dc.Target = null;
	}

	public void PresentToLayeredWindow(nint hwnd, int left, int top, int width, int height)
	{
		if (_d3dDevice == null || _rtTexture == null || _stagingTexture == null)
		{
			return;
		}
		ID3D11DeviceContext immediateContext = _d3dDevice.ImmediateContext;
		immediateContext.CopyResource(_stagingTexture, _rtTexture);
		MappedSubresource mappedSubresource = immediateContext.Map(_stagingTexture, 0u);
		try
		{
			nint hObj = CreateHBitmapFromMapped(mappedSubresource.DataPointer, mappedSubresource.RowPitch, _texW, _texH);
			nint num = Win32Native.CreateCompatibleDC(IntPtr.Zero);
			nint hObj2 = Win32Native.SelectObject(num, hObj);
			Win32Native.POINT pptDst = new Win32Native.POINT
			{
				X = left,
				Y = top
			};
			Win32Native.SIZE psize = new Win32Native.SIZE
			{
				CX = width,
				CY = height
			};
			Win32Native.POINT pptSrc = new Win32Native.POINT
			{
				X = 0,
				Y = 0
			};
			Win32Native.BLENDFUNCTION pblend = new Win32Native.BLENDFUNCTION
			{
				BlendOp = 0,
				SourceConstantAlpha = byte.MaxValue,
				AlphaFormat = 1
			};
			Win32Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref pptDst, ref psize, num, ref pptSrc, 0u, ref pblend, 2u);
			Win32Native.SelectObject(num, hObj2);
			Win32Native.DeleteDC(num);
			Win32Native.DeleteObject(hObj);
		}
		finally
		{
			immediateContext.Unmap(_stagingTexture, 0u);
		}
	}

	public ZoneId HitTest(Point pt)
	{
		foreach (KeyValuePair<ZoneId, RectangleF> zone in _zones)
		{
			if (zone.Value.Contains(pt.X, pt.Y))
			{
				return zone.Key;
			}
		}
		return ZoneId.None;
	}

	public int RowHitTest(float y)
	{
		foreach (var (num, num2, result) in _rowHitAreas)
		{
			if (y >= num && y < num2)
			{
				return result;
			}
		}
		return -1;
	}

	public float SliderValueFromX(int x)
	{
		if (!_zones.TryGetValue(ZoneId.Slider, out var value))
		{
			return 0f;
		}
		return Math.Clamp(((float)x - value.X) / value.Width, 0f, 1f);
	}

	public IReadOnlyList<DpsCanvas.PlayerRow> GetRows()
	{
		return _rows;
	}

	public bool IsToolbarArea(float y)
	{
		if (!CompactMode)
		{
			return y < ToolbarHeight;
		}
		return false;
	}

	public bool IsDragArea(Point pt)
	{
		if (CompactMode)
		{
			return false;
		}
		if ((float)pt.Y >= ToolbarHeight)
		{
			return false;
		}
		foreach (KeyValuePair<ZoneId, RectangleF> zone in _zones)
		{
			if (zone.Value.Contains(pt.X, pt.Y))
			{
				return false;
			}
		}
		return true;
	}

	public void ApplySettings()
	{
		RebuildFonts();
		ApplyThemeBrushes();
	}

	public void Dispose()
	{
		_geoEyeBottom?.Dispose();
		_geoEyeTop?.Dispose();
		_geoUnlockShackle?.Dispose();
		_geoLockShackle?.Dispose();
		_icons?.Dispose();
		_fontTotalC?.Dispose();
		_fontCpScoreC?.Dispose();
		_fontSmallC?.Dispose();
		_fontNumberC?.Dispose();
		_fontNameC?.Dispose();
		_fontTotal?.Dispose();
		_fontCpScore?.Dispose();
		_fontSmall?.Dispose();
		_fontNumber?.Dispose();
		_fontName?.Dispose();
		_brushToolbarBg?.Dispose();
		_brushNameElyos?.Dispose();
		_brushNameAsmo?.Dispose();
		_brushHpFill?.Dispose();
		_brushHpBg?.Dispose();
		_brushAccent?.Dispose();
		_brushIconTint?.Dispose();
		_brushGold?.Dispose();
		_brushTextDim?.Dispose();
		_brushTextBright?.Dispose();
		_brushText?.Dispose();
		_brushBarBorder?.Dispose();
		_brushBarFill?.Dispose();
		_brushBarBg?.Dispose();
		_targetBitmap?.Dispose();
		_stagingTexture?.Dispose();
		_rtTexture?.Dispose();
		_dc?.Dispose();
		_d2dDevice?.Dispose();
		_d3dDevice?.Dispose();
		_dwFactory?.Dispose();
		_d2dFactory?.Dispose();
	}

	private void EnsureTextures(int w, int h)
	{
		if (w == _texW && h == _texH && _rtTexture != null)
		{
			return;
		}
		_targetBitmap?.Dispose();
		_rtTexture?.Dispose();
		_stagingTexture?.Dispose();
		_texW = w;
		_texH = h;
		Texture2DDescription description = new Texture2DDescription
		{
			Width = (uint)w,
			Height = (uint)h,
			MipLevels = 1u,
			ArraySize = 1u,
			Format = Format.B8G8R8A8_UNorm,
			SampleDescription = new SampleDescription(1u, 0u),
			Usage = ResourceUsage.Default,
			BindFlags = (BindFlags.ShaderResource | BindFlags.RenderTarget)
		};
		_rtTexture = _d3dDevice.CreateTexture2D(in description);
		Texture2DDescription description2 = description;
		description2.Usage = ResourceUsage.Staging;
		description2.BindFlags = BindFlags.None;
		description2.CPUAccessFlags = CpuAccessFlags.Read;
		_stagingTexture = _d3dDevice.CreateTexture2D(in description2);
		using IDXGISurface surface = _rtTexture.QueryInterface<IDXGISurface>();
		BitmapProperties1 value = new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96f, 96f, BitmapOptions.Target | BitmapOptions.CannotDraw);
		_targetBitmap = _dc.CreateBitmapFromDxgiSurface(surface, value);
	}

	private unsafe static nint CreateHBitmapFromMapped(nint srcData, uint srcPitch, int w, int h)
	{
		Win32Native.BITMAPINFOHEADER pbmi = new Win32Native.BITMAPINFOHEADER
		{
			biSize = Marshal.SizeOf<Win32Native.BITMAPINFOHEADER>(),
			biWidth = w,
			biHeight = -h,
			biPlanes = 1,
			biBitCount = 32
		};
		nint ppvBits;
		nint result = Win32Native.CreateDIBSection(IntPtr.Zero, ref pbmi, 0u, out ppvBits, IntPtr.Zero, 0u);
		if (ppvBits == IntPtr.Zero)
		{
			return result;
		}
		int num = w * 4;
		for (int i = 0; i < h; i++)
		{
			Buffer.MemoryCopy((void*)(srcData + (int)(i * srcPitch)), (void*)(ppvBits + i * num), num, num);
		}
		return result;
	}

	private void ApplyThemeBrushes()
	{
		if (!(_brushBarBg == null))
		{
			AppSettings.ThemeColors theme = AppSettings.Instance.Theme;
			_brushBarBg.Color = ColorToD2D(theme.HeaderColor);
			_brushText.Color = ColorToD2D(theme.TextColor);
			_brushTextDim.Color = ColorToD2D(theme.TextDimColor);
			_brushAccent.Color = ColorToD2D(theme.AccentColor);
			_brushNameElyos.Color = ColorToD2D(theme.ElyosColor);
			_brushNameAsmo.Color = ColorToD2D(theme.AsmodianColor);
			System.Drawing.Color headerColor = theme.HeaderColor;
			_brushToolbarBg.Color = new Color4((float)(int)headerColor.R / 255f, (float)(int)headerColor.G / 255f, (float)(int)headerColor.B / 255f, 0.85f);
		}
	}

	private void RebuildFonts()
	{
		if (_fonts != null)
		{
			_fontName?.Dispose();
			_fontNumber?.Dispose();
			_fontSmall?.Dispose();
			_fontCpScore?.Dispose();
			_fontTotal?.Dispose();
			_fontNameC?.Dispose();
			_fontNumberC?.Dispose();
			_fontSmallC?.Dispose();
			_fontCpScoreC?.Dispose();
			_fontTotalC?.Dispose();
			AppSettings instance = AppSettings.Instance;
			float num = instance.FontSize * (float)instance.FontScale / 100f;
			_fontName = CreateFont(num + 4f);
			_fontNumber = CreateFont(num + 4f);
			_fontSmall = CreateFont(num + 1f);
			_fontCpScore = CreateFont(num);
			_fontTotal = CreateFont(num + 6f);
			float num2 = num - 1f;
			_fontNameC = CreateFont(num2 + 4f);
			_fontNumberC = CreateFont(num2 + 4f);
			_fontSmallC = CreateFont(num2 + 1f);
			_fontCpScoreC = CreateFont(num2);
			_fontTotalC = CreateFont(num2 + 6f);
		}
		IDWriteTextFormat CreateFont(float size)
		{
			IDWriteTextFormat iDWriteTextFormat = _fonts.CreateUi(size);
			iDWriteTextFormat.WordWrapping = WordWrapping.NoWrap;
			return iDWriteTextFormat;
		}
	}

	private static Color4 ColorToD2D(System.Drawing.Color c)
	{
		return new Color4((float)(int)c.R / 255f, (float)(int)c.G / 255f, (float)(int)c.B / 255f);
	}

	private void BuildIconGeometries()
	{
		if (_d2dFactory == null)
		{
			return;
		}
		_geoLockShackle = _d2dFactory.CreatePathGeometry();
		using (ID2D1GeometrySink iD2D1GeometrySink = _geoLockShackle.Open())
		{
			iD2D1GeometrySink.BeginFigure(new Vector2(-4f, 0f), FigureBegin.Hollow);
			iD2D1GeometrySink.AddLine(new Vector2(-4f, -3f));
			iD2D1GeometrySink.AddArc(new ArcSegment
			{
				Point = new Vector2(4f, -3f),
				Size = new Vortice.Mathematics.Size(4f, 4f),
				SweepDirection = SweepDirection.Clockwise
			});
			iD2D1GeometrySink.AddLine(new Vector2(4f, 0f));
			iD2D1GeometrySink.EndFigure(FigureEnd.Open);
			iD2D1GeometrySink.Close();
		}
		_geoUnlockShackle = _d2dFactory.CreatePathGeometry();
		using (ID2D1GeometrySink iD2D1GeometrySink2 = _geoUnlockShackle.Open())
		{
			iD2D1GeometrySink2.BeginFigure(new Vector2(-4f, 0f), FigureBegin.Hollow);
			iD2D1GeometrySink2.AddLine(new Vector2(-4f, -3f));
			iD2D1GeometrySink2.AddArc(new ArcSegment
			{
				Point = new Vector2(4f, -3f),
				Size = new Vortice.Mathematics.Size(4f, 4f),
				SweepDirection = SweepDirection.Clockwise
			});
			iD2D1GeometrySink2.AddLine(new Vector2(4f, -1f));
			iD2D1GeometrySink2.EndFigure(FigureEnd.Open);
			iD2D1GeometrySink2.Close();
		}
		_geoEyeTop = _d2dFactory.CreatePathGeometry();
		using (ID2D1GeometrySink iD2D1GeometrySink3 = _geoEyeTop.Open())
		{
			iD2D1GeometrySink3.BeginFigure(new Vector2(-7f, 0f), FigureBegin.Hollow);
			iD2D1GeometrySink3.AddArc(new ArcSegment
			{
				Point = new Vector2(7f, 0f),
				Size = new Vortice.Mathematics.Size(7f, 5f),
				SweepDirection = SweepDirection.Clockwise
			});
			iD2D1GeometrySink3.EndFigure(FigureEnd.Open);
			iD2D1GeometrySink3.Close();
		}
		_geoEyeBottom = _d2dFactory.CreatePathGeometry();
		using ID2D1GeometrySink iD2D1GeometrySink4 = _geoEyeBottom.Open();
		iD2D1GeometrySink4.BeginFigure(new Vector2(-7f, 0f), FigureBegin.Hollow);
		iD2D1GeometrySink4.AddArc(new ArcSegment
		{
			Point = new Vector2(7f, 0f),
			Size = new Vortice.Mathematics.Size(7f, 5f),
			SweepDirection = SweepDirection.CounterClockwise
		});
		iD2D1GeometrySink4.EndFigure(FigureEnd.Open);
		iD2D1GeometrySink4.Close();
	}

	private float DrawToolbar(float y, int width)
	{
		ID2D1DeviceContext dc = _dc;
		_zones.Clear();
		float num = Math.Max(0.12f, (float)AppSettings.Instance.Opacity / 100f);
		System.Drawing.Color headerColor = AppSettings.Instance.Theme.HeaderColor;
		_brushToolbarBg.Color = new Color4((float)(int)headerColor.R / 255f, (float)(int)headerColor.G / 255f, (float)(int)headerColor.B / 255f, 0.85f * num);
		dc.FillRectangle(new Rect(0f, y, width, ToolbarHeight), _brushToolbarBg);
		string text = "A2Meter v" + AutoUpdater.CurrentVersion.ToString(3);
		IDWriteTextLayout iDWriteTextLayout = _dwFactory.CreateTextLayout(text, _fontSmall, 400f, ToolbarHeight);
		iDWriteTextLayout.ParagraphAlignment = ParagraphAlignment.Center;
		float widthIncludingTrailingWhitespace = iDWriteTextLayout.Metrics.WidthIncludingTrailingWhitespace;
		dc.DrawTextLayout(new Vector2(5f, y), iDWriteTextLayout, _brushText);
		iDWriteTextLayout.Dispose();
		float num2 = 5f + widthIncludingTrailingWhitespace + 8f;
		Color4 color = ((ActiveTab == TabId.Dps) ? new Color4(0.44f, 0.78f, 1f) : new Color4(0.45f, 0.48f, 0.55f));
		_brushBarFill.Color = color;
		IDWriteTextLayout iDWriteTextLayout2 = _dwFactory.CreateTextLayout("DPS", _fontSmall, 120f, ToolbarHeight);
		iDWriteTextLayout2.ParagraphAlignment = ParagraphAlignment.Center;
		float widthIncludingTrailingWhitespace2 = iDWriteTextLayout2.Metrics.WidthIncludingTrailingWhitespace;
		dc.DrawTextLayout(new Vector2(num2, y), iDWriteTextLayout2, _brushBarFill);
		iDWriteTextLayout2.Dispose();
		_zones[ZoneId.TabDps] = new RectangleF(num2, y, widthIncludingTrailingWhitespace2, ToolbarHeight);
		num2 += widthIncludingTrailingWhitespace2 + 4f;
		Color4 color2 = ((ActiveTab == TabId.Party) ? new Color4(0.4f, 0.85f, 0.55f) : new Color4(0.45f, 0.48f, 0.55f));
		_brushBarFill.Color = color2;
		IDWriteTextLayout iDWriteTextLayout3 = _dwFactory.CreateTextLayout("조회", _fontSmall, 120f, ToolbarHeight);
		iDWriteTextLayout3.ParagraphAlignment = ParagraphAlignment.Center;
		float widthIncludingTrailingWhitespace3 = iDWriteTextLayout3.Metrics.WidthIncludingTrailingWhitespace;
		dc.DrawTextLayout(new Vector2(num2, y), iDWriteTextLayout3, _brushBarFill);
		iDWriteTextLayout3.Dispose();
		_zones[ZoneId.TabParty] = new RectangleF(num2, y, widthIncludingTrailingWhitespace3, ToolbarHeight);
		int num3 = (int)(ToolbarHeight * 0.72f);
		int num4 = Math.Max(2, (int)(ToolbarHeight * 0.11f));
		float y2 = y + (ToolbarHeight - (float)num3) / 2f;
		float num5 = (float)width - 8f - (float)num3;
		DrawIconButton(dc, ZoneId.Close, num5, y2, num3, DrawCloseIcon);
		num5 -= (float)(num3 + num4);
		DrawIconButton(dc, ZoneId.Settings, num5, y2, num3, DrawGearIcon);
		num5 -= (float)(num3 + num4);
		DrawIconButton(dc, ZoneId.History, num5, y2, num3, DrawHistoryIcon);
		num5 -= (float)(num3 + num4);
		DrawIconButton(dc, ZoneId.Anon, num5, y2, num3, _anonymous ? new Action<ID2D1DeviceContext, float, float, ID2D1SolidColorBrush>(DrawEyeOffIcon) : new Action<ID2D1DeviceContext, float, float, ID2D1SolidColorBrush>(DrawEyeIcon));
		num5 -= (float)(num3 + num4);
		DrawIconButton(dc, ZoneId.Lock, num5, y2, num3, _locked ? new Action<ID2D1DeviceContext, float, float, ID2D1SolidColorBrush>(DrawLockIcon) : new Action<ID2D1DeviceContext, float, float, ID2D1SolidColorBrush>(DrawUnlockIcon));
		float toolbarHeight = ToolbarHeight;
		float num6 = ToolbarHeight * 0.56f;
		num5 -= toolbarHeight + (float)num4;
		float y3 = y + (ToolbarHeight - num6) / 2f;
		DrawSlider(dc, num5, y3, toolbarHeight, num6);
		_zones[ZoneId.Slider] = new RectangleF(num5, y3, toolbarHeight, num6);
		dc.DrawLine(new Vector2(0f, y + ToolbarHeight - 1f), new Vector2(width, y + ToolbarHeight - 1f), _brushBarBorder, 1f);
		return y + ToolbarHeight;
	}

	private void DrawIconButton(ID2D1DeviceContext dc, ZoneId zone, float x, float y, int size, Action<ID2D1DeviceContext, float, float, ID2D1SolidColorBrush> drawIcon)
	{
		RectangleF value = new RectangleF(x, y, size, size);
		_zones[zone] = value;
		if (_hoveredZone == zone)
		{
			Color4 color = ((zone == ZoneId.Close) ? new Color4(0.86f, 0.27f, 0.27f, 0.43f) : new Color4(0.24f, 0.39f, 0.63f, 0.27f));
			_brushBarFill.Color = color;
			dc.FillRoundedRectangle(new RoundedRectangle
			{
				Rect = new Rect(x, y, size, size),
				RadiusX = 4f,
				RadiusY = 4f
			}, _brushBarFill);
		}
		float arg = x + (float)size / 2f;
		float arg2 = y + (float)size / 2f;
		ID2D1SolidColorBrush arg3 = ((_hoveredZone == zone) ? _brushTextBright : _brushText);
		drawIcon(dc, arg, arg2, arg3);
	}

	private void DrawLockIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
	{
		dc.DrawRectangle(new Rect(cx - 5f, cy - 1f, 10f, 8f), brush, 1.6f);
		if (_geoLockShackle != null)
		{
			Matrix3x2 transform = dc.Transform;
			dc.Transform = Matrix3x2.CreateTranslation(cx, cy - 1f);
			dc.DrawGeometry(_geoLockShackle, brush, 1.6f);
			dc.Transform = transform;
		}
	}

	private void DrawUnlockIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
	{
		dc.DrawRectangle(new Rect(cx - 5f, cy - 1f, 10f, 8f), brush, 1.6f);
		if (_geoUnlockShackle != null)
		{
			Matrix3x2 transform = dc.Transform;
			dc.Transform = Matrix3x2.CreateTranslation(cx - 2f, cy - 1f);
			dc.DrawGeometry(_geoUnlockShackle, brush, 1.6f);
			dc.Transform = transform;
		}
	}

	private void DrawEyeIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
	{
		if (_geoEyeTop != null && _geoEyeBottom != null)
		{
			Matrix3x2 transform = dc.Transform;
			dc.Transform = Matrix3x2.CreateTranslation(cx, cy);
			dc.DrawGeometry(_geoEyeTop, brush, 1.6f);
			dc.DrawGeometry(_geoEyeBottom, brush, 1.6f);
			dc.Transform = transform;
		}
		dc.DrawEllipse(new Ellipse(new Vector2(cx, cy), 2f, 2f), brush, 1.6f);
	}

	private void DrawEyeOffIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
	{
		DrawEyeIcon(dc, cx, cy, brush);
		dc.DrawLine(new Vector2(cx - 6f, cy + 5f), new Vector2(cx + 6f, cy - 5f), brush, 1.6f);
	}

	private static void DrawHistoryIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
	{
		dc.DrawEllipse(new Ellipse(new Vector2(cx, cy), 6f, 6f), brush, 1.6f);
		dc.DrawLine(new Vector2(cx, cy), new Vector2(cx, cy - 4f), brush, 1.6f);
		dc.DrawLine(new Vector2(cx, cy), new Vector2(cx + 3f, cy + 1f), brush, 1.6f);
	}

	private static void DrawGearIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
	{
		dc.DrawEllipse(new Ellipse(new Vector2(cx, cy), 4f, 4f), brush, 1.6f);
		for (int i = 0; i < 6; i++)
		{
			double num = (double)i * Math.PI / 3.0;
			float x = cx + (float)(4.0 * Math.Cos(num));
			float y = cy + (float)(4.0 * Math.Sin(num));
			float x2 = cx + (float)(6.0 * Math.Cos(num));
			float y2 = cy + (float)(6.0 * Math.Sin(num));
			dc.DrawLine(new Vector2(x, y), new Vector2(x2, y2), brush, 1.6f);
		}
	}

	private static void DrawCloseIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
	{
		dc.DrawLine(new Vector2(cx - 5f, cy - 5f), new Vector2(cx + 5f, cy + 5f), brush, 1.6f);
		dc.DrawLine(new Vector2(cx + 5f, cy - 5f), new Vector2(cx - 5f, cy + 5f), brush, 1.6f);
	}

	private void DrawSlider(ID2D1DeviceContext dc, float x, float y, float w, float h)
	{
		float num = Math.Clamp((float)(AppSettings.Instance.Opacity - _sliderMin) / (float)Math.Max(1, _sliderMax - _sliderMin), 0f, 1f);
		float num2 = x + 6f;
		float num3 = x + w - 6f;
		float num4 = y + h / 2f;
		_brushBarFill.Color = new Color4(0.12f, 0.16f, 0.24f);
		dc.FillRoundedRectangle(new RoundedRectangle
		{
			Rect = new Rect(num2, num4 - 2f, num3 - num2, 4f),
			RadiusX = 2f,
			RadiusY = 2f
		}, _brushBarFill);
		float num5 = num * (num3 - num2);
		if (num5 > 1f)
		{
			dc.FillRoundedRectangle(new RoundedRectangle
			{
				Rect = new Rect(num2, num4 - 2f, num5, 4f),
				RadiusX = 2f,
				RadiusY = 2f
			}, _brushAccent);
		}
		float x2 = num2 + num5;
		Color4 color = ((_hoveredZone == ZoneId.Slider) ? new Color4(0.86f, 0.92f, 1f) : new Color4(0.93f, 0.95f, 1f));
		_brushBarFill.Color = color;
		dc.FillEllipse(new Ellipse(new Vector2(x2, num4), 6f, 6f), _brushBarFill);
	}

	private float DrawHeader(float y, int width)
	{
		ID2D1DeviceContext dc = _dc;
		float num = (float)width - 10f;
		float rowH = RowH;
		float num2 = 13f;
		float num3 = num - 16f;
		IDWriteTextLayout iDWriteTextLayout = _dwFactory.CreateTextLayout(_timerText, _fontTotal, 200f, rowH);
		iDWriteTextLayout.ParagraphAlignment = ParagraphAlignment.Center;
		dc.DrawTextLayout(new Vector2(num2, y), iDWriteTextLayout, _brushText);
		float widthIncludingTrailingWhitespace = iDWriteTextLayout.Metrics.WidthIncludingTrailingWhitespace;
		iDWriteTextLayout.Dispose();
		float num4 = num2 + widthIncludingTrailingWhitespace + 6f;
		string text = ((CountdownSec <= 0) ? "⏱off" : (CountdownExpired ? $"⏱{CountdownSec}s ■" : $"⏱{CountdownSec}s"));
		ID2D1SolidColorBrush defaultForegroundBrush = (CountdownExpired ? _brushGold : _brushTextDim);
		IDWriteTextLayout iDWriteTextLayout2 = _dwFactory.CreateTextLayout(text, _fontSmall, 200f, rowH);
		iDWriteTextLayout2.ParagraphAlignment = ParagraphAlignment.Center;
		float widthIncludingTrailingWhitespace2 = iDWriteTextLayout2.Metrics.WidthIncludingTrailingWhitespace;
		dc.DrawTextLayout(new Vector2(num4, y), iDWriteTextLayout2, defaultForegroundBrush);
		iDWriteTextLayout2.Dispose();
		_zones[ZoneId.Countdown] = new RectangleF(num4, y, widthIncludingTrailingWhitespace2, rowH);
		num4 += widthIncludingTrailingWhitespace2 + 6f;
		AppSettings instance = AppSettings.Instance;
		Color4 color = (instance.ShowCombatPower ? new Color4(0.39f, 0.71f, 1f) : new Color4(0.35f, 0.35f, 0.4f));
		_brushBarFill.Color = color;
		IDWriteTextLayout iDWriteTextLayout3 = _dwFactory.CreateTextLayout("전투력", _fontSmall, 120f, rowH);
		iDWriteTextLayout3.ParagraphAlignment = ParagraphAlignment.Center;
		float widthIncludingTrailingWhitespace3 = iDWriteTextLayout3.Metrics.WidthIncludingTrailingWhitespace;
		dc.DrawTextLayout(new Vector2(num4, y), iDWriteTextLayout3, _brushBarFill);
		iDWriteTextLayout3.Dispose();
		_zones[ZoneId.CpToggle] = new RectangleF(num4, y, widthIncludingTrailingWhitespace3, rowH);
		num4 += widthIncludingTrailingWhitespace3 + 6f;
		Color4 color2 = (instance.ShowCombatScore ? new Color4(0.91f, 0.78f, 0.3f) : new Color4(0.35f, 0.35f, 0.4f));
		_brushBarFill.Color = color2;
		IDWriteTextLayout iDWriteTextLayout4 = _dwFactory.CreateTextLayout("아툴", _fontSmall, 120f, rowH);
		iDWriteTextLayout4.ParagraphAlignment = ParagraphAlignment.Center;
		float widthIncludingTrailingWhitespace4 = iDWriteTextLayout4.Metrics.WidthIncludingTrailingWhitespace;
		dc.DrawTextLayout(new Vector2(num4, y), iDWriteTextLayout4, _brushBarFill);
		iDWriteTextLayout4.Dispose();
		_zones[ZoneId.ScoreToggle] = new RectangleF(num4, y, widthIncludingTrailingWhitespace4, rowH);
		num4 += widthIncludingTrailingWhitespace4 + 6f;
		if (PingMs > 0)
		{
			string text2 = $"{PingMs}ms";
			IDWriteTextLayout iDWriteTextLayout5 = _dwFactory.CreateTextLayout(text2, _fontSmall, 160f, rowH);
			iDWriteTextLayout5.ParagraphAlignment = ParagraphAlignment.Center;
			dc.DrawTextLayout(new Vector2(num4, y), iDWriteTextLayout5, _brushTextDim);
			iDWriteTextLayout5.Dispose();
		}
		IDWriteTextLayout iDWriteTextLayout6 = _dwFactory.CreateTextLayout(FormatDamage(_totalDamage), _fontTotal, num3 * 0.5f, rowH);
		iDWriteTextLayout6.TextAlignment = TextAlignment.Trailing;
		iDWriteTextLayout6.ParagraphAlignment = ParagraphAlignment.Center;
		dc.DrawTextLayout(new Vector2(num2 + num3 * 0.5f, y), iDWriteTextLayout6, _brushAccent);
		iDWriteTextLayout6.Dispose();
		return y + rowH + RowGap;
	}

	private float DrawCompactHeader(float y, int width)
	{
		ID2D1DeviceContext? dc = _dc;
		float num = (float)width - 10f;
		float rowH = RowH;
		_brushBarFill.Color = new Color4(0f, 0f, 0f, 0.1f);
		dc.FillRectangle(new Rect(5f, y, num, rowH), _brushBarFill);
		IDWriteTextLayout iDWriteTextLayout = _dwFactory.CreateTextLayout(_timerText, FTotal, 200f, rowH);
		iDWriteTextLayout.ParagraphAlignment = ParagraphAlignment.Center;
		dc.DrawTextLayout(new Vector2(9f, y), iDWriteTextLayout, _brushText);
		iDWriteTextLayout.Dispose();
		IDWriteTextLayout iDWriteTextLayout2 = _dwFactory.CreateTextLayout(FormatDamage(_totalDamage), FTotal, num * 0.6f, rowH);
		iDWriteTextLayout2.TextAlignment = TextAlignment.Trailing;
		iDWriteTextLayout2.ParagraphAlignment = ParagraphAlignment.Center;
		dc.DrawTextLayout(new Vector2(5f + num * 0.4f - 4f, y), iDWriteTextLayout2, _brushAccent);
		iDWriteTextLayout2.Dispose();
		return y + rowH + RowGap;
	}

	private float DrawTargetBar(float y, int width)
	{
		if ((object)_target == null)
		{
			return y;
		}
		ID2D1DeviceContext dc = _dc;
		float num = (float)width - 10f;
		Rect value = new Rect(5f, y, num, TargetBarH);
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
				Rect = new Rect(5f, y, num3, TargetBarH),
				RadiusX = 3f,
				RadiusY = 3f
			}, _brushHpFill);
		}
		float x = 5f + num / 3f;
		float x2 = 5f + num * 2f / 3f;
		dc.DrawLine(new Vector2(x, y + 2f), new Vector2(x, y + TargetBarH - 2f), _brushBarBorder, 1f);
		dc.DrawLine(new Vector2(x2, y + 2f), new Vector2(x2, y + TargetBarH - 2f), _brushBarBorder, 1f);
		string text = $"{_target.Name}   {FormatDamage(_target.CurrentHp)} / {FormatDamage(_target.MaxHp)}   {num2 * 100.0:0.#}%";
		IDWriteTextLayout iDWriteTextLayout = _dwFactory.CreateTextLayout(text, _fontSmall, num, TargetBarH);
		iDWriteTextLayout.TextAlignment = TextAlignment.Center;
		iDWriteTextLayout.ParagraphAlignment = ParagraphAlignment.Center;
		dc.DrawTextLayout(new Vector2(5f, y), iDWriteTextLayout, _brushText);
		iDWriteTextLayout.Dispose();
		return y + TargetBarH + 6f;
	}

	private void DrawRows(float startY, int width, int height)
	{
		ID2D1DeviceContext dc = _dc;
		float num = startY;
		float num2 = (float)width - 10f;
		float num3 = 5f;
		float num4 = num2;
		float num5 = (float)height - 6f;
		long num6 = ((_rows.Count > 0) ? _rows[0].Damage : 1);
		if (num6 <= 0)
		{
			num6 = 1L;
		}
		_rowHitAreas.Clear();
		int num7 = 0;
		float rowH = RowH;
		float num8 = Math.Clamp((float)AppSettings.Instance.BarOpacity / 100f, 0.05f, 1f);
		foreach (DpsCanvas.PlayerRow row in _rows)
		{
			if (num + rowH > num5)
			{
				break;
			}
			_brushBarBg.Color = new Color4(0.078f, 0.098f, 0.157f, 0.6f * num8);
			Rect rectangle = new Rect(num3, num, num4, rowH);
			dc.FillRectangle(in rectangle, _brushBarBg);
			float num9 = (float)Math.Clamp((double)row.Damage / (double)num6, 0.0, 1.0) * num4;
			if (num9 > 1f)
			{
				_brushBarFill.Color = new Color4(row.AccentColor.R, row.AccentColor.G, row.AccentColor.B, 0.35f * num8);
				dc.FillRectangle(new Rect(num3, num, num9, rowH), _brushBarFill);
			}
			_brushBarBorder.Color = new Color4(1f, 1f, 1f, 0.07f * num8);
			dc.DrawRectangle(in rectangle, _brushBarBorder);
			float num10 = num3 + 4f;
			float num11 = num + (rowH - IconSize) / 2f;
			ID2D1Bitmap1 iD2D1Bitmap = _icons?.Get(row.JobIconKey);
			if (iD2D1Bitmap != null)
			{
				DrawTintedIcon(dc, iD2D1Bitmap, num10, num11, row.AccentColor);
			}
			else
			{
				_brushAccent.Color = row.AccentColor;
				dc.FillEllipse(new Ellipse(new Vector2(num10 + IconSize / 2f, num11 + IconSize / 2f), IconSize / 2f - 2f, IconSize / 2f - 2f), _brushAccent);
			}
			float num12 = num10 + IconSize + 4f;
			float num13 = num4 - (num12 - num3) - 8f;
			int serverId = row.ServerId;
			ID2D1SolidColorBrush iD2D1SolidColorBrush;
			if (serverId < 2000)
			{
				if (serverId < 1000)
				{
					goto IL_02db;
				}
				iD2D1SolidColorBrush = _brushNameElyos;
			}
			else
			{
				if (serverId >= 3000)
				{
					goto IL_02db;
				}
				iD2D1SolidColorBrush = _brushNameAsmo;
			}
			goto IL_02e3;
			IL_02e3:
			ID2D1SolidColorBrush defaultForegroundBrush = iD2D1SolidColorBrush;
			string text = ((_anonymous && !string.IsNullOrEmpty(row.JobIconKey)) ? AnonName(row) : (CompactMode ? CompactName(row) : row.Name));
			AppSettings instance = AppSettings.Instance;
			IDWriteTextLayout iDWriteTextLayout = _dwFactory.CreateTextLayout(text, FName, num13, rowH);
			iDWriteTextLayout.ParagraphAlignment = ParagraphAlignment.Center;
			dc.DrawTextLayout(new Vector2(num12, num), iDWriteTextLayout, defaultForegroundBrush);
			float widthIncludingTrailingWhitespace = iDWriteTextLayout.Metrics.WidthIncludingTrailingWhitespace;
			iDWriteTextLayout.Dispose();
			float num14 = num12 + widthIncludingTrailingWhitespace + 4f;
			if (instance.ShowCombatPower)
			{
				string text2 = ((row.CombatPower > 0) ? FormatAbbrev(row.CombatPower) : "—");
				_brushBarFill.Color = new Color4(0.39f, 0.71f, 1f);
				IDWriteTextLayout iDWriteTextLayout2 = _dwFactory.CreateTextLayout(text2, FCpScore, 160f, rowH);
				iDWriteTextLayout2.ParagraphAlignment = ParagraphAlignment.Center;
				dc.DrawTextLayout(new Vector2(num14, num), iDWriteTextLayout2, _brushBarFill);
				num14 += iDWriteTextLayout2.Metrics.WidthIncludingTrailingWhitespace + 3f;
				iDWriteTextLayout2.Dispose();
			}
			if (instance.ShowCombatScore)
			{
				string text3 = ((row.CombatScore > 0) ? FormatAbbrev(row.CombatScore) : "—");
				_brushBarFill.Color = new Color4(0.91f, 0.78f, 0.3f);
				IDWriteTextLayout iDWriteTextLayout3 = _dwFactory.CreateTextLayout(text3, FCpScore, 160f, rowH);
				iDWriteTextLayout3.ParagraphAlignment = ParagraphAlignment.Center;
				dc.DrawTextLayout(new Vector2(num14, num), iDWriteTextLayout3, _brushBarFill);
				iDWriteTextLayout3.Dispose();
			}
			float num15 = 0f;
			string slotText = GetSlotText(instance.BarSlot3, row);
			if (slotText != null)
			{
				IDWriteTextFormat slotFont = GetSlotFont(instance.BarSlot3);
				IDWriteTextLayout iDWriteTextLayout4 = _dwFactory.CreateTextLayout(slotText, slotFont, num13, rowH);
				iDWriteTextLayout4.TextAlignment = TextAlignment.Trailing;
				iDWriteTextLayout4.ParagraphAlignment = ParagraphAlignment.Center;
				num15 = iDWriteTextLayout4.Metrics.Width;
				using ID2D1SolidColorBrush defaultForegroundBrush2 = dc.CreateSolidColorBrush(ParseSlotColor(instance.BarSlot3.Color));
				dc.DrawTextLayout(new Vector2(num12, num), iDWriteTextLayout4, defaultForegroundBrush2);
				iDWriteTextLayout4.Dispose();
			}
			float num16 = 0f;
			string slotText2 = GetSlotText(instance.BarSlot2, row);
			if (slotText2 != null)
			{
				IDWriteTextFormat slotFont2 = GetSlotFont(instance.BarSlot2);
				IDWriteTextLayout iDWriteTextLayout5 = _dwFactory.CreateTextLayout(slotText2, slotFont2, num13 - num15 - 5f, rowH);
				iDWriteTextLayout5.TextAlignment = TextAlignment.Trailing;
				iDWriteTextLayout5.ParagraphAlignment = ParagraphAlignment.Center;
				num16 = iDWriteTextLayout5.Metrics.Width;
				using ID2D1SolidColorBrush defaultForegroundBrush3 = dc.CreateSolidColorBrush(ParseSlotColor(instance.BarSlot2.Color));
				dc.DrawTextLayout(new Vector2(num12, num), iDWriteTextLayout5, defaultForegroundBrush3);
				iDWriteTextLayout5.Dispose();
			}
			string slotText3 = GetSlotText(instance.BarSlot1, row);
			if (slotText3 != null)
			{
				IDWriteTextFormat slotFont3 = GetSlotFont(instance.BarSlot1);
				IDWriteTextLayout iDWriteTextLayout6 = _dwFactory.CreateTextLayout(slotText3, slotFont3, num13 - num15 - num16 - 10f, rowH);
				iDWriteTextLayout6.TextAlignment = TextAlignment.Trailing;
				iDWriteTextLayout6.ParagraphAlignment = ParagraphAlignment.Center;
				using ID2D1SolidColorBrush defaultForegroundBrush4 = dc.CreateSolidColorBrush(ParseSlotColor(instance.BarSlot1.Color));
				dc.DrawTextLayout(new Vector2(num12, num), iDWriteTextLayout6, defaultForegroundBrush4);
				iDWriteTextLayout6.Dispose();
			}
			_rowHitAreas.Add((num, num + rowH, num7));
			num += rowH + RowGap;
			num7++;
			continue;
			IL_02db:
			iD2D1SolidColorBrush = _brushTextBright;
			goto IL_02e3;
		}
		if (_rows.Count == 0 && _summary != null)
		{
			DrawSummary(startY, width);
		}
	}

	private void DrawPartyRows(float startY, int width, int height)
	{
		ID2D1DeviceContext dc = _dc;
		float num = startY;
		float num2 = (float)width - 10f;
		float num3 = (float)height - 6f;
		float rowH = RowH;
		float num4 = Math.Clamp((float)AppSettings.Instance.BarOpacity / 100f, 0.05f, 1f);
		_rowHitAreas.Clear();
		if (_partyRows.Count == 0)
		{
			IDWriteTextLayout iDWriteTextLayout = _dwFactory.CreateTextLayout("파티 정보 없음", FName, num2, rowH);
			iDWriteTextLayout.ParagraphAlignment = ParagraphAlignment.Center;
			iDWriteTextLayout.TextAlignment = TextAlignment.Center;
			dc.DrawTextLayout(new Vector2(5f, num), iDWriteTextLayout, _brushTextDim);
			iDWriteTextLayout.Dispose();
			return;
		}
		foreach (PartyRow partyRow in _partyRows)
		{
			if (num + rowH > num3)
			{
				break;
			}
			_brushBarBg.Color = new Color4(0.078f, 0.098f, 0.157f, 0.6f * num4);
			Rect rectangle = new Rect(5f, num, num2, rowH);
			dc.FillRectangle(in rectangle, _brushBarBg);
			if (partyRow.IsSelf)
			{
				_brushBarFill.Color = new Color4(0.44f, 0.78f, 1f, 0.12f * num4);
				dc.FillRectangle(in rectangle, _brushBarFill);
			}
			_brushBarBorder.Color = new Color4(1f, 1f, 1f, 0.07f * num4);
			dc.DrawRectangle(in rectangle, _brushBarBorder);
			float num5 = 9f;
			float y = num + (rowH - IconSize) / 2f;
			ID2D1Bitmap1 iD2D1Bitmap = _icons?.Get(partyRow.JobIconKey);
			if (iD2D1Bitmap != null)
			{
				DrawTintedIcon(dc, iD2D1Bitmap, num5, y, JobAccentFromName(partyRow.JobIconKey));
			}
			float num6 = num5 + IconSize + 4f;
			float maxWidth = num2 - (num6 - 5f) - 8f;
			int serverId = partyRow.ServerId;
			ID2D1SolidColorBrush iD2D1SolidColorBrush;
			if (serverId < 2000)
			{
				if (serverId < 1000)
				{
					goto IL_023f;
				}
				iD2D1SolidColorBrush = _brushNameElyos;
			}
			else
			{
				if (serverId >= 3000)
				{
					goto IL_023f;
				}
				iD2D1SolidColorBrush = _brushNameAsmo;
			}
			goto IL_0247;
			IL_023f:
			iD2D1SolidColorBrush = _brushTextBright;
			goto IL_0247;
			IL_0247:
			ID2D1SolidColorBrush defaultForegroundBrush = iD2D1SolidColorBrush;
			IDWriteTextLayout iDWriteTextLayout2 = _dwFactory.CreateTextLayout(partyRow.Name, FName, maxWidth, rowH);
			iDWriteTextLayout2.ParagraphAlignment = ParagraphAlignment.Center;
			dc.DrawTextLayout(new Vector2(num6, num), iDWriteTextLayout2, defaultForegroundBrush);
			float widthIncludingTrailingWhitespace = iDWriteTextLayout2.Metrics.WidthIncludingTrailingWhitespace;
			iDWriteTextLayout2.Dispose();
			float num7 = num6 + widthIncludingTrailingWhitespace + 6f;
			if (partyRow.Level > 0)
			{
				_brushTextDim.Color = new Color4(0.7f, 0.7f, 0.7f);
				IDWriteTextLayout iDWriteTextLayout3 = _dwFactory.CreateTextLayout($"Lv.{partyRow.Level}", FCpScore, 120f, rowH);
				iDWriteTextLayout3.ParagraphAlignment = ParagraphAlignment.Center;
				dc.DrawTextLayout(new Vector2(num7, num), iDWriteTextLayout3, _brushTextDim);
				num7 += iDWriteTextLayout3.Metrics.WidthIncludingTrailingWhitespace + 4f;
				iDWriteTextLayout3.Dispose();
			}
			if (partyRow.CombatPower > 0)
			{
				_brushBarFill.Color = new Color4(0.39f, 0.71f, 1f);
				IDWriteTextLayout iDWriteTextLayout4 = _dwFactory.CreateTextLayout(FormatAbbrev(partyRow.CombatPower), FCpScore, 160f, rowH);
				iDWriteTextLayout4.ParagraphAlignment = ParagraphAlignment.Center;
				dc.DrawTextLayout(new Vector2(num7, num), iDWriteTextLayout4, _brushBarFill);
				num7 += iDWriteTextLayout4.Metrics.WidthIncludingTrailingWhitespace + 4f;
				iDWriteTextLayout4.Dispose();
			}
			if (partyRow.CombatScore > 0)
			{
				_brushBarFill.Color = new Color4(0.91f, 0.78f, 0.3f);
				IDWriteTextLayout iDWriteTextLayout5 = _dwFactory.CreateTextLayout(FormatAbbrev(partyRow.CombatScore), FCpScore, 160f, rowH);
				iDWriteTextLayout5.ParagraphAlignment = ParagraphAlignment.Center;
				dc.DrawTextLayout(new Vector2(num7, num), iDWriteTextLayout5, _brushBarFill);
				iDWriteTextLayout5.Dispose();
			}
			if (!string.IsNullOrEmpty(partyRow.ServerName))
			{
				IDWriteTextLayout iDWriteTextLayout6 = _dwFactory.CreateTextLayout(partyRow.ServerName, FSmall, maxWidth, rowH);
				iDWriteTextLayout6.TextAlignment = TextAlignment.Trailing;
				iDWriteTextLayout6.ParagraphAlignment = ParagraphAlignment.Center;
				dc.DrawTextLayout(new Vector2(num6, num), iDWriteTextLayout6, _brushTextDim);
				iDWriteTextLayout6.Dispose();
			}
			num += rowH + RowGap;
		}
	}

	private void DrawSummary(float startY, int width)
	{
		if ((object)_summary != null)
		{
			ID2D1DeviceContext dc = _dc;
			float num = (float)width - 10f;
			dc.FillRoundedRectangle(new RoundedRectangle
			{
				Rect = new Rect(5f, startY, num, 92f),
				RadiusX = 6f,
				RadiusY = 6f
			}, _brushBarBg);
			IDWriteTextLayout iDWriteTextLayout = _dwFactory.CreateTextLayout("Last fight", _fontName, num - 12f, 26f);
			iDWriteTextLayout.ParagraphAlignment = ParagraphAlignment.Center;
			dc.DrawTextLayout(new Vector2(13f, startY), iDWriteTextLayout, _brushText);
			iDWriteTextLayout.Dispose();
			IDWriteTextLayout iDWriteTextLayout2 = _dwFactory.CreateTextLayout($"{_summary.DurationSec:0}s", _fontSmall, num - 12f, 26f);
			iDWriteTextLayout2.TextAlignment = TextAlignment.Trailing;
			iDWriteTextLayout2.ParagraphAlignment = ParagraphAlignment.Center;
			dc.DrawTextLayout(new Vector2(9f, startY), iDWriteTextLayout2, _brushTextDim);
			iDWriteTextLayout2.Dispose();
			string text = $"total {FormatDamage(_summary.TotalDamage)}   avg {FormatDamage(_summary.AverageDps)}/s   peak {FormatDamage(_summary.PeakDps)}/s";
			string text2 = ((!string.IsNullOrEmpty(_summary.TopActorName)) ? (_summary.TopActorName + "  " + FormatDamage(_summary.TopActorDamage)) : "");
			if (!string.IsNullOrEmpty(_summary.BossName))
			{
				text2 = ((text2.Length > 0) ? (text2 + "   ") : "") + "vs " + _summary.BossName;
			}
			IDWriteTextLayout iDWriteTextLayout3 = _dwFactory.CreateTextLayout(text, _fontSmall, num - 12f, 20f);
			iDWriteTextLayout3.ParagraphAlignment = ParagraphAlignment.Center;
			dc.DrawTextLayout(new Vector2(13f, startY + 30f), iDWriteTextLayout3, _brushText);
			iDWriteTextLayout3.Dispose();
			if (text2.Length > 0)
			{
				IDWriteTextLayout iDWriteTextLayout4 = _dwFactory.CreateTextLayout(text2, _fontSmall, num - 12f, 20f);
				iDWriteTextLayout4.ParagraphAlignment = ParagraphAlignment.Center;
				dc.DrawTextLayout(new Vector2(13f, startY + 50f), iDWriteTextLayout4, _brushTextDim);
				iDWriteTextLayout4.Dispose();
			}
			IDWriteTextLayout iDWriteTextLayout5 = _dwFactory.CreateTextLayout("waiting for next fight...", _fontSmall, num - 12f, 20f);
			iDWriteTextLayout5.TextAlignment = TextAlignment.Center;
			iDWriteTextLayout5.ParagraphAlignment = ParagraphAlignment.Center;
			dc.DrawTextLayout(new Vector2(9f, startY + 72f), iDWriteTextLayout5, _brushTextDim);
			iDWriteTextLayout5.Dispose();
		}
	}

	private void DrawToasts(int width, int height)
	{
		List<(string, DateTime)> list;
		lock (_toasts)
		{
			while (_toasts.Count > 0 && _toasts.Peek().Expires < DateTime.UtcNow)
			{
				_toasts.Dequeue();
			}
			if (_toasts.Count == 0)
			{
				return;
			}
			list = new List<(string, DateTime)>(_toasts);
		}
		ID2D1DeviceContext dc = _dc;
		float num = (float)height - 6f - 26f * (float)list.Count;
		foreach (var item2 in list)
		{
			string item = item2.Item1;
			float num2 = (float)width - 10f;
			_brushBarFill.Color = new Color4(0.1f, 0.1f, 0.18f, 0.85f);
			dc.FillRoundedRectangle(new RoundedRectangle
			{
				Rect = new Rect(5f, num, num2, 24f),
				RadiusX = 4f,
				RadiusY = 4f
			}, _brushBarFill);
			IDWriteTextLayout iDWriteTextLayout = _dwFactory.CreateTextLayout(item, _fontSmall, num2 - 12f, 24f);
			iDWriteTextLayout.TextAlignment = TextAlignment.Center;
			iDWriteTextLayout.ParagraphAlignment = ParagraphAlignment.Center;
			dc.DrawTextLayout(new Vector2(11f, num), iDWriteTextLayout, _brushText);
			iDWriteTextLayout.Dispose();
			num += 26f;
		}
	}

	private string? GetSlotText(BarSlotConfig slot, DpsCanvas.PlayerRow row)
	{
		return slot.Content switch
		{
			"percent" => $" {row.Percent * 100.0:0.#}%", 
			"damage" => CompactMode ? FormatAbbrev(row.Damage) : FormatDamage(row.Damage), 
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

	private static string FormatDamage(long v)
	{
		if (AppSettings.Instance.NumberFormat == "full")
		{
			return v.ToString("N0");
		}
		return FormatAbbrev(v);
	}

	private static string FormatAbbrev(long v)
	{
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

	private string AnonName(DpsCanvas.PlayerRow row)
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

	private void DrawTintedIcon(ID2D1DeviceContext dc, ID2D1Bitmap1 icon, float x, float y, Color4 accent)
	{
		AntialiasMode antialiasMode = dc.AntialiasMode;
		dc.AntialiasMode = AntialiasMode.Aliased;
		Matrix3x2 transform = dc.Transform;
		float scale = IconSize / icon.Size.Width;
		dc.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(x, y);
		_brushIconTint.Color = accent;
		dc.FillOpacityMask(icon, _brushIconTint);
		dc.Transform = transform;
		dc.AntialiasMode = antialiasMode;
	}

	private static Color4 JobAccentFromName(string jobName)
	{
		if (!_jobAccentByName.TryGetValue(jobName, out var value))
		{
			return new Color4(0.7f, 0.7f, 0.7f);
		}
		return value;
	}

	private static string CompactName(DpsCanvas.PlayerRow row)
	{
		if (row.ServerId <= 0)
		{
			return row.Name;
		}
		string shortName = ServerMap.GetShortName(row.ServerId);
		int num = row.Name.IndexOf('[');
		string text = ((num > 0) ? row.Name.Substring(0, num) : row.Name);
		if (!string.IsNullOrEmpty(shortName))
		{
			return text + "[" + shortName + "]";
		}
		return text;
	}
}
