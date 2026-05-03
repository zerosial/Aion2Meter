using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using A2Meter.Dps;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DwFontStyle = Vortice.DirectWrite.FontStyle;
using D2DColor    = Vortice.Mathematics.Color4;
using DrawColor   = System.Drawing.Color;

namespace A2Meter.Direct2D;

/// High-frequency overlay layer: per-player DPS bars, numbers, debuff bars.
/// Renders only when SetData() is called or when the form is resized.
///
/// IMPORTANT: Vortice.Mathematics.Rect is (x, y, width, height), NOT (left, top, right, bottom).
internal sealed class DpsCanvas : Control
{
    public sealed record PlayerRow(
        string         Name,
        string         JobIconKey,
        long           Damage,
        double         Percent,    // 0..1 of party
        long           DpsValue,
        double         CritRate,   // 0..1
        long           HealTotal,
        D2DColor       AccentColor,
        IReadOnlyList<SkillBar>? Skills = null,
        int            CombatPower = 0,
        long           PeakDps = 0,
        long           AvgDps = 0,
        long           DotDamage = 0);

    public sealed record SkillBar(
        string Name,
        long   Total,
        long   Hits,
        double CritRate,        // 0..1
        double PercentOfActor,  // 0..1
        double BackRate      = 0,
        double StrongRate    = 0,
        double PerfectRate   = 0,
        double MultiHitRate  = 0,
        double DodgeRate     = 0,
        double BlockRate     = 0,
        int[]? Specs         = null);

    public sealed record TargetInfo(string Name, long CurrentHp, long MaxHp);

    public sealed record SessionSummary(
        double  DurationSec,
        long    TotalDamage,
        long    AverageDps,
        long    PeakDps,
        string  TopActorName,
        long    TopActorDamage,
        string? BossName);

    private const float PadX         = 10f;
    private const float PadBottom    = 6f;
    private const float HeaderHeight = 28f;
    private const float TargetBarH   = 20f;
    private const float RowH         = 30f;
    private const float RowGap       = 3f;
    private const float BarRadius    = 5f;
    private const float IconSize     = 22f;
    private D2DContext? _ctx;
    private D2DFontProvider? _fonts;
    private JobIconAtlas? _icons;
    private ID2D1SolidColorBrush? _brushBarBg;
    private ID2D1SolidColorBrush? _brushBarFill;
    private ID2D1SolidColorBrush? _brushText;
    private ID2D1SolidColorBrush? _brushTextDim;
    private ID2D1SolidColorBrush? _brushAccent;
    private IDWriteTextFormat? _fontName;
    private IDWriteTextFormat? _fontNumber;
    private IDWriteTextFormat? _fontSmall;
    private IDWriteTextFormat? _fontTotal;

    private readonly List<PlayerRow> _rows = new();
    private readonly List<(float Top, float Bottom, int Index)> _rowHitAreas = new();
    private string _timerText = "0:00";
    private long _totalDamage;
    private TargetInfo? _target;
    private SessionSummary? _summary;
    private ID2D1SolidColorBrush? _brushHpBg;
    private ID2D1SolidColorBrush? _brushHpFill;

    /// Fired when the user clicks a player row. Passes the clicked PlayerRow.
    public event Action<PlayerRow>? PlayerRowClicked;

