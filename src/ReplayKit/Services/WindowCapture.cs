using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WinRT;

namespace ReplayKit.Services;

/// <summary>Windows Graphics Capture for explicitly selected windows or monitors.</summary>
internal sealed class WindowCapture : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly IDirect3DDevice _runtimeDevice;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly GraphicsCaptureItem _item;
    private BitmapSource? _last;
    private volatile bool _closed;
    public WindowCapture(GraphicsCaptureItem item)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows Graphics Capture недоступен.");
        _item = item;
        Vortice.Direct3D11.D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_0 }, out _device, out ID3D11DeviceContext context).CheckError();
        context.Dispose();
        using var dxgi = _device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var pointer));
        try { _runtimeDevice = MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_runtimeDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        _session = _pool.CreateCaptureSession(item); item.Closed += OnClosed; _session.StartCapture();
    }
    private void OnClosed(GraphicsCaptureItem sender, object args) => _closed = true;
    public BitmapSource? Capture()
    {
        if (_closed) throw new InvalidOperationException("Выбранное окно закрыто. Выберите источник заново.");
        Windows.Graphics.SizeInt32 size;
        int surfaceWidth, surfaceHeight;
        using (var frame = _pool.TryGetNextFrame())
        {
            if (frame == null) return _last;
            size = frame.ContentSize;
            if (size.Width <= 0 || size.Height <= 0) return null;
            using var bitmap = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask().GetAwaiter().GetResult();
            surfaceWidth = bitmap.PixelWidth; surfaceHeight = bitmap.PixelHeight;
            var bytes = new byte[surfaceWidth * surfaceHeight * 4]; bitmap.CopyToBuffer(bytes.AsBuffer());
            var source = BitmapSource.Create(surfaceWidth, surfaceHeight, 96, 96, PixelFormats.Bgra32, null, bytes, surfaceWidth * 4); source.Freeze();
            var width = Math.Min(size.Width, surfaceWidth); var height = Math.Min(size.Height, surfaceHeight);
            _last = width == surfaceWidth && height == surfaceHeight ? source : Images.Crop(source, new(0, 0, width, height));
        }
        // All outstanding frames must be released before resizing the frame pool.
        if (size.Width != surfaceWidth || size.Height != surfaceHeight)
            _pool.Recreate(_runtimeDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
        return _last;
    }

    public void Dispose() { _item.Closed -= OnClosed; _session.Dispose(); _pool.Dispose(); _runtimeDevice.Dispose(); _device.Dispose(); }
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr device, out IntPtr graphicsDevice);
}
