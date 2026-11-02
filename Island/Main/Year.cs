using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Island;

// Шаг 77: итоги года, как Spotify Wrapped и Apple Replay.
// В декабре (а если декабрь пропустили, то в январе) остров один раз предлагает итоги года, если за год набралось
// хотя бы два часа музыки. Клик — серия из пяти картинок (сколько музыки, треки, исполнители, коллекция, рекорды)
// в папке «Изображения\ShyFlow\Итоги <год>»; первая сразу в буфере, папка открывается. В любой момент — плитка
// «Итоги года» в быстрых действиях.
public partial class MainWindow
{
    private void YearCheck()
    {
        if (!_ready) return;
        var today = DateTime.Today;
        int year = today.Month == 12 ? today.Year : today.Month == 1 ? today.Year - 1 : 0;
        if (year == 0 || _cfg.YearShown == year.ToString()) return;
        _cfg.YearShown = year.ToString();
        _cfg.Save();
        StatsSummary sum;
        try { sum = ListenStats.QueryRange(new DateTime(year, 1, 1), new DateTime(year, 12, 31)); }
        catch (Exception ex) { App.Log("Year.Check", ex); return; }
        if (sum.TotalSec < 2 * 3600) return;
        ShowNotif("ShyFlow", Loc.T("Итоги года готовы", "Your year in music") + ": " + year,
                  Loc.T("Клик: пять картинок для сторис — треки, исполнители, коллекция и рекорды", "Click: five story pictures — tracks, artists, collection and records"));
        _notifClick = () => { ApplyMode(Rest()); MakeYear(year); };
        Sfx.Ui("unlock");
    }

    internal void MakeYear(int year)
    {
        try
        {
            var sum = ListenStats.QueryRange(new DateTime(year, 1, 1), new DateTime(year, 12, 31));
            if (sum.TotalSec < 60) { Tell("chart-column", Loc.T("За этот год музыки ещё нет", "No music this year yet")); return; }
            var cards = CardCollection.All.Where(c => c.Added.Year == year).ToList();
            // месяц, когда слушал больше всего, и сколько дней вообще была музыка
            var byMonth = sum.PerDay.GroupBy(d => d.day.Month).Select(g => (m: g.Key, sec: g.Sum(x => x.sec))).OrderByDescending(x => x.sec).FirstOrDefault();
            string bestMonth = byMonth.sec > 0 ? new DateTime(year, byMonth.m, 1).ToString("MMMM", Loc.Culture) : "—";
            int days = sum.PerDay.Count(d => d.sec >= 60);
            var slides = StoryCards.YearSlides(year, sum, cards, bestMonth, days);

            string dir = Path.Combine(StoryCards.Folder, Loc.T("Итоги ", "Year ") + year);
            Directory.CreateDirectory(dir);
            string first = "";
            for (int i = 0; i < slides.Count; i++)
            {
                string path = Path.Combine(dir, (i + 1).ToString("00") + ".png");
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(slides[i]));
                using (var fs = File.Create(path)) enc.Save(fs);
                if (i == 0) first = path;
            }
            _ignoreClipUntil = DateTime.UtcNow.AddSeconds(2);
            try { Clipboard.SetImage(slides[0]); } catch (Exception ex) { App.LogOnce("Year.Clipboard", ex); }
            Sfx.Ui("unlock");
            ShowNotif("ShyFlow", Loc.T("Итоги года готовы", "Your year in music") + ": " + year,
                      Loc.T("Пять картинок в папке, первая уже в буфере. Клик: открыть папку", "Five pictures in the folder, the first one is copied. Click: open the folder"));
            _notifClick = () => { StoryCards.ShowInFolder(first); ApplyMode(Rest()); };
            StoryCards.ShowInFolder(first);
        }
        catch (Exception ex) { App.Log("Year", ex); Tell("triangle-alert", Loc.T("Не получилось сделать итоги года", "Couldn't make the year in review")); }
    }
}
