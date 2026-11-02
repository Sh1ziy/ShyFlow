using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Island;

// Язык интерфейса и выбор монитора.
public partial class MainWindow
{
    // ============================================================
    //  ЯЗЫК
    // ============================================================
    private int _langBuilt = -1;

    private void ApplyLanguage()
    {
        _langBuilt = _cfg.Language;
        Loc.Init(_cfg.Language);
        Loc.ApplyTree(this);
        if (Pill.ContextMenu != null) Loc.ApplyTree(Pill.ContextMenu);
        BuildActions();
        RenderPocket();
        if (_devicesOpen) RenderDevices();
        if (_session == null) EnterIdle();   // «Ничего не играет» и часы на нужном языке
        else _ = RefreshAsync();
    }

    // ============================================================
    //  МОНИТОР
    // ============================================================
    internal Rect MonitorDip
    {
        get
        {
            var list = Monitors.All();
            var m = list[Math.Clamp(CurMonitor, 0, list.Count - 1)];   // шаг 74: -1 = где активное окно (Screens.cs)
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX; // общий масштаб Windows
            return new Rect(m.Left / scale, m.Top / scale, m.Width / scale, m.Height / scale);
        }
    }

    private bool _displayHooked;

    // Шаг 60: окно больше не во всю ширину монитора, а полоса по центру под остров, пузыри и док.
    // Прозрачное окно WPF перерисовывается целиком, поэтому лишняя ширина (на 4K 3840 px) стоила кадров и батареи.
    // Ширина меняется редко: растёт сразу, как только чему-то стало тесно, а сужается с задержкой и ступенями,
    // чтобы окно не пересоздавало поверхность на каждом кадре анимации.
    private const double WinMinW = 1100;    // запас под жидкие растяжения, тени и смахивание уведомления в сторону
    private const double WinSideGap = 120;  // поле за самым крайним элементом
    private const double WinStep = 160;     // ступень ширины
    private double _winW;
    private DispatcherTimer? _winShrink;

    // сколько нужно окну, чтобы всё влезло: остров (с учётом того, куда он растёт) и капсулы по бокам
    private double NeededWinW()
    {
        if (Duo) return Math.Min(DuoWinW, MonitorDip.Width);   // шаг 84: остров в правом верхнем углу, капсул по бокам нет (Duo.cs)
        double pill = Math.Max(Pill.Width, Math.Max(24, _w.T) + 2 * _earNow);
        double bubbles = BubbleSpace + EvSpace;
        double apps = AppsBar.Visibility == Visibility.Visible ? AppsBar.ActualWidth + 8 : 0;
        bool appsRight = !AppsLeft, bubblesRight = !BubbleLeft;
        double right = (bubblesRight ? bubbles : 0) + (appsRight ? apps : 0);
        double left = (bubblesRight ? 0 : bubbles) + (appsRight ? 0 : apps);
        double need = pill + 2 * (Math.Max(left, right) + 8 + WinSideGap);
        need = Math.Ceiling(Math.Max(need, WinMinW) / WinStep) * WinStep;
        return Math.Min(need, MonitorDip.Width);
    }

    // Шаг 103: высота так же. Прозрачное окно перерисовывается целиком, а свёрнутому острову с плашками и тенью хватает 140 из 420.
    // Растёт сразу, до первого кадра раскрытия; сжимается через 2 секунды, когда всё встало. В Duo и notch высота прежняя: там остров течёт вниз.
    private const double WinFullH = 420, WinCompactH = 140;
    private DispatcherTimer? _winShrinkH;

    private double NeededWinH() =>
        Duo || NotchOn || Math.Max(_h.T, _h.P) > 60 || _liveOpen || Bubble.ActualHeight > 44 ? WinFullH : WinCompactH;

    private void FitWindowH()
    {
        if (DuoMid) return;
        double need = NeededWinH();
        if (need > Height + 0.5) { _winShrinkH?.Stop(); Height = need; }
        else if (need < Height - 0.5)
        {
            if (_winShrinkH == null)
            {
                _winShrinkH = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _winShrinkH.Tick += (_, _) => { _winShrinkH.Stop(); if (!DuoMid && NeededWinH() < Height - 0.5) Height = NeededWinH(); };
            }
            if (!_winShrinkH.IsEnabled) _winShrinkH.Start();
        }
    }

