using System;
using SharpGen.Runtime;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using D2D = Vortice.Direct2D1;
using D3D11 = Vortice.Direct3D11;
using DW = Vortice.DirectWrite;

namespace A2Meter.Direct2D;

/// Owns the DXGI swap chain and Direct2D device context for a single HWND.
/// One context per DpsCanvas. Recreate target on resize.
internal sealed class D2DContext : IDisposable
{
    public ID2D1Factory1   D2DFactory   { get; }
    public IDWriteFactory  DWriteFactory { get; }
    public ID3D11Device    D3DDevice    { get; private set; } = null!;
    public IDXGISwapChain1 SwapChain    { get; private set; } = null!;
    public ID2D1Device     D2DDevice    { get; private set; } = null!;
    public ID2D1DeviceContext DC        { get; private set; } = null!;
    public ID2D1Bitmap1    TargetBitmap { get; private set; } = null!;

    private readonly IntPtr _hwnd;
    private int _width;
    private int _height;

    public D2DContext(IntPtr hwnd, int width, int height)
    {
        _hwnd   = hwnd;
        _width  = Math.Max(1, width);
        _height = Math.Max(1, height);

        D2DFactory    = D2D.D2D1.D2D1CreateFactory<ID2D1Factory1>(D2D.FactoryType.SingleThreaded);
        DWriteFactory = DW.DWrite.DWriteCreateFactory<IDWriteFactory>(DW.FactoryType.Shared);

        CreateDeviceAndSwapChain();
    }

    private void CreateDeviceAndSwapChain()
    {
        var creationFlags = DeviceCreationFlags.BgraSupport;

        D3D11.D3D11.D3D11CreateDevice(
            adapter: null,
            DriverType.Hardware,
            creationFlags,
            new[]
            {
                Vortice.Direct3D.FeatureLevel.Level_11_1,
                Vortice.Direct3D.FeatureLevel.Level_11_0,
                Vortice.Direct3D.FeatureLevel.Level_10_1,
                Vortice.Direct3D.FeatureLevel.Level_10_0,
            },
            out var device).CheckError();
        D3DDevice = device;

        using var dxgiDevice = D3DDevice.QueryInterface<IDXGIDevice1>();
        using var adapter    = dxgiDevice.GetAdapter();
        using var dxgiFactory = adapter.GetParent<IDXGIFactory2>();

        var swapDesc = new SwapChainDescription1
        {
            Width  = (uint)_width,
            Height = (uint)_height,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode  = Vortice.DXGI.AlphaMode.Ignore,
            Flags = SwapChainFlags.None,
        };
        SwapChain = dxgiFactory.CreateSwapChainForHwnd(D3DDevice, _hwnd, swapDesc);

        D2DDevice = D2DFactory.CreateDevice(dxgiDevice);
        DC        = D2DDevice.CreateDeviceContext(DeviceContextOptions.None);

        CreateBackBufferTarget();
    }

    private void CreateBackBufferTarget()
    {
        using var backBuffer = SwapChain.GetBuffer<IDXGISurface>(0);
        var props = new BitmapProperties1(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore),
            96f, 96f,
            BitmapOptions.Target | BitmapOptions.CannotDraw);
        TargetBitmap = DC.CreateBitmapFromDxgiSurface(backBuffer, props);
        DC.Target = TargetBitmap;
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (width == _width && height == _height) return;

        _width  = width;
        _height = height;

        DC.Target = null;
        TargetBitmap.Dispose();

        SwapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None).CheckError();
        CreateBackBufferTarget();
    }

    public void Present()
    {
        SwapChain.Present(0, PresentFlags.None);
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
