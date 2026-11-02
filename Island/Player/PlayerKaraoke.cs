using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Island;

// Шаг 80: караоке в плеере на весь экран (кнопка с микрофоном или клавиша K, выход — K или Esc).
//   Экран: текущая строка крупно по центру, слова заливаются цветом по мере пения (то же время слов, что у волны в плеере),
//   под ней следующая строка мельче. Перед вступлением после паузы — три точки обратного отсчёта.
//   Дуэт (Services\Duet.cs): у второго голоса свой цвет. Строка въезжает «желе», каждое слово на старте подпрыгивает
//   с деформацией (сплющивается и вытягивается).
//   Микрофон: уровень голоса и очки. Слово засчитано, если пока оно поётся, в микрофоне есть голос хотя бы 40 % его времени.
//     Ноты не сравниваются: голос певца смешан с музыкой, надёжно его не выделить. Музыка из колонок тоже слышна микрофону —
//     порог подстраивается под фон, но честнее всего в наушниках.
//   Запись: голос и звук Windows пишутся в два файла с общими часами (тишина дописывается нулями: в паузе Windows
//     звук не присылает), после остановки сводятся в один и сохраняются в M4A в «Музыка\ShyFlow Karaoke», файл сразу в кармане.
//   Итоги: когда песня сменилась — карточка с процентом и звёздами.
public sealed partial class PlayerWindow
{
    private sealed class KWord
    {
        public string Text = "";
        public double T0, T1;
        public bool Back;
        public Grid? El;
        public TextBlock? Lit;
        public readonly ScaleTransform Sc = new(1, 1);
        public double P = -1;
        public bool Started, Judged;
        public double Seen, Voiced;
    }

    private sealed class KLine
    {
        public double T, End;
        public int Side;
        public bool Interlude;
        public readonly List<KWord> Words = new();
    }

