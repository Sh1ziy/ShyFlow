using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Island;

// Шаг 79: единый стиль ShyFlow для всех окон — тот же, что у настроек (шаг 72) и у самого острова.
// Почти чёрный фон, тонкая белая рамка, полупрозрачные белые карточки и кнопки, круглые плитки под значками,
// «главное» и «включено» — белым. Цвет появляется только там, где он что-то значит (редкость карточки, праздник, обложка).
// Раньше коллекция, статистика, календарь, блокнот и диалоги были серыми в стиле macOS и каждый со своим цветом
// (оранжевый, фиолетово-розовый, красный, жёлтый, синие кнопки).
internal static class ShyStyle
{
    // ---- Шаг 95: цвета Apple (тёмная тема iOS/macOS) ----
    // Раньше заливки были чисто белыми с прозрачностью (#1FFFFFFF, #26FFFFFF...). У Apple они чуть холодные и серые:
    // «system fill» = rgba(120,120,128) с прозрачностью 36 / 32 / 24 / 18 %, и именно это даёт «яблочное» ощущение.
    // Яркость почти прежняя (на чёрном 18 % серого ≈ 8 % белого против прежних 7 %), поменялся в основном оттенок.
    // Все значения ниже — замороженные кисти (SafeBrush), как требует LiveBrush.cs.
    public static readonly Color Label2C = Color.FromArgb(0x99, 0xEB, 0xEB, 0xF5);       // secondaryLabel, 60 %: подписи, исполнитель
    public static readonly Color Label3C = Color.FromArgb(0x4D, 0xEB, 0xEB, 0xF5);       // tertiaryLabel, 30 %: только подсказки и «выключено», читаемый мелкий текст так не красим
    public static readonly Color SeparatorC = Color.FromArgb(0x99, 0x54, 0x54, 0x58);    // separator
    public static readonly Color Fill1C = Color.FromArgb(0x5C, 0x78, 0x78, 0x80);        // systemFill 36 %: выбранное, нажатое, дорожка ползунка
    public static readonly Color Fill2C = Color.FromArgb(0x52, 0x78, 0x78, 0x80);        // secondarySystemFill 32 %: кнопки, «выключено»
    public static readonly Color Fill3C = Color.FromArgb(0x3D, 0x78, 0x78, 0x80);        // tertiarySystemFill 24 %: плитки, чипы
    public static readonly Color Fill4C = Color.FromArgb(0x2E, 0x78, 0x78, 0x80);        // quaternarySystemFill 18 %: карточки, поля ввода
    public static readonly Color Fill5C = Color.FromArgb(0x1F, 0x78, 0x78, 0x80);        // 12 %: боковая панель, наведение на строку
    public static readonly Color FillHoverC = Color.FromArgb(0x70, 0x78, 0x78, 0x80);    // 44 %: наведение на кнопку (чуть ярче Fill1)

    public static readonly Color BgColor = Color.FromRgb(0x08, 0x08, 0x09);
    public static readonly Brush Bg = B(0xFF, 0x08, 0x08, 0x09);
    public static readonly Brush Frame = B(0x24, 255, 255, 255);
    public static readonly Brush Panel = B(Fill5C);                      // боковая панель
    public static readonly Brush PanelFrame = B(0x12, 255, 255, 255);
    public static readonly Brush Card = B(Fill4C);
    public static readonly Brush CardFrame = B(0x10, 255, 255, 255);
    public static readonly Brush Line = B(SeparatorC);
    public static readonly Brush Tile = B(Fill3C);                       // кружок под значком, чипы, поле сортировки
    public static readonly Brush Sel = B(Fill1C);                        // выбранный пункт
    public static readonly Brush Hover = B(Fill5C);
    public static readonly Brush Field = B(Fill4C);                      // поле ввода
    public static readonly Brush Label2 = B(Label2C);
    public static readonly Brush Label3 = B(Label3C);
    public static readonly Brush Gray = Label2;                          // раньше #98989D; на чёрном контраст ≈ 6:1
    public static readonly Brush Dim = Label2;                           // раньше 40 % белого (≈ 3,7:1): мелкому тексту не хватало до 4,5:1
    public static readonly Brush Ink = B(0xFF, 0x1C, 0x1C, 0x1E);        // тёмный текст на белом
    public static readonly Brush Fill1 = B(Fill1C);                      // дорожки ползунков
    public static readonly Color GlassC = Fill3C, GlassHoverC = Fill2C;
    public static readonly Color BtnC = Fill2C, BtnHoverC = FillHoverC;

    public static Brush B(byte a, byte r, byte g, byte b) => SafeBrush.Solid(Color.FromArgb(a, r, g, b));
    public static Brush B(Color c) => SafeBrush.Solid(c);

    // корпус окна: как у настроек
    public static SBorder Shell(double margin = 14, double radius = 26) => new()
    {
        Margin = new Thickness(margin), CornerRadius = new CornerRadius(radius), Background = Bg,
        BorderBrush = Frame, BorderThickness = new Thickness(1)
    };

