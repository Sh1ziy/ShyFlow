using System.Windows.Controls;
using System.Windows.Media;

namespace Island;

// «Цвет из обложки везде»: полоски прогресса, эквалайзер, переключатели в настройках берут цвет обложки.
// Выключено: всё белое, как раньше.
// Шаг 79: цвет раздаётся замороженными кистями через LiveBrush (Ui\LiveBrush.cs), изменяемая общая кисть роняла ShyFlow.
// 3.9.4 (Настройки, «Интерфейс»): цвет интерфейса из обложки, акцент Windows или свой; у эквалайзера может быть свой цвет.
public partial class MainWindow
{
    private readonly LiveBrush _ui = new(Colors.White);
    private readonly LiveBrush _uiMini = new(Color.FromArgb(0x99, 255, 255, 255));
    private readonly LiveBrush _eqUi = new(Colors.White);   // полоски эквалайзера (Equalizers.cs, Appearance.cs, Duo.cs)

    // нет обложки (ничего не играет, у трека нет картинки): акцент Windows вместо белого (Chrome.cs)
    internal Color UiColor => !_cfg.AccentUi ? Colors.White : _cfg.AccentSource switch
    {
        1 => SysAccent,
        2 => ParseHex(_cfg.AccentHex, SysAccent),
        _ => _lastCover != null ? _accent : SysAccent
    };

    private Color EqColorNow => _cfg.EqColor switch
    {
        1 => ParseHex(_cfg.EqHex, Colors.White),
        2 => Colors.White,
        _ => UiColor
    };

    internal static Color ParseHex(string? hex, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            var c = (Color)ColorConverter.ConvertFromString(hex.Trim().StartsWith('#') ? hex.Trim() : "#" + hex.Trim());
            return Color.FromRgb(c.R, c.G, c.B);
        }
        catch { return fallback; }
    }

    private void BindAccentUi()
    {
        _ui.Use(SeekFill, Border.BackgroundProperty);
        _ui.Use(SeekThumb, System.Windows.Shapes.Shape.FillProperty);
        _ui.Use(SeekWave, WaveBar.FillProperty);
        _ui.Use(OsdFill, Border.BackgroundProperty);
        _uiMini.Use(MiniProg, Border.BackgroundProperty);
    }

    private void ApplyAccentUi()
    {
        var c = UiColor;
        _ui.To(c, 500);
        _uiMini.To(Color.FromArgb(0x99, c.R, c.G, c.B), 500);
        _eqUi.To(EqColorNow, 500);
        _settingsWin?.SetAccent(c);
        ApplyTheme();   // контур, свечение и «Аврора» красятся в цвет обложки
    }
}
