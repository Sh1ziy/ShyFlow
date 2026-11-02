using System.Windows;
using System.Windows.Threading;

namespace Island;

// Автообновление в острове (шаг 55, сама проверка и скачивание — Services\Updater.cs).
// Через полминуты после запуска и потом раз в час ShyFlow смотрит, нет ли новой версии на GitHub (шаг 84).
// Есть: уведомление «Доступна версия X», клик — скачать (прогресс в плашке) и поставить; ShyFlow перезапустится сам.
// «Не сейчас» в настройках запоминает версию, и про неё больше не напоминаем. Проверить вручную: «Система», «О программе».
public partial class MainWindow
{
    internal static MainWindow? Instance { get; private set; }
    private readonly DispatcherTimer _updTimer = new() { Interval = TimeSpan.FromHours(6) };
    private UpdateInfo? _update;
    private bool _updating;

    // ---------- «Что нового» после обновления (шаг 57, Ui\WhatsNewWindow.cs) ----------
    private WelcomeWindow? _welcome;

    private void MaybeWhatsNew()
    {
        var cur = Updater.Current;
        string curS = cur.ToString(3);
        if (_cfg.IsFresh) { _cfg.LastSeenVersion = curS; _cfg.Save(); return; }   // первый запуск: и так всё новое
        // до 3.2 версию не запоминали: такие люди пришли максимум с 3.0
        var seen = Version.TryParse(_cfg.LastSeenVersion, out var v) ? v : new Version(3, 0, 0);
        // шаг 79: в ту же версию дописали новинки (тестовые сборки) — показать снова, только новые для этой версии
        int curCount = WhatsNew.All.FirstOrDefault(r => r.Ver == cur)?.Items.Length ?? 0;
        bool more = seen == cur && curCount > _cfg.LastSeenCount;
        if (seen >= cur && !more) return;
        var from = more ? WhatsNew.All.Where(r => r.Ver < cur).Select(r => r.Ver).DefaultIfEmpty(new Version(3, 0, 0)).Max()! : seen;
        _cfg.LastSeenVersion = curS;
        _cfg.LastSeenCount = curCount;
        _cfg.Save();
        if (!_cfg.ShowWhatsNew || WhatsNew.Since(from).Count == 0) return;
        void Open() => WhatsNewWindow.ShowSince(from, _cfg.UiSounds);
        // приветствие открыто: «Что нового» после него
        if (_welcome is { IsLoaded: true }) { _welcome.Closed += (_, _) => Open(); return; }
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        t.Tick += (_, _) => { t.Stop(); Open(); };
        t.Start();
    }

    private void WhatsNew_Click(object s, RoutedEventArgs e) => WhatsNewWindow.ShowSince(new Version(3, 0, 0), _cfg.UiSounds);

