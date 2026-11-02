using System.Windows.Media;

namespace Island;

// Шаг 95: шкала шрифтов как у Apple (iOS/macOS, тёмная тема). Раньше размеры брались «на глаз»: в коде ~30 разных значений,
// от 7 до 150, с полушагами 10.5, 11.5, 12.5, 13.5. Новые места берут размер отсюда, старые переводятся по мере правок.
//
//   LargeTitle 34 Bold      заголовки крупных экранов
//   Title2     22 Semibold  заголовки окон
//   Title3     20 Semibold  заголовки поменьше
//   Headline   17 Semibold  название трека в развёрнутом острове
//   Subhead    15 Regular   исполнитель
//   Footnote   13           подписи, время
//   Caption    12 / 11      компактный остров, бейджи
//
// Face(size): от 20 пунктов текст пишется Inter Display (он плотнее и с меньшим трекингом, как SF Display),
// ниже — обычным Inter. Выбранный в настройках шрифт (Segoe, SF Pro, свой файл) подставляется сам через UiFonts.
internal static class ShyType
{
    public const double LargeTitle = 34, Title2 = 22, Title3 = 20, Headline = 17, Subhead = 15, Footnote = 13, Caption = 12, Caption2 = 11;

    public const double DisplayFrom = 20;

    public static FontFamily Face(double size) => size >= DisplayFrom ? UiFonts.Display : UiFonts.Text;
}
