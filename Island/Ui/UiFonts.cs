using System.Windows.Media;

namespace Island;

// Шрифты всей программы (3.9.4).
// По умолчанию Inter (rsms/inter, лицензия SIL OFL — Fonts\Inter-OFL.txt): ближе всего к SF Pro, а SF Pro по лицензии Apple
// нельзя класть в программу для Windows. Для крупного текста — Inter Display (как SF Display: плотнее и с меньшим трекингом).
// Шрифты лежат внутри ShyFlow.exe (Resource), ставить их в систему не нужно.
// Выбор в настройках («Интерфейс» → «Шрифт»): Inter, Segoe UI (как было) или свой файл. Он действует на остров сразу,
// а на остальные окна (настройки, плеер, блокнот...) — с их следующего открытия; часть окон берёт его при запуске ShyFlow.
internal static class UiFonts
{
    private static readonly Uri Pack = new("pack://application:,,,/");

    public static readonly FontFamily Inter = new(Pack, "./Fonts/#Inter, Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily InterDisplay = new(Pack, "./Fonts/#Inter Display, Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Segoe = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily SegoeDisplay = new("Segoe UI Variable Display, Segoe UI");

    // 3.9.5: SF Pro, если человек сам установил его в Windows (developer.apple.com/fonts). Внутрь ShyFlow его класть нельзя
    // по лицензии Apple, поэтому берём только из системы; нет в системе — запасной Inter
    public static readonly FontFamily SfText = new("SF Pro Text, SF Pro, SF Pro Display, Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily SfDisplay = new("SF Pro Display, SF Pro, SF Pro Text, Segoe UI Variable Display, Segoe UI");
    public const string SfUrl = "https://developer.apple.com/fonts/";

    // значения AppSettings.FontFile: «segoe» — шрифт, который был до Inter; «sf» — SF Pro из системы;
    // пусто — Inter, иначе путь к своему файлу
    public const string SegoeKey = "segoe", SfKey = "sf";

    public static bool SfInstalled
    {
        get
        {
            try
            {
                return System.Windows.Media.Fonts.SystemFontFamilies.Any(f =>
                    f.Source.StartsWith("SF Pro", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }
    }

    public static FontFamily Text { get; private set; } = Inter;
    public static FontFamily Display { get; private set; } = InterDisplay;

    public static void Init(string? fontFile)
    {
        if (fontFile == SegoeKey) { Text = Segoe; Display = SegoeDisplay; return; }
        if (fontFile == SfKey) { if (SfInstalled) { Text = SfText; Display = SfDisplay; } else { Text = Inter; Display = InterDisplay; } return; }
        var custom = string.IsNullOrEmpty(fontFile) ? null : MainWindow.LoadFontFile(fontFile);
        if (custom != null) { Text = Display = custom; return; }
        Text = Inter; Display = InterDisplay;
    }
}
