using System.Windows;
using System.Windows.Threading;

namespace Island;

// Учёт прослушиваний для статистики (шаг 49): раз в 5 секунд, пока музыка играет, трек получает 5 секунд.
// После 30 секунд трека засчитывается «прослушивание» и сохраняется маленькая обложка. Файл пишется раз в минуту и при выходе.
public partial class MainWindow
{
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private string _statKey = "";
    private int _statRun, _statTicks;
    private bool _statCounted;

    private void StartStats()
    {
        _statsTimer.Tick += (_, _) => StatsTick();
        if (!_cfg.DropQrReset79) { _cfg.DropQrReset79 = true; _cfg.DropQr = false; _cfg.Save(); }   // шаг 79 (DropFx.cs)
        if (!_cfg.LinkPreviewsReset397) { _cfg.LinkPreviewsReset397 = true; _cfg.LinkPreviews = false; _cfg.Save(); }   // 3.9.7 (SmartClip.cs)
        if (!_cfg.StatsCleaned79 && _cfg.StatsMusicOnly)
        {
            try { ListenStats.CleanVideos(); } catch (Exception ex) { App.LogOnce("Stats.Clean", ex); }
            _cfg.StatsCleaned79 = true;
            _cfg.Save();
        }
        _statsTimer.Start();
        Closed += (_, _) => ListenStats.Save();
    }

    private void StatsTick()
    {
        if (++_statTicks % 12 == 0) ListenStats.Save();
        if (!_cfg.ListenStats || !_playing || _session == null || _curTitle.Length == 0) return;
        if (_adNow) return;   // реклама в Spotify — не трек (Ads.cs)
        if (_cfg.StatsMusicOnly && !IsMusic(_curSource, _curAlbum, _curTitle)) return;
        try
        {
            string key = _curArtist + "|" + _curTitle;
            if (key != _statKey) { _statKey = key; _statRun = 0; _statCounted = false; }
            ListenStats.Add(_curTitle, _curArtist, 5);
            _statRun += 5;
            if (!_statCounted && _statRun >= 30)
            {
                _statCounted = true;
                if (_lastCover != null) ListenStats.SaveCover(_curTitle, _curArtist, _lastCover);
                ListenStats.CountPlay(_curTitle, _curArtist);
                CheckAchievements();
            }
        }
        catch (Exception ex) { App.LogOnce("Stats.Tick", ex); }
    }

    // Шаг 79: «Только музыка». Раньше считалось всё, что играет, и в итоги года попадали ролики из TikTok и YouTube в браузере.
    // Музыкальное приложение (Spotify, Яндекс Музыка, Apple Music, AIMP...) — всегда музыка. Браузер и прочее — только если у трека есть
    // альбом: YouTube Music, SoundCloud и музыкальные сайты его передают, обычные ролики нет.
    private string _curAlbum = "", _curSource = "";
    private static readonly string[] MusicApps =
        { "spotify", "music", "музык", "deezer", "tidal", "aimp", "foobar", "winamp", "musicbee", "soundcloud", "yandex", "zune", "itunes", "qobuz", "audacious", "dopamine" };
    private static readonly string[] Browsers = { "chrome", "msedge", "edge", "firefox", "opera", "brave", "vivaldi", "browser", "arc", "thorium" };

    internal static bool IsMusic(string source, string album, string title)
    {
        string s = source.ToLowerInvariant();
        if (LooksLikeVideo(title)) return false;
        bool browser = Browsers.Any(b => s.Contains(b));
        // у Яндекс Браузера в имени есть и «yandex», и «browser»: это браузер
        if (!browser && MusicApps.Any(m => s.Contains(m))) return true;
        return album.Trim().Length > 0;
    }

    // заголовок вкладки, а не трек: «(99+) TikTok - Make Your Day», «… - YouTube», поиск TikTok, ролики с хэштегами
    internal static bool LooksLikeVideo(string title) =>
        System.Text.RegularExpressions.Regex.IsMatch(title, @"^\(\d+\+?\)|\bTikTok\b|- YouTube$|^Поиск по теме|(^|\s)#\w", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private void Stats_Click(object s, RoutedEventArgs e) => OpenStats();
    internal void OpenStats() => StatsWindow.ShowStats();
}
