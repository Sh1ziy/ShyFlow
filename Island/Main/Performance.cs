using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Island;

// Экономный режим, «поверх всего» и счётчик кадров.
//
// Экономный режим (Настройки, «Система», «Производительность»): выкл, авто, всегда.
//   Что делает: эквалайзер, волна текста и плеер обновляются 30 раз в секунду, а смена вида острова идёт на 60, чтобы не дёргалась;
//   выключает жидкие анимации, свечение строк и слов, размытие дальних строк, визуализатор и свечение в плеере,
//   объёмную обложку и анимацию по буквам. Настройки при этом не меняются: выключили режим, и всё вернулось.
//   «Авто» включает его на слабом ПК (WeakPc), пока ноутбук работает от батареи или пока остров не успевает рисовать
//   (кадров заметно меньше обычного 3 секунды подряд, например когда игра забирает видеокарту).
//
// В любом режиме: покадровая анимация останавливается, когда ей нечего показывать (эквалайзер не виден,
// остров спрятан Ctrl+Alt+I), а спрятанному острову не нужен захват звука.
//
// Поверх всего: при каждой смене активного окна (и раз в 3 секунды) остров заново поднимается наверх,
// поэтому его не перекрывают окна на весь экран и игры в оконном и безрамочном режиме. Мешает: Ctrl+Alt+I.
// Шаг 74: в играх и видео на весь экран остров по умолчанию сам уезжает вверх (Screens.cs).
// Игру в «настоящем» полноэкранном режиме (exclusive fullscreen) не перекрыть никакому окну Windows: там нужен безрамочный.
public partial class MainWindow
{
    private bool _eco;                 // экономный режим действует прямо сейчас
    private string _ecoWhy = "";
    private bool _onBattery;
    private DateTime _lowFpsUntil = DateTime.MinValue;
    private int _lowFpsSec;

    private bool LiquidOn => _cfg.LiquidFx && !_eco && _cfg.MorphStyle == 0;   // жидкость только в жидком стиле морфинга (Vibe.cs)
    private bool GlowFx => _cfg.LyricsGlow && !_eco;

    // ---------- кадры ----------
    private TimeSpan _lastRt;
    private DateTime _lastWork = DateTime.MinValue;
    private int _frames;
    private TimeSpan _fpsFrom;
    private double _fps;
    private const double LowFps = 36;   // меньше этого глазу уже заметно, на любой частоте экрана

    // без видеоускорения (удалённый стол, нет драйвера) или 4 потока и меньше: сразу без тяжёлых эффектов, не дожидаясь просадки кадров
    private static readonly bool WeakPc = (RenderCapability.Tier >> 16) < 2 || Environment.ProcessorCount <= 4;
    private DateTime _fpsAt = DateTime.MinValue;

    // Вызывается в начале покадровой анимации. Событие кадра WPF приходит несколько раз за один кадр:
    // считаем и рисуем только первый. В экономном режиме не чаще 30 раз в секунду, а пока остров меняет размер — 60.
    private bool FrameDue(EventArgs e)
    {
        if (e is RenderingEventArgs re)
        {
            if (re.RenderingTime == _lastRt) return false;
            _lastRt = re.RenderingTime;
            CountFrame(re.RenderingTime);
        }
        if (!_eco) return true;
        var now = DateTime.UtcNow;
        bool moving = !_w.Settled || !_h.Settled || !_r.Settled;
        if ((now - _lastWork).TotalMilliseconds < (moving ? 15 : 31)) return false;
        _lastWork = now;
        return true;
    }

    private void CountFrame(TimeSpan rt)
    {
        if (_frames == 0 || rt < _fpsFrom) { _fpsFrom = rt; _frames = 1; return; }
        _frames++;
        double sec = (rt - _fpsFrom).TotalSeconds;
        if (sec < 1) return;
        _fps = (_frames - 1) / sec;
        _fpsAt = DateTime.UtcNow;
        _frames = 0;
    }

    private bool FpsFresh => (DateTime.UtcNow - _fpsAt).TotalSeconds < 1.6;

