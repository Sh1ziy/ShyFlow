using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;
using Windows.Media.Control;

namespace Island;

// Связь острова с полноэкранным плеером: плеер сам ничего не знает про остров,
// он только читает текущий трек, обложку, слова и управляет воспроизведением.
public partial class MainWindow
{
    private PlayerWindow? _player;

    internal string CurTitle => _curTitle;
    internal string CurArtist => _curArtist;
    internal BitmapSource? CurCover => _lastCover;
    internal List<(double t, string s)>? CurLyrics => _lyr;
    internal List<WordTimes?>? CurWords => _lyrWords;   // точное время слов (того же размера, что CurLyrics) или null
    internal bool IsPlaying => _playing;
    internal double LyricOffsetNow => _lyrOffset;
    // в экономном режиме тяжёлые эффекты выключены, даже если включены в настройках (см. Performance.cs)
    internal bool LyricBlur => _cfg.LyricsBlur && !_eco;
    internal bool LyricGlow => _cfg.LyricsGlow && !_eco;
    internal System.Windows.Media.Color AccentNow => _accent;
    internal GlobalSystemMediaTransportControlsSession? CurSession => _session;
    internal MMDevice? VolumeDevice => _dev;
    internal double[] EqLevels => _bp;
    internal bool PlayerViz => _cfg.PlayerViz && !_eco;
    internal bool PlayerGlow => _cfg.PlayerGlow && !_eco;
    internal bool PlayerLiveBg => _cfg.PlayerLiveBg;   // шаг 77: живой фон (в экономном режиме не двигается)
    internal bool LetterAnim => _cfg.LetterAnim && !_eco;
    internal bool CoverFlip => _cfg.CoverFlip;
    internal bool Cover3D => _cfg.Cover3D && !_eco;
    internal bool Eco => _eco;
    internal bool SeekClick => _cfg.SeekClick;
    internal bool WaveSeek => SeekStyleNow is 1 or 3 or 4;   // в плеере на весь экран — волна для всех «неровных» стилей
    internal AppSettings Cfg => _cfg;   // шаг 80: караоке помнит, включён ли микрофон

    // шаг 80: файл в карман, не открывая его (запись караоке, пока открыт плеер)
    internal void PocketAddFile(string path)
    {
        _files.Remove(path);
        _files.Insert(0, path);
        SaveFiles();
        if (_pocketOpen && _tab == 0) RenderPocket();
    }

    // направление последнего переключения (для анимации смены трека)
    internal int SkipDirNow => (DateTime.UtcNow - _skipAt).TotalSeconds < 4 ? _skipDir : 1;
    internal void NoteSkip(int dir) { _skipDir = dir; _skipAt = DateTime.UtcNow; }

    private void Expand_Click(object s, RoutedEventArgs e)
    {
        if (s is FrameworkElement b) Pop(b, 0.7, 450);
        OpenPlayer();
    }

    // клик по обложке в развёрнутом острове открывает плеер на весь экран (как кнопка справа)
    private void CoverBig_Click(object s, MouseButtonEventArgs e)
    {
        e.Handled = true;   // остров при этом не сворачивается
        Pop(CoverBig, 0.85, 450);
        OpenPlayer();
    }

    internal void OpenPlayer()
    {
        if (_player != null) { _player.Activate(); return; }
        // шаг 80: плеер вылетает из острова (Ui\IslandMorph.cs); в экономном режиме и при спрятанном острове — как раньше
        var from = _eco ? null : IslandRectOnMonitor();
        _playerFrom = from;
        var p = new PlayerWindow(this) { FromIsland = from != null };
        _player = p;
        if (from is Rect r)
        {
            IslandMorph? m = null;
            m = IslandMorph.Open(MonitorDip, r, PlayerFill(), () =>
            {
                if (!ReferenceEquals(_player, p)) { m?.Release(); return; }
                p.ContentRendered += (_, _) => m?.Release();   // капля растворяется, когда плеер под ней уже нарисован
                p.Show();
            });
        }
        else p.Show();
        HideForPlayer(true);
        if (_playing) StartCapture(); // звук для визуализатора нужен, пока открыт плеер
    }

