using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Island;

// 3.9.4: Liquid Glass — остров из настоящего стекла (Настройки, «Интерфейс»).
//
// Под островом видно размытое то, что на экране за ним: окна, обои, видео. Прозрачное окно WPF размывать фон не умеет,
// поэтому остров сам снимает экран под собой (Ui\GlassGrab.cs), размывает и рисует снимок внутри себя, а поверх — тонировку
// (ползунок «Прозрачность»), блик сверху и световую кромку (Chrome.cs). Чтобы в снимок не попадал сам остров, окну ставится
// WDA_EXCLUDEFROMCAPTURE (Windows 10 2004+): побочный эффект — пока стекло включено, остров не виден на скриншотах,
// в записи экрана и в OBS. Об этом сказано в настройках.
//
// Нагрузка: снимок экрана дорогой, поэтому он идёт в фоновом потоке и не держит анимации острова. Обычно 12 раз в секунду,
// в экономном режиме 4, на статичном фоне 2,5. Если снимок медленный (слабый ПК), пауза растёт сама: на снимки уходит не больше
// ~15 % времени. Пока остров меняет размер, снимок заморожен: растянуть готовую картинку почти ничего не стоит. На весь экран
// (игра, видео) снимков нет вообще, даже если «Прятать в играх» выключено. Окно перерисовывается, только если фон поменялся.
public partial class MainWindow
{
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
    private const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;

    private Grid? _glassLayer;
    private Border? _glassImg, _glassTint;
    private WriteableBitmap? _glassWb;
    private GlassGrab? _grab;
    private DispatcherTimer? _glassTimer;
    private bool _glassApplied, _glassFailed, _glassBusy;
    private int _glassSame;
    private DateTime _glassNext = DateTime.MinValue;

    internal bool GlassOn => _cfg.Glass && !_glassFailed;
    private static readonly SolidColorBrush GlassSide = SafeBrush.Solid(0xC8, 0x14, 0x14, 0x16);

