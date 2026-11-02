using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Island;

// Шаг 80: плеер на весь экран вылетает из острова и залетает обратно.
// Окно плеера непрозрачное и во весь экран (так оно быстрое), менять его размер на каждом кадре нельзя — дёргается.
// Поэтому переход рисует отдельное прозрачное окно поверх всего, только на время перехода (меньше секунды):
// капля цвета обложки (тот же фон, что у плеера) растёт из острова до всего экрана. Ширина растёт быстрее высоты —
// капля «вытекает» вниз, как сам остров; углы из капсулы становятся прямыми, чёрный цвет острова перетекает в обложку.
// Открытие: капля доросла — под ней уже плеер, капля растворяется. Закрытие: капля во весь экран поверх плеера,
// плеер закрывается, капля сжимается в остров (а он в это время съезжает на место) и гаснет.
// Клики сквозь окно проходят (WS_EX_TRANSPARENT), фокус оно не забирает.
internal sealed class IslandMorph : Window
{
    private readonly Grid _blob = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Border _black = new() { Background = Brushes.Black };
    private readonly Rect _island, _full;
    private readonly bool _open;
    private readonly Action? _atFull;
    private TimeSpan _t0;
    private bool _hooked, _fading, _holding;
    private const double OpenMs = 520, CloseMs = 430, FadeMs = 200;

    private IslandMorph(Rect monitor, Rect island, Brush fill, bool open, Action? atFull)
    {
        Left = monitor.Left; Top = monitor.Top; Width = monitor.Width; Height = monitor.Height;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false;
        IsHitTestVisible = false;
        _island = island;
        _full = new Rect(0, 0, monitor.Width, monitor.Height);
        _open = open;
        _atFull = atFull;
        _blob.Children.Add(new Border { Background = fill });
        var shade = new LinearGradientBrush(Color.FromArgb(0x30, 0, 0, 0), Color.FromArgb(0x90, 0, 0, 0), 90);   // как затемнение плеера
        shade.Freeze();
        _blob.Children.Add(new Border { Background = shade });
        _blob.Children.Add(_black);
        Content = new Canvas { Children = { _blob } };
        Apply(open ? 0 : 1);
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20 | 0x80 | 0x08000000);   // прозрачен для мыши, без кнопки, без фокуса
        };
        Loaded += (_, _) =>
        {
            _hooked = true;
            CompositionTarget.Rendering += Frame;
            // страховка: если кадры перестанут приходить (капля перестала меняться), переход всё равно закончится
            var safe = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds((open ? OpenMs : CloseMs) + 120) };
            safe.Tick += (_, _) => { safe.Stop(); Finish(TimeSpan.Zero); };
            safe.Start();
        };
        Closed += (_, _) => { if (_hooked) CompositionTarget.Rendering -= Frame; };
    }

    // открыть: из острова (rect в DIP относительно монитора) на весь монитор. atFull — капля заняла экран: показать плеер
    // и, когда он нарисуется, вызвать Release (капля растворится). null — перехода не будет (ошибка), atFull уже вызван
    public static IslandMorph? Open(Rect monitor, Rect island, Brush fill, Action atFull)
    {
        try { var m = new IslandMorph(monitor, island, fill, true, atFull); m.Show(); return m; }
        catch (Exception ex) { App.LogOnce("Morph.Open", ex); atFull(); return null; }
    }

    // плеер под каплей нарисован: капля растворяется
    public void Release()
    {
        if (_fading) return;
        _fading = true;
        _t0 = TimeSpan.Zero;
        // и окно закрывается, даже если кадры перестанут приходить
        var end = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FadeMs + 300) };
        end.Tick += (_, _) => { end.Stop(); if (IsLoaded) Close(); };
        end.Start();
    }

    // закрыть: со всего экрана в остров. shown — капля уже нарисована поверх плеера, его можно закрывать (без мелькания стола)
    public static void Back(Rect monitor, Rect island, Brush fill, Action shown)
    {
        try
        {
            var m = new IslandMorph(monitor, island, fill, false, null);
            bool done = false;
            m.ContentRendered += (_, _) => { if (!done) { done = true; shown(); } };
            m.Show();
        }
        catch (Exception ex) { App.LogOnce("Morph.Close", ex); shown(); }
    }

    // открытие: мягкий старт (видно, что растёт из острова) и долгое плавное торможение у краёв экрана
    private static double EaseOut(double t) { t = Math.Pow(Math.Clamp(t, 0, 1), 0.8); return t * t * t * (t * (6 * t - 15) + 10); }   // smootherstep
    private static double EaseInOut(double t) { t = Math.Clamp(t, 0, 1); return t < 0.5 ? 8 * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 4) / 2; }

    private void Frame(object? s, EventArgs e)
    {
        if (e is not RenderingEventArgs re || (_holding && !_fading)) return;
        if (_t0 == TimeSpan.Zero) _t0 = re.RenderingTime;
        double ms = (re.RenderingTime - _t0).TotalMilliseconds;
        if (_fading)
        {
            Opacity = Math.Clamp(1 - ms / FadeMs, 0, 1);
            if (ms >= FadeMs) Close();
            return;
        }
        double t = ms / (_open ? OpenMs : CloseMs);
        // p: 0 — остров, 1 — весь экран; ширина ведёт, высота догоняет (капля вытекает вниз)
        double pw, ph;
        if (_open) { pw = EaseOut(t * 1.18); ph = EaseOut(t); }
        else { double u = 1 - Math.Clamp(t, 0, 1); pw = EaseInOut(Math.Min(1, u * 1.15)); ph = EaseInOut(u); }
        Apply(pw, ph);
        if (t >= 1) Finish(re.RenderingTime);
    }

    private void Finish(TimeSpan at)
    {
        if (_fading || _holding) return;
        Apply(_open ? 1 : 0);
        if (_open)
        {
            // открытие: капля держит экран, пока плеер под ней не нарисуется (Release), но не дольше 1,5 с
            _holding = true;
            var guard = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            guard.Tick += (_, _) => { guard.Stop(); Release(); };
            guard.Start();
            _atFull?.Invoke();
            return;
        }
        Release();
        _t0 = at;   // растворение — с этого кадра (TimeSpan.Zero: со следующего)
    }

    private void Apply(double p) => Apply(p, p);

    private void Apply(double pw, double ph)
    {
        double w = _island.Width + (_full.Width - _island.Width) * pw;
        double h = _island.Height + (_full.Height - _island.Height) * ph;
        // по горизонтали от центра острова к центру экрана, по вертикали — сверху вниз
        double cx = _island.X + _island.Width / 2 + (_full.Width / 2 - (_island.X + _island.Width / 2)) * pw;
        double y = _island.Y + (_full.Y - _island.Y) * ph;
        // углы: у острова капсула, посередине пути крупные (форма остаётся жидкой), у края экрана прямые
        double pm = Math.Min(pw, ph);
        double r = _island.Height / 2 * (1 - pm) + 44 * Math.Sin(Math.PI * Math.Clamp(pm, 0, 1));
        _blob.Width = Math.Max(1, w);
        _blob.Height = Math.Max(1, h);
        Canvas.SetLeft(_blob, cx - w / 2);
        Canvas.SetTop(_blob, y);
        _blob.Clip = Smooth.Shape(w, h, new CornerRadius(Math.Min(r, Math.Min(w, h) / 2)));   // замороженная фигура на кадр
        _black.Opacity = Math.Clamp(1 - Math.Min(pw, ph) * 2.2, 0, 1);   // чёрный остров перетекает в цвет обложки
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
}
