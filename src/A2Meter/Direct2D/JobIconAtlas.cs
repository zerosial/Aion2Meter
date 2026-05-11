using System;
using System.Collections.Generic;
using System.IO;
using Vortice.Direct2D1;
using Vortice.WIC;

namespace A2Meter.Direct2D;

internal sealed class JobIconAtlas : IDisposable
{
	private readonly Dictionary<string, ID2D1Bitmap1> _bitmaps = new Dictionary<string, ID2D1Bitmap1>();

	private readonly IWICImagingFactory _wic;

	public JobIconAtlas(ID2D1DeviceContext dc)
	{
		_wic = new IWICImagingFactory();
		string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons");
		if (!Directory.Exists(path))
		{
			return;
		}
		foreach (string item in Directory.EnumerateFiles(path, "*.png"))
		{
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(item);
			try
			{
				ID2D1Bitmap1 iD2D1Bitmap = LoadBitmap(dc, item);
				if (iD2D1Bitmap != null)
				{
					_bitmaps[fileNameWithoutExtension] = iD2D1Bitmap;
				}
			}
			catch
			{
			}
		}
	}

	public ID2D1Bitmap1? Get(string jobName)
	{
		if (!_bitmaps.TryGetValue(jobName, out ID2D1Bitmap1 value))
		{
			return null;
		}
		return value;
	}

	private ID2D1Bitmap1? LoadBitmap(ID2D1DeviceContext dc, string path)
	{
		using IWICBitmapDecoder iWICBitmapDecoder = _wic.CreateDecoderFromFileName(path);
		using IWICBitmapFrameDecode iSource = iWICBitmapDecoder.GetFrame(0u);
		using IWICFormatConverter iWICFormatConverter = _wic.CreateFormatConverter();
		iWICFormatConverter.Initialize(iSource, PixelFormat.Format32bppPBGRA, BitmapDitherType.None, null, 0.0, BitmapPaletteType.Custom);
		return dc.CreateBitmapFromWicBitmap(iWICFormatConverter);
	}

	public void Dispose()
	{
		foreach (ID2D1Bitmap1 value in _bitmaps.Values)
		{
			value.Dispose();
		}
		_bitmaps.Clear();
		_wic.Dispose();
	}
}
