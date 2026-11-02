using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Island;

// Шаг 77: мини-плеер в отдельном окне, как MiniPlayer в Apple Music и мини-плеер Spotify.
// Чёрная капсула в стиле острова поверх всех окон: обложка, название, исполнитель, назад / пауза / вперёд и тонкая
// полоска прогресса цвета обложки. Таскается за любое место, место запоминается. Крестик появляется при наведении.
// Открывается плиткой «Мини-плеер» в быстрых действиях или из меню острова (правый клик).
internal sealed class MiniPlayerWindow : Window
{
    private static MiniPlayerWindow? _open;

    public static void Toggle(MainWindow host)
    {
        if (_open != null) { _open.Close(); return; }
        _open = new MiniPlayerWindow(host);
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    private readonly MainWindow _host;
    private readonly SBorder _cover = new() { Width = 64, Height = 64, CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x30)) };
    private readonly TextBlock _title = new() { Foreground = Brushes.White, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { Foreground = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255)), FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
    // шаг 79: строка песни, которая поётся сейчас (если текст нашёлся), мелко под исполнителем
    private readonly TextBlock _line = new() { Foreground = new SolidColorBrush(Color.FromArgb(0xD8, 255, 255, 255)), FontSize = 12, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
    private string _lineShown = "";
    private readonly VIcon _playIcon = new() { Text = "play", FontSize = 17, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly SBorder _fill = new() { Height = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.White };
    private readonly Grid _track = new() { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18, 0, 18, 9) };
    private readonly SBorder _close;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private BitmapSource? _coverShown;
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly LiveBrush _fillBrush = new(Colors.White);
    private Color _fillTo = Colors.White;

    private MiniPlayerWindow(MainWindow host)
    {
        _host = host;
        Title = "ShyFlow Mini";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        Width = 380; Height = 124;
        FontFamily = UiFonts.Text;

        var (x, y) = host.MiniPos;
        var wa = SystemParameters.WorkArea;
        if (x < -90000 || x < SystemParameters.VirtualScreenLeft || x > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60)
        { x = wa.Right - Width - 12; y = wa.Bottom - Height - 12; }
        Left = x; Top = y;

        var root = new SBorder
        {
            Margin = new Thickness(10), CornerRadius = new CornerRadius(28), Background = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.5 }
        };
        var g = new Grid { Margin = new Thickness(14, 12, 14, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _cover.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(_cover);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 10, 4) };
        names.Children.Add(_title);
        names.Children.Add(_artist);
        names.Children.Add(_line);
        Grid.SetColumn(names, 1);
        g.Children.Add(names);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        btns.Children.Add(Btn(new VIcon { Text = "skip-back", FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, 36, () => _ = Skip(-1)));
        btns.Children.Add(Btn(_playIcon, 42, () => _ = PlayPause()));
        btns.Children.Add(Btn(new VIcon { Text = "skip-forward", FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, 36, () => _ = Skip(1)));
        Grid.SetColumn(btns, 2);
        g.Children.Add(btns);

        _track.Children.Add(new SBorder { CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)) });
        _track.Children.Add(_fill);
        _fillBrush.Use(_fill, Border.BackgroundProperty);   // шаг 80: цвет обложки перетекает (LiveBrush), а не прыгает

        _close = new SBorder
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 8, 0),
            Opacity = 0, Cursor = Cursors.Hand, ToolTip = Loc.T("Закрыть мини-плеер", "Close the mini player"),
            Child = new VIcon { Text = "x", FontSize = 9, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        _close.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _close.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); };

        var layers = new Grid();
        layers.Children.Add(g);
        layers.Children.Add(_track);
        layers.Children.Add(_close);
        root.Child = layers;
        Content = root;

        // таскать за любое место, кроме кнопок
        root.MouseLeftButtonDown += (_, e) => { try { DragMove(); } catch { /* окно закрывается */ } };
        root.MouseEnter += (_, _) => _close.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
        root.MouseLeave += (_, _) => _close.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        LocationChanged += (_, _) => { _save.Stop(); _save.Start(); };
        _save.Tick += (_, _) => { _save.Stop(); _host.MiniPos = (Left, Top); };

        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            _timer.Start();
            var sc = new ScaleTransform(0.9, 0.9);
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            root.RenderTransform = sc;
            var a = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        };
        Closed += (_, _) => _timer.Stop();
    }

    private static SBorder Btn(FrameworkElement icon, double size, Action click)
    {
        var sc = new ScaleTransform(1, 1);
        var b = new SBorder
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Brushes.Transparent, Child = icon, Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = sc
        };
        // шаг 79: подсветка — готовые замороженные кисти, а не перекраска одной (Ui\LiveBrush.cs)
        b.MouseEnter += (_, _) => b.Background = ShyStyle.B(0x26, 255, 255, 255);
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonDown += (_, e) => { e.Handled = true; sc.ScaleX = sc.ScaleY = 0.88; };
        b.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var a = new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            click();
        };
        return b;
    }

    private void Refresh()
    {
        string t = _host.CurTitle;
        _title.Text = t.Length > 0 ? t : Loc.T("Ничего не играет");
        _artist.Text = _host.CurArtist;
        _playIcon.Text = _host.IsPlaying ? "pause" : "play";
        var cov = _host.CurCover;
        if (!ReferenceEquals(cov, _coverShown))
        {
            _coverShown = cov;
            if (cov != null)
            {
                var br = new ImageBrush(cov) { Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(br, BitmapScalingMode.HighQuality);
                _cover.Background = br;
            }
            else _cover.Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x30));
            var a = new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
            var sc = new ScaleTransform(1, 1);
            _cover.RenderTransformOrigin = new Point(0.5, 0.5);
            _cover.RenderTransform = sc;
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        if (_host.AccentNow != _fillTo) { _fillTo = _host.AccentNow; _fillBrush.To(_fillTo, 600); }
        double frac = 0;
        try
        {
            var tl = _host.CurSession?.GetTimelineProperties();
            if (tl != null)
            {
                double total = (tl.EndTime - tl.StartTime).TotalSeconds;
                var pos = tl.Position;
                if (_host.IsPlaying) pos += DateTimeOffset.Now - tl.LastUpdatedTime;
                if (total > 1) frac = Math.Clamp((pos - tl.StartTime).TotalSeconds / total, 0, 1);
            }
        }
        catch { /* плеер закрылся */ }
        _fill.Width = Math.Max(0, _track.ActualWidth * frac);
        UpdateLine();
    }

    private void UpdateLine()
    {
        string text = "";
        try
        {
            var lyr = _host.CurLyrics;
            var tl = _host.CurSession?.GetTimelineProperties();
            if (lyr != null && lyr.Count > 0 && tl != null)
            {
                var pos = tl.Position;
                if (_host.IsPlaying) pos += DateTimeOffset.Now - tl.LastUpdatedTime;
                double t = (pos - tl.StartTime).TotalSeconds + 0.15 - _host.LyricOffsetNow;
                int idx = -1;
                for (int i = 0; i < lyr.Count; i++) { if (lyr[i].t <= t) idx = i; else break; }
                if (idx >= 0) text = Duet.Strip(lyr[idx].s, _host.CurArtist).Trim();
                if (LyricsEngine.IsInterlude(text)) text = "♪";
            }
        }
        catch { /* плеер закрылся */ }
        if (text == _lineShown) return;
        _lineShown = text;
        _line.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _line.Text = text;
        // новая строка мягко поднимается снизу
        var tt = new TranslateTransform(0, 6);
        _line.RenderTransform = tt;
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _line.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
    }

    private async Task PlayPause()
    {
        try { if (_host.CurSession is { } s) await s.TryTogglePlayPauseAsync(); }
        catch (Exception ex) { App.LogOnce("Mini.Play", ex); }
        Refresh();
    }

    private async Task Skip(int dir)
    {
        try
        {
            _host.NoteSkip(dir);
            if (_host.CurSession is not { } s) return;
            if (dir > 0) await s.TrySkipNextAsync(); else await s.TrySkipPreviousAsync();
        }
        catch (Exception ex) { App.LogOnce("Mini.Skip", ex); }
    }
}
