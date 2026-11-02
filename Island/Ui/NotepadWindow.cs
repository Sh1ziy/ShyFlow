using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Island;

// Блокнот (шаг 53): своё окно для длинных записей, в отличие от коротких заметок в кармане.
// Слева страницы (заголовок — первая строка, ниже начало текста и дата), справа сам текст.
// Сохраняется само через полсекунды после ввода и при закрытии: %AppData%\Island\notepad.json.
// Кнопки: новая страница, «поверх всех окон», закрыть. Удалить страницу: корзина при наведении (с вопросом).
// Открывается плиткой «Блокнот» в быстрых действиях, кнопкой в кармане и из меню острова.
public sealed class NotePage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime Updated { get; set; } = DateTime.Now;
}

public sealed class NotepadWindow : Window
{
    private static NotepadWindow? _open;

    public static void ShowNotepad()
    {
        if (_open != null) { if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal; _open.Activate(); return; }
        _open = new NotepadWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "notepad.json");
    // шаг 79: единый стиль ShyFlow (Ui\ShyStyle.cs): белое вместо жёлтого
    private static readonly Color Accent = Colors.White;
    private static SolidColorBrush W(byte a) => new(Color.FromArgb(a, 255, 255, 255));

    private readonly List<NotePage> _pages;
    private NotePage _cur;
    private readonly StackPanel _list = new();
    private readonly TextBox _edit = new();
    private readonly TextBlock _status = new();
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _loading;
    private VIcon? _pinIcon;

    private NotepadWindow()
    {
        Title = Loc.T("Блокнот");
        Width = 820; Height = 560; MinWidth = 520; MinHeight = 360;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = UiFonts.Text;

        _pages = Load();
        if (_pages.Count == 0) _pages.Add(new NotePage());
        _cur = _pages.OrderByDescending(p => p.Updated).First();

        var root = ShyStyle.Shell();

        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
        shell.RowDefinitions.Add(new RowDefinition());
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        shell.ColumnDefinitions.Add(new ColumnDefinition());
        root.Child = shell;
        shell.SizeChanged += (_, e) => shell.Clip = Smooth.Shape(e.NewSize.Width, e.NewSize.Height, new CornerRadius(25));

        // боковая панель как в настройках
        var side = ShyStyle.SidePanel();
        Grid.SetRowSpan(side, 2);
        shell.Children.Add(side);

        // шапка
        var head = new Grid { Background = Brushes.Transparent, Margin = new Thickness(18, 0, 14, 0) };
        Grid.SetColumnSpan(head, 2);
        head.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else try { DragMove(); } catch { } };
        var t = ShyStyle.Header("sticky-note", Loc.T("Блокнот"));
        t.Margin = new Thickness(4, 6, 0, 0);
        head.Children.Add(t);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        btns.Children.Add(HeadBtn("plus", Loc.T("Новая страница (Ctrl+N)"), NewPage));
        var pin = HeadBtn("pin", Loc.T("Поверх всех окон"), () => { Topmost = !Topmost; UpdatePin(); });
        _pinIcon = (VIcon)((SBorder)pin).Child;
        pin.Margin = new Thickness(6, 0, 0, 0);
        btns.Children.Add(pin);
        btns.Children.Add(HeadBtn("x", Loc.T("Закрыть (Esc)"), Close));
        head.Children.Add(btns);
        shell.Children.Add(head);

