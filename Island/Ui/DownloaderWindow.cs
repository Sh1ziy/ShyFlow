using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island;

// Шаг 90: окно «Загрузки» — скачать видео или звук по ссылке (Services\MediaDl.cs, yt-dlp; YouTube не качаем).
// Сверху поле для ссылки (Enter — скачать), «Видео» или «Звук MP3» и превью (обложка, название, сайт, длительность).
// Ниже очередь: прогресс, скорость, отмена; готовое — «показать в папке». Внизу — установка yt-dlp при первом запуске
// и необязательный ffmpeg для лучшего качества.
// Открывается: меню острова → «Загрузки», Ctrl+Alt+J по выделенной ссылке, клик по плашке скопированной ссылки,
// ссылка, брошенная на остров.
public sealed class DownloaderWindow : Window
{
    private static DownloaderWindow? _open;

    public static void ShowDownloader(string? url = null, bool start = false)
    {
        if (_open == null)
        {
            _open = new DownloaderWindow();
            _open.Closed += (_, _) => _open = null;
            _open.Show();
        }
        else { if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal; _open.Activate(); }
        if (url != null) _open.SetUrl(url, start);
    }

    private static SolidColorBrush W(byte a) => new(Color.FromArgb(a, 255, 255, 255));

    private readonly TextBox _url = new();
    private readonly TextBlock _msg = new();
    private readonly StackPanel _jobs = new();
    private readonly SBorder _preview = new();
    private readonly StackPanel _tools = new();
    private bool _audio;
    private SBorder? _btnVideo, _btnAudio;
    private string _probed = "";
    private int _probeId;

    private DownloaderWindow()
    {
        Title = Loc.T("Загрузки");
        Width = 520; Height = 640; MinWidth = 420; MinHeight = 460;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = UiFonts.Text;

        var root = ShyStyle.Shell();
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition());
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Child = shell;
        shell.SizeChanged += (_, e) => shell.Clip = Smooth.Shape(e.NewSize.Width, e.NewSize.Height, new CornerRadius(25));

        // шапка
        var head = new Grid { Background = Brushes.Transparent, Margin = new Thickness(18, 0, 14, 0) };
        head.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
        var h = ShyStyle.Header("download", Loc.T("Загрузки"));
        h.Margin = new Thickness(4, 4, 0, 0);
        head.Children.Add(h);
        var hb = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var folder = ShyStyle.Glass("folder", Loc.T("Папка с загрузками"), OpenFolder, 32);
        var clear = ShyStyle.Glass("trash-2", Loc.T("Убрать готовые из списка"), MediaDl.Clear, 32);
        var close = ShyStyle.Glass("x", Loc.T("Закрыть (Esc)"), Close, 32);
        clear.Margin = close.Margin = new Thickness(6, 0, 0, 0);
        hb.Children.Add(folder); hb.Children.Add(clear); hb.Children.Add(close);
        head.Children.Add(hb);
        shell.Children.Add(head);

