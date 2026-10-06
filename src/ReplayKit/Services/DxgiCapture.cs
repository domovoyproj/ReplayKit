using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace ReplayKit.Services;

public sealed class DxgiCapture : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;
    private CaptureFrame? _last;
    private BitmapSource? _lastBitmap;
    private BitmapSource? _encodedBitmap;
    private string? _monitorId;
    private ModeRotation _rotation;

    private void Initialize(string monitorId)
    {
        Dispose();
        using var factory = CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            using (adapter)
            {
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    using (output)
                    {
                        if (output.Description.DeviceName != monitorId) continue;
                        using var output1 = output.QueryInterface<IDXGIOutput1>();
                        D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                            new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 }, out _device, out _context).CheckError();
                        _duplication = output1.DuplicateOutput(_device);
                        _rotation = output.Description.Rotation;
                        _monitorId = monitorId;
                        return;
                    }
                }
            }
        }
        throw new InvalidOperationException("Монитор отключён. Выберите доступный монитор.");
    }

    public CaptureFrame? Capture(string monitorId, DateTimeOffset at)
    {
        var bitmap = CaptureBitmap(monitorId);
        if (bitmap == null) return null;
        if (!ReferenceEquals(bitmap, _encodedBitmap))
        {
            _last = new CaptureFrame(at, monitorId, bitmap.PixelWidth, bitmap.PixelHeight, Images.EncodeJpeg(bitmap));
            _encodedBitmap = bitmap;
        }
        return _last! with { CapturedAt = at };
    }

    public BitmapSource? CaptureBitmap(string monitorId, uint timeoutMilliseconds = 100)
    {
        if (_duplication == null || _monitorId != monitorId) Initialize(monitorId);
        // Pointer-only notifications do not contain a newly presented desktop surface.
        // In particular, the first such surface may be uninitialized (all black).
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var result = _duplication!.AcquireNextFrame(attempt == 0 ? timeoutMilliseconds : (_lastBitmap == null ? Math.Min(timeoutMilliseconds, 16) : 0u), out var frameInfo, out var resource);
            if (result.Code == unchecked((int)0x887A0027)) break; // DXGI_ERROR_WAIT_TIMEOUT: unchanged desktop.
            result.CheckError();
            try
            {
                using (resource)
                {
                    if (frameInfo.LastPresentTime == 0)
                    {
                        continue;
                    }
                    using var texture = resource.QueryInterface<ID3D11Texture2D>();
                    var desc = texture.Description;
                    if (_staging == null || _staging.Description.Width != desc.Width || _staging.Description.Height != desc.Height)
                    {
                        _staging?.Dispose();
                        desc.Usage = ResourceUsage.Staging; desc.BindFlags = BindFlags.None;
                        desc.CPUAccessFlags = CpuAccessFlags.Read; desc.MiscFlags = ResourceOptionFlags.None;
                        _staging = _device!.CreateTexture2D(desc);
                    }
                    _context!.CopyResource(_staging, texture);
                    var map = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    try
                    {
                        int width = (int)desc.Width, height = (int)desc.Height, stride = width * 4;
                        var pixels = new byte[stride * height];
                        for (var y = 0; y < height; y++) Marshal.Copy(map.DataPointer + y * (int)map.RowPitch, pixels, y * stride, stride);
                        BitmapSource latest = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
                        var angle = _rotation switch { ModeRotation.Rotate90 => 90, ModeRotation.Rotate180 => 180, ModeRotation.Rotate270 => 270, _ => 0 };
                        if (angle != 0) latest = new TransformedBitmap(latest, new RotateTransform(angle));
                        latest.Freeze();
                        _lastBitmap = latest;
                    }
                    finally { _context.Unmap(_staging, 0); }
                }
            }
            finally { _duplication.ReleaseFrame().CheckError(); }
        }
        return _lastBitmap;
    }
    public void Dispose()
    {
        _staging?.Dispose(); _duplication?.Dispose(); _context?.Dispose(); _device?.Dispose();
        _staging = null; _duplication = null; _context = null; _device = null; _last = null; _monitorId = null; _lastBitmap = _encodedBitmap = null;
    }
}
