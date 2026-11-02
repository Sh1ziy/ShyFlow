using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island;

// Шаг 80: текст с «перекатом» цифр, как в Live Activities на iPhone. Меняется только тот знак, что изменился
// (4:59 -> 4:58: уезжает одна последняя цифра): старый уходит вверх и гаснет, новый выезжает снизу с пружиной.
// Длина текста поменялась (9:59 -> 10:00) или это не цифры — текст просто меняется целиком, мягко.
// Цвет, размер и жирность берутся как у обычного элемента (Foreground, FontSize, FontWeight наследуются ячейкам).
public sealed class RollText : ContentControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(RollText),
        new PropertyMetadata("", (d, e) => ((RollText)d).Roll((string?)e.OldValue ?? "", (string?)e.NewValue ?? "")));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    // false: без анимации (экономный режим)
    public bool Animate { get; set; } = true;

    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private static readonly IEasingFunction In = new SpringEase(7, 12);
    private static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseIn };

    public RollText()
    {
        Content = _row;
        IsTabStop = false;
        Focusable = false;
    }

    // ячейка: свой обрезанный по высоте кусочек, внутри один знак
    private Grid Cell(string ch)
    {
        var g = new Grid { ClipToBounds = true };
        g.Children.Add(Glyph(ch));
        return g;
    }

    private static TextBlock Glyph(string ch) => new() { Text = ch, RenderTransform = new TranslateTransform() };

    private void Roll(string old, string now)
    {
        bool digits = now.Any(char.IsDigit);
        if (!Animate || old.Length != now.Length || _row.Children.Count != old.Length || !digits)
        {
            _row.Children.Clear();
            foreach (var ch in now) _row.Children.Add(Cell(ch.ToString()));
            if (Animate && old.Length > 0 && old != now)
                _row.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(260)));
            return;
        }
        double h = Math.Max(10, FontSize * 1.25);
        for (int i = 0; i < now.Length; i++)
        {
            if (old[i] == now[i]) continue;
            var cell = (Grid)_row.Children[i];
            // старые знаки (в том числе недоехавшие с прошлого раза) уходят вверх
            foreach (var oldTb in cell.Children.OfType<TextBlock>().ToList())
            {
                var t = (TranslateTransform)oldTb.RenderTransform;
                var up = new DoubleAnimation(-h * 0.9, TimeSpan.FromMilliseconds(220)) { EasingFunction = Out };
                up.Completed += (_, _) => cell.Children.Remove(oldTb);
                t.BeginAnimation(TranslateTransform.YProperty, up);
                oldTb.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
            }
            var tb = Glyph(now[i].ToString());
            cell.Children.Add(tb);
            ((TranslateTransform)tb.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(h * 0.9, 0, TimeSpan.FromMilliseconds(460)) { EasingFunction = In });
            tb.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
    }
}
