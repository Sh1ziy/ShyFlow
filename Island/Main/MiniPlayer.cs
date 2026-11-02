using System.Windows;

namespace Island;

// Шаг 77: мини-плеер (Ui\MiniPlayerWindow.cs): открыть из меню острова и запомнить, где его оставили.
public partial class MainWindow
{
    private void Mini_Click(object s, RoutedEventArgs e) => MiniPlayerWindow.Toggle(this);

    internal (double x, double y) MiniPos
    {
        get => (_cfg.MiniX, _cfg.MiniY);
        set { _cfg.MiniX = value.x; _cfg.MiniY = value.y; _cfg.Save(); }
    }
}
