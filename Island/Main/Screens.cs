using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Island;

// Шаг 74: полноэкранные приложения и несколько мониторов.
//
// АВТО-СКРЫТИЕ. Раньше остров нарочно поднимался поверх всего, и игра в безрамочном режиме или видео на весь экран
// оказывались с чёрной капсулой сверху. Теперь, если активное окно занимает весь монитор острова и у него нет
// заголовка (игра, F11 в браузере, плеер на весь экран), остров уезжает вверх, как по Ctrl+Alt+I, и возвращается,
// когда это окно перестало быть на весь экран или активным. Развёрнутое окно с заголовком (обычный браузер на весь
// экран) не считается. Ctrl+Alt+I в игре всё равно показывает остров — до выхода из полноэкранного режима.
// Настройки, «Основное», «Прятать в играх и видео на весь экран» (вкл).
//
// МОНИТОР «ГДЕ АКТИВНОЕ ОКНО». Остров переезжает на монитор, где сейчас активное окно. Настройки, «Основное», «Монитор».
public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct WRECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONINFO
    {
        public int cbSize;
        public WRECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }
    // GetForegroundWindow (Translator.cs), GetClassName (CopyProgress.cs), IsWindowVisible и IsIconic (AppsBar.cs) уже объявлены
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out WRECT r);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonInfo(IntPtr hMon, ref MONINFO mi);

    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    // окна оболочки: рабочий стол, панель задач, Пуск, поиск, Alt+Tab — это не «приложение на весь экран»
    private static readonly string[] ShellClasses =
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "ForegroundStaging", "TaskListThumbnailWnd", "NotifyIconOverflowWindow"
    };

    private readonly DispatcherTimer _scrTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool _fsHidden;      // остров спрятан, потому что сейчас полноэкранное приложение
    private bool _fsOverride;    // в полноэкранном его показали Ctrl+Alt+I: не прятать снова, пока полноэкранное не кончится
    private bool _autoToggling;  // ToggleHidden вызвал сам остров, а не человек
    private int _followMon;      // монитор с активным окном (когда Monitor = -1)

    private void InitScreens()
    {
        _scrTimer.Tick += (_, _) => ScreensCheck();
        _scrTimer.Start();
        InitIdleStrip();
    }

    // вызывается при смене активного окна (Performance.cs) и по таймеру: F11 в браузере не меняет активное окно
    private void ScreensCheck()
    {
        if (_hwnd == IntPtr.Zero) return;
        IntPtr fg;
        try { fg = GetForegroundWindow(); } catch { return; }
        FollowMonitor(fg);
        FullscreenCheck(fg);
    }

    // ---------- монитор с активным окном ----------
    private int CurMonitor => _cfg.Monitor >= 0 ? _cfg.Monitor : _followMon;

    private void FollowMonitor(IntPtr fg)
    {
        if (_cfg.Monitor >= 0 || fg == IntPtr.Zero || IsShellOrOwn(fg)) return;
        int idx = MonitorIndexOf(fg);
        if (idx < 0 || idx == _followMon) return;
        _followMon = idx;
        PlaceOnMonitor();
        NotchRetarget();
        PlaceDock();   // шаг 76: док снизу едет на тот же монитор
    }

    private static int MonitorIndexOf(IntPtr h)
    {
        try
        {
            var mi = new MONINFO { cbSize = Marshal.SizeOf<MONINFO>() };
            if (!GetMonInfo(MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST), ref mi)) return -1;
            var list = Monitors.All();
            return list.FindIndex(m => m.Left == mi.rcMonitor.Left && m.Top == mi.rcMonitor.Top);
        }
        catch { return -1; }
    }

    private bool IsShellOrOwn(IntPtr h)
    {
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == OwnPid) return true;
        var sb = new StringBuilder(128);
        GetClassName(h, sb, sb.Capacity);
        string cls = sb.ToString();
        return ShellClasses.Contains(cls);
    }

    // ---------- авто-скрытие ----------
    // Шаг 76: два вида. «Тонкая полоска» (по умолчанию): свёрнутый остров сжимается в короткую полупрозрачную черту
    // и не закрывает игру, но плашки (трек, громкость, уведомления) всё равно показываются и потом снова сжимаются в черту.
    // «Прятать совсем»: как раньше, остров уезжает вверх.
    private bool _fsMini;   // сейчас полноэкранное приложение и выбран вид «тонкая полоска»

    // Игра в exclusive fullscreen (D3D) или режим презентации: окно игры может и не накрывать монитор по GetWindowRect,
    // а topmost-окно поверх exclusive-режима выбивает игру в оконный (мерцание, Alt+Tab). Тут остров прячется совсем, без полоски.
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;
    private bool _fsExclusive;
    private bool _fsAny;   // сейчас что-то на весь экран, даже если «Прятать в играх» выключено: стекло не снимает экран (Glass.cs)

    private static bool ExclusiveFullscreen()
    {
        try { return SHQueryUserNotificationState(out int s) == 0 && s is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE; }
        catch { return false; }
    }

    private void FullscreenCheck(IntPtr fg)
    {
        bool exclusive = ExclusiveFullscreen();
        _fsAny = exclusive || IsFullscreen(fg);
        GameCheck(fg, exclusive);   // шаг 97: игровой режим (GameMode.cs) прячет остров совсем
        bool hide = _cfg.HideFullscreen || _turbo;
        _fsExclusive = hide && exclusive;
        bool fs = hide && _fsAny;
        bool strip = _cfg.FsStyle == 0 && !_fsExclusive && !_turbo;
        if (!fs || !strip) SetFsMini(_stripIdle && !_fsPeek);   // шаг 79: вне игры полоска только в простое
        if (!fs)
        {
            _fsOverride = false;
            if (_fsHidden) { _fsHidden = false; if (_hidden) AutoToggle(); }
            return;
        }
        if (strip)
        {
            if (_fsHidden) { _fsHidden = false; if (_hidden) AutoToggle(); }   // вид сменили прямо в игре
            SetFsMini(!_fsOverride && !_fsPeek);
            return;
        }
        if (_fsOverride || _fsHidden || _hidden) return;
        _fsHidden = true;
        AutoToggle();
    }

    private void SetFsMini(bool on)
    {
        if (_fsMini == on) return;
        _fsMini = on;
        if (_mode == Mode.Mini) ApplyMode(Mode.Mini);   // сжаться в черту или вернуться
        Pill.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
            on && _mode == Mode.Mini ? StripOpacity : 1, TimeSpan.FromMilliseconds(300)));
        AppsHiddenChanged();   // пузыри и капсула значков в игре не нужны
        UpdateChrome();        // в полоске без тени и кромки (Chrome.cs)
        StripSync();
    }

    // ---------- шаг 79: полоска в простое ----------
    // Ничего не играет и ничего не идёт (таймер, копирование, загрузка) дольше полутора минут — свёрнутый остров тоже
    // становится полоской, но заметнее игровой: шире, почти непрозрачная. Музыка, событие или мышь — и остров снова целиком.
    // Настройки, «Основное», «Полоска, когда ничего не играет».
    private bool _stripIdle;
    private DateTime _idleSince = DateTime.UtcNow;
    private readonly DispatcherTimer _idleStripTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private const double IdleStripSec = 90;

    internal double StripW => _stripIdle ? 84 : 56;
    internal double StripH => _stripIdle ? 6 : 5;
    private double StripOpacity => _stripIdle ? 0.9 : 0.45;

    private void InitIdleStrip()
    {
        _idleStripTimer.Tick += (_, _) => IdleStripTick();
        _idleStripTimer.Start();
    }

    private void IdleStripTick()
    {
        bool busy = _playing || _hidden || !_cfg.IdleStrip || PickActivity() != null || (_mode != Mode.Mini && _mode != Mode.Osd);
        if (busy) _idleSince = DateTime.UtcNow;
        bool want = !busy && (DateTime.UtcNow - _idleSince).TotalSeconds >= IdleStripSec;
        if (want == _stripIdle) return;
        _stripIdle = want;
        if (_fsMini && _mode == Mode.Mini) { _fsMini = false; SetFsMini(true); }   // размер поменялся: перерисовать полоску
        else ScreensCheck();
    }

    // ---------- шаг 79: полоска живая ----------
    // В полоске идёт прогресс трека цветом обложки, заливка дышит с басами. Навёл мышь на полоску — остров выглядывает
    // целиком (свайпы, клик, колесо работают), убрал — через секунду снова полоска. Клик по выглянувшему острову раскрывает его.
    private SBorder? _fsBar;
    private readonly DispatcherTimer _fsBarTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool _fsPeek, _fsBarHooked;
    private DateTime _fsBarProgAt;
    private DispatcherTimer? _fsPeekOff;

    private void StripSync()
    {
        bool on = _fsMini && _mode == Mode.Mini;
        if (_fsBar == null)
        {
            if (!on) return;
            _fsBar = new SBorder
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Width = 0, CornerRadius = new System.Windows.CornerRadius(2.5),
                IsHitTestVisible = false, Opacity = 0
            };
            _ui.Use(_fsBar, System.Windows.Controls.Border.BackgroundProperty);
            PillContent.Children.Add(_fsBar);
            Pill.MouseEnter += (_, _) => StripPeek(true);
            Pill.MouseLeave += (_, _) => StripPeek(false);
        }
        if (!_fsBarHooked) { _fsBarTimer.Tick += (_, _) => StripTick(); _fsBarHooked = true; }
        if (on) _fsBarTimer.Start(); else { _fsBarTimer.Stop(); _fsBar.Opacity = 0; }
    }

    private void StripTick()
    {
        if (_fsBar == null || !_fsMini || _mode != Mode.Mini) { _fsBarTimer.Stop(); if (_fsBar != null) _fsBar.Opacity = 0; return; }
        // басы: заливка ярче на ударе (уровни эквалайзера уже сглажены, Tick в MainWindow)
        double bass = _cap != null && _bp.Length > 1 ? Math.Clamp((_bp[0] + _bp[1]) / 2, 0, 1.2) : 0;
        _fsBar.Opacity = _playing ? 0.7 + 0.3 * Math.Min(1, bass) : 0.45;
        if ((DateTime.UtcNow - _fsBarProgAt).TotalMilliseconds < 500) return;
        _fsBarProgAt = DateTime.UtcNow;
        try
        {
            var tl = _session?.GetTimelineProperties();
            if (tl == null) { _fsBar.Width = 0; return; }
            double total = (tl.EndTime - tl.StartTime).TotalSeconds;
            if (total <= 1) { _fsBar.Width = Pill.ActualWidth; return; }   // без длительности (стрим): полная черта
            var pos = tl.Position + (_playing ? DateTimeOffset.Now - tl.LastUpdatedTime : TimeSpan.Zero);
            double k = Math.Clamp((pos - tl.StartTime).TotalSeconds / total, 0, 1);
            _fsBar.Width = Math.Max(5, Pill.ActualWidth * k);
        }
        catch (Exception ex) { App.LogOnce("Strip", ex); }
    }

    private void StripPeek(bool over)
    {
        if (over)
        {
            _fsPeekOff?.Stop();
            if (!_fsMini || _mode != Mode.Mini) return;
            _fsPeek = true;
            SetFsMini(false);
            return;
        }
        if (!_fsPeek) return;
        _fsPeekOff ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _fsPeekOff.Tick -= PeekOffTick;
        _fsPeekOff.Tick += PeekOffTick;
        _fsPeekOff.Start();
    }

    private void PeekOffTick(object? s, EventArgs e)
    {
        _fsPeekOff?.Stop();
        if (Pill.IsMouseOver || _swipeDown) { _fsPeekOff?.Start(); return; }
        _fsPeek = false;
        if (_mode == Mode.Mini && _stripIdle) { SetFsMini(true); return; }   // полоска в простое (выше)
        if (_mode == Mode.Mini && !_fsOverride && _cfg.HideFullscreen && _cfg.FsStyle == 0)
        {
            try { if (IsFullscreen(GetForegroundWindow())) SetFsMini(true); } catch { }
        }
    }

    private void AutoToggle()
    {
        _autoToggling = true;
        try { ToggleHidden(); }
        finally { _autoToggling = false; }
    }

    // человек сам нажал Ctrl+Alt+I или «Скрыть / показать» (из ToggleHidden)
    private void OnManualToggle()
    {
        if (_autoToggling) return;
        if (_fsHidden) { _fsHidden = false; _fsOverride = true; }
        if (_fsMini) { _fsOverride = true; SetFsMini(false); }   // в игре показали остров целиком
    }

    private bool IsFullscreen(IntPtr h)
    {
        try
        {
            if (h == IntPtr.Zero || !IsWindowVisible(h) || IsIconic(h) || IsShellOrOwn(h)) return false;
            if ((GetWindowLong(h, GWL_STYLE) & WS_CAPTION) == WS_CAPTION) return false;   // обычное развёрнутое окно с заголовком
            if (!GetWindowRect(h, out var r)) return false;
            var list = Monitors.All();
            var m = list[Math.Clamp(CurMonitor, 0, list.Count - 1)];
            // окно накрывает весь монитор острова (пару пикселей запаса на рамки)
            return r.Left <= m.Left + 2 && r.Top <= m.Top + 2 && r.Right >= m.Left + m.Width - 2 && r.Bottom >= m.Top + m.Height - 2;
        }
        catch { return false; }
    }
}
