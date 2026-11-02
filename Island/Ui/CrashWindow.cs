using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Island;

// Шаг 79: «ShyFlow закрылся в прошлый раз» — что случилось и что с этим сделать (Services\CrashReport.cs).
// Скопировать отчёт (с хвостом crash.log), сообщить на GitHub (откроется страница с готовым текстом), показать дамп в папке.
internal sealed class CrashWindow : Window
{
    private static CrashWindow? _open;

    public static void ShowFor(CrashInfo info)
    {
        _open?.Close();
        _open = new CrashWindow(info);
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    private CrashWindow(CrashInfo info)
    {
        Title = Loc.T("ShyFlow закрылся", "ShyFlow closed");
        Width = 560; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        FontFamily = UiFonts.Text;

        var root = ShyStyle.Shell();
        var col = new StackPanel { Margin = new Thickness(24, 20, 24, 22) };
        root.Child = col;

        var head = new Grid { Background = Brushes.Transparent };
        head.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
        head.Children.Add(ShyStyle.Header("triangle-alert", Loc.T("ShyFlow закрылся", "ShyFlow closed")));
        var x = ShyStyle.Glass("x", Loc.T("Закрыть (Esc)", "Close (Esc)"), Close, 30);
        x.HorizontalAlignment = HorizontalAlignment.Right;
        head.Children.Add(x);
        col.Children.Add(head);

        col.Children.Add(new TextBlock
        {
            Text = Loc.T("В прошлый раз ShyFlow неожиданно закрылся ", "Last time ShyFlow closed unexpectedly ") + info.At.ToString("d MMMM, HH:mm", Loc.Culture) + ". " + info.Human + ".",
            Foreground = Brushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 16, 0, 0)
        });
        col.Children.Add(new TextBlock
        {
            Text = Loc.T("Это уже записано. Если пришлёшь отчёт, будет проще найти причину и исправить. Ничего не отправляется само.",
                         "It's already recorded. Sending the report makes it easier to find and fix the cause. Nothing is sent automatically."),
            Foreground = ShyStyle.Gray, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 6, 0, 0)
        });
        col.Children.Add(ShyStyle.CardBox(new TextBlock
        {
            Text = info.Text().TrimEnd(), Foreground = ShyStyle.B(0xC8, 255, 255, 255), FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas")
        }, 14, new Thickness(14, 11, 14, 12)));
        ((FrameworkElement)col.Children[^1]).Margin = new Thickness(0, 14, 0, 0);

        var btns = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        SBorder status = null!;
        var copy = ShyStyle.Pill(Loc.T("Скопировать отчёт", "Copy report"), true, () =>
        {
            try { Clipboard.SetText(FullReport(info)); ((TextBlock)((StackPanel)status.Child).Children[^1]).Text = Loc.T("Скопировано ✓", "Copied ✓"); }
            catch (Exception ex) { App.LogOnce("Crash.Copy", ex); }
        }, "copy");
        status = copy;
        copy.Margin = new Thickness(0, 0, 8, 0);
        btns.Children.Add(copy);
        var gh = ShyStyle.Pill(Loc.T("Сообщить на GitHub", "Report on GitHub"), false, () => OpenIssue(info), "external-link");
        gh.Margin = new Thickness(0, 0, 8, 0);
        btns.Children.Add(gh);
        if (info.DumpPath.Length > 0 && File.Exists(info.DumpPath))
            btns.Children.Add(ShyStyle.Pill(Loc.T("Дамп в папке", "Show dump"), false, () =>
            {
                try { Process.Start("explorer.exe", "/select,\"" + info.DumpPath + "\""); } catch (Exception ex) { App.LogOnce("Crash.Show", ex); }
            }, "folder-open"));
        col.Children.Add(btns);

        Content = ShyStyle.WithShadow(root);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => ShyStyle.PopIn(root);
    }

    // отчёт целиком: что и где + последние строки crash.log
    private static string FullReport(CrashInfo info)
    {
        var text = info.Text();
        try
        {
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "crash.log");
            if (File.Exists(log))
            {
                var tail = File.ReadLines(log).Reverse().Take(25).Reverse();
                text += "\ncrash.log:\n" + string.Join("\n", tail);
            }
        }
        catch { }
        return text;
    }

    private static void OpenIssue(CrashInfo info)
    {
        string title = Uri.EscapeDataString("Crash: " + info.Human + " (" + info.Version + ")");
        string body = Uri.EscapeDataString("```\n" + info.Text() + "```\n");
        try { Process.Start(new ProcessStartInfo("https://github.com/Sh1ziy/ShyFlow/issues/new?title=" + title + "&body=" + body) { UseShellExecute = true }); }
        catch (Exception ex) { App.LogOnce("Crash.Issue", ex); }
    }
}
