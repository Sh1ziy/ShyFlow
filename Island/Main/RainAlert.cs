using System.Windows.Threading;

namespace Island;

// Шаг 80: «через 15 минут дождь». Раз в 10 минут ShyFlow смотрит осадки на 2 часа вперёд (Open-Meteo, по 15 минут;
// город из «Погоды» или примерно по IP) и говорит плашкой:
//   сухо, а в ближайший час пойдёт — «Через ~15 мин дождь» (небольшой дождь, ливень, снег — по силе), клик — прогноз;
//   идёт, и скоро на полчаса и больше перестанет — «Дождь кончится через ~20 мин».
// Об одном и том же дожде говорит один раз. Строка про дождь видна и в раскрытой карточке погоды (LiveActivity.cs).
// Настройки, «Плашки», «Предупреждать о дожде».
public partial class MainWindow
{
    private readonly DispatcherTimer _rainTimer = new() { Interval = TimeSpan.FromMinutes(10) };
    private string _rainLine = "";
    private DateTime _rainToldFor = DateTime.MinValue, _dryToldFor = DateTime.MinValue;
    private bool _rainBusy;

    private void StartRain()
    {
        _rainTimer.Tick += (_, _) => _ = RainCheck();
        _rainTimer.Start();
        // первая проверка — когда всё остальное уже встало
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
        first.Tick += (_, _) => { first.Stop(); _ = RainCheck(); };
        first.Start();
    }

    private async Task RainCheck()
    {
        if (!_cfg.RainAlert) { _rainLine = ""; return; }
        if (_rainBusy) return;
        _rainBusy = true;
        try
        {
            var n = await Weather.Rain(_cfg.WeatherCity ?? "");
            if (n == null) return;
            var d = RainDecide(n, DateTime.UtcNow);
            _rainLine = d.Line;
            if (d.Toast != null && _ready && !_hidden)
            {
                var told = d.Starts ? _rainToldFor : _dryToldFor;
                if (Math.Abs((d.At - told).TotalMinutes) > 45)   // этот дождь (или его конец) уже объявляли
                {
                    if (d.Starts) _rainToldFor = d.At; else _dryToldFor = d.At;
                    ShowToast(d.Starts ? (d.Snow ? "cloud-snow" : "cloud-rain") : "cloud-sun", d.Toast, null, "weather");
                    _osdClick = ShowWeather;
                }
            }
            if (_bubbleKind == "weather") UpdateBubble();
        }
        finally { _rainBusy = false; }
    }

    internal readonly record struct RainSay(string Line, string? Toast, bool Starts, DateTime At, bool Snow);

    private const double WetMm = 0.1;   // миллиметров за 15 минут: меньше — это не дождь, а морось, которой не заметишь

    // что сказать по прогнозу; отдельно от сети и плашек, чтобы можно было проверить на выдуманных прогнозах
    internal static RainSay RainDecide(Weather.Nowcast n, DateTime nowUtc)
    {
        var ahead = n.Slots.Where(s => s.Utc > nowUtc.AddMinutes(-14)).OrderBy(s => s.Utc).ToList();
        var curSlot = ahead.FirstOrDefault(s => s.Utc <= nowUtc);
        bool wetNow = n.NowMm >= WetMm || (curSlot != null && curSlot.Mm >= WetMm);
        int Mins(DateTime t) => Math.Max(5, (int)Math.Round((t - nowUtc).TotalMinutes / 5) * 5);

        if (!wetNow)
        {
            var first = ahead.FirstOrDefault(s => s.Utc > nowUtc && s.Utc <= nowUtc.AddMinutes(65) && s.Mm >= WetMm);
            if (first == null) return new RainSay("", null, true, default, false);
            // сила — по самому сильному куску в первые полчаса дождя
            double peak = ahead.Where(s => s.Utc >= first.Utc && s.Utc < first.Utc.AddMinutes(30)).Max(s => s.Mm);
            string what = What(peak, first.Snow);
            int m = Mins(first.Utc);
            string line = string.Format(Loc.T("{0} через ~{1} мин", "{0} in ~{1} min"), Cap(what), m);
            string toast = string.Format(Loc.T("Через ~{1} мин {0}", "{2} in ~{1} min"), what, m, Cap(what))
                           + (first.Snow ? "" : Loc.T(" — возьми зонт", " — grab an umbrella"));
            return new RainSay(line, toast, true, first.Utc, first.Snow);
        }

        // идёт: когда перестанет хотя бы на полчаса подряд
        for (int i = 0; i < ahead.Count - 1; i++)
        {
            var s = ahead[i];
            if (s.Utc <= nowUtc || s.Utc > nowUtc.AddMinutes(60)) continue;
            if (s.Mm < WetMm && ahead[i + 1].Mm < WetMm)
            {
                bool snow = n.NowSnow || (curSlot?.Snow ?? false);
                string w = Cap(snow ? Loc.T("снег", "snow") : Loc.T("дождь", "rain"));
                int m = Mins(s.Utc);
                string line = string.Format(Loc.T("{0} кончится через ~{1} мин", "{0} stops in ~{1} min"), w, m);
                return new RainSay(line, line, false, s.Utc, snow);
            }
        }
        bool sn = n.NowSnow || (curSlot?.Snow ?? false);
        return new RainSay(Cap(What(Math.Max(n.NowMm, curSlot?.Mm ?? 0), sn)) + Loc.T(" сейчас", " now"), null, false, default, sn);
    }

    private static string What(double mm, bool snow) =>
        snow ? Loc.T("снег", "snow") : mm < 0.5 ? Loc.T("небольшой дождь", "light rain") : mm < 2 ? Loc.T("дождь", "rain") : Loc.T("ливень", "heavy rain");

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Loc.Culture) + s[1..];
}