    public DpsCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.Opaque
               | ControlStyles.UserPaint, true);
        DoubleBuffered = false;
        BackColor = DrawColor.FromArgb(8, 11, 20);

        MouseMove += OnEdgeMouseMove;
        MouseDown += OnEdgeMouseDown;
        MouseClick += OnPlayerRowClick;
    }

    private const int EdgeMargin = 6;

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    private const int WM_NCLBUTTONDOWN = 0x00A1;

    private static int HitFromEdges(System.Drawing.Point pt, int w, int h)
    {
        bool L = pt.X < EdgeMargin;
        bool R = pt.X >= w - EdgeMargin;
        bool T = pt.Y < EdgeMargin;
        bool B = pt.Y >= h - EdgeMargin;
        if (T && L) return 13;
        if (T && R) return 14;
        if (B && L) return 16;
        if (B && R) return 17;
        if (L)      return 10;
        if (R)      return 11;
        if (T)      return 12;
        if (B)      return 15;
        return 0;
    }

    private void OnEdgeMouseMove(object? sender, MouseEventArgs e)
    {
        int code = HitFromEdges(e.Location, ClientSize.Width, ClientSize.Height);
        Cursor = code switch
        {
            10 or 11 => Cursors.SizeWE,
            12 or 15 => Cursors.SizeNS,
            13 or 17 => Cursors.SizeNWSE,
            14 or 16 => Cursors.SizeNESW,
            _        => Cursors.Default,
        };
    }

    private void OnEdgeMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int code = HitFromEdges(e.Location, ClientSize.Width, ClientSize.Height);
        if (code == 0) return;
        var top = FindForm();
        if (top is null) return;
        ReleaseCapture();
        SendMessage(top.Handle, WM_NCLBUTTONDOWN, (IntPtr)code, IntPtr.Zero);
    }

    public void SetData(IReadOnlyList<PlayerRow> rows, long totalDamage, string timerText,
                        MobTarget? target = null, SessionSummary? summary = null)
    {
        _rows.Clear();
        _rows.AddRange(rows);
        _totalDamage = totalDamage;
        _timerText = timerText;
        _target = target is { IsBoss: true, MaxHp: > 0 }
            ? new TargetInfo(target.Name, target.CurrentHp, target.MaxHp)
            : null;
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
        _ctx?.Resize(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        Invalidate();
    }

    private void InitD2D()
    {
        if (DesignMode || Width <= 0 || Height <= 0) return;
        _ctx = new D2DContext(Handle, ClientSize.Width, ClientSize.Height);

        var dc = _ctx.DC;
        _brushBarBg   = dc.CreateSolidColorBrush(new D2DColor(0.10f, 0.13f, 0.20f, 1.00f));
        _brushBarFill = dc.CreateSolidColorBrush(new D2DColor(0.44f, 0.78f, 1.00f, 1.00f));
        _brushText    = dc.CreateSolidColorBrush(new D2DColor(0.97f, 0.98f, 1.00f, 1.00f));
        _brushTextDim = dc.CreateSolidColorBrush(new D2DColor(0.65f, 0.72f, 0.82f, 1.00f));
        _brushAccent  = dc.CreateSolidColorBrush(new D2DColor(0.44f, 0.78f, 1.00f, 1.00f));
        _brushHpBg    = dc.CreateSolidColorBrush(new D2DColor(0.20f, 0.07f, 0.10f, 1.00f));
        _brushHpFill  = dc.CreateSolidColorBrush(new D2DColor(0.85f, 0.20f, 0.25f, 1.00f));

        _fonts = new D2DFontProvider(_ctx.DWriteFactory);
        _fontName   = _fonts.CreateUiName(13f);
        _fontNumber = _fonts.CreateNumeric(13f);
        _fontSmall  = _fonts.CreateUiName(10f, FontWeight.Normal);
        _fontTotal  = _fonts.CreateNumeric(15f);

        _icons = new JobIconAtlas(dc);
    }

    private void DisposeD2D()
    {
        _icons?.Dispose();
        _fontTotal?.Dispose();
        _fontSmall?.Dispose();
        _fontNumber?.Dispose();
        _fontName?.Dispose();
        _brushHpFill?.Dispose();
        _brushHpBg?.Dispose();
        _brushAccent?.Dispose();
        _brushTextDim?.Dispose();
        _brushText?.Dispose();
        _brushBarFill?.Dispose();
        _brushBarBg?.Dispose();
        _ctx?.Dispose();
        _ctx = null;
    }

    // ── Rendering ────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_ctx is null) { base.OnPaint(e); return; }
        var dc = _ctx.DC;

        dc.BeginDraw();
        dc.Clear(new D2DColor(0.031f, 0.043f, 0.078f, 1.00f));

        float y = 6f;
        y = DrawHeader(dc, y);
        y = DrawTargetBar(dc, y);
        DrawRows(dc, y);

        dc.EndDraw();
        _ctx.Present();
    }

    private float DrawHeader(ID2D1DeviceContext dc, float y)
    {
        float w = ClientSize.Width - PadX * 2;

        // Rect = (x, y, width, height)
        dc.DrawText(_timerText, _fontTotal!, new Rect(PadX, y, 120f, HeaderHeight), _brushText!,
            DrawTextOptions.None, MeasuringMode.Natural);

        var totalLayout = _ctx!.DWriteFactory.CreateTextLayout(
            FormatDamage(_totalDamage), _fontTotal!, w, HeaderHeight);
        totalLayout.TextAlignment = TextAlignment.Trailing;
        dc.DrawTextLayout(new Vector2(PadX, y), totalLayout, _brushAccent!);
        totalLayout.Dispose();

        return y + HeaderHeight + 6f;
    }

    private float DrawTargetBar(ID2D1DeviceContext dc, float y)
    {
        if (_target is null) return y;

        float w = ClientSize.Width - PadX * 2;

        var bgRect = new Rect(PadX, y, w, TargetBarH);
        dc.FillRoundedRectangle(new RoundedRectangle { Rect = bgRect, RadiusX = 3f, RadiusY = 3f }, _brushHpBg!);

        double pct = _target.MaxHp > 0 ? (double)_target.CurrentHp / _target.MaxHp : 0;
        float fillW = (float)Math.Clamp(pct, 0, 1) * w;
        if (fillW > 1)
        {
            dc.FillRoundedRectangle(new RoundedRectangle
            {
                Rect = new Rect(PadX, y, fillW, TargetBarH),
                RadiusX = 3f, RadiusY = 3f
            }, _brushHpFill!);
        }

        string label = $"{_target.Name}   {FormatDamage(_target.CurrentHp)} / {FormatDamage(_target.MaxHp)}   {pct * 100:0.#}%";
        var textLayout = _ctx!.DWriteFactory.CreateTextLayout(label, _fontSmall!, w, TargetBarH);
        textLayout.TextAlignment = TextAlignment.Center;
        textLayout.ParagraphAlignment = ParagraphAlignment.Center;
        dc.DrawTextLayout(new Vector2(PadX, y), textLayout, _brushText!);
        textLayout.Dispose();

        return y + TargetBarH + 6f;
    }

    private void DrawRows(ID2D1DeviceContext dc, float startY)
    {
        float y = startY;
        float w = ClientSize.Width - PadX * 2;

        const float IconLane = IconSize + 8f;
        float barLeft  = PadX + IconLane;
        float barW     = w - IconLane;
        float bottom   = ClientSize.Height - PadBottom;

        // Top player = 100% bar fill; others scale relative to top.
        long maxDamage = _rows.Count > 0 ? _rows[0].Damage : 1;
        if (maxDamage <= 0) maxDamage = 1;

        _rowHitAreas.Clear();
        int idx = 0;

        foreach (var row in _rows)
        {
            if (y + RowH > bottom) break;

            // Job icon.
            float iconY = y + (RowH - IconSize) / 2f;
            var icon = _icons?.Get(row.JobIconKey);
            if (icon != null)
            {
                var saveTransform = dc.Transform;
                float scale = IconSize / icon.Size.Width;
                dc.Transform = Matrix3x2.CreateScale(scale)
                             * Matrix3x2.CreateTranslation(PadX, iconY);
                dc.DrawImage(icon, null, null, InterpolationMode.Linear, CompositeMode.SourceOver);
                dc.Transform = saveTransform;
            }
            else
            {
                _brushAccent!.Color = row.AccentColor;
                dc.FillEllipse(new Ellipse(new Vector2(PadX + IconSize / 2, iconY + IconSize / 2),
                                           IconSize / 2 - 2, IconSize / 2 - 2), _brushAccent);
            }

            // Background track.  Rect(x, y, width, height)
            dc.FillRoundedRectangle(new RoundedRectangle
            {
                Rect = new Rect(barLeft, y, barW, RowH),
                RadiusX = BarRadius, RadiusY = BarRadius
            }, _brushBarBg!);

            // Accent fill — relative to top player (top = 100%).
            float fillW = (float)Math.Clamp((double)row.Damage / maxDamage, 0, 1) * barW;
            if (fillW > 1)
            {
                _brushBarFill!.Color = new D2DColor(row.AccentColor.R, row.AccentColor.G, row.AccentColor.B, 0.90f);
                dc.FillRoundedRectangle(new RoundedRectangle
                {
                    Rect = new Rect(barLeft, y, fillW, RowH),
                    RadiusX = BarRadius, RadiusY = BarRadius
                }, _brushBarFill);
            }

            // Text content.
            float textLeft = barLeft + 8f;
            float numW     = barW - 16f;

            dc.DrawText(row.Name, _fontName!, new Rect(textLeft, y + 5, numW, 16), _brushText!,
                DrawTextOptions.None, MeasuringMode.Natural);

            var totalTextLayout = _ctx!.DWriteFactory.CreateTextLayout(
                FormatDamage(row.Damage), _fontNumber!, numW, 16);
            totalTextLayout.TextAlignment = TextAlignment.Trailing;
            dc.DrawTextLayout(new Vector2(textLeft, y + 4), totalTextLayout, _brushText!);
            totalTextLayout.Dispose();

            string secondary = $"{FormatDamage(row.DpsValue)}/s · {row.Percent * 100:0.#}% · crit {row.CritRate * 100:0}%";
            if (row.HealTotal > 0) secondary += $" · heal {FormatDamage(row.HealTotal)}";
            var dpsLayout = _ctx.DWriteFactory.CreateTextLayout(
                secondary, _fontSmall!, numW, 14);
            dpsLayout.TextAlignment = TextAlignment.Trailing;
            dc.DrawTextLayout(new Vector2(textLeft, y + RowH - 14 - 2), dpsLayout, _brushTextDim!);
            dpsLayout.Dispose();

            _rowHitAreas.Add((y, y + RowH, idx));
            y += RowH + RowGap;
            idx++;
        }

        if (_rows.Count == 0)
        {
            if (_summary != null) DrawSummary(dc, startY);
            else
            {
                float cy = ClientSize.Height / 2 - 12;
                float cw = ClientSize.Width - PadX * 2;
                var emptyLayout = _ctx!.DWriteFactory.CreateTextLayout(
                    "waiting for combat...", _fontSmall!, cw, 24);
                emptyLayout.TextAlignment = TextAlignment.Center;
                dc.DrawTextLayout(new Vector2(PadX, cy), emptyLayout, _brushTextDim!);
                emptyLayout.Dispose();
            }
        }
    }

    private void OnPlayerRowClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        float clickY = e.Location.Y;
        foreach (var (top, bottom, i) in _rowHitAreas)
        {
            if (clickY >= top && clickY < bottom && i < _rows.Count)
            {
                PlayerRowClicked?.Invoke(_rows[i]);
                break;
            }
        }
    }

    private void DrawSummary(ID2D1DeviceContext dc, float startY)
    {
        if (_summary is null) return;
        float w = ClientSize.Width - PadX * 2;
        const float CardH = 92f;

        dc.FillRoundedRectangle(new RoundedRectangle
        {
            Rect = new Rect(PadX, startY, w, CardH),
            RadiusX = 6f, RadiusY = 6f
        }, _brushBarBg!);

        var titleLayout = _ctx!.DWriteFactory.CreateTextLayout("Last fight", _fontName!, w - 12, 20);
        dc.DrawTextLayout(new Vector2(PadX + 8, startY + 6), titleLayout, _brushText!);
        titleLayout.Dispose();
        var durLayout = _ctx.DWriteFactory.CreateTextLayout($"{_summary.DurationSec:0}s", _fontSmall!, w - 12, 20);
        durLayout.TextAlignment = TextAlignment.Trailing;
        dc.DrawTextLayout(new Vector2(PadX + 4, startY + 8), durLayout, _brushTextDim!);
        durLayout.Dispose();

        string line1 = $"total {FormatDamage(_summary.TotalDamage)}   avg {FormatDamage(_summary.AverageDps)}/s   peak {FormatDamage(_summary.PeakDps)}/s";
        string line2 = !string.IsNullOrEmpty(_summary.TopActorName)
            ? $"{_summary.TopActorName}  {FormatDamage(_summary.TopActorDamage)}"
            : "";
        if (!string.IsNullOrEmpty(_summary.BossName)) line2 = (line2.Length > 0 ? line2 + "   " : "") + "vs " + _summary.BossName;

        var l1 = _ctx.DWriteFactory.CreateTextLayout(line1, _fontSmall!, w - 12, 18);
        dc.DrawTextLayout(new Vector2(PadX + 8, startY + 32), l1, _brushText!);
        l1.Dispose();

        if (line2.Length > 0)
        {
            var l2 = _ctx.DWriteFactory.CreateTextLayout(line2, _fontSmall!, w - 12, 18);
            dc.DrawTextLayout(new Vector2(PadX + 8, startY + 52), l2, _brushTextDim!);
            l2.Dispose();
        }

        var hint = _ctx.DWriteFactory.CreateTextLayout("waiting for next fight...", _fontSmall!, w - 12, 18);
        hint.TextAlignment = TextAlignment.Center;
        dc.DrawTextLayout(new Vector2(PadX + 4, startY + 72), hint, _brushTextDim!);
        hint.Dispose();
    }

    private static string FormatDamage(long v)
    {
        if (v >= 1_000_000_000) return (v / 1_000_000_000d).ToString("0.##") + "B";
        if (v >= 1_000_000)     return (v / 1_000_000d).ToString("0.##") + "M";
        if (v >= 1_000)         return (v / 1_000d).ToString("0.#") + "K";
        return v.ToString();
    }
}
