using System.Runtime;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Island;

// Шаг 97: игровой режим. ShyFlow уступает ресурсы игре или другой тяжёлой программе.
//
// Когда: активное окно на весь экран (Screens.cs) или процессор занят больше 80 % 15 секунд подряд, а активна та же программа.
// Браузеры и плееры не считаются: F11 на видео — не игра. Настройки, «Система», «Игровой режим»: выкл, спрашивать, всегда.
// Спрашивает маленькое окно без фокуса (Ui\GamePrompt.cs), один раз за запуск для каждой программы; «Всегда» запоминается в настройках.
// В настоящем полноэкранном режиме окно не видно и выбивало бы игру в оконный, поэтому там сразу включается без вопроса.
// Ctrl+Alt+G включает и выключает вручную: в игре с захваченной мышью по кнопкам не кликнуть.
//
// Что делает: остров на весь экран прячется совсем (нечего рисовать), экономный режим, нет захвата звука и снимков стекла,
// приоритет процесса ниже обычного, память отдаётся системе. Когда программа закрыта или свёрнута на 5 секунд, всё возвращается.
public partial class MainWindow
{
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedge", "msedgewebview2", "chrome", "firefox", "opera", "brave", "browser", "vivaldi", "vlc", "mpv", "wmplayer", "potplayermini64",
        "mpc-hc64", "mpc-be64", "powerpnt", "acrord32", "explorer", "applicationframehost", "obs64", "taskmgr", "searchhost", "shellexperiencehost"
    };

    private bool _turbo, _turboManual, _heavy;
    private string _turboExe = "", _manualOffExe = "";
    private DateTime _turboLostAt = DateTime.MinValue;
    private readonly HashSet<string> _gameAsked = new(StringComparer.OrdinalIgnoreCase), _gameYes = new(StringComparer.OrdinalIgnoreCase);
    private GamePrompt? _gamePrompt;
    private int _cpuHighSec, _cpuLowSec;
    private long _cpuIdle, _cpuTotal;

    [StructLayout(LayoutKind.Sequential)] private struct FILETIME64 { public long V; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME64 idle, out FILETIME64 kernel, out FILETIME64 user);
    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);

    // раз в секунду из PerfTick
    private void GameCpuTick()
    {
        if (_cfg.GameMode == 0 || !GetSystemTimes(out var idle, out var kernel, out var user)) { _heavy = false; return; }
        long total = kernel.V + user.V, dTotal = total - _cpuTotal, dIdle = idle.V - _cpuIdle;
        _cpuTotal = total; _cpuIdle = idle.V;
        if (dTotal <= 0) return;
        double busy = 1 - (double)dIdle / dTotal;
        _cpuHighSec = busy >= 0.8 ? _cpuHighSec + 1 : 0;
        _cpuLowSec = busy < 0.5 ? _cpuLowSec + 1 : 0;
        if (_cpuHighSec >= 15) _heavy = true;
        else if (_cpuLowSec >= 30) _heavy = false;
    }

    private string GameExe(IntPtr fg)
    {
        if (fg == IntPtr.Zero || IsShellOrOwn(fg)) return "";
        try
        {
            GetWindowThreadProcessId(fg, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    // из FullscreenCheck, до решения прятать ли остров
    private void GameCheck(IntPtr fg, bool exclusive)
    {
        if (_turboManual) return;
        if (_cfg.GameMode == 0) { if (_turbo) SetTurbo(false, ""); return; }
        if (!_fsAny && !_heavy && !_turbo) return;   // обычная работа: процесс не трогаем

        string exe = GameExe(fg);
        bool candidate = exe.Length > 0 && !NotGames.Contains(exe) && (_fsAny || _heavy);
        if (_turbo)
        {
            if (candidate && exe.Equals(_turboExe, StringComparison.OrdinalIgnoreCase)) { _turboLostAt = DateTime.MinValue; return; }
            if (_turboLostAt == DateTime.MinValue) _turboLostAt = DateTime.UtcNow;
            if ((DateTime.UtcNow - _turboLostAt).TotalSeconds >= 5) SetTurbo(false, "");
            return;
        }
        if (!candidate || exe.Equals(_manualOffExe, StringComparison.OrdinalIgnoreCase)) return;
        if (_cfg.GameChoices.TryGetValue(exe, out bool remembered))
        {
            if (remembered) SetTurbo(true, exe);
            return;
        }
        if (_cfg.GameMode == 2 || _gameYes.Contains(exe)) { SetTurbo(true, exe); return; }
        if (!_gameAsked.Add(exe)) return;
        if (exclusive) { _gameYes.Add(exe); SetTurbo(true, exe); return; }
        AskGame(exe);
    }

    private void AskGame(string exe)
    {
        _gamePrompt?.Close();
        var r = MonitorDip;
        _gamePrompt = new GamePrompt(exe, r.Left + r.Width / 2, r.Top + 60, answer =>
        {
            _gamePrompt = null;
            if (answer == GameAnswer.None) return;
            if (answer == GameAnswer.Yes) _gameYes.Add(exe);
            else { _cfg.GameChoices[exe] = answer == GameAnswer.Always; _cfg.Save(); }
            if (answer != GameAnswer.No) { SetTurbo(true, exe); ScreensCheck(); }
        });
        _gamePrompt.Show();
    }

    // Ctrl+Alt+G
    private void ToggleGameMode()
    {
        bool on = !_turbo;
        string exe = GameExe(GetForegroundWindow());
        _turboManual = on;
        _manualOffExe = on ? "" : (_turboExe.Length > 0 ? _turboExe : exe);
        _gamePrompt?.Close();
        SetTurbo(on, exe);
        ScreensCheck();
        if (!_hidden) ShowToast("gauge", on ? Loc.T("Игровой режим включён", "Game mode on") : Loc.T("Игровой режим выключен", "Game mode off"), null, "key");
    }

    private void SetTurbo(bool on, string exe)
    {
        if (on == _turbo) return;
        _turbo = on;
        _turboExe = on ? exe : "";
        _turboLostAt = DateTime.MinValue;
        if (!on) _turboManual = false;
        try { using var me = Process.GetCurrentProcess(); me.PriorityClass = on ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal; }
        catch (Exception ex) { App.LogOnce("GameMode.Priority", ex); }
        PerfTick();   // экономный режим
        if (!NeedCapture()) StopCapture();
        else if (_playing) StartCapture();
        if (on) _ = Task.Run(TrimMemory);
    }

    private static void TrimMemory()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            using var me = Process.GetCurrentProcess();
            SetProcessWorkingSetSize(me.Handle, -1, -1);
        }
        catch (Exception ex) { App.LogOnce("GameMode.Trim", ex); }
    }
}