    // Тень окна — отдельный пустой слой под корпусом. Эффект на самом корпусе пересчитывал тень при каждом изменении внутри окна
    // (наведение, прокрутка, анимация). Слой готов один раз (BitmapCache) и только повторяет масштаб и прозрачность корпуса.
    public static Grid WithShadow(SBorder body, double blur = 34, double opacity = 0.65, double depth = 6)
    {
        var shadow = new SBorder
        {
            Margin = body.Margin, CornerRadius = body.CornerRadius, Background = Bg, IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = blur, Opacity = opacity, ShadowDepth = depth, Direction = 270, RenderingBias = RenderingBias.Performance },
            CacheMode = new BitmapCache()
        };
        shadow.SetBinding(UIElement.OpacityProperty, new System.Windows.Data.Binding(nameof(UIElement.Opacity)) { Source = body });
        shadow.SetBinding(UIElement.RenderTransformProperty, new System.Windows.Data.Binding(nameof(UIElement.RenderTransform)) { Source = body });
        shadow.SetBinding(UIElement.RenderTransformOriginProperty, new System.Windows.Data.Binding(nameof(UIElement.RenderTransformOrigin)) { Source = body });
        var host = new Grid();
        host.Children.Add(shadow);
        host.Children.Add(body);
        return host;
    }

    // боковая панель внутри окна: как у настроек
    public static SBorder SidePanel() => new()
    {
        Margin = new Thickness(8), CornerRadius = new CornerRadius(20), Background = Panel,
        BorderBrush = PanelFrame, BorderThickness = new Thickness(1)
    };

    // круглая плитка под значком (заголовок окна, разделы)
    public static SBorder IconTile(string glyph, double size = 30, Brush? bg = null, Brush? fg = null) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = bg ?? Tile,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new VIcon
        {
            Text = glyph, FontSize = size * 0.5, Foreground = fg ?? Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        }
    };

    // заголовок окна: плитка со значком и название
    public static StackPanel Header(string glyph, string title)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(IconTile(glyph));
        row.Children.Add(new TextBlock
        {
            Text = title, Foreground = Brushes.White, FontSize = ShyType.Title2, FontWeight = FontWeights.SemiBold,   // шаг 95: Title 2 (было 19 Bold), Inter Display
            FontFamily = ShyType.Face(ShyType.Title2),
            Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }

    // круглая стеклянная кнопка (закрыть, свернуть, добавить...): как у настроек
    public static SBorder Glass(string icon, string? tip, Action click, double size = 30)
    {
        var bg = new LiveBrush(GlassC);
        var sc = new ScaleTransform(1, 1);
        var b = new SBorder
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Cursor = Cursors.Hand, ToolTip = tip,
            RenderTransform = sc, RenderTransformOrigin = new Point(0.5, 0.5), VerticalAlignment = VerticalAlignment.Center,
            Child = new VIcon
            {
                Text = icon, FontSize = Math.Round(size * 0.43), Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        bg.Use(b, Border.BackgroundProperty);
        Springy(b, sc, () => bg.To(GlassHoverC, 140), () => bg.To(GlassC, 220), click);
        return b;
    }

    // кнопка-капсула: главная белая с тёмным текстом, обычная — полупрозрачная белая
    public static SBorder Pill(string text, bool primary, Action click, string? icon = null, double height = 34)
    {
        Color normal = primary ? Colors.White : BtnC, hover = primary ? Color.FromRgb(0xE6, 0xE6, 0xE6) : BtnHoverC;
        var fg = primary ? Ink : Brushes.White;
        var bg = new LiveBrush(normal);
        var sc = new ScaleTransform(1, 1);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null)
            row.Children.Add(new VIcon { Text = icon, FontSize = 13, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        row.Children.Add(new TextBlock { Text = text, Foreground = fg, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var b = new SBorder
        {
            Height = height, MinWidth = height, CornerRadius = new CornerRadius(height / 2), Padding = new Thickness(16, 0, 16, 0),
            Cursor = Cursors.Hand, Child = row, RenderTransform = sc, RenderTransformOrigin = new Point(0.5, 0.5),
            VerticalAlignment = VerticalAlignment.Center
        };
        bg.Use(b, Border.BackgroundProperty);
        Springy(b, sc, () => bg.To(hover, 140), () => bg.To(normal, 220), click);
        return b;
    }

    // карточка с содержимым: как группы в настройках
    public static SBorder CardBox(UIElement child, double radius = 18, Thickness? padding = null)
    {
        var c = new SBorder
        {
            CornerRadius = new CornerRadius(radius), Background = Card, BorderBrush = CardFrame, BorderThickness = new Thickness(1),
            Padding = padding ?? new Thickness(0), Child = child
        };
        return c;
    }

    // наведение, нажатие с пружинкой и клик. Нажатие не уходит в шапку: там окно перетаскивается (DragMove), и отпускание терялось
    private static void Springy(FrameworkElement b, ScaleTransform sc, Action enter, Action leave, Action click)
    {
        void To(double v, int ms)
        {
            var an = new DoubleAnimation(v, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, an);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, an);
        }
        b.MouseEnter += (_, _) => enter();
        b.MouseLeave += (_, _) => { leave(); To(1, 220); };
        b.MouseLeftButtonDown += (_, e) => { e.Handled = true; To(0.9, 110); };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; To(1, 200); click(); };
    }

    // появление окна: лёгкая пружина и проявление, одинаково у всех окон
    public static void PopIn(FrameworkElement root)
    {
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        var sc = new ScaleTransform(0.96, 0.96);
        root.RenderTransform = sc;
        var a = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }
}
