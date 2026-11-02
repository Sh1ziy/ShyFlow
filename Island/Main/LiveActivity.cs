using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Act = (string kind, string text, double frac, System.Windows.Media.Color col, string icon, bool paused);

namespace Island;

// Шаг 80: пузырь как Live Activity на iPhone.
//   1) Кольцо прогресса идёт плавно: у таймера и фокуса течёт непрерывно, у секундомера бежит секундной стрелкой
//      (раньше щёлкало раз в секунду или с каждым процентом).
//   2) Цифры перекатываются (Ui\RollText.cs).
//   3) Наведи мышь на пузырь и подержи — он вытягивается в карточку: крупное время или проценты, что это, и кнопки:
//      таймер — пауза, +1 мин, стоп; секундомер — пауза, сброс; фокус — пауза, перерыв, стоп; звонок — микрофон;
//      копирование и загрузки — полоса и «Открыть»; погода — прогноз и дождь; часы — дата и праздник; батарея — сколько осталось.
//      Убери мышь — карточка втягивается обратно в пузырь с желе. Настройки, «Плашки», «Раскрывать пузырь».
public partial class MainWindow
{
    // ---------- кольцо ----------
    private double _ringShown = -1, _ringTarget;
    private string _ringKind = "";
    private bool _ringHooked;
    private TimeSpan _ringRt;

    private void RingTo(string kind, double frac)
    {
        frac = Math.Clamp(frac, 0, 1);
        bool snap = kind != _ringKind || _ringShown < 0 || Math.Abs(frac - _ringShown) > 0.35 || _eco;
        _ringKind = kind;
        _ringTarget = frac;
        if (snap) { _ringShown = frac; DrawRing(); }
        if (!_ringHooked && (!snap || LiveFrac(kind) != null) && !_eco)
        {
            _ringHooked = true;
            _ringRt = TimeSpan.Zero;
            CompositionTarget.Rendering += RingFrame;
        }
    }

    // доля прямо сейчас, с точностью до кадра: идущие таймер, фокус и секундомер
    private double? LiveFrac(string kind) => kind switch
    {
        "timer" when _tmRun && _tmTotal > 0 => TmLeftNow / _tmTotal,
        "focus" when _focusOn && !_focusPaused && _focusTotal > 0 => FocusLeftNow / _focusTotal,
        "watch" when _swRun => SwNow.TotalSeconds % 60 / 60,
        _ => null
    };

    private void RingFrame(object? s, EventArgs e)
    {
        if (e is not RenderingEventArgs re || re.RenderingTime == _ringRt) return;
        double dt = _ringRt == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((re.RenderingTime - _ringRt).TotalSeconds, 0, 0.1);
        _ringRt = re.RenderingTime;
        var live = _bubbleOn && !_hidden ? LiveFrac(_ringKind) : null;
        if (live is double lf)
        {
            if (Math.Abs(lf - _ringShown) > 0.35) _ringShown = lf;   // круг секундомера замкнулся: с нуля
            _ringTarget = lf;
        }
        _ringShown += (_ringTarget - _ringShown) * (1 - Math.Exp(-dt * 9));
        bool done = Math.Abs(_ringTarget - _ringShown) < 0.0008;
        if (done) _ringShown = _ringTarget;
        DrawRing();
        if (done && live == null)
        {
            CompositionTarget.Rendering -= RingFrame;
            _ringHooked = false;
        }
    }

    private void DrawRing()
    {
        BubbleRing.Data = Arc(_ringShown);
        if (_liveRing != null && _liveOpen) _liveRing.Data = ArcAt(_ringShown, 22, 19.5);
    }

    // дуга от 12 часов по часовой стрелке с центром (c, c) и радиусом r
    private static Geometry ArcAt(double f, double c, double r)
    {
        f = Math.Clamp(f, 0, 1);
        if (f <= 0.001) return Geometry.Empty;
        if (f >= 0.999) return new EllipseGeometry(new Point(c, c), r, r);
        double a = f * 2 * Math.PI;
        var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry { Figures = { fig } };
        g.Freeze();
        return g;
    }

