using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island;

// Прогресс в виде волны из полосок, как у голосовых сообщений в Telegram.
// Высоты полосок считаются по названию трека (у каждой песни своя волна), пройденная часть красится цветом Fill,
// остальное тусклое. Клики обрабатывает хозяин (полоска сама мышь не ловит).
internal sealed class WaveBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(WaveBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    // 0..1: полоски вырастают слева направо при смене трека
    public static readonly DependencyProperty GrowProperty = DependencyProperty.Register(
        nameof(Grow), typeof(double), typeof(WaveBar),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(WaveBar),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, Math.Clamp(value, 0, 1)); }
    public double Grow { get => (double)GetValue(GrowProperty); set => SetValue(GrowProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    // 3.9.4: вид полоски (Настройки, «Интерфейс» → «Полоска перемотки»): 0 волна из столбиков, 1 точки, 2 змейка
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(int), typeof(WaveBar), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    // фаза змейки: двигается, пока трек играет (MainWindow.UpdateProgress, ~15 раз в секунду и только в раскрытом острове)
    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(double), typeof(WaveBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Mode { get => (int)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public double Phase { get => (double)GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }

    private const int Base = 240;
    private readonly double[] _h = new double[Base];
    private string _seed = "\u0001";

    public WaveBar()
    {
        IsHitTestVisible = false;
        Generate("");
    }

    public void SetSeed(string seed)
    {
        if (seed == _seed) return;
        _seed = seed;
        Generate(seed);
        BeginAnimation(GrowProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(750))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        InvalidateVisual();
    }

    private void Generate(string seed)
    {
        uint hash = 2166136261;
        foreach (char c in seed) hash = (hash ^ (uint)c) * 16777619u;
        var rnd = new Random(unchecked((int)hash));
        var a = new double[Base];
        for (int i = 0; i < Base; i++) a[i] = rnd.NextDouble();
        double p1 = rnd.NextDouble() * 6.28, p2 = rnd.NextDouble() * 6.28, p3 = rnd.NextDouble() * 6.28;
        for (int i = 0; i < Base; i++)
        {
            double t = i / (double)Base;
            double env = 0.55 + 0.22 * Math.Sin(t * 9 + p1) + 0.14 * Math.Sin(t * 23 + p2) + 0.09 * Math.Sin(t * 51 + p3);
            double sm = 0.5 * a[i] + 0.25 * a[(i + Base - 1) % Base] + 0.25 * a[(i + 1) % Base];
            double v = 0.12 + 0.88 * Math.Clamp(env * 0.62 + sm * 0.75, 0, 1);
            _h[i] = Math.Clamp(v, 0.16, 1.0);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 6 || h < 6) return;
        if (Mode == 2) { RenderSquiggle(dc, w, h); return; }
        if (Mode == 1) { RenderDots(dc, w, h); return; }
        double bw = Math.Max(2, Math.Min(4, h * 0.14));
        double gap = bw * 0.85;
        int n = Math.Max(1, (int)((w + gap) / (bw + gap)));
        double total = n * bw + (n - 1) * gap;
        double x0 = (w - total) / 2;
        double played = Value * n;
        double grow = Grow;
        var fill = Fill;

        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1) dc.PushOpacity(0.3);
            for (int i = 0; i < n; i++)
            {
                bool isPlayed = i + 0.5 <= played;
                if (isPlayed == (pass == 1)) continue;
                int idx = Math.Min(Base - 1, (int)(i * (double)Base / n));
                double g = Math.Clamp(grow * 1.6 - (double)i / n * 0.6, 0, 1);
                double bh = Math.Max(bw, _h[idx] * h * g);
                dc.DrawRoundedRectangle(fill, null, new Rect(x0 + i * (bw + gap), (h - bh) / 2, bw, bh), bw / 2, bw / 2);
            }
            if (pass == 1) dc.Pop();
        }
    }

    // точки: ровный ряд, пройденные — цветом интерфейса, остальные тусклые; текущая чуть крупнее
    private void RenderDots(DrawingContext dc, double w, double h)
    {
        double d = Math.Clamp(h * 0.22, 3, 5), gap = d * 1.1;
        int n = Math.Max(1, (int)((w + gap) / (d + gap)));
        double x0 = (w - (n * d + (n - 1) * gap)) / 2, played = Value * n, grow = Grow;
        for (int i = 0; i < n; i++)
        {
            bool on = i + 0.5 <= played, head = (int)played == i;
            double g = Math.Clamp(grow * 1.6 - (double)i / n * 0.6, 0, 1);
            double r = d / 2 * (head ? 1.45 : 1) * (0.4 + 0.6 * g);
            if (!on && !head) dc.PushOpacity(0.3);
            dc.DrawEllipse(Fill, null, new Point(x0 + i * (d + gap) + d / 2, h / 2), r, r);
            if (!on && !head) dc.Pop();
        }
    }

    // змейка (как в Android 13): пройденная часть — волнистая линия, которая течёт, пока трек играет; остальное — прямая
    private void RenderSquiggle(DrawingContext dc, double w, double h)
    {
        double t = 3, pad = t / 2, x1 = pad + (w - t) * Value, amp = Math.Min(h * 0.18, 3.2) * Math.Clamp(Grow, 0, 1), len = 22;
        var dim = new Pen(Fill, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.PushOpacity(0.3);
        if (w - pad > x1 + 4) dc.DrawLine(dim, new Point(x1 + 4, h / 2), new Point(w - pad, h / 2));
        dc.Pop();
        if (x1 <= pad + 0.5) return;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(pad, h / 2 + amp * Math.Sin(Phase)), false, false);
            for (double x = pad + 1.5; x < x1; x += 1.5)
                c.LineTo(new Point(x, h / 2 + amp * Math.Sin((x - pad) / len * 2 * Math.PI + Phase)), true, true);
            c.LineTo(new Point(x1, h / 2 + amp * Math.Sin((x1 - pad) / len * 2 * Math.PI + Phase)), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, new Pen(Fill, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, g);
        dc.DrawEllipse(Fill, null, new Point(x1, h / 2), 2.2, 6);   // вертикальная «ручка», как у Android
    }
}
