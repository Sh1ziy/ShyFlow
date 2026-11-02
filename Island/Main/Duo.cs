using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Island;

// Шаг 84: режим iPhone Duo (отдельный переключатель рядом с notch, «Внешний вид»). У складного iPhone Duo Dynamic Island
// вертикальный, у края экрана, и вырастает внутрь экрана.
//
// Где стоит (шаг 88, «iPhone Duo: где стоит»): в правом верхнем углу или посередине правого края. В углу остров растёт
// из угла влево и вниз, посередине — влево и в обе стороны по высоте; прячется (Ctrl+Alt+I) тогда за правый край.
// Свёрнутый остров — узкая вертикальная капсула. Содержимое НЕ поворачивается, а перекладывается в колонку, как компактный
// вид Live Activity у Apple: обложка, под ней свой короткий эквалайзер (4 полоски), под ним идущий таймер (значок над временем).
// Без музыки — часы в две строки.
// Текст: наведение мыши (и смена трека, на 2,5 с) выдвигает из капсулы влево «язычок» с названием и исполнителем — текст
// ровный, капсула остаётся у края. Во время слов песни вместо исполнителя текущая строка.
// Шаг 88: то же для обычного острова — «Компактный остров»: только обложка и эквалайзер, название при наведении.
// Громкость, яркость, копирование и скачивание — вертикальная капсула: значок сверху, полоска наливается снизу вверх, число внизу.
// Пузырь активностей (таймер, звонок, погода...) — тоже вертикальный: кольцо над коротким текстом, под островом у того же края;
// раскрытая карточка Live Activity растёт от края влево.
// Шаг 88: карман и «Кинь файл сюда» в Duo не широкие, а почти квадратные (4:3): вкладки только значком и счётчиком.
// Плашки с текстом и раскрытые панели обычные, горизонтальные. Капсула значков, пузырь событий и notch в Duo выключены.
public partial class MainWindow
{
    private bool Duo => _cfg.DuoMode;
    private bool DuoMid => Duo && _cfg.DuoPlace == 1;
    private const double DuoGap = 8;       // от края экрана до острова
    private const double DuoWinW = 640;    // самая широкая панель (480) + запас под желе и тень
    private const double DuoWinMaxH = 820; // окно посередине края: по высоте с запасом под раскрытые панели в обе стороны
    private const double DuoW = 40;        // ширина вертикальной капсулы
    private const double DuoPad = 9;       // поле сверху и снизу внутри неё
    private const double DuoOsdH = 156;    // вертикальная плашка громкости
    private const double DuoTrackH = 84;   // её полоска
    private const double PeekMaxW = 230;   // язычок с названием
    private const double DuoPocketW = 320, DuoPocketH = 240;   // карман 4:3
    private const double DuoDropW = 176, DuoDropH = 132;       // «Кинь файл сюда» 4:3
    private int? _duoApplied;   // 0 обычный остров, 1 Duo в углу, 2 Duo посередине

    private double DuoWinH => Math.Min(MonitorDip.Height, DuoWinMaxH);

    private Grid? _duoMini;
    private StackPanel? _duoCol, _duoPeek;
    private TextBlock? _duoClock, _peekTitle, _peekSub;
    private StackPanel? _duoEq;
    private readonly List<Border> _duoBars = new();
    private int _coverIdx, _timerIdx;   // где обложка и таймер стояли в Mini, чтобы вернуть на место

    private Grid? _duoOsd;
    private VIcon? _duoOsdIcon;
    private Border? _duoOsdFill;
    private TextBlock? _duoOsdValue;
    private bool _duoOsdVert;   // текущая плашка — вертикальная (громкость и т.п. в Duo)

    // ---------- язычок с названием (Duo и компактный остров) ----------
    private bool _peekHover, _peekAuto;
    private DispatcherTimer? _peekTimer;
    private bool PeekOn => (_peekHover || _peekAuto) && _mode == Mode.Mini && !_fsMini;
    // шаг 88: обычный остров без текста (часы — это текст, их не прячем)
    private bool Compact => _cfg.CompactMini && !Duo && !_clockMode && !_fsMini;
    private bool PeekMode => Duo || Compact;
    private double CompactW => 8 + 24 + (MiniEqOn ? 10 + 42 : 0) + 12 + MiniTimerW;

