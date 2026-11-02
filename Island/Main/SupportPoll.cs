using System.Windows;
using System.Windows.Threading;

namespace Island;

// Шаг 90: ответы поддержки в фоне (Services\Support.cs). Пока ждём ответа (писали в поддержку за последние две недели),
// раз в 3 минуты спрашиваем бота; пришёл ответ — уведомление в острове, клик открывает окно «Поддержка».
// Окно открыто — оно само проверяет каждые 15 секунд, здесь только обновляем его.
public partial class MainWindow
{
    private readonly DispatcherTimer _supportTimer = new() { Interval = TimeSpan.FromMinutes(3) };

    private void StartSupportPoll()
    {
        _supportTimer.Tick += async (_, _) =>
        {
            if (!Support.Awaiting || SupportWindow.IsOpen) return;
            var got = await Support.Poll();
            if (got.Count == 0) return;
            var last = got[^1];
            ShowNotif(Loc.T("Поддержка ShyFlow"), last.Text, Loc.T("Клик: открыть переписку"));
            _notifClick = SupportWindow.ShowSupport;
        };
        _supportTimer.Start();
    }

    private void Support_Click(object sender, RoutedEventArgs e) => SupportWindow.ShowSupport();
}
