using System;
using Vortice.DCommon;
using Vortice.DXGI;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;

namespace A2Meter.Direct2D;

internal sealed class D2DContext : IDisposable
{
	private readonly nint _hwnd;

	private int _width;

	private int _height;

	public ID2D1Factory1 D2DFactory { get; }

	public IDWriteFactory DWriteFactory { get; }

	public ID3D11Device D3DDevice { get; private set; }

	public IDXGISwapChain1 SwapChain { get; private set; }

	public ID2D1Device D2DDevice { get; private set; }

	public ID2D1DeviceContext DC { get; private set; }

	public ID2D1Bitmap1 TargetBitmap { get; private set; }

	public bool IsWarp { get; private set; }

	public D2DContext(nint hwnd, int width, int height, bool forceWarp = false)
	{
		_hwnd = hwnd;
		_width = Math.Max(1, width);
		_height = Math.Max(1, height);
		D2DFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
		DWriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
		CreateDeviceAndSwapChain(forceWarp);
	}

	private void CreateDeviceAndSwapChain(bool forceWarp)
	{
		DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;
		Vortice.Direct3D.FeatureLevel[] featureLevels = new Vortice.Direct3D.FeatureLevel[4]
		{
			Vortice.Direct3D.FeatureLevel.Level_11_1,
			Vortice.Direct3D.FeatureLevel.Level_11_0,
			Vortice.Direct3D.FeatureLevel.Level_10_1,
			Vortice.Direct3D.FeatureLevel.Level_10_0
		};
		ID3D11Device device = null;
		if (!forceWarp && D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, featureLevels, out device).Failure)
		{
			device = null;
		}
		if (device == null)
		{
			D3D11.D3D11CreateDevice(null, DriverType.Warp, flags, featureLevels, out device).CheckError();
			IsWarp = true;
		}
		D3DDevice = device;
		using IDXGIDevice1 iDXGIDevice = D3DDevice.QueryInterface<IDXGIDevice1>();
		using IDXGIAdapter iDXGIAdapter = iDXGIDevice.GetAdapter();
		using IDXGIFactory2 iDXGIFactory = iDXGIAdapter.GetParent<IDXGIFactory2>();
		SwapChainDescription1 desc = new SwapChainDescription1
		{
			Width = (uint)_width,
			Height = (uint)_height,
			Format = Format.B8G8R8A8_UNorm,
			Stereo = false,
			SampleDescription = new SampleDescription(1u, 0u),
			BufferUsage = Usage.RenderTargetOutput,
			BufferCount = 2u,
			Scaling = Scaling.Stretch,
			SwapEffect = SwapEffect.FlipSequential,
			AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
			Flags = SwapChainFlags.None
		};
		SwapChain = iDXGIFactory.CreateSwapChainForHwnd(D3DDevice, _hwnd, desc);
		D2DDevice = D2DFactory.CreateDevice(iDXGIDevice);
		DC = D2DDevice.CreateDeviceContext(DeviceContextOptions.None);
		CreateBackBufferTarget();
	}

	private void CreateBackBufferTarget()
	{
		using IDXGISurface surface = SwapChain.GetBuffer<IDXGISurface>(0u);
		BitmapProperties1 value = new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), 96f, 96f, BitmapOptions.Target | BitmapOptions.CannotDraw);
		TargetBitmap = DC.CreateBitmapFromDxgiSurface(surface, value);
		DC.Target = TargetBitmap;
	}

	public void Resize(int width, int height)
	{
		if (width > 0 && height > 0 && (width != _width || height != _height))
		{
			_width = width;
			_height = height;
			DC.Target = null;
			TargetBitmap.Dispose();
			SwapChain.ResizeBuffers(0u, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None).CheckError();
			CreateBackBufferTarget();
		}
	}

	public void Present()
	{
		SwapChain.Present(0u, PresentFlags.None);
	}

	public void Dispose()
	{
		TargetBitmap?.Dispose();
		DC?.Dispose();
		D2DDevice?.Dispose();
		SwapChain?.Dispose();
		D3DDevice?.Dispose();
		DWriteFactory?.Dispose();
		D2DFactory?.Dispose();
	}
}