    // вызывается из PlaceAppsBar: при каждом изменении ширины острова и капсул
    private void FitWindow()
    {
        if (_winW <= 0) return;   // окно ещё не поставлено
        FitWindowH();
        double need = NeededWinW();
        if (need > _winW + 0.5) { _winShrink?.Stop(); SetWinW(need); }
        else if (need < _winW - WinStep - 0.5)
        {
            if (_winShrink == null)
            {
                _winShrink = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _winShrink.Tick += (_, _) => { _winShrink.Stop(); double n = NeededWinW(); if (n < _winW - 0.5) SetWinW(n); };
            }
            if (!_winShrink.IsEnabled) _winShrink.Start();
        }
    }

    private void SetWinW(double w)
    {
        var r = MonitorDip;
        _winW = w;
        // шаг 74: остров слева или справа — окно прижато к этому краю монитора, сам остров сдвигается в PlaceStage
        Left = Duo ? r.Left + r.Width - w : _cfg.IslandPos switch   // шаг 84: Duo всегда в правом верхнем углу (Duo.cs)
        {
            1 => r.Left,
            2 => r.Left + r.Width - w,
            4 => r.Left + Math.Clamp(Math.Round(_cfg.IslandFreeX * r.Width - w / 2), 0, Math.Max(0, r.Width - w)),   // 3.9.4: «Свободно» (IslandDrag.cs)
            _ => r.Left + Math.Round((r.Width - w) / 2)
        };
        Width = w;
        PlaceStage();
    }

    // ============================================================
    //  ПОЛОЖЕНИЕ (шаг 74): по центру, слева, справа
    //  Всё в окне (остров, пузыри, капсула значков) выложено от центра. Слева и справа окно прижато к краю монитора,
    //  а вся сцена сдвигается так, чтобы край острова был в Edge px от края экрана; остров растёт от края.
    //  Пузыри и значки тогда всегда с внутренней стороны, иначе они ушли бы за край экрана.
    // ============================================================
    private const double Edge = 18;
    private readonly TranslateTransform _stageMove = new();
    // «Свободно» у края ведёт себя как «Слева» или «Справа»: пузыри и значки с внутренней стороны
    private int SidePos => _cfg.IslandPos != 4 ? _cfg.IslandPos : _cfg.IslandFreeX < 0.22 ? 1 : _cfg.IslandFreeX > 0.78 ? 2 : 0;
    private bool BubbleLeft => SidePos == 1 ? false : SidePos == 2 || _cfg.BubbleSide == 1;
    private bool AppsLeft => SidePos == 1 ? false : SidePos == 2 ? _cfg.AppsPlace is 1 or 2 : _cfg.AppsPlace == 1;

    // центр острова там, куда его поставили, но не ближе Edge к краю экрана
    private double FreeStageX(double w, double half)
    {
        var r = MonitorDip;
        double c = Math.Clamp(_cfg.IslandFreeX * r.Width, Edge + half, Math.Max(Edge + half, r.Width - Edge - half));
        return c - (Left - r.Left) - w / 2;
    }

    private void PlaceStage()
    {
        if (!ReferenceEquals(Stage.RenderTransform, _stageMove)) Stage.RenderTransform = _stageMove;
        double w = _winW > 0 ? _winW : Width;
        double half = Math.Max(24, Pill.Width) / 2;
        double x = Duo ? 0 : _cfg.IslandPos switch
        {
            1 => Edge + half - w / 2,
            2 => w / 2 - Edge - half,
            4 => FreeStageX(w, half),
            _ => 0   // в Duo остров сам прижат вправо (HorizontalAlignment.Right, Duo.cs)
        };
        if (Math.Abs(_stageMove.X - x) > 0.25) _stageMove.X = Math.Round(x);
    }

    private void PlaceOnMonitor()
    {
        var r = MonitorDip;
        _winW = 0;
        SetWinW(NeededWinW());
        if (DuoMid)
        {
            // шаг 88: Duo посередине правого края — окно по центру по высоте, остров посередине окна (Duo.cs)
            Height = DuoWinH;
            Top = r.Top + Math.Round((r.Height - Height) / 2);
        }
        else
        {
            Top = r.Top;
            Height = WinFullH;   // шаг 49: с запасом под шторку уведомлений и четыре ряда быстрых действий; свёрнутому хватает WinCompactH (FitWindow)
        }
        DuoLayout();    // шаг 84: остров iPhone Duo у правого края (Duo.cs)
        if (!_displayHooked)
        {
            _displayHooked = true;
            // монитор отключили или поменяли разрешение: остров ставится на место заново
            SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => { Monitors.Invalidate(); PlaceOnMonitor(); }));
            // Per-monitor DPI (app.manifest): переехали на монитор с другим масштабом — WPF подгоняет окно под «предложенный»
            // Windows прямоугольник, и остров съезжал с центра. Ставим заново уже в новом масштабе (DpiChanged только при реальной смене).
            DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(PlaceOnMonitor));
        }
    }
}
