using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Island;

// Шаг 77: долгое нажатие на свёрнутый остров, как на iPhone.
// Зажал на полсекунды и не двигал мышь — остров чуть подрастает и показывает быстрые кнопки: тише, назад, пауза, вперёд,
// громче. Раскрывать остров не нужно. Кнопки сами прячутся через 3,5 с без нажатий или через секунду, как убрал мышь.
// Отпускание после долгого нажатия не считается кликом (остров не раскрывается), а движение — свайпом.
public partial class MainWindow
{
    private readonly DispatcherTimer _lpTimer = new() { Interval = TimeSpan.FromMilliseconds(480) };
    private readonly DispatcherTimer _quickClose = new() { Interval = TimeSpan.FromMilliseconds(3500) };
    private Point _lpStart;
    private bool _lpFired;

    private void InitLongPress()
    {
        _lpTimer.Tick += (_, _) =>
        {
            _lpTimer.Stop();
            if (!MiniLike || Mouse.LeftButton != MouseButtonState.Pressed) return;
            _lpFired = true;
            _swiped = true;      // дальше мышь уже не свайп (Input.cs)
            ReleaseRubber();
            OpenQuick();
        };
        _quickClose.Tick += (_, _) =>
        {
            _quickClose.Stop();
            if (_mode != Mode.Quick) return;
            if (Pill.IsMouseOver) { _quickClose.Start(); return; }   // мышь ещё над кнопками
            ApplyMode(Rest());
        };
        Pill.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _lpFired = false;
            _lpTimer.Stop();
            if (!MiniLike || _fsMini) return;   // шаг 79: и поверх плашки трека (Input.cs, MiniLike)
            _lpStart = e.GetPosition(this);
            _lpTimer.Start();
        };
        Pill.PreviewMouseMove += (_, e) =>
        {
            if (_lpTimer.IsEnabled && (e.GetPosition(this) - _lpStart).Length > 6) _lpTimer.Stop();
        };
        Pill.PreviewMouseLeftButtonUp += (_, e) =>
        {
            _lpTimer.Stop();
            if (_lpFired) { _lpFired = false; e.Handled = true; }   // не раскрывать остров
        };
        Pill.MouseLeave += (_, _) =>
        {
            if (_mode != Mode.Quick) return;
            _quickClose.Stop();
            _quickClose.Interval = TimeSpan.FromMilliseconds(1100);
            _quickClose.Start();
        };
    }

    private void OpenQuick()
    {
        QuickPlayIcon.Text = _playing ? "pause" : "play";
        _toastTimer.Stop();   // открыли поверх плашки трека: её таймер иначе закрыл бы кнопки
        ApplyMode(Mode.Quick);
        try { Sfx.Ui("open"); } catch { /* нет звука */ }
        QuickTouch();
    }

    // любое нажатие продлевает показ кнопок
    private void QuickTouch()
    {
        _quickClose.Stop();
        _quickClose.Interval = TimeSpan.FromMilliseconds(3500);
        _quickClose.Start();
    }

    private async void QuickPlay_Click(object s, RoutedEventArgs e)
    {
        QuickTouch();
        if (s is FrameworkElement b) Press(b, 0);
        QuickPlayIcon.Text = _playing ? "play" : "pause";   // сразу, не дожидаясь плеера
        try { if (_session != null) await _session.TryTogglePlayPauseAsync(); }
        catch (Exception ex) { App.LogOnce("Quick.Play", ex); }
    }

    private void QuickPrev_Click(object s, RoutedEventArgs e) { QuickTouch(); Prev_Click(s, e); }
    private void QuickNext_Click(object s, RoutedEventArgs e) { QuickTouch(); Next_Click(s, e); }
    private void QuickVolDown_Click(object s, RoutedEventArgs e) => QuickVol(s, -0.06);
    private void QuickVolUp_Click(object s, RoutedEventArgs e) => QuickVol(s, 0.06);

    private void QuickVol(object s, double d)
    {
        QuickTouch();
        if (s is FrameworkElement b) Press(b, 0);
        try
        {
            var v = _dev?.AudioEndpointVolume;
            if (v == null) return;
            if (d > 0 && v.Mute) v.Mute = false;
            v.MasterVolumeLevelScalar = (float)Math.Clamp(v.MasterVolumeLevelScalar + d, 0, 1);
        }
        catch (Exception ex) { App.LogOnce("Quick.Vol", ex); }
    }
}