    // ---------- карточка ----------
    private bool _liveOpen;
    private Grid? _liveCard;
    private Path? _liveRing;
    private Ellipse? _liveTrack;
    private VIcon? _liveIcon;
    private RollText? _liveBig;
    private TextBlock? _liveTitle, _liveSub;
    private Border? _liveBarHost, _liveBar;
    private StackPanel? _liveBtns;
    private string _liveSig = "";
    private double _liveCompactW = 38;
    private readonly DispatcherTimer _liveEnter = new() { Interval = TimeSpan.FromMilliseconds(480) };
    private readonly DispatcherTimer _liveLeave = new() { Interval = TimeSpan.FromMilliseconds(380) };
    private const double LiveW = 268;
    private double _liveH;   // высота карточки по содержимому (прогноз в две строки выше, чем таймер)
    private static readonly SolidColorBrush LiveTrackStroke = Frozen(Color.FromArgb(0x33, 255, 255, 255));
    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private void InitLive()
    {
        var root = (Grid)Bubble.Child;
        _liveCard = new Grid { Visibility = Visibility.Collapsed, Opacity = 0, Margin = new Thickness(14, 12, 14, 12), Width = LiveW - 28 };
        _liveCard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _liveCard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _liveCard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // кольцо и значок крупно, справа что это и время
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        var ring = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center };
        _liveTrack = new Ellipse { Stroke = LiveTrackStroke, StrokeThickness = 3.5, Margin = new Thickness(0.75) };
        _liveRing = new Path { StrokeThickness = 3.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _liveIcon = new VIcon { FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        ring.Children.Add(_liveTrack);
        ring.Children.Add(_liveRing);
        ring.Children.Add(_liveIcon);
        head.Children.Add(ring);
        var names = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _liveTitle = new TextBlock { FontSize = 12, Foreground = ShyStyle.Gray, TextTrimming = TextTrimming.CharacterEllipsis };
        _liveBig = new RollText { FontSize = 27, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, -2, 0, -2) };
        Typography.SetNumeralAlignment(_liveBig, FontNumeralAlignment.Tabular);
        names.Children.Add(_liveTitle);
        names.Children.Add(_liveBig);
        Grid.SetColumn(names, 1);
        head.Children.Add(names);
        _liveCard.Children.Add(head);

        // полоса прогресса (копирование, загрузки) или строка подробностей (прогноз, праздник)
        var mid = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        _liveBar = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _liveBarHost = new Border
        {
            Height = 5, CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
            Child = _liveBar, Margin = new Thickness(0, 2, 0, 4), Visibility = Visibility.Collapsed
        };
        _liveSub = new TextBlock { FontSize = 11.5, Foreground = Brushes.White, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxHeight = 32, TextTrimming = TextTrimming.CharacterEllipsis };
        mid.Children.Add(_liveBarHost);
        mid.Children.Add(_liveSub);
        Grid.SetRow(mid, 1);
        _liveCard.Children.Add(mid);

        _liveBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetRow(_liveBtns, 2);
        _liveCard.Children.Add(_liveBtns);
        root.Children.Add(_liveCard);

        // наведение: подержал — раскрылась; ушёл — через мгновение свернулась (можно вернуть мышь, не закрывая)
        _liveEnter.Tick += (_, _) => { _liveEnter.Stop(); if (Bubble.IsMouseOver && Mouse.LeftButton != MouseButtonState.Pressed) LiveExpand(); };
        _liveLeave.Tick += (_, _) => { _liveLeave.Stop(); if (!Bubble.IsMouseOver) LiveCollapse(); };
        Bubble.MouseEnter += (_, _) => { _liveLeave.Stop(); if (!_liveOpen && _cfg.LiveExpand) { _liveEnter.Stop(); _liveEnter.Start(); } };
        Bubble.MouseLeave += (_, _) => { _liveEnter.Stop(); if (_liveOpen) { _liveLeave.Stop(); _liveLeave.Start(); } };
    }

    private Act? CurrentAct()
    {
        if (_stackActs.Count == 0) return null;
        int i = Math.Max(0, _stackActs.FindIndex(a => a.kind == _bubbleKind));
        return _stackActs[i];
    }

