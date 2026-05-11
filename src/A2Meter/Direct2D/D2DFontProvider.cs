using System;
using System.Collections.Generic;
using A2Meter.Core;
using Vortice.DirectWrite;

namespace A2Meter.Direct2D;

internal sealed class D2DFontProvider
{
	private readonly IDWriteFactory _factory;

	private readonly IDWriteFontCollection _system;

	public D2DFontProvider(IDWriteFactory factory)
	{
		_factory = factory;
		_system = factory.GetSystemFontCollection(false);
	}

	public IDWriteTextFormat Create(string family, FontWeight weight, float size)
	{
		string fontFamilyName = Resolve(family) ?? Resolve("Malgun Gothic") ?? "Segoe UI";
		return _factory.CreateTextFormat(fontFamilyName, weight, Vortice.DirectWrite.FontStyle.Normal, size);
	}

	public IDWriteTextFormat CreateUi(float size, string? userFont = null, FontWeight? weightOverride = null)
	{
		AppSettings instance = AppSettings.Instance;
		string family = userFont ?? instance.FontName;
		FontWeight weight = (FontWeight)(((int?)weightOverride) ?? instance.FontWeight);
		return Create(family, weight, size);
	}

	public string[] GetFamilyNames()
	{
		int fontFamilyCount = (int)_system.FontFamilyCount;
		string[] array = new string[fontFamilyCount];
		for (int i = 0; i < fontFamilyCount; i++)
		{
			using IDWriteFontFamily iDWriteFontFamily = _system.GetFontFamily((uint)i);
			using IDWriteLocalizedStrings iDWriteLocalizedStrings = iDWriteFontFamily.FamilyNames;
			iDWriteLocalizedStrings.FindLocaleName("en-us", out var index);
			if (index == uint.MaxValue)
			{
				index = 0u;
			}
			array[i] = iDWriteLocalizedStrings.GetString(index);
		}
		Array.Sort(array, (IComparer<string>?)StringComparer.OrdinalIgnoreCase);
		return array;
	}

	private string? Resolve(string family)
	{
		_system.FindFamilyName(family, out var index);
		if (index == uint.MaxValue)
		{
			return null;
		}
		return family;
	}
}