        // ссылка, формат, превью
        var top = new StackPanel { Margin = new Thickness(18, 0, 18, 8) };
        Grid.SetRow(top, 1);
        var field = new Grid();
        field.ColumnDefinitions.Add(new ColumnDefinition());
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new SBorder { CornerRadius = new CornerRadius(18), Background = ShyStyle.Field, Padding = new Thickness(14, 0, 8, 0), Height = 40 };
        var ph = new TextBlock { Text = Loc.T("Ссылка: TikTok, Pinterest, Instagram, X, VK, Reddit…"), Foreground = W(0x60), FontSize = 13.5, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        _url.Background = Brushes.Transparent; _url.BorderThickness = new Thickness(0); _url.Foreground = Brushes.White; _url.CaretBrush = Brushes.White;
        _url.FontSize = 13.5; _url.VerticalContentAlignment = VerticalAlignment.Center; _url.SelectionBrush = Brushes.White; _url.SelectionOpacity = 0.25;
        _url.TextChanged += (_, _) => { ph.Visibility = _url.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; _ = ProbeLater(); };
        _url.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Start(); } };
        var paste = ShyStyle.Glass("clipboard", Loc.T("Вставить из буфера"), () => { try { if (Clipboard.ContainsText()) _url.Text = Clipboard.GetText().Trim(); } catch { } }, 30);
        paste.HorizontalAlignment = HorizontalAlignment.Right;
        box.Child = new Grid { Children = { ph, _url, paste } };
        _url.Margin = new Thickness(0, 0, 36, 0);
        field.Children.Add(box);
        var go = ShyStyle.Glass("download", Loc.T("Скачать (Enter)"), Start, 40);
        go.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(go, 1);
        field.Children.Add(go);
        top.Children.Add(field);

        var kinds = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        _btnVideo = ShyStyle.Pill(Loc.T("Видео"), true, () => SetAudio(false), "video", 32);
        _btnAudio = ShyStyle.Pill(Loc.T("Звук MP3"), false, () => SetAudio(true), "music", 32);
        _btnAudio.Margin = new Thickness(8, 0, 0, 0);
        kinds.Children.Add(_btnVideo); kinds.Children.Add(_btnAudio);
        top.Children.Add(kinds);

        _preview.Visibility = Visibility.Collapsed;
        _preview.Margin = new Thickness(0, 12, 0, 0);
        top.Children.Add(_preview);
        _msg.Foreground = W(0x90); _msg.FontSize = 12; _msg.TextWrapping = TextWrapping.Wrap; _msg.Margin = new Thickness(6, 8, 0, 0);
        top.Children.Add(_msg);
        shell.Children.Add(top);

        // очередь
        var scroll = new ScrollViewer { Content = _jobs, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Margin = new Thickness(18, 0, 18, 0) };
        Grid.SetRow(scroll, 2);
        shell.Children.Add(scroll);

        // инструменты
        _tools.Margin = new Thickness(18, 6, 18, 16);
        Grid.SetRow(_tools, 3);
        shell.Children.Add(_tools);

        Content = ShyStyle.WithShadow(root);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Action onChange = () => Dispatcher.BeginInvoke(RenderJobs);
        MediaDl.Changed += onChange;
        Closed += (_, _) => MediaDl.Changed -= onChange;
        Loaded += (_, _) =>
        {
            RenderJobs();
            RenderTools();
            ShyStyle.PopIn(root);
            _url.Focus();
            // в буфере уже ссылка с видео — подставить
            try { if (_url.Text.Length == 0 && Clipboard.ContainsText() && MediaDl.IsMediaLink(Clipboard.GetText().Trim())) _url.Text = Clipboard.GetText().Trim(); } catch { }
        };
    }

    private void SetUrl(string url, bool start)
    {
        _url.Text = url.Trim();
        if (start) Start();
    }

    private void SetAudio(bool audio)
    {
        _audio = audio;
        // пересоздать «таблетки» с нужной подсветкой проще, чем перекрашивать
        var parent = (StackPanel)_btnVideo!.Parent;
        parent.Children.Clear();
        _btnVideo = ShyStyle.Pill(Loc.T("Видео"), !audio, () => SetAudio(false), "video", 32);
        _btnAudio = ShyStyle.Pill(Loc.T("Звук MP3"), audio, () => SetAudio(true), "music", 32);
        _btnAudio.Margin = new Thickness(8, 0, 0, 0);
        parent.Children.Add(_btnVideo); parent.Children.Add(_btnAudio);
    }

    private void Start()
    {
        string url = _url.Text.Trim();
        if (url.Length == 0) return;
        if (!MediaDl.HasYtDlp) { _msg.Text = Loc.T("Сначала скачай загрузчик внизу окна — это один раз"); return; }
        string? err = MediaDl.Enqueue(url, _audio);
        _msg.Text = err ?? "";
        if (err == null) { _url.Text = ""; _preview.Visibility = Visibility.Collapsed; }
    }

    // превью: через полсекунды после ввода, если это ссылка
    private async Task ProbeLater()
    {
        int id = ++_probeId;
        await Task.Delay(500);
        if (id != _probeId) return;
        string url = _url.Text.Trim();
        _msg.Text = "";
        if (url.Length == 0 || url == _probed) { if (url.Length == 0) _preview.Visibility = Visibility.Collapsed; return; }
        if (MediaDl.IsYouTube(url)) { _msg.Text = Loc.T("YouTube пока не поддерживается"); _preview.Visibility = Visibility.Collapsed; return; }
        if (!MediaDl.HasYtDlp || !Uri.TryCreate(url, UriKind.Absolute, out _)) return;
        _probed = url;
        _msg.Text = Loc.T("Смотрю, что по ссылке…");
        var info = await MediaDl.Probe(url);
        if (id != _probeId) return;
        _msg.Text = info == null ? Loc.T("Не получилось посмотреть, но можно попробовать скачать") : "";
        if (info == null) { _preview.Visibility = Visibility.Collapsed; return; }
        ShowPreview(info);
    }

    private void ShowPreview(MediaDl.Info info)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var thumb = new SBorder { Width = 84, Height = 56, CornerRadius = new CornerRadius(10), Background = ShyStyle.Tile };
        if (info.Thumb != null)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.UriSource = new Uri(info.Thumb); bmp.DecodePixelWidth = 200; bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();
                thumb.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            }
            catch { }
        }
        g.Children.Add(thumb);
        var txt = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        txt.Children.Add(new TextBlock { Text = info.Title.Length > 0 ? info.Title : Loc.T("Без названия"), Foreground = Brushes.White, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        string meta = info.Site + (info.Duration > 0 ? " · " + TimeSpan.FromSeconds(info.Duration).ToString(info.Duration >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "");
        txt.Children.Add(new TextBlock { Text = meta, Foreground = W(0x80), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(txt, 1);
        g.Children.Add(txt);
        _preview.Child = ShyStyle.CardBox(g, 16, new Thickness(10));
        _preview.Visibility = Visibility.Visible;
    }

    private void RenderJobs()
    {
        _jobs.Children.Clear();
        if (MediaDl.Jobs.Count == 0)
        {
            _jobs.Children.Add(new TextBlock
            {
                Text = Loc.T("Скопируй ссылку на видео в браузере — ShyFlow предложит его скачать. Или выдели ссылку и нажми Ctrl+Alt+J."),
                Foreground = W(0x60), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(30, 30, 30, 0)
            });
            return;
        }
        foreach (var j in MediaDl.Jobs) _jobs.Children.Add(JobRow(j));
    }

    private FrameworkElement JobRow(MediaDl.Job j)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ShyStyle.IconTile(j.Failed ? "triangle-alert" : j.Done ? "check" : j.Audio ? "music" : "video", 34);
        g.Children.Add(icon);
        var mid = new StackPanel { Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = j.Title, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        string sub = j.Failed || j.Done || j.Canceled ? j.Status
            : j.Frac > 0 ? $"{(int)Math.Round(j.Frac * 100)}%" + (j.Speed.Length > 0 ? " · " + j.Speed : "") + (j.Eta.Length > 0 ? " · " + j.Eta : "")
            : j.Status;
        mid.Children.Add(new TextBlock { Text = sub, Foreground = j.Failed ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x60)) : W(0x80), FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        if (!j.Done && !j.Failed && !j.Canceled)
        {
            var track = new SBorder { Height = 4, CornerRadius = new CornerRadius(2), Background = W(0x1F), Margin = new Thickness(0, 6, 0, 0) };
            var fill = new SBorder { Height = 4, CornerRadius = new CornerRadius(2), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
            track.Child = fill;
            track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * j.Frac;
            mid.Children.Add(track);
        }
        Grid.SetColumn(mid, 1);
        g.Children.Add(mid);
        var act = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (j.Done && j.File != null)
        {
            act.Children.Add(ShyStyle.Glass("folder-open", Loc.T("Показать в папке"), () => ShowInFolder(j.File), 30));
            var open = ShyStyle.Glass("play", Loc.T("Открыть"), () => OpenFile(j.File), 30);
            open.Margin = new Thickness(6, 0, 0, 0);
            act.Children.Add(open);
        }
        else if (!j.Failed && !j.Canceled) act.Children.Add(ShyStyle.Glass("x", Loc.T("Отменить"), () => MediaDl.Cancel(j), 30));
        else act.Children.Add(ShyStyle.Glass("rotate-ccw", Loc.T("Ещё раз"), () => { MediaDl.Jobs.Remove(j); MediaDl.Enqueue(j.Url, j.Audio); }, 30));
        Grid.SetColumn(act, 2);
        g.Children.Add(act);
        return new SBorder { Child = ShyStyle.CardBox(g, 16, new Thickness(10)), Margin = new Thickness(0, 0, 0, 8) };
    }

    // установка yt-dlp (обязательно) и ffmpeg (по желанию)
    private void RenderTools()
    {
        _tools.Children.Clear();
        if (!MediaDl.HasYtDlp)
            _tools.Children.Add(ToolRow("download", Loc.T("Нужен загрузчик yt-dlp"), Loc.T("Скачается один раз, ~18 МБ, с GitHub. Дальше обновляется сам"), Loc.T("Скачать"), MediaDl.InstallYtDlp));
        else if (!MediaDl.HasFfmpeg)
            _tools.Children.Add(ToolRow("sparkles", Loc.T("Лучшее качество (по желанию)"), Loc.T("ffmpeg склеивает видео и звук, которые Reddit, Instagram и другие отдают отдельно. ~100 МБ"), Loc.T("Установить"), MediaDl.InstallFfmpeg));
    }

    private FrameworkElement ToolRow(string icon, string title, string sub, string btn, Func<IProgress<double>, Task<string?>> install)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(ShyStyle.IconTile(icon, 34));
        var mid = new StackPanel { Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold });
        var subT = new TextBlock { Text = sub, Foreground = W(0x80), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        mid.Children.Add(subT);
        Grid.SetColumn(mid, 1);
        g.Children.Add(mid);
        SBorder? b = null;
        bool busy = false;
        b = ShyStyle.Pill(btn, true, async () =>
        {
            if (busy) return;
            busy = true;
            b!.Opacity = 0.5;
            var err = await install(new Progress<double>(f => subT.Text = Loc.T("Скачиваю… ") + (int)(f * 100) + "%"));
            busy = false;
            if (err != null) { subT.Text = err; b.Opacity = 1; return; }
            RenderTools();
            _ = ProbeLater();
        }, null, 32);
        b.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(b, 2);
        g.Children.Add(b);
        return ShyStyle.CardBox(g, 16, new Thickness(10));
    }

    private static void OpenFolder()
    {
        try { Directory.CreateDirectory(MediaDl.OutDir); Process.Start(new ProcessStartInfo(MediaDl.OutDir) { UseShellExecute = true }); } catch (Exception ex) { App.LogOnce("Dl.OpenDir", ex); }
    }

    private static void ShowInFolder(string file)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true }); } catch (Exception ex) { App.LogOnce("Dl.Show", ex); }
    }

    private static void OpenFile(string file)
    {
        try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch (Exception ex) { App.LogOnce("Dl.Open", ex); }
    }
}