        var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Margin = new Thickness(18, 4, 18, 18) };
        Grid.SetRow(scroll, 1);
        shell.Children.Add(scroll);

        // редактор
        var right = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        right.RowDefinitions.Add(new RowDefinition());
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 1);
        _edit.AcceptsReturn = true; _edit.AcceptsTab = true; _edit.TextWrapping = TextWrapping.Wrap;
        _edit.Background = Brushes.Transparent; _edit.BorderThickness = new Thickness(0); _edit.Foreground = Brushes.White;
        _edit.CaretBrush = Brushes.White; _edit.SelectionBrush = Brushes.White; _edit.SelectionOpacity = 0.25;
        _edit.FontSize = 15; _edit.Padding = new Thickness(22, 6, 22, 6);
        _edit.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _edit.TextChanged += (_, _) =>
        {
            if (_loading) return;
            _cur.Text = _edit.Text;
            _cur.Updated = DateTime.Now;
            _save.Stop(); _save.Start();
            UpdateStatus(false);
        };
        right.Children.Add(_edit);
        _status.Foreground = W(0x70); _status.FontSize = 11.5; _status.Margin = new Thickness(24, 0, 0, 0); _status.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(_status, 1);
        right.Children.Add(_status);
        shell.Children.Add(right);

        _save.Tick += (_, _) => { _save.Stop(); Save(); RenderList(); UpdateStatus(true); };

        Content = ShyStyle.WithShadow(root);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { NewPage(); e.Handled = true; }
        };
        Closing += (_, _) => { _save.Stop(); Save(); };
        Loaded += (_, _) =>
        {
            RenderList();
            Open(_cur);
            UpdatePin();
            ShyStyle.PopIn(root);
        };
    }

    private static FrameworkElement HeadBtn(string icon, string tip, Action act)
    {
        var b = ShyStyle.Glass(icon, tip, act, 32);
        b.Margin = new Thickness(6, 0, 0, 0);
        return b;
    }

    // «поверх всех окон»: включено — значок белый, выключено — серый (как переключатели ShyFlow)
    private void UpdatePin()
    {
        if (_pinIcon != null) _pinIcon.Foreground = Topmost ? Brushes.White : ShyStyle.Dim;
    }

    private static string TitleOf(NotePage p)
    {
        var first = p.Text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return string.IsNullOrEmpty(first) ? Loc.T("Новая страница") : first;
    }

    private static string PreviewOf(NotePage p)
    {
        var lines = p.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Skip(1);
        return string.Join(" ", lines);
    }

    private void RenderList()
    {
        _list.Children.Clear();
        foreach (var p in _pages.OrderByDescending(x => x.Updated))
        {
            bool sel = ReferenceEquals(p, _cur);
            var col = new StackPanel();
            col.Children.Add(new TextBlock { Text = TitleOf(p), Foreground = Brushes.White, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 22, 0) });
            string pre = PreviewOf(p);
            col.Children.Add(new TextBlock
            {
                Text = (p.Updated.Date == DateTime.Today ? p.Updated.ToString("HH:mm") : p.Updated.ToString("d MMM", Loc.Culture)) + (pre.Length > 0 ? "   " + pre : ""),
                Foreground = W(0x80), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0)
            });
            var trash = new SBorder
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = W(0x20), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Opacity = 0, Cursor = Cursors.Hand, ToolTip = Loc.T("Удалить страницу"),
                Child = new VIcon { Text = "trash-2", FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            var g = new Grid();
            g.Children.Add(col);
            g.Children.Add(trash);
            var item = new SBorder
            {
                Child = g, CornerRadius = new CornerRadius(16), Padding = new Thickness(12, 9, 8, 10), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand,
                Background = sel ? ShyStyle.Sel : Brushes.Transparent
            };
            item.MouseEnter += (_, _) => { trash.Opacity = 1; if (!sel) item.Background = W(0x10); };
            item.MouseLeave += (_, _) => { trash.Opacity = 0; if (!sel) item.Background = Brushes.Transparent; };
            var page = p;
            item.MouseLeftButtonUp += (_, _) => Open(page);
            trash.MouseLeftButtonUp += (_, e) => { e.Handled = true; DeletePage(page); };
            _list.Children.Add(item);
        }
    }

    private void Open(NotePage p)
    {
        _save.Stop();
        Save();
        _cur = p;
        _loading = true;
        _edit.Text = p.Text;
        _loading = false;
        _edit.CaretIndex = _edit.Text.Length;
        _edit.Focus();
        RenderList();
        UpdateStatus(true);
    }

    private void NewPage()
    {
        // пустая новая уже есть: просто на неё
        var empty = _pages.FirstOrDefault(p => string.IsNullOrWhiteSpace(p.Text));
        var p = empty ?? new NotePage();
        if (empty == null) _pages.Add(p);
        p.Updated = DateTime.Now;
        Open(p);
    }

    private void DeletePage(NotePage p)
    {
        if (!string.IsNullOrWhiteSpace(p.Text) && !MacDialog.Ask(this, Loc.T("Удалить страницу «") + TitleOf(p) + "»?", Loc.T("Удалить"))) return;
        _pages.Remove(p);
        if (_pages.Count == 0) _pages.Add(new NotePage());
        if (ReferenceEquals(p, _cur)) _cur = _pages.OrderByDescending(x => x.Updated).First();
        Save();
        Open(_cur);
    }

    private void UpdateStatus(bool saved)
    {
        string text = _edit.Text;
        int words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        _status.Text = words + " " + Loc.T("слов") + "  ·  " + text.Length + " " + Loc.T("символов") + (saved ? "  ·  " + Loc.T("сохранено") : "");
    }

    private static List<NotePage> Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<List<NotePage>>(File.ReadAllText(FilePath)) ?? new List<NotePage>();
        }
        catch (Exception ex) { App.Log("Notepad.Load", ex); }
        return new List<NotePage>();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var keep = _pages.Where(p => !string.IsNullOrWhiteSpace(p.Text)).ToList();
            if (keep.Count == 0 && !File.Exists(FilePath)) return;   // пока ничего не написано, файл не нужен
            SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(keep, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { App.Log("Notepad.Save", ex); }
    }
}
