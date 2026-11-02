using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Island;

// Шаг 90: окно «Поддержка» — переписка с админами ShyFlow через бота @ShyFlow_app (Services\Support.cs).
// Свои сообщения справа (светлые), ответы поддержки слева. Enter — отправить, Shift+Enter — новая строка.
// Пока окно открыто, ответы проверяются раз в 15 секунд; в фоне — остров (Main\SupportPoll.cs).
// Открывается из меню острова (правый клик → «Поддержка») и из уведомления об ответе.
public sealed class SupportWindow : Window
{
    private static SupportWindow? _open;
    public static bool IsOpen => _open != null;

    public static void ShowSupport()
    {
        if (_open != null) { if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal; _open.Activate(); _open.Render(); return; }
        _open = new SupportWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    // остров получил ответы в фоне: если окно открыто, показать сразу
    public static void Refresh() => _open?.Render();

    private static SolidColorBrush W(byte a) => new(Color.FromArgb(a, 255, 255, 255));

    private readonly StackPanel _list = new() { Margin = new Thickness(0, 4, 0, 8) };
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input = new();
    private readonly TextBlock _hint = new();
    private readonly TextBlock _status = new();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _sending;

    private SupportWindow()
    {
        Title = Loc.T("Поддержка");
        Width = 460; Height = 620; MinWidth = 360; MinHeight = 420;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = UiFonts.Text;

        var root = ShyStyle.Shell();
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        shell.RowDefinitions.Add(new RowDefinition());
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Child = shell;
        shell.SizeChanged += (_, e) => shell.Clip = Smooth.Shape(e.NewSize.Width, e.NewSize.Height, new CornerRadius(25));

        // шапка
        var head = new Grid { Background = Brushes.Transparent, Margin = new Thickness(18, 0, 14, 0) };
        head.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
        var titleRow = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 4, 0, 0) };
        titleRow.Children.Add(ShyStyle.Header("message-circle", Loc.T("Поддержка")));
        head.Children.Add(titleRow);
        var close = ShyStyle.Glass("x", Loc.T("Закрыть (Esc)"), Close, 32);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        head.Children.Add(close);
        shell.Children.Add(head);

