using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Island;

// Шаг 77: плеер на весь экран, как в Apple Music.
//   Живой фон: поверх размытой обложки медленно плавают четыре большие мягкие «капли» цветов этой обложки,
//     и фон всё время чуть переливается. Цвета берутся из самой обложки (Palette) и плавно перетекают при смене трека.
//     Настройки, «Плеер», «Живой фон» (вкл); в экономном режиме не двигается.
//   Бэк-вокал: то, что в строке в скобках («(oh-oh)»), стоит мелко под строкой, а не в ней (SplitBacking).
//   Поделиться строчкой: при наведении на строку справа (у второго голоса слева) кнопка; клик — картинка для сторис
//     со строкой крупно, обложкой и цветами трека (StoryCards.LyricCard), сразу в буфере.
public sealed partial class PlayerWindow
{
    private readonly Canvas _live = new() { IsHitTestVisible = false, ClipToBounds = true };
    private sealed class Blob
    {
        public Ellipse E = null!;
        public Color From = Colors.White, To = Colors.White, Now = Colors.White;   // цвет капли: переход от прошлой обложки к новой
        public readonly TranslateTransform T = new();
        public readonly ScaleTransform S = new(1, 1);
    }

    // шаг 79: капля красится новой замороженной кистью на каждом шаге перехода, а не перекраской стопов градиента
    // (изменяемые кисти роняли поток отрисовки WPF, см. Ui\LiveBrush.cs)
    private static Brush BlobBrush(Color c)
    {
        var br = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xC0, c.R, c.G, c.B), 0),
                new GradientStop(Color.FromArgb(0x50, 255, 255, 255), 0.45),
                new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1)
            }
        };
        br.Freeze();
        return br;
    }
    private readonly List<Blob> _blobs = new();
    private double _liveW = -1, _liveH = -1;
    private bool _liveMoving;
    // шаг 78: размытая обложка под каплями тоже медленно плывёт, а слой капель поворачивается — фон «течёт», как в Apple Music
    private readonly ScaleTransform _bgScale = new(1.3, 1.3);
    private readonly RotateTransform _bgRot = new(0);
    private readonly RotateTransform _liveRot = new(0);

    private void BuildLive()
    {
        Panel.SetZIndex(_live, 1);   // над размытой обложкой (она тоже на 1, но раньше в списке), под затемнением (2)
        for (int i = 0; i < 4; i++)
        {
            var b = new Blob();
            // BitmapCache: капля рисуется один раз в половинном размере, дальше видеокарта её только двигает (она и так размытая)
            b.E = new Ellipse { Fill = BlobBrush(b.Now), RenderTransformOrigin = new Point(0.5, 0.5), CacheMode = new BitmapCache(0.5) };
            var g = new TransformGroup();
            g.Children.Add(b.S);
            g.Children.Add(b.T);
            b.E.RenderTransform = g;
            _live.Children.Add(b.E);
            _blobs.Add(b);
        }
        _live.Opacity = 0.75;
        _live.RenderTransformOrigin = new Point(0.5, 0.5);
        _live.RenderTransform = _liveRot;
        _root.Children.Add(_live);
        // обе подложки (старая и новая обложка при смене трека) плывут вместе
        var drift = new TransformGroup();
        drift.Children.Add(_bgScale);
        drift.Children.Add(_bgRot);
        foreach (var bgb in new[] { _bgA, _bgB })
        {
            bgb.RenderTransformOrigin = new Point(0.5, 0.5);
            bgb.RenderTransform = drift;
            bgb.CacheMode = new BitmapCache(0.5);
        }
    }

    private bool LiveOn => _host.PlayerLiveBg;

    private void LiveLayout(double W, double H)
    {
        _live.Visibility = LiveOn ? Visibility.Visible : Visibility.Collapsed;
        if (Math.Abs(W - _liveW) < 1 && Math.Abs(H - _liveH) < 1) return;
        _liveW = W; _liveH = H;
        // слой больше экрана (по диагонали), чтобы при повороте не открывались углы; капли по-прежнему вокруг середины
        double side = Math.Sqrt(W * W + H * H);
        _live.Width = side; _live.Height = side;
        _live.Margin = new Thickness((W - side) / 2, (H - side) / 2, (W - side) / 2, (H - side) / 2);
        _live.HorizontalAlignment = HorizontalAlignment.Left; _live.VerticalAlignment = VerticalAlignment.Top;
        double ox = (side - W) / 2, oy = (side - H) / 2;
        double d = Math.Max(W, H) * 0.85;
        // четыре угла с заходом за край: капли перекрываются в середине
        var pos = new[] { (-0.15, -0.2), (0.45, -0.25), (-0.1, 0.4), (0.5, 0.35) };
        for (int i = 0; i < _blobs.Count; i++)
        {
            var b = _blobs[i];
            b.E.Width = b.E.Height = d;
            Canvas.SetLeft(b.E, ox + W * pos[i].Item1);
            Canvas.SetTop(b.E, oy + H * pos[i].Item2);
        }
        _liveMoving = false;
        LiveMotion();
    }

    // Движение: у каждой капли свои периоды, поэтому узор не повторяется заметно.
    // Шаг 78: раньше было слишком медленно (проход 17–29 с) и в экономном режиме не двигалось совсем — казалось, что фон
    // стоит. Теперь заметнее (8–15 с, размах шире), обложка под каплями плывёт и поворачивается, слой капель вращается.
    // В экономном режиме движение не выключается, а идёт вдвое медленнее: всё закэшировано, видеокарте это почти ничего не стоит.
    private bool _liveEco;
    private void LiveMotion()
    {
        bool move = LiveOn;
        bool eco = _host.Eco;
        if (move == _liveMoving && eco == _liveEco) return;
        _liveMoving = move; _liveEco = eco;
        double slow = eco ? 2 : 1;
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        DoubleAnimation Loop(double from, double to, double sec) =>
            new(from, to, TimeSpan.FromSeconds(sec * slow)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        for (int i = 0; i < _blobs.Count; i++)
        {
            var b = _blobs[i];
            if (!move)
            {
                b.T.BeginAnimation(TranslateTransform.XProperty, null);
                b.T.BeginAnimation(TranslateTransform.YProperty, null);
                b.S.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                b.S.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                continue;
            }
            double ax = _liveW * (0.22 + 0.04 * i), ay = _liveH * (0.20 + 0.04 * ((i + 1) % 3));
            b.T.BeginAnimation(TranslateTransform.XProperty, Loop(-ax, ax, 9 + i * 2));
            b.T.BeginAnimation(TranslateTransform.YProperty, Loop(ay, -ay, 7.5 + i * 2.5));
            b.S.BeginAnimation(ScaleTransform.ScaleXProperty, Loop(0.75, 1.4, 10 + i * 1.5));
            b.S.BeginAnimation(ScaleTransform.ScaleYProperty, Loop(1.35, 0.8, 11 + i * 2));
        }
        if (!move)
        {
            _liveRot.BeginAnimation(RotateTransform.AngleProperty, null);
            _bgRot.BeginAnimation(RotateTransform.AngleProperty, null);
            _bgScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _bgScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            return;
        }
        // слой капель медленно крутится по кругу, обложка под ним покачивается и дышит
        _liveRot.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(70 * slow)) { RepeatBehavior = RepeatBehavior.Forever });
        _bgRot.BeginAnimation(RotateTransform.AngleProperty, Loop(-9, 9, 16));
        var breathe = Loop(1.3, 1.5, 12);
        _bgScale.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
        _bgScale.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
    }

    // цвета новой обложки перетекают в капли
    private void LiveColors(BitmapSource? bmp)
    {
        _live.Visibility = LiveOn ? Visibility.Visible : Visibility.Collapsed;
        LiveMotion();
        var pal = Palette(bmp, _host.AccentNow);
        for (int i = 0; i < _blobs.Count; i++)
        {
            var b = _blobs[i];
            b.From = b.Now;
            b.To = pal[i % pal.Count];
        }
        _blobStart = DateTime.UtcNow;
        if (!_blobHooked) { CompositionTarget.Rendering += BlobTick; _blobHooked = true; }
    }

    private DateTime _blobStart;
    private bool _blobHooked;
    private const double BlobMs = 1400;

    // переход цвета капель: шагом ~30 раз в секунду хватает (капли закэшированы, каждая новая кисть — новая картинка кэша)
    private DateTime _blobLast;
    private void BlobTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        double t = Math.Clamp((now - _blobStart).TotalMilliseconds / BlobMs, 0, 1);
        if (t < 1 && (now - _blobLast).TotalMilliseconds < 33) return;
        _blobLast = now;
        double k = t * t * (3 - 2 * t);
        foreach (var b in _blobs)
        {
            var c = t >= 1 ? b.To : Mix(b.From, b.To, k);
            if (c == b.Now) continue;
            b.Now = c;
            b.E.Fill = BlobBrush(c);
        }
        if (t >= 1 || _closing) { CompositionTarget.Rendering -= BlobTick; _blobHooked = false; }
    }

    // четыре главных цвета обложки: оттенки по 30°, вес — насыщенность и яркость; серое не считается
    internal static List<Color> Palette(BitmapSource? bmp, Color fallback)
    {
        var res = new List<Color>();
        try
        {
            if (bmp != null)
            {
                const int N = 24;
                var small = new FormatConvertedBitmap(new TransformedBitmap(bmp,
                    new ScaleTransform((double)N / bmp.PixelWidth, (double)N / bmp.PixelHeight)), PixelFormats.Bgra32, null, 0);
                int w = small.PixelWidth, h = small.PixelHeight;
                var px = new byte[w * h * 4];
                small.CopyPixels(px, w * 4, 0);
                var weight = new double[12];
                var sum = new double[12, 3];
                for (int i = 0; i < w * h; i++)
                {
                    double b = px[i * 4], g = px[i * 4 + 1], r = px[i * 4 + 2];
                    double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), dd = mx - mn;
                    double sat = mx <= 0 ? 0 : dd / mx, val = mx / 255;
                    if (sat < 0.18 || val < 0.15) continue;
                    double hh = mx == r ? (g - b) / dd % 6 : mx == g ? (b - r) / dd + 2 : (r - g) / dd + 4;
                    if (hh < 0) hh += 6;
                    int k = (int)(hh * 2) % 12;
                    double wt = sat * val;
                    weight[k] += wt;
                    sum[k, 0] += r * wt; sum[k, 1] += g * wt; sum[k, 2] += b * wt;
                }
                foreach (int k in Enumerable.Range(0, 12).OrderByDescending(k => weight[k]).Take(4))
                {
                    if (weight[k] < 0.5) break;
                    res.Add(Lively(Color.FromRgb((byte)(sum[k, 0] / weight[k]), (byte)(sum[k, 1] / weight[k]), (byte)(sum[k, 2] / weight[k]))));
                }
            }
        }
        catch { /* битая картинка: берём запасной цвет */ }
        if (res.Count == 0) res.Add(Lively(fallback));
        // не хватает цветов: светлее и темнее того, что есть
        int n0 = res.Count;
        for (int i = 0; res.Count < 4; i++)
        {
            var c = res[i % n0];
            double f = i % 2 == 0 ? 1.3 : 0.65;
            res.Add(Color.FromRgb((byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255)));
        }
        return res;
    }

    // чуть насыщеннее и не слишком тёмный: на тёмном фоне капли должны быть видны
    private static Color Lively(Color c)
    {
        double r = c.R, g = c.G, b = c.B;
        double mx = Math.Max(r, Math.Max(g, b));
        if (mx < 120 && mx > 0) { double k = 120 / mx; r *= k; g *= k; b *= k; }
        double avg = (r + g + b) / 3;
        r = avg + (r - avg) * 1.25; g = avg + (g - avg) * 1.25; b = avg + (b - avg) * 1.25;
        return Color.FromRgb((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
    }

    // ---------- бэк-вокал ----------
    private static readonly Regex Paren = new(@"\s*[\(（]([^\)）]+)[\)）]\s*", RegexOptions.Compiled);

    private const double BackScale = 0.6;

    // шаг 79: какие слова строки — бэк-вокал (внутри скобок). Строка целиком в скобках — не бэк-вокал, а обычная строка
    internal static bool[] BackingMask(string[] parts)
    {
        var mask = new bool[parts.Length];
        int depth = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            bool opens = p.IndexOfAny(new[] { '(', '（' }) >= 0;
            if (opens) depth++;
            mask[i] = depth > 0;
            depth -= p.Count(c => c is ')' or '）');
            if (depth < 0) depth = 0;
        }
        if (mask.All(m => m)) Array.Clear(mask);
        return mask;
    }

    // «I'm alive (alive, alive)» -> main «I'm alive», backing «alive, alive». Строка целиком в скобках остаётся как есть
    internal static (string main, string backing) SplitBacking(string text)
    {
        var ms = Paren.Matches(text);
        if (ms.Count == 0) return (text, "");
        string main = Regex.Replace(Paren.Replace(text, " "), @"\s{2,}", " ").Trim();
        if (main.Length == 0) return (text, "");
        return (main, string.Join(", ", ms.Select(m => m.Groups[1].Value.Trim()).Where(s => s.Length > 0)));
    }

    // ---------- поделиться строчкой ----------
    private FrameworkElement ShareLineButton(string line, bool right)
    {
        var b = new SBorder
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = MakeBrush(0x30, 255, 255, 255),
            HorizontalAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = right ? new Thickness(-46, 0, 0, 0) : new Thickness(0, 0, -46, 0),
            Opacity = 0, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = Loc.T("Поделиться строчкой: картинка сразу в буфере", "Share this line: the picture is copied right away"),
            Child = new VIcon { Text = "share", FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; ShareLine(line); };
        b.MouseRightButtonUp += (_, e) => e.Handled = true;
        return b;
    }

    private void ShareLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || LyricsEngine.IsInterlude(line)) return;
        try
        {
            var bmp = StoryCards.LyricCard(_coverSrc, line, _title ?? "", _artist ?? "", Palette(_coverSrc, _host.AccentNow));
            StoryCards.Save(bmp, "ShyFlow-line");
            try { Sfx.Ui("unlock"); } catch { /* нет звука */ }
            ShowHint("share", Loc.T("Строчка в буфере: вставь в сторис или чат", "Line copied: paste it into a story or chat"));
        }
        catch (Exception ex) { App.Log("Player.ShareLine", ex); ShowHint("triangle-alert", Loc.T("Не получилось сделать картинку", "Couldn't make the picture")); }
    }
}
