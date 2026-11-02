using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Island;

internal enum GameAnswer { None, Yes, Always, No }

// Шаг 97: вопрос игрового режима (Main\GameMode.cs) под островом. Окно не берёт фокус (WS_EX_NOACTIVATE): игра его не теряет,
// даже если нажать кнопку. Без ответа исчезает через 8 секунд.
internal sealed class GamePrompt : Window
{
    private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetLong(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetLong(IntPtr h, int i, IntPtr v);

    private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromSeconds(8) };
    private GameAnswer _answer = GameAnswer.None;

    public GamePrompt(string exe, double centerX, double top, Action<GameAnswer> done)
    {
        Title = "ShyFlow";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = UiFonts.Text;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = centerX - 218; Top = top;   // ширина окна 436; точнее по центру встанет в Loaded

        var col = new StackPanel { Width = 372 };
        col.Children.Add(new TextBlock
        {
            Text = Loc.T("Снизить нагрузку от ShyFlow?", "Reduce ShyFlow's load?"), Foreground = Brushes.White,
            FontSize = ShyType.Headline, FontWeight = FontWeights.SemiBold
        });
        col.Children.Add(new TextBlock
        {
            Text = exe + Loc.T(" требует ресурсов. Ctrl+Alt+G включает и выключает в любой момент.", " is demanding. Ctrl+Alt+G turns it on and off at any time."),
            Foreground = ShyStyle.Gray, FontSize = ShyType.Footnote, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12)
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        void Add(string text, bool primary, GameAnswer a)
        {
            var b = ShyStyle.Pill(text, primary, () => { _answer = a; Close(); });
            b.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(b);
        }
        Add(Loc.T("Нет", "No"), false, GameAnswer.No);
        Add(Loc.T("Всегда", "Always"), false, GameAnswer.Always);
        Add(Loc.T("Да", "Yes"), true, GameAnswer.Yes);
        col.Children.Add(row);

        var body = ShyStyle.Shell(14, 24);
        body.Padding = new Thickness(18, 16, 18, 16);
        body.Child = col;
        Content = ShyStyle.WithShadow(body, 18, 0.5, 3);

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetLong(h, GWL_EXSTYLE, new IntPtr(GetLong(h, GWL_EXSTYLE).ToInt64() | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        };
        Loaded += (_, _) => { Left = centerX - ActualWidth / 2; ShyStyle.PopIn(body); };
        _timeout.Tick += (_, _) => Close();
        _timeout.Start();
        Closed += (_, _) => { _timeout.Stop(); done(_answer); };
    }
}