    // Шаг 79: прошлый запуск закончился вылетом — через несколько секунд после старта уведомление, клик — что случилось.
    // В первый раз только запоминаем время: старые вылеты (до этой версии) не показываем.
    private void CheckCrashLater()
    {
        var since = _cfg.LastCrashCheck;
        _cfg.LastCrashCheck = DateTime.Now;
        _cfg.Save();
        if (since == default) return;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            Task.Run(() => CrashReport.FindSince(since)).ContinueWith(r => Dispatcher.BeginInvoke(() =>
            {
                if (!r.IsCompletedSuccessfully || r.Result is not { } info) return;
                ShowNotif(Loc.T("ShyFlow закрылся в прошлый раз", "ShyFlow closed last time"), info.Human, Loc.T("Клик: что случилось", "Click: what happened"));
                _notifClick = () => CrashWindow.ShowFor(info);
            }));
        };
        t.Start();
    }

    // шаг 84: раньше GitHub спрашивали раз в 6 часов и показывали уведомление один раз — если остров был спрятан,
    // шла игра или фокус, оно терялось до следующей проверки. Теперь таймер тикает раз в 10 минут: GitHub — раз в час
    // (и сразу после выхода из сна), а найденное обновление напоминает о себе раз в 6 часов, когда остров видно.
    private static readonly TimeSpan UpdNetEvery = TimeSpan.FromHours(1), UpdRemindEvery = TimeSpan.FromHours(6);
    private DateTime _updNetAt, _updShownAt;
    private string _updShownTag = "";
    private bool _updChecking;

    private void StartUpdates()
    {
        Instance = this;
        CheckCrashLater();
        if (AppInfo.IsPackaged) return;   // шаг 75: версию из Microsoft Store обновляет сам Store
        _updTimer.Interval = TimeSpan.FromMinutes(10);
        _updTimer.Tick += (_, _) => _ = AutoCheck();
        _updTimer.Start();
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume) Dispatcher.BeginInvoke(() => _updNetAt = default);   // проснулись — спросить на ближайшем тике
        };
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        first.Tick += (_, _) => { first.Stop(); _ = AutoCheck(); };
        first.Start();
    }

    private async Task AutoCheck()
    {
        if (!_cfg.AutoUpdate || _updating || _updChecking) return;
        if (DateTime.UtcNow - _updNetAt >= UpdNetEvery)
        {
            _updChecking = true;
            try
            {
                var found = await Updater.Check();
                _updNetAt = DateTime.UtcNow;
                if (found != null) _update = found;
            }
            finally { _updChecking = false; }
        }
        if (_update is not { } u || u.Ver <= Updater.Current) return;
        if (_cfg.SkipVersion == u.Tag) return;   // эту версию просили не предлагать
        if (_updShownTag == u.Tag && DateTime.UtcNow - _updShownAt < UpdRemindEvery) return;
        if (_hidden || _fsMini || FocusQuiet || _player != null) return;   // не видно — напомнить на следующем тике
        _updShownTag = u.Tag;
        _updShownAt = DateTime.UtcNow;
        ShowNotif("ShyFlow", Loc.T("Доступна версия ") + u.Ver, Loc.T("Клик: обновить. Настройки и всё твоё останутся"));
        _notifClick = () => { ApplyMode(Rest()); _ = RunUpdate(u); };
    }

    // кнопка «Проверить» в настройках
    internal async Task CheckUpdatesNow(Window owner)
    {
        if (_updating) { MacDialog.Info(owner, Loc.T("Обновление уже скачивается")); return; }
        var u = await Updater.Check();
        if (u == null) { MacDialog.Info(owner, Loc.T("У тебя последняя версия: ") + AppInfo.CurrentVersion); return; }
        _update = u;
        string notes = Updater.PlainNotes(u.Notes);
        bool go = MacDialog.Ask(owner, Loc.T("Доступна версия ") + u.Ver + Loc.T(" (у тебя ") + AppInfo.CurrentVersion + ")" +
                                        (notes.Length > 0 ? "\n\n" + notes : "") + "\n\n" + Loc.T("ShyFlow закроется на пару секунд и откроется уже новым."), Loc.T("Обновить"));
        if (go) { _cfg.SkipVersion = ""; _cfg.Save(); _ = RunUpdate(u); }
        else { _cfg.SkipVersion = u.Tag; _cfg.Save(); }
    }

    private async Task RunUpdate(UpdateInfo u)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            int lastPct = -1;
            var prog = new Progress<double>(p =>
            {
                int pct = (int)(p * 100);
                if (pct == lastPct) return;
                lastPct = pct;
                _dlFrac = p;   // активность «скачивание» в стеке пузыря (Activities.cs)
                UpdateBubble();
                ShowToast("download", null, p, "dl");
                OsdValue.Text = pct + "%";
            });
            ShowToast("download", Loc.T("Скачиваю обновление ") + u.Ver, null, "dl");
            string? path = await Updater.Download(u, prog);
            if (path == null)
            {
                ShowToast("triangle-alert", Loc.T("Обновление не скачалось, попробую позже"), null, "key");
                return;
            }
            ShowToast("", Loc.T("Устанавливаю ") + u.Ver + "…", null, "dl");
            await Task.Delay(700);
            if (!Updater.Install(path)) { ShowToast("triangle-alert", Loc.T("Не получилось запустить установку"), null, "key"); return; }
            // установщик сам закроет ShyFlow; выходим сами, чтобы файл точно освободился, и сохраняем всё нужное
            ListenStats.Save();
            await Task.Delay(300);
            Application.Current.Shutdown();
        }
        finally { _updating = false; _dlFrac = -1; UpdateBubble(); }
    }
}