    private bool _karaoke;
    private List<KLine>? _kLines;
    private Grid? _kLayer;
    private readonly WrapPanel _kNow = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly WrapPanel _kNext = new() { HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.5 };
    private readonly StackPanel _kDots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _kInfo = new() { Foreground = Brushes.White, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _kSong = new() { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 17, Opacity = 0.85 };
    private readonly TextBlock _kChip = new() { FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0 };
    private readonly ScaleTransform _kChipSc = new(1, 1);
    private readonly TranslateTransform _kNowT = new();
    private readonly ScaleTransform _kNowS = new(1, 1);
    private int _kShow = -2, _kNextIdx = -2;
    private double _kFont = 64, _kLastT = double.NaN;
    private string? _kTitle;

    // очки
    private int _kWords, _kHits, _kCombo, _kBest;
    private readonly TextBlock _kScore = new() { Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 52 };
    private readonly TextBlock _kComboTb = new() { Foreground = Brushes.White, FontSize = 13, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, MinWidth = 34 };

    // микрофон (уровень пишется из потока захвата, читается в кадре)
    private WasapiCapture? _kMic;
    private volatile float _kLevel;
    private double _kFloor = 0.02, _kShown;
    private readonly Border _kMeter = new() { Width = 0, Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly VIcon _kMicIcon = new() { FontSize = 17, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    // запись
    private KRecorder? _kRec;
    private readonly TextBlock _kRecTb = new() { Foreground = Brushes.White, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _kRecDot = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private VIcon? _kPlayIcon;
    private Border? _kResult;

    private static readonly SolidColorBrush KDim = MakeBrush(0x52, 255, 255, 255);
    private static readonly SolidColorBrush KRed = MakeBrush(0xFF, 0xFF, 0x45, 0x3A);
    private static readonly SolidColorBrush KPanel = MakeBrush(0xC8, 0x12, 0x12, 0x14);
    private static readonly SolidColorBrush KEdge = MakeBrush(0x22, 255, 255, 255);
    private static readonly SolidColorBrush KDuet = MakeBrush(0xFF, 0xFF, 0x7A, 0xC0);   // второй голос
    private static readonly IEasingFunction KSpring = new SpringEase(6, 13);

    // ---------- вход и выход ----------
    private void ToggleKaraoke()
    {
        if (_karaoke) KaraokeOff(); else KaraokeOn();
    }

    private void KaraokeOn()
    {
        _karaoke = true;
        BuildKaraokeLayer();
        KaraokeModel();
        ResetScore();
        _kTitle = _title;
        _kShow = _kNextIdx = -2;
        _kLayer!.Visibility = Visibility.Visible;
        _kLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)));
        foreach (var el in new FrameworkElement[] { _left, _lyrPanel })
        {
            el.IsHitTestVisible = false;
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)));
        }
        if (_host.Cfg.KaraokeMic) MicStart();
        UpdateMicUi();
        KaraokeLayout();
    }

    private void KaraokeOff()
    {
        _karaoke = false;
        StopRecording();
        MicStop();
        if (_kLayer != null)
        {
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            a.Completed += (_, _) => { if (!_karaoke && _kLayer != null) _kLayer.Visibility = Visibility.Collapsed; };
            _kLayer.BeginAnimation(OpacityProperty, a);
        }
        _left.IsHitTestVisible = true;
        _left.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(320)));
        _lyrPanel.IsHitTestVisible = _hasLyrics;
        _lyrPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(_hasLyrics ? 1 : 0, TimeSpan.FromMilliseconds(380)));
        _idx = -2;   // строки плеера заново встанут на место
    }

    private void KaraokeClosed()
    {
        StopRecording();
        MicStop();
    }

    // ---------- экран ----------
    private void BuildKaraokeLayer()
    {
        if (_kLayer != null) return;
        _kLayer = new Grid { Visibility = Visibility.Collapsed, Opacity = 0 };
        Panel.SetZIndex(_kLayer, 6);
        _kLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });
        _kLayer.RowDefinitions.Add(new RowDefinition());
        _kLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(110) });

        // сверху: что поём (кнопка «свернуть» плеера слева остаётся на месте)
        _kSong.HorizontalAlignment = HorizontalAlignment.Center;
        _kSong.VerticalAlignment = VerticalAlignment.Center;
        _kSong.TextTrimming = TextTrimming.CharacterEllipsis;
        _kSong.Margin = new Thickness(90, 0, 90, 0);
        _kLayer.Children.Add(_kSong);

        // по центру: отсчёт, строка, следующая строка, оценка строки
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(60, 0, 60, 0) };
        _kDots.Margin = new Thickness(0, 0, 0, 22);
        for (int i = 0; i < 3; i++)
            _kDots.Children.Add(new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Brushes.White, Margin = new Thickness(9, 0, 9, 0),
                Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
            });
        center.Children.Add(_kDots);
        _kChip.RenderTransformOrigin = new Point(0.5, 0.5);
        _kChip.RenderTransform = _kChipSc;
        _kChip.Margin = new Thickness(0, 0, 0, 14);
        center.Children.Add(_kChip);
        _kNow.RenderTransformOrigin = new Point(0.5, 1);
        _kNow.RenderTransform = new TransformGroup { Children = { _kNowS, _kNowT } };
        center.Children.Add(_kNow);
        _kNext.Margin = new Thickness(0, 26, 0, 0);
        center.Children.Add(_kNext);
        center.Children.Add(_kInfo);
        Grid.SetRow(center, 1);
        _kLayer.Children.Add(center);

        // снизу: панель управления
        var bar = BuildKaraokeBar();
        Grid.SetRow(bar, 2);
        _kLayer.Children.Add(bar);
        _root.Children.Add(_kLayer);
    }

    private FrameworkElement BuildKaraokeBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Border Btn(UIElement child, Action click, string tip, double w = 44)
        {
            var b = new SBorder
            {
                Height = 44, MinWidth = w, CornerRadius = new CornerRadius(22), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = child, Padding = new Thickness(w > 44 ? 16 : 0, 0, w > 44 ? 16 : 0, 0), ToolTip = Loc.T(tip), Margin = new Thickness(2, 0, 2, 0)
            };
            b.MouseEnter += (_, _) => b.Background = Hov;
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
            return b;
        }
        VIcon Icon(string name, double size = 17) => new() { Text = name, FontSize = size, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        // микрофон и его уровень
        row.Children.Add(Btn(_kMicIcon, ToggleMic, "Микрофон: очки за пение"));
        var meter = new Grid { Width = 90, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 14, 0) };
        meter.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = KDim });
        meter.Children.Add(_kMeter);
        row.Children.Add(meter);
        // очки
        row.Children.Add(_kScore);
        row.Children.Add(_kComboTb);
        row.Children.Add(new Border { Width = 1, Height = 24, Background = KEdge, Margin = new Thickness(10, 0, 10, 0) });
        // запись
        var rec = new StackPanel { Orientation = Orientation.Horizontal };
        _kRecDot.Background = KRed;
        rec.Children.Add(_kRecDot);
        rec.Children.Add(_kRecTb);
        row.Children.Add(Btn(rec, ToggleRecording, "Записать голос вместе с музыкой", 110));
        row.Children.Add(new Border { Width = 1, Height = 24, Background = KEdge, Margin = new Thickness(10, 0, 10, 0) });
        // воспроизведение
        row.Children.Add(Btn(Icon("skip-back"), () => Skip(-1), "Назад"));
        _kPlayIcon = Icon("pause", 19);
        row.Children.Add(Btn(_kPlayIcon, Toggle, "Пауза"));
        row.Children.Add(Btn(Icon("skip-forward"), () => Skip(1), "Вперёд"));
        row.Children.Add(new Border { Width = 1, Height = 24, Background = KEdge, Margin = new Thickness(10, 0, 10, 0) });
        row.Children.Add(Btn(Icon("x"), KaraokeOff, "Выйти из караоке (K)"));

        var bar = new SBorder
        {
            CornerRadius = new CornerRadius(30), Background = KPanel, BorderBrush = KEdge, BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 8), Child = row,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top
        };
        _kRecTb.Text = Loc.T("Запись");
        return bar;
    }

    private void KaraokeLayout()
    {
        if (_kLayer == null) return;
        double W = ActualWidth, H = ActualHeight;
        if (W <= 0 || H <= 0) return;
        _kFont = Math.Clamp(H * 0.072, 34, 86);
        _kNow.MaxWidth = _kNext.MaxWidth = W * 0.84;
        _kInfo.FontSize = _kFont * 0.36;
        _kChip.FontSize = _kFont * 0.34;
        _kShow = _kNextIdx = -2;   // строки пересоберутся под новый размер
    }

    // ---------- модель: строки и время слов (как в BuildLyrics) ----------
    private void KaraokeModel()
    {
        _kLines = null;
        _kShow = _kNextIdx = -2;
        _kJudgedLines.Clear();
        _kSrc = _lyrShown;
        var list = _lyrShown;
        if (list == null || list.Count == 0 || list.All(x => x.t <= 0.001)) return;   // без времени строк петь не получится
        var duet = Duet.Analyze(list.Select(x => x.s).ToList(), _artist);
        var lineWords = _host.CurWords;
        var res = new List<KLine>();
        for (int i = 0; i < list.Count; i++)
        {
            string text = duet.text[i];
            double t0 = list[i].t, t1 = i + 1 < list.Count ? list[i + 1].t : t0 + 4;
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = new KLine { T = t0, End = t0, Side = duet.side != null ? duet.side[i] : 0 };
            line.Interlude = parts.Length == 0 || LyricsEngine.IsInterlude(text);
            if (!line.Interlude)
            {
                var wt = lineWords != null && i < lineWords.Count ? lineWords[i] : null;
                if (wt != null && wt.Count != parts.Length) wt = null;
                double win = wt != null ? Math.Max(0.3, wt.End[^1]) : LyricsEngine.LineWindow(text, t0, t1);
                var wts = LyricsEngine.WordWeights(parts);
                var back = BackingMask(parts);
                double total = Math.Max(0.001, wts.Sum()), acc = 0;
                for (int k = 0; k < parts.Length; k++)
                {
                    double dur = wts[k] / total * win, s0 = t0 + acc;
                    acc += dur;
                    if (wt != null) { s0 = t0 + wt.Start[k]; dur = Math.Max(0.05, wt.End[k] - wt.Start[k]); }
                    string shown = back[k] ? parts[k].Replace("(", "").Replace(")", "").Replace("（", "").Replace("）", "") : parts[k];
                    if (shown.Length == 0) continue;
                    line.Words.Add(new KWord { Text = shown, T0 = s0, T1 = s0 + Math.Max(dur, 0.18), Back = back[k] });
                }
                if (line.Words.Count == 0) line.Interlude = true;
                else line.End = line.Words[^1].T1;
            }
            res.Add(line);
        }
        _kLines = res;
    }

    private Brush KColor(int side)
    {
        if (side == 1) return KDuet;
        // цвет обложки, поднятый до яркого: тёмная обложка не должна давать тёмную заливку
        var c = _host.AccentNow;
        double l = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
        double k = l < 0.62 ? 0.62 / Math.Max(0.05, l) : 1;
        var br = new SolidColorBrush(Color.FromRgb((byte)Math.Min(255, c.R * k + 30), (byte)Math.Min(255, c.G * k + 30), (byte)Math.Min(255, c.B * k + 30)));
        br.Freeze();
        return br;
    }

    // слово: тусклый текст снизу и цветной сверху; цветной открывается слева направо (Clip) по мере пения
    private void FillLine(WrapPanel host, KLine? line, double font, bool live)
    {
        host.Children.Clear();
        if (line == null) return;
        var color = KColor(line.Side);
        foreach (var w in line.Words)
        {
            double fs = w.Back ? font * 0.6 : font;
            var g = new Grid
            {
                Margin = new Thickness(0, 0, fs * 0.3, w.Back ? 0 : fs * 0.08), RenderTransformOrigin = new Point(0.5, 1),
                VerticalAlignment = VerticalAlignment.Bottom, Opacity = w.Back ? 0.8 : 1
            };
            if (live) g.RenderTransform = w.Sc;   // подпрыгивает только слово в текущей строке
            g.Children.Add(new TextBlock { Text = w.Text, FontSize = fs, FontWeight = FontWeights.Bold, Foreground = KDim });
            if (live)
            {
                var lit = new TextBlock { Text = w.Text, FontSize = fs, FontWeight = FontWeights.Bold, Foreground = color, Clip = Geometry.Empty };
                g.Children.Add(lit);
                w.Lit = lit;
                w.P = -1;
            }
            w.El = g;
            host.Children.Add(g);
        }
    }

    // ---------- каждый кадр ----------
    private void KaraokeFrame(double t)
    {
        if (!_karaoke || _kLayer == null) return;
        // песня сменилась: итоги прошлой и новый текст
        if (_title != _kTitle)
        {
            if (_kWords >= 8) ShowKaraokeResult();
            _kTitle = _title;
            ResetScore();
        }
        if (_kLines == null || !ReferenceEquals(_kSrc, _lyrShown)) { _kSrc = _lyrShown; KaraokeModel(); }
        string song = string.IsNullOrEmpty(_artist) ? _title ?? "" : _title + "  ·  " + _artist;
        if (_kSong.Text != song) _kSong.Text = song;
        double dt = double.IsNaN(_kLastT) ? 0 : Math.Clamp(t - _kLastT, 0, 0.1);
        _kLastT = t;
        MicFrame();
        RecFrame();
        if (_kPlayIcon != null) _kPlayIcon.Text = _playing ? "pause" : "play";

        var lines = _kLines;
        if (lines == null || lines.Count == 0)
        {
            if (_kShow != -1)
            {
                _kShow = -1;
                _kNow.Children.Clear(); _kNext.Children.Clear();
                _kInfo.Text = Loc.T(_lyrShown == null ? "Для этой песни нет текста" : "Для караоке нужен текст со временем строк, а у этой песни он без времени");
                _kInfo.Visibility = Visibility.Visible;
            }
            Dots(0);
            return;
        }
        _kInfo.Visibility = Visibility.Collapsed;

        // что крупно: текущая строка, пока её поют (и чуть после); в паузе — следующая, ещё не залитая
        int cur = -1;
        for (int i = 0; i < lines.Count; i++) { if (lines[i].T <= t) cur = i; else break; }
        int show = cur >= 0 && !lines[cur].Interlude && t <= lines[cur].End + 0.9 ? cur : NextSung(lines, cur + 1);
        int next = show >= 0 ? NextSung(lines, show + 1) : -1;
        if (show != _kShow)
        {
            _kShow = show;
            FillLine(_kNow, show >= 0 ? lines[show] : null, _kFont, true);
            LineIn();
        }
        if (next != _kNextIdx)
        {
            _kNextIdx = next;
            FillLine(_kNext, next >= 0 ? lines[next] : null, _kFont * 0.56, false);
            _kNext.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.5, TimeSpan.FromMilliseconds(420)));
        }

        // отсчёт перед вступлением: пауза перед строкой не меньше 3 с — за 3 секунды до неё точки гаснут по одной
        int dots = 0;
        if (show >= 0 && t < lines[show].T)
        {
            double gapFrom = show > 0 ? lines[show - 1].End : 0;
            double left = lines[show].T - t;
            if (lines[show].T - gapFrom >= 3 && left <= 3) dots = (int)Math.Ceiling(left);
        }
        Dots(dots);

        if (show < 0) return;
        foreach (var w in lines[show].Words)
        {
            // заливка слова
            double p = Math.Clamp((t - w.T0) / Math.Max(0.05, w.T1 - w.T0), 0, 1);
            // (пока строка не разложена и у слова нет ширины, ждём следующего кадра)
            if (w.Lit != null && w.Lit.ActualWidth > 0 && Math.Abs(p - w.P) > 0.002)
            {
                w.P = p;
                double lw = w.Lit.ActualWidth, lh = w.Lit.ActualHeight;
                if (p <= 0) w.Lit.Clip = Geometry.Empty;
                else if (p >= 1) w.Lit.Clip = null;
                else
                {
                    var r = new RectangleGeometry(new Rect(0, -lh * 0.2, lw * p, lh * 1.4));
                    r.Freeze();
                    w.Lit.Clip = r;
                }
            }
            // слово началось: подпрыгивает с деформацией (сплющилось — вытянулось — успокоилось)
            if (!w.Started && t >= w.T0 && t < w.T1)
            {
                w.Started = true;
                if (!_host.Eco)
                {
                    w.Sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.16, 1, TimeSpan.FromMilliseconds(560)) { EasingFunction = KSpring });
                    w.Sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = KSpring });
                }
            }
            // очки: пока слово поётся, слушаем микрофон
            if (_kMic != null && _playing && dt > 0 && t >= w.T0 && t <= w.T1 && !w.Back)
            {
                w.Seen += dt;
                if (_kLevel > MicThreshold) w.Voiced += dt;
            }
            if (!w.Judged && t > w.T1 && !w.Back)
            {
                w.Judged = true;
                if (_kMic != null && w.Seen > 0.05) Judge(w, w.Voiced / w.Seen >= 0.4);
            }
        }
        // строка спета: оценка
        var ln = lines[show];
        if (_kMic != null && !ln.Interlude && ln.Words.Count > 0 && t > ln.End && !_kJudgedLines.Contains(show))
        {
            _kJudgedLines.Add(show);
            var judged = ln.Words.Where(w => !w.Back && w.Seen > 0.05).ToList();
            if (judged.Count > 0) LineChip(judged.Count(w => w.Voiced / w.Seen >= 0.4) / (double)judged.Count);
        }
    }

    private object? _kSrc;
    private readonly HashSet<int> _kJudgedLines = new();

    private static int NextSung(List<KLine> lines, int from)
    {
        for (int i = Math.Max(0, from); i < lines.Count; i++) if (!lines[i].Interlude) return i;
        return -1;
    }

    // новая строка въезжает снизу желе: сплющена и шире, потом пружинит в обычную форму
    private void LineIn()
    {
        if (_host.Eco)
        {
            _kNowS.ScaleX = _kNowS.ScaleY = 1; _kNowT.Y = 0;
            return;
        }
        _kNowT.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_kFont * 0.7, 0, TimeSpan.FromMilliseconds(640)) { EasingFunction = new SpringEase(6.5, 10) });
        _kNowS.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.78, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = KSpring });
        _kNowS.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.08, 1, TimeSpan.FromMilliseconds(760)) { EasingFunction = KSpring });
        _kNow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(300)));
    }

    private int _kDotsShown = -1;

    private void Dots(int n)
    {
        if (n == _kDotsShown) return;
        _kDotsShown = n;
        for (int i = 0; i < _kDots.Children.Count; i++)
        {
            var d = (Border)_kDots.Children[i];
            bool on = i < n;
            d.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0.9 : 0, TimeSpan.FromMilliseconds(on ? 200 : 260)));
            var sc = (ScaleTransform)d.RenderTransform;
            // точка гаснет «каплей»: сплющивается и сжимается
            var a = new DoubleAnimation(on ? 1 : 0.2, TimeSpan.FromMilliseconds(on ? 380 : 300)) { EasingFunction = on ? KSpring : new CubicEase { EasingMode = EasingMode.EaseIn } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(on ? 1 : 0.05, TimeSpan.FromMilliseconds(on ? 380 : 300)) { EasingFunction = a.EasingFunction });
        }
    }

    // ---------- очки ----------
    private void ResetScore()
    {
        _kWords = _kHits = _kCombo = _kBest = 0;
        _kJudgedLines.Clear();
        if (_kLines != null) foreach (var l in _kLines) foreach (var w in l.Words) { w.Started = w.Judged = false; w.Seen = w.Voiced = 0; }
        UpdateScore();
    }

    private void Judge(KWord w, bool hit)
    {
        _kWords++;
        if (hit) { _kHits++; _kCombo++; _kBest = Math.Max(_kBest, _kCombo); }
        else
        {
            _kCombo = 0;
            if (w.Lit != null) w.Lit.Opacity = 0.45;   // не спето: заливка бледнее
        }
        UpdateScore();
    }

    private int ScorePct => _kWords == 0 ? 0 : (int)Math.Round(100.0 * _kHits / _kWords);

    private void UpdateScore()
    {
        _kScore.Text = _kMic == null ? "—" : ScorePct + " %";
        _kComboTb.Text = _kCombo >= 3 ? "×" + _kCombo : "";
    }

    private void LineChip(double share)
    {
        (string text, Color c) = share >= 0.8 ? (Loc.T("Отлично!"), Color.FromRgb(0x30, 0xD1, 0x58))
            : share >= 0.5 ? (Loc.T("Хорошо"), Color.FromRgb(0x64, 0xD2, 0xFF))
            : (Loc.T("Мимо"), Color.FromRgb(0xFF, 0x9F, 0x0A));
        var br = new SolidColorBrush(c);
        br.Freeze();
        _kChip.Text = text;
        _kChip.Foreground = br;
        // оценка выскакивает каплей: из плоской в вытянутую и на место
        _kChipSc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.3, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = KSpring });
        _kChipSc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = KSpring });
        var op = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1600) };
        op.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.1)));
        op.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.7)));
        op.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        _kChip.BeginAnimation(OpacityProperty, op);
    }

    private void ShowKaraokeResult()
    {
        if (_kLayer == null || _kMic == null && _kWords == 0) return;
        int pct = ScorePct;
        int stars = pct >= 90 ? 5 : pct >= 75 ? 4 : pct >= 55 ? 3 : pct >= 35 ? 2 : 1;
        string verdict = pct >= 90 ? "Звезда!" : pct >= 75 ? "Очень круто" : pct >= 55 ? "Хорошо спел" : pct >= 35 ? "Неплохо" : "В следующий раз получится";
        var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        col.Children.Add(new TextBlock { Text = _kTitle ?? "", Foreground = Brushes.White, Opacity = 0.7, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 });
        col.Children.Add(new TextBlock { Text = pct + " %", Foreground = Brushes.White, FontSize = 76, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        col.Children.Add(new TextBlock { Text = new string('★', stars) + new string('☆', 5 - stars), Foreground = MakeBrush(0xFF, 0xFF, 0xD6, 0x0A), FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center });
        col.Children.Add(new TextBlock { Text = Loc.T(verdict), Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 4) });
        col.Children.Add(new TextBlock
        {
            Text = Loc.T("Спето слов: ") + $"{_kHits} / {_kWords}" + "   ·   " + Loc.T("Лучшая серия: ") + _kBest,
            Foreground = Brushes.White, Opacity = 0.65, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center
        });
        var sc = new ScaleTransform(1, 1);
        var card = new SBorder
        {
            CornerRadius = new CornerRadius(34), Background = KPanel, BorderBrush = KEdge, BorderThickness = new Thickness(1),
            Padding = new Thickness(48, 30, 48, 32), Child = col, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = sc, Cursor = Cursors.Hand, ToolTip = Loc.T("Клик — закрыть")
        };
        Grid.SetRowSpan(card, 3);
        Panel.SetZIndex(card, 10);
        if (_kResult != null) _kLayer.Children.Remove(_kResult);
        _kResult = card;
        _kLayer.Children.Add(card);
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = KSpring });
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(760)) { EasingFunction = KSpring });
        void Close()
        {
            if (!ReferenceEquals(_kResult, card)) return;
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            a.Completed += (_, _) => { _kLayer?.Children.Remove(card); if (ReferenceEquals(_kResult, card)) _kResult = null; };
            card.BeginAnimation(OpacityProperty, a);
        }
        card.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); };
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }

    // ---------- микрофон ----------
    private double MicThreshold => Math.Max(0.012, _kFloor * 2.2 + 0.004);

    private void ToggleMic()
    {
        _host.Cfg.KaraokeMic = !_host.Cfg.KaraokeMic;
        _host.Cfg.Save();
        if (_host.Cfg.KaraokeMic) MicStart(); else { StopRecording(); MicStop(); }
        UpdateMicUi();
    }

    private void UpdateMicUi()
    {
        _kMicIcon.Text = _kMic != null ? "mic" : "mic-off";
        _kMicIcon.Opacity = _kMic != null ? 1 : 0.55;
        UpdateScore();
    }

    private void MicStart()
    {
        if (_kMic != null) return;
        try
        {
            using var en = new MMDeviceEnumerator();
            var dev = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            var cap = new WasapiCapture(dev) { ShareMode = AudioClientShareMode.Shared };
            var fmt = cap.WaveFormat;
            cap.DataAvailable += (_, e) =>
            {
                _kRec?.Mic(e.Buffer, e.BytesRecorded);
                _kLevel = Rms(e.Buffer, e.BytesRecorded, fmt);
            };
            cap.StartRecording();
            _kMic = cap;
            _kFloor = 0.02;
            if (!_host.Cfg.KaraokeHintShown)
            {
                _host.Cfg.KaraokeHintShown = true;
                _host.Cfg.Save();
                ShowHint("headphones", Loc.T("Пой в наушниках: музыка из колонок тоже слышна микрофону, и очки будут неточными"));
            }
        }
        catch (Exception ex)
        {
            App.LogOnce("Karaoke.Mic", ex);
            _kMic = null;
            ShowHint("mic-off", Loc.T("Микрофон недоступен: проверь, что он подключён и в Windows разрешён доступ к микрофону для приложений"));
        }
    }

    private void MicStop()
    {
        var m = _kMic;
        _kMic = null;
        _kLevel = 0;
        if (m == null) return;
        try { m.StopRecording(); } catch { }
        try { m.Dispose(); } catch { }
    }

    // громкость куска звука (среднеквадратичная), 0..1
    private static float Rms(byte[] b, int n, WaveFormat f)
    {
        double sum = 0;
        int count = 0;
        if (f.Encoding == WaveFormatEncoding.IeeeFloat || (f.Encoding == WaveFormatEncoding.Extensible && f.BitsPerSample == 32))
            for (int i = 0; i + 4 <= n; i += 4) { double v = BitConverter.ToSingle(b, i); sum += v * v; count++; }
        else if (f.BitsPerSample == 16)
            for (int i = 0; i + 2 <= n; i += 2) { double v = BitConverter.ToInt16(b, i) / 32768.0; sum += v * v; count++; }
        return count == 0 ? 0 : (float)Math.Sqrt(sum / count);
    }

    private void MicFrame()
    {
        double lv = _kMic != null ? _kLevel : 0;
        // фон (тишина в комнате, музыка из колонок): быстро опускается, медленно поднимается
        if (lv < _kFloor) _kFloor += (lv - _kFloor) * 0.2;
        else _kFloor += (lv - _kFloor) * 0.0015;
        _kShown += (Math.Min(1, lv * 6) - _kShown) * (lv * 6 > _kShown ? 0.5 : 0.12);
        _kMeter.Width = 90 * Math.Clamp(_kShown, 0, 1);
        _kMeter.Background = lv > MicThreshold ? Brushes.White : KDim;
    }

    // ---------- запись ----------
    private void ToggleRecording()
    {
        if (_kRec != null) { StopRecording(); return; }
        if (_kMic == null)
        {
            _host.Cfg.KaraokeMic = true;
            _host.Cfg.Save();
            MicStart();
            UpdateMicUi();
            if (_kMic == null) return;
        }
        try
        {
            string tmp = Path.Combine(Path.GetTempPath(), "ShyFlow-Karaoke");
            Directory.CreateDirectory(tmp);
            _kRec = new KRecorder(tmp, _kMic.WaveFormat, _title ?? "", _artist ?? "");
            _kRecDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }
        catch (Exception ex)
        {
            App.LogOnce("Karaoke.Rec", ex);
            _kRec = null;
            ShowHint("mic-off", Loc.T("Не получилось начать запись"));
        }
    }

    private void RecFrame()
    {
        if (_kRec == null) return;
        var s = _kRec.Elapsed;
        _kRecTb.Text = $"{(int)s.TotalMinutes}:{s.Seconds:00}";
    }

    private void StopRecording()
    {
        var rec = _kRec;
        if (rec == null) return;
        _kRec = null;
        _kRecDot.BeginAnimation(OpacityProperty, null);
        _kRecDot.Opacity = 1;
        _kRecTb.Text = Loc.T("Сохраняю…");
        _ = SaveRecording(rec);
    }

    private async Task SaveRecording(KRecorder rec)
    {
        try
        {
            string? path = await rec.FinishAsync();
            if (path != null)
            {
                _host.PocketAddFile(path);
                ShowHint("mic-vocal", Loc.T("Запись сохранена в «Музыка\\ShyFlow Karaoke» и лежит в кармане"));
            }
        }
        catch (Exception ex)
        {
            App.Log("Karaoke.Save", ex);
            ShowHint("mic-off", Loc.T("Не получилось сохранить запись"));
        }
        finally
        {
            if (_kRec == null) _kRecTb.Text = Loc.T("Запись");
        }
    }

    // Запись: звук Windows (что играет) и микрофон — каждый в свой WAV с общими часами, потом сведение в один файл.
    private sealed class KRecorder
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly PadWriter _sys, _mic;
        private readonly WasapiLoopbackCapture _loop;
        private readonly string _dir, _name;

        public TimeSpan Elapsed => _clock.Elapsed;

        public KRecorder(string dir, WaveFormat micFormat, string title, string artist)
        {
            _dir = dir;
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm");
            string baseName = string.IsNullOrWhiteSpace(title) ? "Karaoke" : (string.IsNullOrWhiteSpace(artist) ? title : artist + " — " + title);
            _name = string.Concat(baseName.Split(Path.GetInvalidFileNameChars())).Trim() + " (" + Loc.T("караоке") + ") " + stamp;
            if (_name.Length > 120) _name = _name[..120];
            string id = Guid.NewGuid().ToString("N")[..8];
            _loop = new WasapiLoopbackCapture();
            _sys = new PadWriter(Path.Combine(dir, id + "-sys.wav"), _loop.WaveFormat, _clock);
            _mic = new PadWriter(Path.Combine(dir, id + "-mic.wav"), micFormat, _clock);
            _loop.DataAvailable += (_, e) => _sys.Write(e.Buffer, e.BytesRecorded);
            _loop.StartRecording();
        }

        public void Mic(byte[] b, int n) => _mic.Write(b, n);

        public Task<string?> FinishAsync() => Task.Run(async () =>
        {
            try { _loop.StopRecording(); } catch { }
            await Task.Delay(150);   // последний кусок от захвата
            try { _loop.Dispose(); } catch { }
            _sys.Close();
            _mic.Close();
            if (_clock.Elapsed.TotalSeconds < 1.5) { Cleanup(); return null; }
            string outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "ShyFlow Karaoke");
            Directory.CreateDirectory(outDir);
            string wav = Path.Combine(outDir, _name + ".wav");
            for (int i = 2; File.Exists(wav) || File.Exists(Path.ChangeExtension(wav, ".m4a")); i++) wav = Path.Combine(outDir, $"{_name} ({i}).wav");
            Mix(_sys.Path, _mic.Path, wav);
            Cleanup();
            try
            {
                string m4a = await Converter.Convert(wav, "M4A", new Progress<double>());
                File.Delete(wav);
                return m4a;
            }
            catch (Exception ex)
            {
                App.LogOnce("Karaoke.M4A", ex);   // не сжалось — остаётся WAV
                return wav;
            }
        });

        private void Cleanup()
        {
            try { File.Delete(_sys.Path); } catch { }
            try { File.Delete(_mic.Path); } catch { }
        }

        // сведение: оба потока в стерео 48 кГц, музыка чуть тише голоса, мягкий ограничитель от перегруза
        internal static void Mix(string sysWav, string micWav, string outWav)
        {
            using var a = new AudioFileReader(sysWav);
            using var b = new AudioFileReader(micWav);
            var mix = new MixingSampleProvider(new[]
            {
                new VolumeSampleProvider(Stereo48(a)) { Volume = 0.8f },
                new VolumeSampleProvider(Stereo48(b)) { Volume = 1.0f }
            });
            WaveFileWriter.CreateWaveFile16(outWav, new SoftClip(mix));
        }

        private static ISampleProvider Stereo48(ISampleProvider s)
        {
            if (s.WaveFormat.Channels == 1) s = new MonoToStereoSampleProvider(s);
            else if (s.WaveFormat.Channels > 2) s = new FirstTwo(s);
            if (s.WaveFormat.SampleRate != 48000) s = new WdlResamplingSampleProvider(s, 48000);
            return s;
        }
    }

    // WAV с часами: если поток молчал (Windows не присылает звук в тишине), дописываем тишину, чтобы голос и музыка совпали
    private sealed class PadWriter
    {
        private readonly WaveFileWriter _w;
        private readonly Stopwatch _clock;
        private readonly int _rate, _block;
        private long _frames;
        private bool _closed;
        public readonly string Path;

        public PadWriter(string path, WaveFormat f, Stopwatch clock)
        {
            Path = path;
            _w = new WaveFileWriter(path, f);
            _clock = clock;
            _rate = f.SampleRate;
            _block = f.BlockAlign;
        }

        public void Write(byte[] b, int n)
        {
            lock (this)
            {
                if (_closed || n <= 0) return;
                long got = n / _block;
                long gap = (long)(_clock.Elapsed.TotalSeconds * _rate) - (_frames + got);
                if (gap > _rate / 10) Silence(gap);
                _w.Write(b, 0, (int)(got * _block));
                _frames += got;
            }
        }

        private void Silence(long frames)
        {
            var z = new byte[Math.Min(frames, _rate) * _block];
            while (frames > 0)
            {
                long f = Math.Min(frames, z.Length / _block);
                _w.Write(z, 0, (int)(f * _block));
                _frames += f;
                frames -= f;
            }
        }

        public void Close()
        {
            lock (this)
            {
                if (_closed) return;
                long gap = (long)(_clock.Elapsed.TotalSeconds * _rate) - _frames;
                if (gap > 0) Silence(gap);
                _closed = true;
                _w.Dispose();
            }
        }
    }

    private sealed class FirstTwo : ISampleProvider
    {
        private readonly ISampleProvider _s;
        private float[] _buf = Array.Empty<float>();
        public WaveFormat WaveFormat { get; }
        public FirstTwo(ISampleProvider s) { _s = s; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(s.WaveFormat.SampleRate, 2); }

        public int Read(float[] buffer, int offset, int count)
        {
            int ch = _s.WaveFormat.Channels, frames = count / 2;
            if (_buf.Length < frames * ch) _buf = new float[frames * ch];
            int got = _s.Read(_buf, 0, frames * ch) / ch;
            for (int i = 0; i < got; i++) { buffer[offset + 2 * i] = _buf[i * ch]; buffer[offset + 2 * i + 1] = _buf[i * ch + 1]; }
            return got * 2;
        }
    }

    private sealed class SoftClip : ISampleProvider
    {
        private readonly ISampleProvider _s;
        public WaveFormat WaveFormat => _s.WaveFormat;
        public SoftClip(ISampleProvider s) => _s = s;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = _s.Read(buffer, offset, count);
            for (int i = offset; i < offset + n; i++)
            {
                float x = buffer[i], a = Math.Abs(x);
                if (a > 0.8f) buffer[i] = Math.Sign(x) * (0.8f + 0.2f * (float)Math.Tanh((a - 0.8f) / 0.2f));
            }
            return n;
        }
    }
}