        // переписка
        var area = new Grid();
        Grid.SetRow(area, 1);
        _scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(18, 0, 18, 0) };
        area.Children.Add(_scroll);
        _hint.Foreground = W(0x80); _hint.FontSize = 13; _hint.TextWrapping = TextWrapping.Wrap; _hint.TextAlignment = TextAlignment.Center;
        _hint.HorizontalAlignment = HorizontalAlignment.Center; _hint.VerticalAlignment = VerticalAlignment.Center; _hint.Margin = new Thickness(40, 0, 40, 30);
        _hint.IsHitTestVisible = false;
        area.Children.Add(_hint);
        shell.Children.Add(area);

        // поле ввода и кнопка
        var bottom = new StackPanel { Margin = new Thickness(16, 6, 16, 16) };
        Grid.SetRow(bottom, 2);
        var field = new Grid();
        field.ColumnDefinitions.Add(new ColumnDefinition());
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new SBorder { CornerRadius = new CornerRadius(18), Background = ShyStyle.Field, Padding = new Thickness(14, 8, 14, 8), MinHeight = 40 };
        _input.Background = Brushes.Transparent; _input.BorderThickness = new Thickness(0); _input.Foreground = Brushes.White;
        _input.CaretBrush = Brushes.White; _input.SelectionBrush = Brushes.White; _input.SelectionOpacity = 0.25;
        _input.FontSize = 14; _input.TextWrapping = TextWrapping.Wrap; _input.AcceptsReturn = true; _input.MaxLength = 2000;
        _input.MaxHeight = 120; _input.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden; _input.VerticalContentAlignment = VerticalAlignment.Center;
        var ph = new TextBlock { Text = Loc.T("Напиши вопрос…"), Foreground = W(0x60), FontSize = 14, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        _input.TextChanged += (_, _) => ph.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift) { e.Handled = true; _ = SendAsync(); }
        };
        box.Child = new Grid { Children = { ph, _input } };
        field.Children.Add(box);
        var send = ShyStyle.Glass("arrow-up", Loc.T("Отправить (Enter)"), () => _ = SendAsync(), 40);
        send.Margin = new Thickness(8, 0, 0, 0);
        send.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(send, 1);
        field.Children.Add(send);
        bottom.Children.Add(field);
        _status.Foreground = W(0x70); _status.FontSize = 11.5; _status.Margin = new Thickness(8, 6, 0, 0); _status.TextWrapping = TextWrapping.Wrap;
        bottom.Children.Add(_status);
        shell.Children.Add(bottom);

        Content = ShyStyle.WithShadow(root);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        _poll.Tick += async (_, _) => { if ((await Support.Poll()).Count > 0) Render(); };
        Closed += (_, _) => _poll.Stop();
        Loaded += async (_, _) =>
        {
            Render();
            ShyStyle.PopIn(root);
            _input.Focus();
            if (!Support.Configured) return;
            _poll.Start();
            if ((await Support.Poll()).Count > 0) Render();
        };
    }

    private void Render()
    {
        _list.Children.Clear();
        var msgs = Support.Data.Messages;
        if (!Support.Configured)
            _hint.Text = Loc.T("Поддержка пока не подключена. Загляни после обновления ShyFlow.");
        else
            _hint.Text = Loc.T("Напиши вопрос, идею или что сломалось — ответят админы ShyFlow. Ответ придёт сюда и уведомлением в остров.");
        _hint.Visibility = msgs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DateTime? day = null;
        foreach (var m in msgs)
        {
            if (day != m.At.Date)
            {
                day = m.At.Date;
                _list.Children.Add(new TextBlock
                {
                    Text = m.At.Date == DateTime.Today ? Loc.T("Сегодня") : m.At.ToString("d MMMM", Loc.Culture),
                    Foreground = W(0x60), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 6)
                });
            }
            _list.Children.Add(Bubble(m));
        }
        _scroll.ScrollToEnd();
    }

    private static FrameworkElement Bubble(SupportMsg m)
    {
        var text = new TextBlock
        {
            Text = m.Text, TextWrapping = TextWrapping.Wrap, FontSize = 14,
            Foreground = m.Mine ? ShyStyle.Ink : Brushes.White
        };
        var time = new TextBlock
        {
            Text = (m.Mine ? "" : Loc.T("Поддержка") + " · ") + m.At.ToString("HH:mm"), FontSize = 10.5, Margin = new Thickness(0, 4, 0, 0),
            Foreground = m.Mine ? new SolidColorBrush(Color.FromArgb(0x80, 0x1C, 0x1C, 0x1E)) : W(0x70),
            HorizontalAlignment = m.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        var b = new SBorder
        {
            CornerRadius = m.Mine ? new CornerRadius(18, 18, 6, 18) : new CornerRadius(18, 18, 18, 6),
            Background = m.Mine ? Brushes.White : ShyStyle.Card, Padding = new Thickness(13, 9, 13, 8),
            MaxWidth = 330, Margin = new Thickness(m.Mine ? 50 : 0, 3, m.Mine ? 0 : 50, 3),
            HorizontalAlignment = m.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Child = new StackPanel { Children = { text, time } }
        };
        return b;
    }

    private async Task SendAsync()
    {
        string text = _input.Text.Trim();
        if (text.Length == 0 || _sending) return;
        _sending = true;
        _status.Text = Loc.T("Отправляю…");
        string? err = await Support.Send(text);
        _sending = false;
        if (err != null) { _status.Text = err; return; }
        _input.Text = "";
        _status.Text = Loc.T("Отправлено. Ответ придёт сюда и уведомлением в остров");
        Render();
        if (!_poll.IsEnabled) _poll.Start();
    }
}
