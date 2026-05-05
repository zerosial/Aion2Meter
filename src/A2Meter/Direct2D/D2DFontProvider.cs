using System;
using Vortice.DirectWrite;
using DwFontStyle = Vortice.DirectWrite.FontStyle;

namespace A2Meter.Direct2D;

/// Font provider for DirectWrite. Uses IDWriteFontCollection to enumerate
/// system fonts by family name, separate from weight selection.
internal sealed class D2DFontProvider
{
    private readonly IDWriteFactory _factory;
    private readonly IDWriteFontCollection _system;

    public D2DFontProvider(IDWriteFactory factory)
    {
        _factory = factory;
        _system  = factory.GetSystemFontCollection(false);
    }

    /// Creates a TextFormat with the specified family, weight, and size.
    public IDWriteTextFormat Create(string family, FontWeight weight, float size)
    {
        // Verify family exists; fallback to Malgun Gothic → Segoe UI.
        string resolved = Resolve(family) ?? Resolve("Malgun Gothic") ?? "Segoe UI";
        return _factory.CreateTextFormat(resolved, weight, DwFontStyle.Normal, size);
    }

    /// UI font shorthand using AppSettings values.
    public IDWriteTextFormat CreateUi(float size, string? userFont = null, FontWeight? weightOverride = null)
    {
        var s = Core.AppSettings.Instance;
        string family = userFont ?? s.FontName;
        var weight = weightOverride ?? (FontWeight)s.FontWeight;
        return Create(family, weight, size);
    }

    /// Get all DirectWrite font family names for the settings UI.
    public string[] GetFamilyNames()
    {
        int count = (int)_system.FontFamilyCount;
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            using var fam = _system.GetFontFamily((uint)i);
            using var locNames = fam.FamilyNames;
            // Prefer en-us, fallback to index 0.
            locNames.FindLocaleName("en-us", out uint nameIdx);
            if (nameIdx == uint.MaxValue) nameIdx = 0;
            names[i] = locNames.GetString(nameIdx);
        }
        Array.Sort(names, StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private string? Resolve(string family)
    {
        _system.FindFamilyName(family, out uint index);
        return index != uint.MaxValue ? family : null;
    }
}
