using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace Island;

// Тень, световая кромка, тема и акцент Windows.
//
// Остров сам остаётся чёрным, как у Apple. Но на рабочем столе (в отличие от экрана iPhone, где он сливается с камерой)
// чёрная капсула на тёмных обоях теряет форму, а на светлых выглядит «наклейкой». Поэтому:
//   • мягкая тень — отдельным слоем ПОД островом. Эффект на самом острове заставлял бы WPF перерисовывать всё
//     содержимое (эквалайзер, текст) в промежуточную текстуру на каждом кадре; слою-подложке перерисовка нужна,
//     только когда меняется размер острова;
//   • кромка в 1 px: сверху чуть светлее (свет падает сверху), снизу почти не видна. Во время музыки — цвета обложки;
//   • тема Windows: в тёмной тень гуще и кромка заметнее, в светлой тень мягче и кромка тише;
//   • акцент Windows — цвет интерфейса острова, пока нет обложки (ничего не играет или у трека нет картинки).
// В экономном режиме, в полоске поверх игры и в режиме notch (остров сливается с краем экрана) тени и кромки нет.
public partial class MainWindow
{
    private SBorder? _pillShadow, _pillEdge;
    private DropShadowEffect? _shadowFx;
    private bool _lightTheme;
    private Color _sysAccent = Color.FromRgb(0x4C, 0xA0, 0xFF);
    private Windows.UI.ViewManagement.UISettings? _uiSettings;   // держим ссылку: иначе событие смены цвета перестаёт приходить

    internal Color SysAccent => _sysAccent;

    private SBorder? _pillHover;