    // свёрнутый остров (шаг 76: в игре тонкая черта, Screens.cs)
    private void GoMini()
    {
        if (_fsMini) Go(StripW, StripH, StripH / 2);
        else if (Duo && _duoMini != null) GoDuoMini();
        else if (Compact && !PeekOn) Go(CompactW, 38, 19);
        else Go(_miniW, 38, 19);
        CompactText(!Compact || PeekOn);
    }

    // шаг 88: компактный остров — название проявляется, когда остров уже почти раздвинулся, и гаснет сразу при сжатии.
    // Обложка и эквалайзер при этом едут вместе с краями острова (Tick: CompactTrack), а не стоят на итоговых местах
    private bool? _compactShown;
    private void CompactText(bool show)
    {
        if (_compactShown == show) return;
        _compactShown = show;
        MiniTextBox.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 240 : 110))
        { BeginTime = TimeSpan.FromMilliseconds(show && Compact ? 110 : 0), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // ширина содержимого компактного острова идёт за пружиной острова покадрово
    private bool CompactTrack => Compact && _mode == Mode.Mini;

    // ширина содержимого свёрнутого острова (FixWidths): у компактного без названия — только обложка и эквалайзер
    private double MiniContentW => (Compact && !PeekOn ? CompactW : _miniW) - 22;

    private void GoDuoMini()
    {
        _duoClock!.Visibility = _clockMode ? Visibility.Visible : Visibility.Collapsed;
        if (_clockMode) _duoClock.Text = DateTime.Now.ToString("HH\nmm");
        _duoEq!.Visibility = MiniEq.Visibility;
        _duoCol!.Measure(new Size(DuoW, double.PositiveInfinity));
        double h = Math.Max(DuoW, Math.Ceiling(_duoCol.DesiredSize.Height) + 2 * DuoPad);
        double w = DuoW;
        bool peek = PeekOn;
        if (peek)
        {
            // язычок: название и подпись, ширина по тексту; капсула с обложкой остаётся у края
            string title, sub;
            if (_clockMode) { title = DateTime.Now.ToString("dddd, d MMMM", Loc.Culture); sub = ""; }
            else if (_curTitle.Length > 0) { title = _curTitle; sub = _lyricMode && MiniTitle.Text.Length > 0 ? MiniTitle.Text : _curArtist; }
            else { title = MiniTitle.Text; sub = ""; }
            _peekTitle!.Text = title;
            _peekSub!.Text = sub;
            _peekSub.Visibility = sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            double tw = Math.Min(PeekMaxW, Math.Max(TextW(title, 12.5, FontWeights.SemiBold), sub.Length > 0 ? TextW(sub, 11, FontWeights.Normal) : 0) + 4);
            _duoPeek!.Width = tw;
            w = DuoW + 14 + tw;
            h = Math.Max(h, sub.Length > 0 ? 48 : 40);
        }
        if (_duoPeek!.Tag as bool? != peek)
        {
            _duoPeek.Tag = peek;
            _duoPeek.BeginAnimation(OpacityProperty, new DoubleAnimation(peek ? 1 : 0, TimeSpan.FromMilliseconds(peek ? 260 : 120))
            { BeginTime = TimeSpan.FromMilliseconds(peek ? 70 : 0) });   // текст проявляется, когда язычок уже выдвинулся
        }
        Go(w, h, DuoW / 2);
    }

    // наведение и смена трека
    private void PeekHover(bool on)
    {
        if (_peekHover == on) return;
        _peekHover = on;
        if (_mode == Mode.Mini) Retarget();
    }

    private void PeekAuto()
    {
        if (_peekTimer == null)
        {
            _peekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
            _peekTimer.Tick += (_, _) => { _peekTimer.Stop(); _peekAuto = false; if (_mode == Mode.Mini) Retarget(); };
        }
        _peekAuto = true;
        _peekTimer.Stop(); _peekTimer.Start();
        if (_mode == Mode.Osd) ApplyMode(Rest());
        else if (_mode == Mode.Mini) Retarget();
    }

    private void EnsureDuoMini()
    {
        if (_duoMini != null) return;
        _duoMini = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch, Opacity = 0 };
        _duoMini.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _duoMini.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DuoW) });

        _peekTitle = new TextBlock { Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _peekSub = new TextBlock { Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 255, 255, 255)), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        _duoPeek = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), Opacity = 0, Width = 0, Children = { _peekTitle, _peekSub } };
        _duoMini.Children.Add(_duoPeek);

        _duoCol = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_duoCol, 1);
        _duoClock = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
            LineHeight = 15, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(_duoClock, FontNumeralAlignment.Tabular);
        _duoCol.Children.Add(_duoClock);
        // свой эквалайзер: 4 полоски по 3 px (обычный, 7 полосок по 42 px, в узкой капсуле слишком длинный)
        _duoEq = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 14, Margin = new Thickness(0, 9, 0, 0) };
        for (int i = 0; i < 4; i++)
        {
            var b = new Border { Width = 3, Height = 3, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(i == 0 ? 0 : 2.5, 0, 0, 0) };
            if (_eqUi != null) _eqUi.Use(b, Border.BackgroundProperty); else b.Background = Brushes.White;
            _duoBars.Add(b);
            _duoEq.Children.Add(b);
        }
        _duoMini.Children.Add(_duoCol);
        PillContent.Children.Insert(PillContent.Children.IndexOf(Mini) + 1, _duoMini);

        // вертикальная плашка громкости: значок, полоска снизу вверх, число
        _duoOsdIcon = new VIcon { FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        var track = new Border { Width = 6, Height = DuoTrackH, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        _duoOsdFill = new Border { Width = 6, Height = 0, CornerRadius = new CornerRadius(3), Background = Brushes.White, VerticalAlignment = VerticalAlignment.Bottom };
        track.Child = _duoOsdFill;
        _duoOsdValue = new TextBlock { Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 9, 0, 0) };
        System.Windows.Documents.Typography.SetNumeralAlignment(_duoOsdValue, FontNumeralAlignment.Tabular);
        _duoOsd = new Grid { HorizontalAlignment = HorizontalAlignment.Right, Width = DuoW, Opacity = 0, IsHitTestVisible = false };
        _duoOsd.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _duoOsdIcon, track, _duoOsdValue } });
        PillContent.Children.Add(_duoOsd);
    }

    // покадрово (Tick): полоски берут уровни обычного эквалайзера, по две полосы на одну
    private void UpdateDuoEq()
    {
        if (_duoBars.Count == 0 || _duoEq!.Visibility != Visibility.Visible) return;
        for (int i = 0; i < 4; i++)
        {
            int a = Math.Min(Bands - 1, i * 2), b = Math.Min(Bands - 1, i * 2 + 1);
            double v = Math.Clamp((_bp[a] + _bp[b]) / 2, 0, 1);
            _duoBars[i].Height = 3 + 11 * v;
        }
    }

    // ShowToast: громкость, яркость, копирование, скачивание в Duo — вертикально. Текстовые плашки как обычно
    private void DuoOsd(string glyph, double v, Color col, bool muted, bool vol)
    {
        _duoOsdIcon!.Text = glyph;
        _duoOsdIcon.Foreground = new SolidColorBrush(IsWhiteish(col) ? Colors.White : col);
        if (vol && !muted) _ui.Use(_duoOsdFill!, Border.BackgroundProperty);
        else _duoOsdFill!.Background = SafeBrush.Solid(IsWhiteish(col) ? Colors.White : col);
        _duoOsdFill!.BeginAnimation(HeightProperty, new DoubleAnimation(DuoTrackH * Math.Clamp(v, 0, 1), TimeSpan.FromMilliseconds(160))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _duoOsdValue!.Text = muted ? "—" : ((int)Math.Round(v * 100)).ToString();
    }

    // раскладка под режим; вызывается из PlaceOnMonitor (запуск и смена настроек)
    private void DuoLayout()
    {
        if (_cfg.IslandPos == 3) { _cfg.IslandPos = 2; _cfg.DuoMode = true; _cfg.Save(); }   // тестовая сборка 3.9.2 хранила Duo в «Положении»
        EnsureDuoMini();
        int st = !Duo ? 0 : DuoMid ? 2 : 1;
        if (_duoApplied == st) return;
        bool wasOn = _duoApplied is > 0, on = st > 0, first = _duoApplied == null;
        _duoApplied = st;
        Pill.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        Pill.VerticalAlignment = st == 2 ? VerticalAlignment.Center : VerticalAlignment.Top;
        _notchTopApplied = -1;   // NotchLayout заново выставит отступы острова
        if (on != wasOn || first)
        {
            if (on) DuoColumnIn(); else if (!first) DuoColumnOut();
            DuoBubbleLayout(on);
            DuoPocketLayout(on);
        }
        if (IsLoaded)
        {
            NotchLayout();
            bool mini = _mode == Mode.Mini && !_fsMini;
            Reveal(Mini, mini && !on, -8);
            Reveal(_duoMini!, mini && on, -8);
            Retarget();
            UpdateBubble();
            UpdateMiniTimer();
        }
    }

    // обложка и таймер переезжают из строки Mini в колонку (те же элементы: весь код с ними работает как раньше)
    private void DuoColumnIn()
    {
        _coverIdx = Mini.Children.IndexOf(CoverMini); _timerIdx = Mini.Children.IndexOf(MiniTimer);
        Mini.Children.Remove(CoverMini); Mini.Children.Remove(MiniTimer);
        CoverMini.HorizontalAlignment = HorizontalAlignment.Center;
        MiniTimer.Orientation = Orientation.Vertical;
        MiniTimer.HorizontalAlignment = HorizontalAlignment.Center;
        MiniTimer.Margin = new Thickness(0, 9, 0, 0);
        MiniTimerIcon.HorizontalAlignment = HorizontalAlignment.Center;
        MiniTimerText.Margin = new Thickness(0, 2, 0, 0);
        MiniTimerText.FontSize = 11;
        _duoCol!.Children.Insert(1, CoverMini);
        _duoCol.Children.Add(_duoEq!);
        _duoCol.Children.Add(MiniTimer);
        MiniProg.Visibility = Visibility.Collapsed;   // полоска прогресса идёт по низу горизонтального острова
        AppsBar.Visibility = Visibility.Collapsed;
        if (IsLoaded) HideEvent();
    }

    private void DuoColumnOut()
    {
        _duoCol!.Children.Remove(CoverMini); _duoCol.Children.Remove(MiniTimer); _duoCol.Children.Remove(_duoEq!);
        CoverMini.HorizontalAlignment = HorizontalAlignment.Stretch;
        MiniTimer.Orientation = Orientation.Horizontal;
        MiniTimer.HorizontalAlignment = HorizontalAlignment.Stretch;
        MiniTimer.Margin = new Thickness(8, 0, 0, 0);
        MiniTimerIcon.HorizontalAlignment = HorizontalAlignment.Stretch;
        MiniTimerText.Margin = new Thickness(4, 0, 0, 0);
        MiniTimerText.FontSize = 12;
        foreach (var (el, i) in new (UIElement, int)[] { (CoverMini, _coverIdx), (MiniTimer, _timerIdx) }.OrderBy(p => p.Item2))
            Mini.Children.Insert(Math.Clamp(i, 0, Mini.Children.Count), el);
        MiniProg.Visibility = Visibility.Visible;
        _peekHover = _peekAuto = false;
    }

    // ---------- пузырь активностей: вертикальный в Duo ----------
    private void DuoBubbleLayout(bool on)
    {
        BubbleBody.Orientation = on ? Orientation.Vertical : Orientation.Horizontal;
        BubbleBody.Margin = on ? new Thickness(0, 9, 0, 9) : new Thickness(8, 0, 12, 0);
        BubbleBody.HorizontalAlignment = on ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        if (BubbleBody.Children[0] is FrameworkElement ring) ring.HorizontalAlignment = on ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        BubbleText.Margin = on ? new Thickness(0, 5, 0, 0) : new Thickness(7, 0, 0, 0);
        BubbleText.FontSize = on ? 11 : 12.5;
        BubbleText.HorizontalAlignment = on ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        BubbleMulti.Orientation = on ? Orientation.Vertical : Orientation.Horizontal;
        BubbleMulti.Margin = on ? new Thickness(0, 5, 0, 0) : new Thickness(6, 0, 0, 0);
        BubbleMulti.HorizontalAlignment = on ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        BubbleDots.Orientation = on ? Orientation.Horizontal : Orientation.Vertical;
        BubbleDots.Margin = on ? new Thickness(0, 6, 0, 0) : new Thickness(8, 0, -4, 0);
        BubbleDots.HorizontalAlignment = on ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        Bubble.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        Bubble.Width = on ? DuoW : double.NaN;
        Bubble.Height = on ? double.NaN : 38;
        Bubble.CornerRadius = new CornerRadius(on ? DuoW / 2 : 19);
        BubbleMove.X = 0;
        if (!on) Bubble.Margin = new Thickness(0, 8, 0, 0);
    }

    // текст в вертикальном пузыре: короткий (время, проценты, градусы) — под кольцом, длинный — только в подсказке
    private bool DuoBubbleText(string text) => !Duo || text.Length <= 5;

    // высота свёрнутого вертикального пузыря (LiveActivity.cs: карточка сворачивается обратно в него)
    private double DuoBubbleH()
    {
        BubbleBody.Measure(new Size(DuoW, double.PositiveInfinity));
        return Math.Max(DuoW, BubbleBody.DesiredSize.Height + BubbleBody.Margin.Top + BubbleBody.Margin.Bottom);
    }

    // пузырь в Duo: под островом, прижат к тому же краю
    private void PlaceBubbleDuo()
    {
        double top = DuoMid ? (Height > 0 ? Height : DuoWinH) / 2 + Pill.Height / 2 + 8 : Pill.Margin.Top + Pill.Height + 8;
        var m = new Thickness(0, top, DuoGap, 0);
        if (Math.Abs(Bubble.Margin.Top - top) > 0.1 || Bubble.Margin.Right != DuoGap) Bubble.Margin = m;
        BubbleMove.X = 0;
    }

    // ---------- карман и «Кинь файл сюда» 4:3 в Duo ----------
    private readonly TextBlock?[] _tabName = new TextBlock?[3];
    private double TabW => Duo ? 54 : 94;

    private void DuoPocketLayout(bool on)
    {
        // вкладки строит сам карман (BuildTabs, после выбора языка) и сразу по режиму; здесь — только смена режима на лету
        foreach (var b in new[] { TabFiles, TabClips, TabNotes }) b.Width = TabW;
        TabPill.Width = TabW;
        for (int i = 0; i < 3; i++) if (_tabName[i] is { } n) n.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (_tabsBuilt) StyleTabs(false);   // подсветка вкладки переезжает на новую ширину
        // подсказка при перетаскивании: значок над текстом
        if (DropBadge.Parent is StackPanel row) { row.Orientation = on ? Orientation.Vertical : Orientation.Horizontal; row.Margin = on ? new Thickness(8, 0, 8, 0) : new Thickness(14, 0, 14, 0); }
        DropBadge.HorizontalAlignment = on ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        if (DropText.Parent is StackPanel txt)
        {
            txt.Margin = on ? new Thickness(0, 9, 0, 0) : new Thickness(12, 0, 0, 0);
            txt.MaxWidth = on ? DuoDropW - 34 : 220;
        }
        DropText.TextAlignment = DropSub.TextAlignment = on ? TextAlignment.Center : TextAlignment.Left;
        DropText.FontSize = on ? 13 : 14;
    }

    // куда залетает плеер на весь экран (PlayerBridge.ReturnRect): к правому краю, в угол или посередине
    private Rect DuoReturn(Rect from, double w, double h) =>
        DuoMid ? new(from.Right - w, from.Y + from.Height / 2 - h / 2, w, h) : new(from.Right - w, from.Y, w, h);
}
