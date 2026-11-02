using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Island;

// Шаг 80: «желе» при нажатии во всех окнах ShyFlow. Всё, что нажимается (кнопки, плитки, чипы, карточки — у них курсор «рука»),
// при нажатии сплющивается по высоте и чуть раздаётся вширь, а при отпускании пружинит обратно с небольшим вытягиванием вверх.
// Сила в пикселях, а не в процентах: маленькая кнопка заметно мнётся, длинная строка настроек — едва-едва.
// Не трогаем: поля ввода, ползунки и полосы прокрутки, элементы со своей трансформацией (их анимирует свой код), PressFx.Off.
// Включается в Настройках («Анимации», «Желе при нажатии»); в экономном режиме выключено (MainWindow.ApplyFeatureSettings).
internal static class PressFx
{
    public static bool On = true;

    public static readonly DependencyProperty OffProperty =
        DependencyProperty.RegisterAttached("Off", typeof(bool), typeof(PressFx), new PropertyMetadata(false));
    public static void SetOff(DependencyObject d, bool v) => d.SetValue(OffProperty, v);
    private static bool IsOff(DependencyObject d) => (bool)d.GetValue(OffProperty);

    // наша трансформация на элементе (если её подменил чужой код, берём заново)
    private static readonly DependencyProperty MineProperty =
        DependencyProperty.RegisterAttached("Mine", typeof(ScaleTransform), typeof(PressFx), new PropertyMetadata(null));

    private static readonly IEasingFunction Press = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction BackY = new SpringEase(6, 16);
    private static readonly IEasingFunction BackX = new SpringEase(5.5, 14);

    // окно может отдать свою ScaleTransform элемента (остров: Tx.S из MainWindow.Get, в покое она всегда 1)
    public static Func<FrameworkElement, ScaleTransform?>? Adopt;

    private static ScaleTransform? _held;
    private static DispatcherTimer? _watch;

    public static void Init()
    {
        // общее событие мыши идёт по дереву от окна к элементу (а «левая кнопка» выводится из него на каждом элементе отдельно)
        EventManager.RegisterClassHandler(typeof(Window), Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Down), true);
        EventManager.RegisterClassHandler(typeof(Window), Mouse.PreviewMouseUpEvent,
            new MouseButtonEventHandler((_, e) => { if (e.ChangedButton == MouseButton.Left) Release(); }), true);
    }

    private static void Down(object sender, MouseButtonEventArgs e)
    {
        if (!On || _held != null || e.ChangedButton != MouseButton.Left) return;
        try
        {
            var fe = Target(e.OriginalSource as DependencyObject, sender as Window);
            if (fe == null) return;
            var s = Transform(fe);
            if (s == null) return;
            double w = fe.ActualWidth, h = fe.ActualHeight;
            // мнётся от середины; если у элемента своя точка опоры (RenderTransformOrigin), она уже учтена
            s.CenterX = w * (0.5 - fe.RenderTransformOrigin.X); s.CenterY = h * (0.5 - fe.RenderTransformOrigin.Y);
            // примерно на 4 px по высоте и 2,5 px по ширине, но не больше 10 % и 5 %; широкие строки (вся строка настроек) — вполсилы
            double wide = Math.Min(1, 300 / w);
            double sy = 1 - Math.Min(0.10, 4.0 / h) * wide, sx = 1 + Math.Min(0.05, 2.5 / w);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(sy, TimeSpan.FromMilliseconds(110)) { EasingFunction = Press });
            s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(sx, TimeSpan.FromMilliseconds(110)) { EasingFunction = Press });
            _held = s;
            // отпускание может не дойти до окна (перетаскивание файла из кармана, окно закрылось): следим за кнопкой мыши
            _watch ??= new DispatcherTimer(TimeSpan.FromMilliseconds(70), DispatcherPriority.Input, (_, _) =>
            {
                if (Mouse.LeftButton != MouseButtonState.Pressed) Release();
            }, Dispatcher.CurrentDispatcher);
            _watch.Start();
        }
        catch (Exception ex) { App.LogOnce("PressFx", ex); }
    }

    private static void Release()
    {
        _watch?.Stop();
        var s = _held;
        _held = null;
        if (s == null) return;
        // пружина обратно: высота перелетает вверх (вытягивание), ширина чуть позже и мягче
        s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(560)) { EasingFunction = BackY });
        s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(620)) { EasingFunction = BackX });
    }

    // от места нажатия вверх по дереву: первый элемент, который нажимается
    private static FrameworkElement? Target(DependencyObject? d, Window? root)
    {
        while (d != null && !ReferenceEquals(d, root))
        {
            if (IsOff(d)) return null;
            if (d is TextBoxBase || d is PasswordBox || d is RangeBase || d is Thumb || d is ComboBox) return null;
            if (d is FrameworkElement fe && fe.IsEnabled && (d is ButtonBase || ReferenceEquals(fe.Cursor, Cursors.Hand)))
                return fe.ActualWidth >= 8 && fe.ActualHeight >= 8 && fe.ActualWidth <= 1000 && fe.ActualHeight <= 400 ? fe : null;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    // своя ScaleTransform только на элементе без трансформации: чужие (и их порядок в группах) не трогаем
    private static ScaleTransform? Transform(FrameworkElement fe)
    {
        if (Adopt?.Invoke(fe) is ScaleTransform own) return own;
        if (fe.GetValue(MineProperty) is ScaleTransform mine && ReferenceEquals(fe.RenderTransform, mine)) return mine;
        var rt = fe.RenderTransform;
        if (rt != null && !ReferenceEquals(rt, System.Windows.Media.Transform.Identity) && !(rt is MatrixTransform m && m.Matrix.IsIdentity))
            return null;
        var s = new ScaleTransform(1, 1);
        fe.RenderTransform = s;
        fe.SetValue(MineProperty, s);
        return s;
    }
}