    private void LiveExpand()
    {
        if (_liveOpen || !_cfg.LiveExpand || !_bubbleOn || Bubble.Visibility != Visibility.Visible || _liveCard == null) return;
        if (CurrentAct() is not { } act) return;
        _liveOpen = true;
        FitWindowH();   // карточка выше компактного окна (Display.cs)
        _liveCompactW = Math.Max(38, Bubble.ActualWidth);
        _liveSig = "";
        _liveH = 0;   // первый замер без анимации: высоту раскрытия задаёт анимация ниже
        BubbleBody.Visibility = Visibility.Collapsed;
        _liveCard.Visibility = Visibility.Visible;
        LiveFill(act);
        _liveCard.UpdateLayout();   // карточка уже в дереве: раскладка сразу, чтобы высота раскрытия была точной
        _liveH = 0;
        LiveHeight();
        _liveCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { BeginTime = TimeSpan.FromMilliseconds(90) });
        Bubble.CornerRadius = new CornerRadius(26);
        var spring = new SpringEase(6, 11);
        Bubble.BeginAnimation(WidthProperty, new DoubleAnimation(_liveCompactW, LiveW, TimeSpan.FromMilliseconds(560)) { EasingFunction = spring });
        // шаг 88: в Duo пузырь вертикальный (выше 38) и прижат к краю — карточка растёт от края влево (Duo.cs)
        Bubble.BeginAnimation(HeightProperty, new DoubleAnimation(Math.Max(38, Bubble.ActualHeight), _liveH, TimeSpan.FromMilliseconds(620)) { EasingFunction = spring });
        // вытекает каплей: сначала сплющен, потом пружинит
        if (!_eco)
        {
            BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.05, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = new SpringEase(6, 13) });
            BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = new SpringEase(6, 13) });
        }
        Sfx.Ui("tap");
    }

    private void LiveCollapse()
    {
        if (!_liveOpen || _liveCard == null) return;
        _liveOpen = false;
        _liveEnter.Stop();
        _liveLeave.Stop();
        _liveCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        // ширина свёрнутого пузыря: по его содержимому (за время раскрытия текст мог стать длиннее)
        BubbleBody.Visibility = Visibility.Visible;
        BubbleBody.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double compact = Duo ? DuoW : Math.Max(38, BubbleBody.DesiredSize.Width + BubbleBody.Margin.Left + BubbleBody.Margin.Right);
        double compactH = Duo ? DuoBubbleH() : 38;
        BubbleBody.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(120) });
        var spring = new SpringEase(6.5, 12);
        var w = new DoubleAnimation(Bubble.ActualWidth, compact, TimeSpan.FromMilliseconds(480)) { EasingFunction = spring };
        var h = new DoubleAnimation(Bubble.ActualHeight, compactH, TimeSpan.FromMilliseconds(500)) { EasingFunction = spring };
        h.Completed += (_, _) =>
        {
            if (_liveOpen) return;   // за это время снова раскрыли
            Bubble.BeginAnimation(WidthProperty, null);
            Bubble.BeginAnimation(HeightProperty, null);
            Bubble.Width = Duo ? DuoW : double.NaN;
            Bubble.Height = Duo ? double.NaN : 38;
            Bubble.CornerRadius = new CornerRadius(Duo ? DuoW / 2 : 19);
            _liveCard.Visibility = Visibility.Collapsed;
            FitWindowH();
        };
        Bubble.BeginAnimation(WidthProperty, w);
        Bubble.BeginAnimation(HeightProperty, h);
        if (!_eco)
        {
            BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = new SpringEase(6, 13) });
            BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.08, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = new SpringEase(6, 13) });
        }
    }

    // содержимое карточки по текущей активности; кнопки пересобираются, только когда меняется их смысл
    private void LiveFill(Act a)
    {
        if (_liveCard == null || _liveBig == null || _liveTitle == null || _liveSub == null || _liveBtns == null || _liveRing == null || _liveIcon == null) return;
        Bubble.ToolTip = null;
        var brush = new SolidColorBrush(a.col);
        brush.Freeze();
        _liveRing.Stroke = brush;
        _liveIcon.Text = a.icon;
        _liveIcon.Foreground = brush;
        _liveBig.Foreground = brush;
        _liveBig.Animate = !_eco;
        bool liquid = IsLiquid(a.kind);
        // кольцо только там, где есть ход времени или заряд; у остального значок в мягком кружке
        bool ring = a.kind is "timer" or "watch" or "focus" or "battery" or "clock";
        _liveRing.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
        _liveTrack!.Stroke = ring ? LiveTrackStroke : Brushes.Transparent;
        _liveTrack.Fill = ring ? null : ShyStyle.Tile;
        _liveRing.Data = ArcAt(_ringShown < 0 ? a.frac : _ringShown, 22, 19.5);
        _liveBarHost!.Visibility = liquid ? Visibility.Visible : Visibility.Collapsed;
        if (liquid)
        {
            _liveBar!.Background = brush;
            _liveBar.Width = Math.Max(0, (LiveW - 28) * Math.Clamp(a.frac, 0, 1));
        }

        var now = DateTime.Now;
        string title, big = a.text, sub = "";
        switch (a.kind)
        {
            case "timer": title = Loc.T(a.paused ? "Таймер · пауза" : "Таймер"); break;
            case "watch": title = Loc.T(a.paused ? "Секундомер · пауза" : "Секундомер"); break;
            case "focus": title = Loc.T(_focusBreak ? "Перерыв" : "Фокус") + " · " + Loc.T("круг ") + (_focusRounds + 1) + (a.paused ? " · " + Loc.T("пауза") : ""); break;
            case "copy": title = Loc.T("Копирование файлов"); break;
            case "dl": title = Loc.T("Скачивается обновление"); break;
            case "media":   // шаг 90: видео по ссылке (MediaDlIsland.cs)
                title = Loc.T("Скачивается");
                if (MediaDl.Active is { } mj) sub = mj.Title + (mj.Speed.Length > 0 ? " · " + mj.Speed : "");
                break;
            case "build": title = Loc.T("Идёт сборка"); break;
            case "steam": title = Loc.T("Загрузка в Steam"); break;
            case "mic":
                title = Loc.T("Звонок") + (_micApp.Length > 0 ? " · " + _micApp : "");
                big = MicMuted ? Loc.T("Микрофон выкл") : Loc.T("Микрофон вкл");
                break;
            case "weather":
                var w = Weather.Last;
                title = Loc.T("Погода") + (w?.City.Length > 0 ? " · " + w.City : "");
                if (w != null)
                {
                    big = Weather.Deg(w.Temp);
                    sub = Weather.Text(w.Code) + (_rainLine.Length > 0 ? " · " + _rainLine : "");
                    var days = w.Days.Skip(1).Take(2).Select((d, i) => (i == 0 ? Loc.T("Завтра") : d.day.ToString("ddd", Loc.Culture)) + " " + Weather.Deg(d.max) + "/" + Weather.Deg(d.min));
                    sub += (sub.Length > 0 ? "\n" : "") + string.Join("  ·  ", days);
                }
                break;
            case "clock":
                title = now.ToString("dddd, d MMMM", Loc.Culture);
                big = now.ToString("HH:mm");
                sub = Holidays.For(now).FirstOrDefault()?.Text ?? "";
                break;
            case "net":
                title = NetSpeed.Online ? NetSpeed.Adapter : Loc.T("Нет сети", "Offline");
                big = "↓ " + NetSpeed.Format(NetSpeed.DownPerSec);
                sub = "↑ " + NetSpeed.Format(NetSpeed.UpPerSec);
                break;
            case "battery":
                bool charging = GetSystemPowerStatus(out var ps) && ps.ACLineStatus == 1;
                title = Loc.T(charging ? "Батарея · заряжается" : "Батарея");
                if (!charging && ps.BatteryLifeTime > 0)
                {
                    var left = TimeSpan.FromSeconds(ps.BatteryLifeTime);
                    sub = Loc.T("Осталось примерно ") + (left.TotalHours >= 1 ? $"{(int)left.TotalHours} {Loc.T("ч")} " : "") + $"{left.Minutes} {Loc.T("мин")}";
                }
                break;
            case "done": title = Loc.T("Готово"); break;
            case "live": title = Loc.T("Чат стрима"); break;
            default:
                title = a.kind.StartsWith("web:") ? a.kind[4..] : Loc.T("Сейчас");
                break;
        }
        if (_liveTitle.Text != title) _liveTitle.Text = title;
        if (_liveBig.Text != big) _liveBig.Text = big;
        if (_liveSub.Text != sub) _liveSub.Text = sub;
        _liveSub.Visibility = sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        string sig = a.kind + "|" + a.paused + "|" + (a.kind == "mic" && MicMuted) + "|" + (a.kind == "focus" && _focusBreak);
        if (sig == _liveSig) { LiveHeight(); return; }
        _liveSig = sig;
        _liveBtns.Children.Clear();
        switch (a.kind)
        {
            case "timer":
                LiveBtn(a.paused ? "play" : "pause", a.paused ? "Дальше" : "Пауза", TimerPauseToggle);
                LiveBtn("plus", "1 мин", () => TmAdd(60));
                LiveBtn("x", "Стоп", () => { _tmRun = _tmSet = false; _tmLeft = _tmTotal; UpdateTimerTexts(); });
                break;
            case "watch":
                LiveBtn(a.paused ? "play" : "pause", a.paused ? "Дальше" : "Пауза", () =>
                {
                    if (_swRun) { _swAcc += DateTime.UtcNow - _swFrom; _swRun = false; }
                    else { _swFrom = DateTime.UtcNow; _swRun = true; }
                    EnsureTmTick();
                    UpdateTimerTexts();
                });
                LiveBtn("rotate-ccw", "Сброс", () => { _swRun = false; _swAcc = TimeSpan.Zero; _laps.Clear(); UpdateTimerTexts(); });
                break;
            case "focus":
                LiveBtn(a.paused ? "play" : "pause", a.paused ? "Дальше" : "Пауза", () =>
                {
                    if (_focusPaused) { _focusEnd = DateTime.UtcNow.AddSeconds(_focusLeft); _focusPaused = false; }
                    else { _focusLeft = FocusLeftNow; _focusPaused = true; }
                    UpdateBubble();
                });
                LiveBtn(_focusBreak ? "zap" : "sun", _focusBreak ? "Работать" : "Перерыв", () => { if (!_focusBreak) _focusRounds++; BeginPhase(!_focusBreak); });
                LiveBtn("x", "Стоп", StopFocus);
                break;
            case "mic":
                LiveBtn(MicMuted ? "mic" : "mic-off", MicMuted ? "Включить" : "Выключить", ToggleMic);
                break;
            case "weather":
                LiveBtn("cloud-sun", "Прогноз", ShowWeather);
                break;
            case "clock":
                LiveBtn("calendar", "Календарь", () => CalendarWindow.ShowCalendar());
                break;
            case "battery":
                LiveBtn("battery", "Экономия", () => Launch("ms-settings:batterysaver"));
                break;
            case "done":
                break;
            case "media":
                LiveBtn("x", "Отменить", () => { if (MediaDl.Active is { } mj) MediaDl.Cancel(mj); });
                LiveBtn("download", "Загрузки", () => DownloaderWindow.ShowDownloader());
                break;
            default:
                LiveBtn("external-link", "Открыть", BubbleAction);
                break;
        }
        LiveHeight();
        // окончательный замер — после раскладки (до неё новый текст иногда меряется прошлыми размерами)
        if (_liveOpen) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { if (_liveOpen) LiveHeight(); }));
    }

    // высота по содержимому; если карточка уже раскрыта и содержимое выросло (появился прогноз), она плавно подрастает
    private void LiveHeight()
    {
        if (_liveCard == null) return;
        // кэш размеров сбрасываем у всех: иначе замер сразу после смены текста иногда брал прошлые размеры
        static void Dirty(DependencyObject d)
        {
            if (d is UIElement u) u.InvalidateMeasure();
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Dirty(VisualTreeHelper.GetChild(d, i));
        }
        Dirty(_liveCard);
        _liveCard.Measure(new Size(LiveW, double.PositiveInfinity));
        // сумма строк и поля карточки (растянутая строка кнопок в замере Grid давала лишнее)
        double h = _liveCard.Margin.Top + _liveCard.Margin.Bottom + _liveCard.Children.OfType<FrameworkElement>().Sum(c => c.DesiredSize.Height);
        h = Math.Clamp(h, 96, 200);
        if (_liveOpen && _liveH > 0 && Math.Abs(h - _liveH) > 1)
            Bubble.BeginAnimation(HeightProperty, new DoubleAnimation(h, TimeSpan.FromMilliseconds(360)) { EasingFunction = new SpringEase(6.5, 12) });
        _liveH = h;
    }

    private void TimerPauseToggle()
    {
        if (_tmRun) { _tmLeft = TmLeftNow; _tmRun = false; }
        else if (_tmSet) { _tmEnd = DateTime.UtcNow.AddSeconds(_tmLeft); _tmRun = true; }
        EnsureTmTick();
        UpdateTimerTexts();
    }

    private void LiveBtn(string icon, string text, Action act)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new VIcon { Text = icon, FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = Loc.T(text), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var b = new SBorder
        {
            Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(12, 0, 13, 0), Margin = new Thickness(0, 0, 6, 0),
            Background = ShyStyle.Tile, Child = row, Cursor = Cursors.Hand
        };
        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255));
        b.MouseLeave += (_, _) => b.Background = ShyStyle.Tile;
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;   // пузырь не должен принять это за свайп или клик
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Sfx.Ui("tap");
            try { act(); }
            catch (Exception ex) { App.LogOnce("Live." + text, ex); }
            _liveSig = "";   // подписи кнопок могли поменяться (пауза -> дальше)
            UpdateBubble();
        };
        _liveBtns!.Children.Add(b);
    }
}
