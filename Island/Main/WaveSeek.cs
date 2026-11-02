using System.Windows;

namespace Island;

// Полоска перемотки в развёрнутом острове.
// 3.9.4: стили (Настройки, «Интерфейс» → «Полоска перемотки»): линия, волна (как у голосовых в Telegram), капсула (толстая,
// без ползунка, как в iOS), точки и змейка (волнистая линия, как в Android 13). Раньше был только переключатель «Прогресс волной».
public partial class MainWindow
{
    internal static readonly string[] SeekStyleNames = { "Линия", "Волна", "Капсула", "Точки", "Змейка" };

    // -1 — настройку ещё не трогали: как было по «Прогрессу волной»
    private int SeekStyleNow => _cfg.SeekStyle is >= 0 and <= 4 ? _cfg.SeekStyle : _cfg.WaveSeek ? 1 : 0;

    private void ApplyWaveSeek()
    {
        int s = SeekStyleNow;
        bool wave = s is 1 or 3 or 4;
        SeekWave.Visibility = wave ? Visibility.Visible : Visibility.Collapsed;
        SeekWave.Mode = s switch { 3 => 1, 4 => 2, _ => 0 };
        var o = wave ? Visibility.Collapsed : Visibility.Visible;
        SeekTrack.Visibility = SeekFill.Visibility = o;
        SeekThumb.Visibility = s == 0 ? Visibility.Visible : Visibility.Collapsed;
        double th = s == 2 ? 8 : 4;   // капсула: толще и без ползунка
        SeekTrack.Height = SeekFill.Height = th;
        SeekTrack.CornerRadius = SeekFill.CornerRadius = new CornerRadius(th / 2);
    }

    // змейка течёт, пока трек играет (зовётся из UpdateProgress, только в раскрытом острове)
    private void SeekPhaseStep()
    {
        if (SeekWave.Mode == 2 && _playing && !_eco) SeekWave.Phase = (SeekWave.Phase + 0.35) % (Math.PI * 2);
    }
}
