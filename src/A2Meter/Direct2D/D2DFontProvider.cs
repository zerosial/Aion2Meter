using System;
using Vortice.DirectWrite;
using DwFontStyle = Vortice.DirectWrite.FontStyle;

namespace A2Meter.Direct2D;

/// Picks the first installed font family from a priority list.
/// Web assets ship .woff2 files, but DirectWrite only consumes OpenType (TTF/OTF).
/// Decoding woff2 (Brotli + SFNT container) into an in-memory IDWriteFontCollection
/// is a separate, larger task; until then we rely on what the user has installed.
internal sealed class D2DFontProvider
{
    private readonly IDWriteFactory _factory;
    private readonly IDWriteFontCollection _system;

    public D2DFontProvider(IDWriteFactory factory)
    {
        _factory = factory;
        _system  = factory.GetSystemFontCollection(false);
    }

    /// Returns a TextFormat using the first installed family in `candidates`.
    /// Falls back to "Segoe UI" if none are present.
    public IDWriteTextFormat CreateTextFormat(
        string[] candidates,
        FontWeight weight,
        DwFontStyle style,
        float size)
    {
        var family = ResolveFirstAvailable(candidates) ?? "Segoe UI";
        return _factory.CreateTextFormat(family, weight, style, size);
    }

    /// Common A2Power Korean UI font stack: Gmarket Sans → LINE Seed Sans KR → Malgun Gothic.
    public IDWriteTextFormat CreateUiName(float size, FontWeight weight = FontWeight.SemiBold)
        => CreateTextFormat(new[] { "Gmarket Sans", "LINE Seed Sans KR", "Malgun Gothic" }, weight, DwFontStyle.Normal, size);

    /// Numeric/monospace stack (matches original `Orbit, Consolas, monospace`).
    public IDWriteTextFormat CreateNumeric(float size, FontWeight weight = FontWeight.Bold)
        => CreateTextFormat(new[] { "Orbit", "Consolas", "Cascadia Mono" }, weight, DwFontStyle.Normal, size);

    private string? ResolveFirstAvailable(string[] families)
    {
        foreach (var name in families)
        {
            _system.FindFamilyName(name, out uint index);
            if (index != uint.MaxValue) return name;
        }
        return null;
    }
}