    // как у macOS: при наведении поверхность чуть светлеет (быстро), при уходе гаснет медленнее
    private void ChromeHover(bool on)
    {
        if (_pillHover == null) return;
        double to = on && !_fsMini ? (_lightTheme ? 0.09 : 0.07) : 0;
        _pillHover.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(on ? 160 : 280))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
    }

    private void InitChrome()
    {
        try
        {
            // внешний слой несёт тень и повторяет положение острова; внутренний — чёрное тело с тем же контуром.
            // Clip острова (волны жидкого стиля, Fluid.cs) ставится на внутренний: на внешнем он обрезал бы саму тень
            _pillShadow = new SBorder { IsHitTestVisible = false, SnapsToDevicePixels = false };
            foreach (var (dp, src) in new (DependencyProperty, string)[]
                     {
                         (WidthProperty, "Width"), (HeightProperty, "Height"), (MarginProperty, "Margin"),
                         (HorizontalAlignmentProperty, "HorizontalAlignment"), (VerticalAlignmentProperty, "VerticalAlignment"),
                         (VisibilityProperty, "Visibility"), (OpacityProperty, "Opacity"),
                         (RenderTransformProperty, "RenderTransform"), (RenderTransformOriginProperty, "RenderTransformOrigin")
                     })
                _pillShadow.SetBinding(dp, new Binding(src) { Source = Pill, Mode = BindingMode.OneWay });
            var body = new SBorder { Background = Brushes.Black, IsHitTestVisible = false };
            body.SetBinding(System.Windows.Controls.Border.CornerRadiusProperty, new Binding("CornerRadius") { Source = Pill, Mode = BindingMode.OneWay });
            body.SetBinding(ClipProperty, new Binding("Clip") { Source = Pill, Mode = BindingMode.OneWay });
            _pillShadow.Child = body;
            Stage.Children.Insert(Math.Max(0, Stage.Children.IndexOf(Pill)), _pillShadow);

            _pillEdge = new SBorder { IsHitTestVisible = false, BorderThickness = new Thickness(1), Background = null };
            _pillEdge.SetBinding(System.Windows.Controls.Border.CornerRadiusProperty, new Binding("CornerRadius") { Source = Pill, Mode = BindingMode.OneWay });
            PillContent.Children.Add(_pillEdge);

            // осветление при наведении: белая вуаль под содержимым (над фоном и обложкой), текст остаётся чистым
            _pillHover = new SBorder { IsHitTestVisible = false, Background = Brushes.White, Opacity = 0 };
            _pillHover.SetBinding(System.Windows.Controls.Border.CornerRadiusProperty, new Binding("CornerRadius") { Source = Pill, Mode = BindingMode.OneWay });
            PillContent.Children.Insert(PillContent.Children.IndexOf(BgShade) + 1, _pillHover);
        }
        catch (Exception ex) { App.Log("Chrome.Init", ex); }

        ReadSystemTheme();
        try
        {
            _uiSettings = new Windows.UI.ViewManagement.UISettings();
            _uiSettings.ColorValuesChanged += (_, _) => Dispatcher.BeginInvoke(() => { ReadSystemTheme(); ApplyAccentUi(); });
        }
        catch (Exception ex) { App.Log("Chrome.UISettings", ex); }
        SystemEvents.UserPreferenceChanged += OnUserPref;
        Closed += (_, _) => SystemEvents.UserPreferenceChanged -= OnUserPref;   // статическое событие: иначе держит окно
    }

    private void OnUserPref(object? s, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Color)
            Dispatcher.BeginInvoke(() => { ReadSystemTheme(); ApplyAccentUi(); });
    }

    private void ReadSystemTheme()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            _lightTheme = k?.GetValue("SystemUsesLightTheme") is int v && v == 1;   // тема панели задач: остров живёт рядом с ней
        }
        catch { _lightTheme = false; }
        try
        {
            var c = (_uiSettings ?? new Windows.UI.ViewManagement.UISettings()).GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            if (c.A > 0) _sysAccent = Color.FromRgb(c.R, c.G, c.B);
        }
        catch (Exception ex) { App.LogOnce("Chrome.Accent", ex); }
    }

    // зовётся из ApplyTheme (обложка, настройки, тема Windows) и при смене полоски в игре
    private void UpdateChrome()
    {
        if (_pillShadow == null || _pillEdge == null) return;
        bool flat = _eco || _fsMini || _nk.T > 0 || (Duo && DuoMid);   // в notch остров продолжает край экрана; у Duo посередине края тень легла бы на край
        if (flat)
        {
            _pillShadow.Effect = null;
            _pillShadow.Visibility = Visibility.Collapsed;
            _pillEdge.BorderBrush = null;
            return;
        }
        if (GlassOn)
        {
            // стекло (Glass.cs): чёрное тело тени просвечивало бы сквозь остров — тени нет, зато кромка ярче, как грань стекла
            _pillShadow.Effect = null;
            _pillShadow.Visibility = Visibility.Collapsed;
        }
        else
        {
            _pillShadow.ClearValue(VisibilityProperty);
            _pillShadow.SetBinding(VisibilityProperty, new Binding("Visibility") { Source = Pill, Mode = BindingMode.OneWay });
            _shadowFx ??= new DropShadowEffect { Color = Colors.Black, Direction = 270, RenderingBias = RenderingBias.Performance };
            _shadowFx.BlurRadius = _lightTheme ? 22 : 26;
            _shadowFx.ShadowDepth = _lightTheme ? 4 : 3;
            _shadowFx.Opacity = _lightTheme ? 0.28 : 0.55;
            if (!ReferenceEquals(_pillShadow.Effect, _shadowFx)) _pillShadow.Effect = _shadowFx;
        }

        // кромка: во время музыки — цвет обложки (или акцент Windows), иначе нейтрально-белая
        var c = _playing && _cfg.AccentUi ? UiColor : Colors.White;
        byte top = (byte)(GlassOn ? 0x66 : _lightTheme ? 0x1A : 0x30), bottom = (byte)(GlassOn ? 0x1C : _lightTheme ? 0x06 : 0x0C);
        var edge = new LinearGradientBrush(Color.FromArgb(top, c.R, c.G, c.B), Color.FromArgb(bottom, c.R, c.G, c.B), 90);
        edge.Freeze();
        _pillEdge.BorderBrush = edge;
    }
}
