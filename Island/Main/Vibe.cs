using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island;

// Звуки интерфейса и упругость острова (шаг 49).
// Звуки синтезируются (Services\Sfx.cs, без файлов): раскрытие и закрытие, уведомление, свайп, смахивание, пузырь, нажатие.
// Шаг 63: вместо трёх ступеней «Упругости» ползунок «Желеобразность» (0..1, затухание пружин) и стиль морфинга:
//   жидкий — как было: пружины плюс жидкие растяжения (Fluid.cs);
//   стекло — чётко и плавно, пружины с критическим затуханием (без перехлёста), без жидкости и «желе»;
//   упругий — заметный отскок, остров сплющивается и отскакивает при каждой смене вида, без жидкости.
public partial class MainWindow
{
    private static bool IsPanel(Mode m) => m is Mode.Full or Mode.Pocket or Mode.Actions or Mode.Devices or Mode.Timer or Mode.Mixer or Mode.Center or Mode.Translate;

    private double JellyK => Math.Clamp(_cfg.Jelly, 0, 1);

    private void ModeSound(Mode prev, Mode m)
    {
        if (_chatWin != null) _chatWin.Opacity = IsPanel(m) ? 0 : 1;   // раскрытый остров не перекрываем чатом стрима (Streamer.cs)
        if (!_ready || prev == m) return;
        if (_cfg.MorphStyle == 2) Wobble();   // упругий стиль: «желе» на каждую смену вида
        if (IsPanel(m) && !IsPanel(prev)) Sfx.Ui("open");
        else if (IsPanel(prev) && m == Mode.Mini) Sfx.Ui("close");
    }

    private void ApplyVibeSettings()
    {
        Sfx.UiLevel = !_cfg.UiSounds ? 0 : _cfg.UiSoundVol switch { 0 => 0.12f, 2 => 0.4f, _ => 0.24f };
        Sfx.Style = _cfg.UiSoundStyle;
        ApplySpringTuning();
    }

    // Пружины размера. Раньше их выставляли и здесь, и в ApplyFluidSettings (Fluid.cs): смена экономного режима
    // (SetEco → ApplyFluidSettings) молча сбрасывала «Желеобразность» и стиль морфинга к старым 170/13.
    // Теперь источник один. Параметры заданы как у Apple (SwiftUI spring: response + dampingFraction):
    //   K = (2π / response)², D = 2·√K·ζ.
    private double _springK = 190, _springD = 2 * Math.Sqrt(190) * 0.7;

    private void ApplySpringTuning()
    {
        double j = JellyK, response, zeta;
        switch (_cfg.MorphStyle)
        {
            case 1:   // стекло: почти без перелёта, «желе» только чуть смягчает
                response = 0.38; zeta = 1 - 0.2 * j;
                break;
            case 2:   // упругий: заметный отскок
                response = 0.36; zeta = 0.42 - 0.2 * j;
                break;
            default:  // жидкий (по умолчанию): как Dynamic Island — быстрый отклик и один мягкий перелёт (ζ ≈ 0.66 при 0.45)
                response = 0.38 - 0.03 * j; zeta = 0.86 - 0.3 * j;
                break;
        }
        if (_eco) zeta = Math.Max(zeta, 0.8);   // экономный режим: меньше кадров на «докачивание» пружины
        double w0 = 2 * Math.PI / response;
        _springK = w0 * w0;
        _springD = 2 * w0 * zeta;
        foreach (var s in new[] { _w, _h, _r }) { s.K = _springK; s.D = _springD; }
    }

    // Apple: раскрытие с лёгким перелётом, сворачивание собранное — без отскока, который выглядит как «дребезг» у края экрана.
    // Зовётся из Go() перед сменой цели.
    private void TuneSpringFor(bool grow)
    {
        double d = grow || _cfg.MorphStyle == 2 ? _springD : Math.Max(_springD, 2 * Math.Sqrt(_springK) * 0.86);
        foreach (var s in new[] { _w, _h, _r }) { s.K = _springK; s.D = d; }
    }

    // «желе»: остров чуть сплющивается и отскакивает обратно
    private void Wobble()
    {
        double j = JellyK;
        if (_cfg.MorphStyle == 1 || j < 0.05 || _eco || _hidden) return;
        var s = Get(Pill).S;
        double amp = (0.015 + 0.07 * j) * (_cfg.MorphStyle == 2 ? 1.5 : 1);
        var ease = new SpringEase(6.5 - 2.5 * j, 13);
        s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1 + amp, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease });
        s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1 - amp * 1.4, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease });
    }
}