    // ---------- раз в секунду: батарея, нехватка кадров, решение про экономный режим ----------
    private readonly DispatcherTimer _perfTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _perfTick;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus s);

    private void StartPerformance()
    {
        _perfTimer.Tick += (_, _) => PerfTick();
        _perfTimer.Start();
        PerfTick();
    }

    private void PerfTick()
    {
        try
        {
            if (_perfTick++ % 5 == 0 && GetSystemPowerStatus(out var ps))
                _onBattery = ps.ACLineStatus == 0 && (ps.BatteryFlag & 128) == 0;   // 128: батареи нет вообще

            // остров не успевает: меньше LowFps кадров 3 секунды подряд. Порог общий для всех экранов: прежний «60 % от самого
            // быстрого экрана» после переезда со 144 Гц на 60 Гц считал обычные 60 к/с просадкой и включал режим без причины.
            // Режим держится 2 минуты, потом проверяем снова (вдруг игру закрыли).
            bool busy = FpsFresh && IsVisible && !_hidden;
            if (busy && _fps < LowFps) _lowFpsSec++; else _lowFpsSec = 0;
            if (_lowFpsSec >= 3) { _lowFpsUntil = DateTime.UtcNow.AddMinutes(2); _lowFpsSec = 0; }
            bool slow = DateTime.UtcNow < _lowFpsUntil;

            GameCpuTick();
            bool want = _turbo || _cfg.PerfMode == 2 || (_cfg.PerfMode == 1 && (WeakPc || _onBattery || slow));
            string why = _turbo ? "игровой режим" : _cfg.PerfMode == 2 ? "всегда" : WeakPc ? "слабый ПК" : _onBattery ? "от батареи" : slow ? "мало кадров" : "";
            SetEco(want, why);
            if (_fpsOn) ShowFps();
        }
        catch (Exception ex) { App.LogOnce("Performance", ex); }
    }

    private void SetEco(bool on, string why)
    {
        _ecoWhy = why;
        if (on == _eco) return;
        _eco = on;
        // всё, что зависит от эффектов, перестраивается; сами настройки не трогаем
        ApplyFluidSettings();
        ApplyFeatureSettings();
        UpdateChrome();
        if (GlassOn) ApplyGlass();   // 3.9.4: реже снимать фон в экономном режиме (Glass.cs)
        if (!NeedCapture()) StopCapture();
        else if (_playing) StartCapture();
        EnsureLoop();
        // авто-переключение показываем плашкой, но не поверх открытого кармана или плеера в острове
        if (_ready && !_turbo && _cfg.PerfMode == 1 && _mode == Mode.Mini)
            ShowToast("gauge", on ? Loc.T("Экономный режим: ") + Loc.T(why) : Loc.T("Экономный режим выключен"), null, "key");
    }

    // настройка поменялась в окне настроек: решаем сразу, не ждём следующей секунды
    private void ApplyPerfSettings() => PerfTick();

    // ---------- счётчик кадров (Ctrl+Alt+F) ----------
    private bool _fpsOn;

    private void ToggleFps()
    {
        _fpsOn = !_fpsOn;
        FpsBox.Visibility = _fpsOn ? Visibility.Visible : Visibility.Collapsed;
        if (_fpsOn) { EnsureLoop(); ShowFps(); }
    }

    private void ShowFps()
    {
        string eco = _eco ? Loc.T("эко: ") + Loc.T(_ecoWhy) : Loc.T("эко выкл");
        FpsText.Text = (FpsFresh ? $"{_fps:0} " + Loc.T("к/с") : Loc.T("простой")) + "  ·  " + eco;
    }

    // ---------- поверх всего ----------
    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private WinEventProc? _fgProc;      // держим ссылку, иначе сборщик мусора уберёт делегат, а Windows будет его звать
    private IntPtr _fgHook;
    private readonly DispatcherTimer _topTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    private void InitTopmost()
    {
        try
        {
            _fgProc = (_, _, h, _, _, _, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { RaiseTop(); OnForeground(h); ScreensCheck(); }));
            _fgHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _fgProc, 0, 0, WINEVENT_OUTOFCONTEXT);
            _topTimer.Tick += (_, _) => RaiseTop();
            _topTimer.Start();
            Closed += (_, _) => { if (_fgHook != IntPtr.Zero) UnhookWinEvent(_fgHook); };
        }
        catch (Exception ex) { App.Log("Topmost", ex); }
    }

    private void RaiseTop()
    {
        // спрятан из-за exclusive fullscreen: не лезем поверх игры, иначе Windows выбивает её из полноэкранного режима (Screens.cs)
        if (_hwnd == IntPtr.Zero || (_fsHidden && _fsExclusive)) return;
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }
}