    // ---------- шаг 80: плеер из острова и обратно ----------
    private Rect? _playerFrom;   // где был остров, когда открыли плеер: туда плеер и залетает

    // остров в DIP относительно своего монитора; null — его не видно (спрятан, за краем)
    private Rect? IslandRectOnMonitor()
    {
        if (_hidden || !IsVisible || Pill.ActualWidth < 4) return null;
        try
        {
            var tl = Pill.TranslatePoint(new Point(0, 0), this);
            var mon = MonitorDip;
            var r = new Rect(Left + tl.X - mon.Left, Top + tl.Y - mon.Top, Pill.ActualWidth, Pill.ActualHeight);
            return r.Bottom < 0 || r.Right < 0 || r.Left > mon.Width ? null : r;
        }
        catch { return null; }
    }

    // капля того же цвета, что фон плеера: размытая обложка (или тёмный градиент, если обложки нет)
    private Brush PlayerFill()
    {
        var bg = _lastCover != null ? PlayerWindow.MakeBg(_lastCover) : null;
        Brush b = bg != null
            ? new ImageBrush(bg) { Stretch = Stretch.Fill }
            : new LinearGradientBrush(Color.FromRgb(0x2A, 0x2A, 0x32), Color.FromRgb(0x0A, 0x0A, 0x0C), 90);
        b.Freeze();
        return b;
    }

    // плеер закрывается: капля закрывает его собой и сжимается в остров; false — без перехода (закрыть как раньше)
    internal bool MorphBack(Action closePlayer)
    {
        if (_eco || _playerFrom is not Rect r) return false;
        IslandMorph.Back(MonitorDip, ReturnRect(r), PlayerFill(), closePlayer);
        return true;
    }

    // шаг 81: куда залетать — остров, каким он вернётся (пока открыт плеер, раскрытый остров сворачивается в мини),
    // а не каким был при открытии: иначе капля застывала большим чёрным пятном вокруг маленького острова
    private Rect ReturnRect(Rect from)
    {
        var m = Rest();
        (double w, double h) = m switch
        {
            Mode.Mini when Duo && !_fsMini => (DuoW, Math.Max(DuoW, _h.T)),   // шаг 84: вертикальная капсула (Duo.cs)
            Mode.Mini => _fsMini ? (StripW, StripH) : (_miniW, 38.0),
            Mode.Full => (_fullW, FullH()),
            _ => (from.Width, from.Height),
        };
        if (Duo) return DuoReturn(from, w, h);   // шаг 84: остров в углу растёт от правого края (Duo.cs)
        return new Rect(from.X + from.Width / 2 - w / 2, from.Y, w, h);
    }

    internal void OnPlayerClosed()
    {
        _player = null;
        HideForPlayer(false);
        if (!NeedCapture()) StopCapture();
    }

    // Пока открыт плеер на весь экран, остров уезжает вверх (как Ctrl+Alt+I) и возвращается, когда плеер закрыли.
    // Если остров уже был спрятан руками, он так и остаётся спрятанным. Ctrl+Alt+I при открытом плеере возвращает его.
    private bool _hiddenForPlayer;

    private void HideForPlayer(bool hide)
    {
        if (hide)
        {
            if (_hidden) return;
            _hiddenForPlayer = true;
            ToggleHidden();
        }
        else
        {
            if (_hiddenForPlayer && _hidden) ToggleHidden();
            _hiddenForPlayer = false;
        }
    }

    // сообщение для человека: плашкой в острове, а если остров спрятан под плеером, то подсказкой в самом плеере
    private void Tell(string icon, string text, string kind = "key")
    {
        if (_player != null && _hidden) _player.ShowHint(icon, Loc.T(text));
        else ShowToast(icon, text, null, kind);
    }

    // меню острова (правый клик): «Коллекция карточек»
    private void Collection_Click(object sender, RoutedEventArgs e) => CollectionWindow.ShowCollection();

    // стикер из острова (гифка, картинка, видео) для обратной стороны обложки в плеере
    internal IReadOnlyList<BitmapSource> StickerFrames => _gifFrames;
    internal IReadOnlyList<int> StickerDelays => _gifDelays;
}
