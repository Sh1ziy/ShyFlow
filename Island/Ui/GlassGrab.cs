using System.Runtime.InteropServices;

namespace Island;

// Снимок того, что на экране под островом, сразу уменьшенный (уменьшение HALFTONE уже усредняет пиксели — это и есть
// основная часть размытия), плюс пара проходов box-blur и чуть больше насыщенности, как у стекла Apple.
// Само окно ShyFlow в снимок не попадает: на нём стоит WDA_EXCLUDEFROMCAPTURE (Main\Glass.cs).
// Обработка крошечной картинки быстрая, но сам снимок экрана дорогой: ~6 мс на быстром ПК, на слабом в разы больше.
// Поэтому Grab зовётся только из фонового потока (Main\Glass.cs), по одному вызову за раз.
internal sealed class GlassGrab : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr prev);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr sec, uint off);

    private const int HALFTONE = 4;
    private const uint SRCCOPY = 0x00CC0020;

    private IntPtr _mem, _dib, _old, _bits;
    private int _w, _h;
    private byte[] _buf = Array.Empty<byte>(), _tmp = Array.Empty<byte>(), _prev = Array.Empty<byte>();
    private readonly object _gate = new();
    private bool _disposed;

    public int Width => _w;
    public int Height => _h;
    public byte[] Pixels => _buf;   // BGRA, сверху вниз, stride = Width * 4

    // false — картинка не изменилась с прошлого раза (перерисовывать окно не нужно) или снять не получилось
    public bool Grab(int sx, int sy, int sw, int sh, int dw, int dh, int passes, double sat)
    {
        lock (_gate) return _disposed ? false : GrabCore(sx, sy, sw, sh, dw, dh, passes, sat);
    }

    private bool GrabCore(int sx, int sy, int sw, int sh, int dw, int dh, int passes, double sat)
    {
        if (sw < 2 || sh < 2 || dw < 2 || dh < 2) return false;
        if (!Ensure(dw, dh)) return false;
        IntPtr screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return false;
        bool ok;
        try
        {
            SetStretchBltMode(_mem, HALFTONE);
            SetBrushOrgEx(_mem, 0, 0, IntPtr.Zero);
            ok = StretchBlt(_mem, 0, 0, dw, dh, screen, sx, sy, sw, sh, SRCCOPY);
        }
        finally { ReleaseDC(IntPtr.Zero, screen); }
        if (!ok) return false;

        int len = dw * dh * 4;
        Marshal.Copy(_bits, _buf, 0, len);
        if (_prev.Length == len && Same(len)) return false;   // фон не поменялся (мигающий курсор и шум за перемену не считаем)
        Buffer.BlockCopy(_buf, 0, _prev, 0, len);

        for (int p = 0; p < passes; p++) { BoxH(dw, dh); BoxV(dw, dh); }
        Vibrance(len, sat);
        return true;
    }

    private bool Same(int len)
    {
        for (int i = 0; i < len; i++)
            if (Math.Abs(_buf[i] - _prev[i]) > 2) return false;
        return true;
    }

    private bool Ensure(int w, int h)
    {
        if (w == _w && h == _h && _dib != IntPtr.Zero) return true;
        Free();
        var bi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h,   // минус: строки сверху вниз
            biPlanes = 1, biBitCount = 32
        };
        IntPtr screen = GetDC(IntPtr.Zero);
        try
        {
            _mem = CreateCompatibleDC(screen);
            _dib = CreateDIBSection(screen, ref bi, 0, out _bits, IntPtr.Zero, 0);
        }
        finally { ReleaseDC(IntPtr.Zero, screen); }
        if (_mem == IntPtr.Zero || _dib == IntPtr.Zero) { Free(); return false; }
        _old = SelectObject(_mem, _dib);
        _w = w; _h = h;
        _buf = new byte[w * h * 4];
        _tmp = new byte[w * h * 4];
        _prev = new byte[w * h * 4];
        return true;
    }

    // размытие по горизонтали и вертикали (окно 3 пикселя, края повторяются)
    private void BoxH(int w, int h)
    {
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int a = row + Math.Max(0, x - 1) * 4, b = row + x * 4, c = row + Math.Min(w - 1, x + 1) * 4;
                for (int k = 0; k < 3; k++) _tmp[b + k] = (byte)((_buf[a + k] + 2 * _buf[b + k] + _buf[c + k]) >> 2);
            }
        }
        (_buf, _tmp) = (_tmp, _buf);
    }

    private void BoxV(int w, int h)
    {
        int stride = w * 4;
        for (int y = 0; y < h; y++)
        {
            int up = Math.Max(0, y - 1) * stride, mid = y * stride, dn = Math.Min(h - 1, y + 1) * stride;
            for (int i = 0; i < stride; i += 4)
                for (int k = 0; k < 3; k++) _tmp[mid + i + k] = (byte)((_buf[up + i + k] + 2 * _buf[mid + i + k] + _buf[dn + i + k]) >> 2);
        }
        (_buf, _tmp) = (_tmp, _buf);
    }

    // стекло Apple чуть «сочнее» фона под ним
    private void Vibrance(int len, double sat)
    {
        for (int i = 0; i < len; i += 4)
        {
            double b = _buf[i], g = _buf[i + 1], r = _buf[i + 2];
            double l = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            _buf[i] = (byte)Math.Clamp(l + (b - l) * sat, 0, 255);
            _buf[i + 1] = (byte)Math.Clamp(l + (g - l) * sat, 0, 255);
            _buf[i + 2] = (byte)Math.Clamp(l + (r - l) * sat, 0, 255);
            _buf[i + 3] = 255;
        }
    }

    private void Free()
    {
        if (_mem != IntPtr.Zero && _old != IntPtr.Zero) SelectObject(_mem, _old);
        if (_dib != IntPtr.Zero) DeleteObject(_dib);
        if (_mem != IntPtr.Zero) DeleteDC(_mem);
        _mem = _dib = _old = _bits = IntPtr.Zero;
        _w = _h = 0;
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; Free(); }
    }
}