    // зовётся из ApplySettings и при смене экономного режима
    private void ApplyGlass()
    {
        bool on = GlassOn;
        if (on && _glassLayer == null) BuildGlass();
        if (_hwnd != IntPtr.Zero && on != _glassApplied)
        {
            if (!SetWindowDisplayAffinity(_hwnd, on ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE) && on)
            {
                // старая Windows: окно нельзя исключить из снимка, стекло показывало бы само себя
                _glassFailed = true; on = false;
                App.Log("Glass", new InvalidOperationException("SetWindowDisplayAffinity failed: " + Marshal.GetLastWin32Error()));
            }
            _glassApplied = on;
        }
        if (_glassLayer != null) _glassLayer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        // фон из обложки закрыл бы стекло целиком: в стекле его роль играет лёгкий цветной оттенок тонировки
        BgImg.Visibility = BgImgOld.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (_glassTint != null)
        {
            double k = Math.Clamp(_cfg.GlassClear, 0, 1);
            var c = _cfg.CoverTint && _lastCover != null ? Mix(Colors.Black, _accent, 0.18) : Colors.Black;
            _glassTint.Background = SafeBrush.Solid(c);
            _glassTint.Opacity = 0.82 - 0.7 * k;   // 0 → почти чёрное стекло, 1 → почти прозрачное
        }
        if (on)
        {
            _glassTimer ??= MakeGlassTimer();
            _glassNext = DateTime.MinValue;
            _glassTimer.Start();
            GlassTick();
        }
        else _glassTimer?.Stop();
        ApplyTheme();   // фон острова: прозрачный в стекле, чёрный без него (Appearance.cs)
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private DispatcherTimer MakeGlassTimer()
    {
        var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        t.Tick += (_, _) => GlassTick();
        Closed += (_, _) => { t.Stop(); _grab?.Dispose(); };
        return t;
    }

    private void BuildGlass()
    {
        _glassImg = new Border();
        _glassTint = new Border();
        // блик: свет падает сверху, у Apple верхняя треть стекла чуть светлее
        var spec = new Border
        {
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x2A, 255, 255, 255), 0), new(Color.FromArgb(0x08, 255, 255, 255), 0.35), new(Color.FromArgb(0, 255, 255, 255), 0.6)
            }, 90)
        };
        spec.Background.Freeze();
        _glassLayer = new Grid { IsHitTestVisible = false };
        _glassLayer.Children.Add(_glassImg);
        _glassLayer.Children.Add(_glassTint);
        _glassLayer.Children.Add(spec);
        PillContent.Children.Insert(0, _glassLayer);   // под всем содержимым; обрезается по форме острова вместе с ним
        _grab = new GlassGrab();
    }

    private void GlassTick()
    {
        if (_glassImg == null || _grab == null || !GlassOn || _hidden || _fsMini || _fsAny || _turbo || !IsVisible || Pill.ActualWidth < 4) return;
        if (_glassBusy || DateTime.UtcNow < _glassNext) return;
        if (!_w.Settled || !_h.Settled) return;   // остров меняет размер: снимок заморожен, снимем, когда встанет
        try
        {
            var p0 = Pill.PointToScreen(new Point(0, 0));
            var p1 = Pill.PointToScreen(new Point(Pill.ActualWidth, Pill.ActualHeight));
            int sx = (int)Math.Round(Math.Min(p0.X, p1.X)), sy = (int)Math.Round(Math.Min(p0.Y, p1.Y));
            int sw = (int)Math.Round(Math.Abs(p1.X - p0.X)), sh = (int)Math.Round(Math.Abs(p1.Y - p0.Y));
            double blur = Math.Clamp(_cfg.GlassBlur, 0, 1);
            double scale = 1 / (3 + 9 * blur);   // сильнее размытие — мельче снимок
            int dw = Math.Max(4, (int)Math.Round(sw * scale)), dh = Math.Max(3, (int)Math.Round(sh * scale));
            int passes = 1 + (int)Math.Round(blur * 2);
            var grab = _grab;
            _glassBusy = true;
            Task.Run(() =>
            {
                var sw0 = System.Diagnostics.Stopwatch.StartNew();
                bool changed = false;
                try { changed = grab.Grab(sx, sy, sw, sh, dw, dh, passes, 1.35); }
                catch (Exception ex) { App.LogOnce("Glass.Grab", ex); }
                double ms = sw0.Elapsed.TotalMilliseconds;
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => GlassDone(changed, dw, dh, ms)));
            });
        }
        catch (Exception ex) { _glassBusy = false; App.LogOnce("Glass.Tick", ex); }
    }

    // Снимок готов. Следующего ещё нет (_glassBusy), поэтому буфер GlassGrab можно читать здесь без блокировки
    private void GlassDone(bool changed, int dw, int dh, double ms)
    {
        _glassBusy = false;
        _glassSame = changed ? 0 : _glassSame + 1;
        double wait = Math.Max(_eco ? 250 : 80, ms * 6);
        if (_glassSame >= 10) wait = Math.Max(wait, 400);
        _glassNext = DateTime.UtcNow.AddMilliseconds(wait);
        if (!changed || _glassImg == null || _grab == null || !GlassOn) return;
        try
        {
            if (_glassWb == null || _glassWb.PixelWidth != dw || _glassWb.PixelHeight != dh)
            {
                _glassWb = new WriteableBitmap(dw, dh, 96, 96, PixelFormats.Bgra32, null);
                var brush = new ImageBrush(_glassWb) { Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);   // плавное увеличение крошечного снимка
                _glassImg.Background = brush;
            }
            _glassWb.WritePixels(new Int32Rect(0, 0, dw, dh), _grab.Pixels, dw * 4, 0);
        }
        catch (Exception ex) { App.LogOnce("Glass.Done", ex); }
    }
}
