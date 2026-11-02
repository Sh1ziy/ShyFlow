using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Island;

// Шаг 90: переписка с поддержкой (бот @ShyFlow_app). Приложение говорит не с Telegram, а с ботом на Cloudflare
// (support-bot\worker.js, адрес — AppInfo.SupportUrl): токена бота в ShyFlow нет.
//   Send — вопрос уходит всем админам в Telegram; Poll — забрать их ответы (админы отвечают реплаем, бот находит, кому).
// Установка опознаётся случайным id (Client), он и история переписки лежат в %AppData%\Island\support.json.
public sealed class SupportMsg
{
    public bool Mine { get; set; }
    public string Text { get; set; } = "";
    public DateTime At { get; set; } = DateTime.Now;
}

public sealed class SupportData
{
    public string Client { get; set; } = Guid.NewGuid().ToString("N");
    public long After { get; set; }   // id последнего полученного ответа
    public List<SupportMsg> Messages { get; set; } = new();
}

internal static class Support
{
    private static readonly HttpClient Web = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static SupportData? _data;
    private static readonly object Lock = new();

    public static bool Configured => AppInfo.SupportUrl.Length > 0;
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island", "support.json");

    public static SupportData Data
    {
        get
        {
            lock (Lock)
            {
                if (_data != null) return _data;
                try { if (File.Exists(FilePath)) _data = JsonSerializer.Deserialize<SupportData>(File.ReadAllText(FilePath)); }
                catch (Exception ex) { App.LogOnce("Support.Load", ex); }
                _data ??= new SupportData();
                if (_data.Client.Length != 32) _data.Client = Guid.NewGuid().ToString("N");
                return _data;
            }
        }
    }

    private static void Save()
    {
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(Data));
            }
            catch (Exception ex) { App.LogOnce("Support.Save", ex); }
        }
    }

    // ждём ответа: писали за последние две недели (тогда ответы проверяются и в фоне)
    public static bool Awaiting => Configured && Data.Messages.Any(m => m.Mine && m.At > DateTime.Now.AddDays(-14));

    // null — отправлено; иначе текст ошибки для пользователя
    public static async Task<string?> Send(string text)
    {
        if (!Configured) return Loc.T("Поддержка пока не подключена");
        try
        {
            var body = JsonSerializer.Serialize(new { client = Data.Client, text, version = AppInfo.CurrentVersion });
            using var resp = await Web.PostAsync(AppInfo.SupportUrl.TrimEnd('/') + "/api/ticket", new StringContent(body, Encoding.UTF8, "application/json"));
            if ((int)resp.StatusCode == 429) return Loc.T("Слишком часто. Подожди минуту");
            if (!resp.IsSuccessStatusCode) return Loc.T("Не отправилось, попробуй позже");
            lock (Lock) Data.Messages.Add(new SupportMsg { Mine = true, Text = text });
            Save();
            return null;
        }
        catch (Exception ex) { App.LogOnce("Support.Send", ex); return Loc.T("Нет связи с поддержкой. Проверь интернет"); }
    }

    // новые ответы админов (и сразу в историю)
    public static async Task<List<SupportMsg>> Poll()
    {
        var got = new List<SupportMsg>();
        if (!Configured) return got;
        try
        {
            string url = $"{AppInfo.SupportUrl.TrimEnd('/')}/api/replies?client={Data.Client}&after={Data.After}";
            using var doc = JsonDocument.Parse(await Web.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("replies", out var arr)) return got;
            lock (Lock)
            {
                foreach (var r in arr.EnumerateArray())
                {
                    long id = r.GetProperty("id").GetInt64();
                    if (id <= Data.After) continue;
                    var m = new SupportMsg { Mine = false, Text = r.GetProperty("text").GetString() ?? "", At = DateTime.Now };
                    if (r.TryGetProperty("at", out var at) && DateTime.TryParse(at.GetString(), out var t)) m.At = t.ToLocalTime();
                    Data.Messages.Add(m);
                    Data.After = Math.Max(Data.After, id);
                    got.Add(m);
                }
            }
            if (got.Count > 0) Save();
        }
        catch (Exception ex) { App.LogOnce("Support.Poll", ex); }
        return got;
    }
}
