using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Island;

// Шаг 90: загрузчик медиа по ссылке (TikTok, Pinterest, Instagram, X, Reddit, VK, Twitch-клипы, SoundCloud...) через yt-dlp.
// YouTube не качаем: ему нужен ещё и JS-движок (Deno), это отдельная большая история.
//
// Инструменты не входят в установщик, а скачиваются при первом использовании в %LocalAppData%\ShyFlow\tools:
//   yt-dlp.exe (~18 МБ, обязательно) — с GitHub yt-dlp, хэш сверяется с SHA2-256SUMS того же релиза; раз в неделю yt-dlp -U
//     (сайты часто меняются, без обновлений через месяц загрузки отвалятся);
//   ffmpeg (по желанию, кнопка в окне) — нужен для лучшего качества там, где видео и звук отдельно (Reddit, Instagram...).
// Без ffmpeg видео берётся одним файлом (видео со звуком), а «только звук» — m4a/mp4 и конвертируется в MP3 силами Windows
// (Services\Converter.cs). Готовое — в «Загрузки\ShyFlow».
internal static class MediaDl
{
    public sealed class Job
    {
        public string Url = "";
        public bool Audio;
        public string Title = "";
        public double Frac;              // 0..1
        public string Speed = "", Eta = "";
        public string Status = "";       // для окна: «Скачиваю», «Готово», ошибка
        public string? File;             // готовый файл
        public bool Done, Failed, Canceled;
        internal Process? Proc;
    }

    public static readonly List<Job> Jobs = new();
    public static event Action? Changed;   // список или прогресс поменялись (зовётся из фона — окно и остров переходят в UI-поток сами)
    private static void Notify() => Changed?.Invoke();

    public static string ToolsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShyFlow", "tools");
    public static string YtDlp => Path.Combine(ToolsDir, "yt-dlp.exe");
    public static string FfmpegDir => Path.Combine(ToolsDir, "ffmpeg");
    public static bool HasYtDlp => File.Exists(YtDlp);
    public static bool HasFfmpeg => File.Exists(Path.Combine(FfmpegDir, "ffmpeg.exe"));
    public static string OutDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ShyFlow");

    private static readonly HttpClient Web = MakeWeb();
    private static HttpClient MakeWeb()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("ShyFlow/" + AppInfo.CurrentVersion);
        return c;
    }

    // ---------- какие ссылки ----------
    private static readonly string[] Sites =
    {
        "tiktok.com", "pinterest.", "pin.it", "instagram.com", "twitter.com", "x.com", "reddit.com", "redd.it", "vk.com", "vk.ru",
        "vkvideo.ru", "twitch.tv", "soundcloud.com", "rutube.ru", "dzen.ru", "ok.ru", "facebook.com", "fb.watch", "vimeo.com",
        "coub.com", "bilibili.com", "threads.net", "threads.com", "tumblr.com", "imgur.com", "likee.video", "snapchat.com", "bsky.app",
    };
    private static readonly string[] YouTube = { "youtube.com", "youtu.be", "youtube-nocookie.com" };

    private static string? HostOf(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https") ? u.Host.ToLowerInvariant() : null;

    private static bool HostIs(string host, string site) =>
        site.EndsWith('.') ? host.Contains(site) : host == site || host.EndsWith("." + site);

    public static bool IsYouTube(string url) => HostOf(url) is { } h && YouTube.Any(s => HostIs(h, s));
    // ссылка с известного сайта с видео (для кнопки «Скачать» у скопированной ссылки)
    public static bool IsMediaLink(string url) => HostOf(url) is { } h && Sites.Any(s => HostIs(h, s));
    public static string SiteName(string url)
    {
        var h = HostOf(url) ?? "";
        if (h.StartsWith("www.")) h = h[4..];
        if (h.StartsWith("m.")) h = h[2..];
        return h;
    }

    // ---------- инструменты ----------
    public static async Task<string?> InstallYtDlp(IProgress<double> progress)
    {
        try
        {
            Directory.CreateDirectory(ToolsDir);
            const string rel = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";
            string sums = await Web.GetStringAsync(rel + "SHA2-256SUMS");
            var line = sums.Split('\n').FirstOrDefault(l => l.TrimEnd().EndsWith(" yt-dlp.exe"));
            string? want = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
            if (want == null) return Loc.T("Не нашёл yt-dlp в релизе, попробуй позже");
            string tmp = YtDlp + ".part";
            await DownloadFile(rel + "yt-dlp.exe", tmp, progress);
            string got;
            await using (var fs = File.OpenRead(tmp)) got = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
            if (got != want) { File.Delete(tmp); return Loc.T("Файл yt-dlp повреждён, попробуй ещё раз"); }
            File.Move(tmp, YtDlp, true);
            Stamp();
            return null;
        }
        catch (Exception ex) { App.Log("MediaDl.Install", ex); return Loc.T("Не скачался yt-dlp: нет интернета или GitHub недоступен"); }
    }

    public static async Task<string?> InstallFfmpeg(IProgress<double> progress)
    {
        string zip = Path.Combine(ToolsDir, "ffmpeg.zip");
        try
        {
            Directory.CreateDirectory(ToolsDir);
            // сборки ffmpeg, которые советуют сами авторы yt-dlp
            const string rel = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/";
            const string name = "ffmpeg-master-latest-win64-gpl.zip";
            string sums = await Web.GetStringAsync(rel + "checksums.sha256");
            string? want = sums.Split('\n').FirstOrDefault(l => l.TrimEnd().EndsWith(" " + name))?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
            if (want == null) return Loc.T("В архиве нет ffmpeg, попробуй позже");
            await DownloadFile(rel + name, zip, progress);
            string got;
            await using (var fs = File.OpenRead(zip)) got = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
            if (got != want) return Loc.T("Файл ffmpeg повреждён, попробуй ещё раз");
            string tmp = FfmpegDir + ".new";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            using (var a = ZipFile.OpenRead(zip))
                foreach (var e in a.Entries.Where(e => e.FullName.Contains("/bin/") && e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                    e.ExtractToFile(Path.Combine(tmp, e.Name), true);
            if (!File.Exists(Path.Combine(tmp, "ffmpeg.exe"))) { Directory.Delete(tmp, true); return Loc.T("В архиве нет ffmpeg, попробуй позже"); }
            if (Directory.Exists(FfmpegDir)) Directory.Delete(FfmpegDir, true);
            Directory.Move(tmp, FfmpegDir);
            return null;
        }
        catch (Exception ex) { App.Log("MediaDl.Ffmpeg", ex); return Loc.T("Не скачался ffmpeg: нет интернета или GitHub недоступен"); }
        finally { try { File.Delete(zip); } catch { } }
    }

    private static async Task DownloadFile(string url, string path, IProgress<double> progress)
    {
        using var resp = await Web.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(path);
        var buf = new byte[81920];
        long done = 0; int n;
        while ((n = await src.ReadAsync(buf)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n));
            done += n;
            if (total > 0) progress.Report((double)done / total);
        }
    }

    // раз в неделю — свежий yt-dlp (он сам сверяет подпись обновления)
    private static string StampPath => Path.Combine(ToolsDir, "yt-dlp.updated");
    private static void Stamp() { try { File.WriteAllText(StampPath, DateTime.UtcNow.ToString("o")); } catch { } }

    private static async Task UpdateIfOld()
    {
        try
        {
            if (File.Exists(StampPath) && DateTime.TryParse(File.ReadAllText(StampPath), out var t) && DateTime.UtcNow - t.ToUniversalTime() < TimeSpan.FromDays(7)) return;
            using var p = Start("-U");
            p.ErrorDataReceived += (_, _) => { };
            p.OutputDataReceived += (_, _) => { };
            p.BeginErrorReadLine();   // непрочитанный вывод заполнил бы канал, и yt-dlp завис бы
            p.BeginOutputReadLine();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch { } return; }
            Stamp();
        }
        catch (Exception ex) { App.LogOnce("MediaDl.Update", ex); }
    }

    // аргументы списком: Windows сама их экранирует. Ссылка с «\"» внутри не может стать отдельным аргументом
    private static Process Start(params string[] args)
    {
        var psi = new ProcessStartInfo(YtDlp)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = ToolsDir
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        return Process.Start(psi)!;
    }

    // ---------- скачивание ----------
    // null — взяли в работу; иначе причина, почему нет
    public static string? Enqueue(string url, bool audio)
    {
        url = url.Trim();
        if (HostOf(url) == null) return Loc.T("Это не ссылка");
        if (IsYouTube(url)) return Loc.T("YouTube пока не поддерживается");
        if (Jobs.Any(j => j.Url == url && !j.Done && !j.Failed && !j.Canceled)) return Loc.T("Уже качается");
        var job = new Job { Url = url, Audio = audio, Title = SiteName(url), Status = Loc.T("В очереди") };
        Jobs.Insert(0, job);
        Notify();
        _ = Run(job);
        return null;
    }

    public static void Cancel(Job j)
    {
        j.Canceled = true;
        j.Status = Loc.T("Отменено");
        try { if (j.Proc is { HasExited: false } p) p.Kill(true); } catch { }
        Notify();
    }

    public static void Clear() { Jobs.RemoveAll(j => j.Done || j.Failed || j.Canceled); Notify(); }

    // что сейчас качается (для пузыря в острове)
    public static Job? Active => Jobs.FirstOrDefault(j => !j.Done && !j.Failed && !j.Canceled);

    private static readonly SemaphoreSlim OneAtATime = new(2);
    private static readonly Regex ProgRx = new(@"^SFPROG\s+([\d.]+)%?\|(.*?)\|(.*)$");

    private static async Task Run(Job job)
    {
        await OneAtATime.WaitAsync();
        try
        {
            if (job.Canceled) return;
            if (!HasYtDlp) { Fail(job, Loc.T("Нужен yt-dlp: открой окно «Загрузки»")); return; }
            job.Status = Loc.T("Готовлюсь…"); Notify();
            await UpdateIfOld();
            Directory.CreateDirectory(OutDir);

            bool ff = HasFfmpeg;
            string fmt = job.Audio
                ? (ff ? "ba/b" : "ba[ext=m4a]/b[ext=mp4]/ba/b")
                : (ff ? "bv*[ext=mp4]+ba[ext=m4a]/bv*+ba/b" : "b[ext=mp4][vcodec!=none][acodec!=none]/b[vcodec!=none][acodec!=none]/b/bv*");
            var a = new List<string>
            {
                "--no-playlist", "--newline", "--no-colors", "--no-mtime", "--progress", "--no-simulate", "--encoding", "utf-8",
                "--progress-template", "download:SFPROG %(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s",
                "--print", "before_dl:SFTITLE %(title).120s",
                "--print", "after_move:SFFILE %(filepath)s",
                "-P", OutDir,
                "-o", "%(title).80B [%(id)s].%(ext)s",
                "-f", fmt
            };
            if (ff)
            {
                a.AddRange(new[] { "--ffmpeg-location", FfmpegDir });
                if (job.Audio) a.AddRange(new[] { "-x", "--audio-format", "mp3", "--audio-quality", "0" });
                else a.AddRange(new[] { "--merge-output-format", "mp4" });
            }
            a.AddRange(new[] { "--", job.Url });

            using var p = Start(a.ToArray());
            job.Proc = p;
            job.Status = Loc.T("Скачиваю…"); Notify();
            var err = new StringBuilder();
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
            p.BeginErrorReadLine();
            DateTime lastNotify = DateTime.MinValue;
            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync()) != null)
            {
                line = line.Trim();
                if (line.StartsWith("SFTITLE ")) { job.Title = line[8..].Trim(); Notify(); }
                else if (line.StartsWith("SFFILE ")) job.File = line[7..].Trim();
                else if (ProgRx.Match(line) is { Success: true } m && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct))
                {
                    job.Frac = Math.Clamp(pct / 100, 0, 1);
                    job.Speed = m.Groups[2].Value.Trim(); job.Eta = m.Groups[3].Value.Trim();
                    if (job.Speed == "NA" || job.Speed.Contains("Unknown")) job.Speed = "";
                    if (job.Eta == "NA" || job.Eta.Contains("Unknown")) job.Eta = "";
                    if ((DateTime.UtcNow - lastNotify).TotalMilliseconds > 250) { lastNotify = DateTime.UtcNow; Notify(); }
                }
            }
            await p.WaitForExitAsync();
            if (job.Canceled) { TryDeleteParts(job); return; }
            if (p.ExitCode != 0 || job.File == null || !File.Exists(job.File))
            {
                string e; lock (err) e = err.ToString();
                Fail(job, Explain(e));
                return;
            }

            // без ffmpeg «только звук» пришёл как m4a или mp4 — в MP3 силами Windows
            if (job.Audio && !ff && !job.File.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                job.Status = Loc.T("Делаю MP3…"); job.Frac = 0; Notify();
                try
                {
                    string src = job.File;
                    job.File = await Converter.Convert(src, "MP3", new Progress<double>(f => { job.Frac = f; Notify(); }));
                    try { File.Delete(src); } catch { }
                }
                catch (Exception ex) { App.LogOnce("MediaDl.Mp3", ex); }   // не вышло — останется исходный файл со звуком
            }
            job.Frac = 1; job.Done = true; job.Status = Loc.T("Готово");
            Notify();
        }
        catch (Exception ex) { App.Log("MediaDl.Run", ex); Fail(job, Loc.T("Не получилось скачать")); }
        finally { job.Proc = null; OneAtATime.Release(); }
    }

    private static void Fail(Job j, string why) { j.Failed = true; j.Status = why; Notify(); }

    private static void TryDeleteParts(Job j)
    {
        if (Active != null) return;   // качаются две загрузки сразу: отмена одной удалила бы куски второй
        try { foreach (var f in Directory.GetFiles(OutDir, "*.part")) if (DateTime.Now - File.GetLastWriteTime(f) < TimeSpan.FromMinutes(30)) File.Delete(f); } catch { }
    }

    // ошибки yt-dlp по-человечески
    private static string Explain(string e)
    {
        string s = e.ToLowerInvariant();
        if (s.Contains("unsupported url")) return Loc.T("Этот сайт не поддерживается");
        if (s.Contains("login") || s.Contains("cookies") || s.Contains("private") || s.Contains("sign in")) return Loc.T("Нужен вход в аккаунт или пост закрытый");
        if (s.Contains("no video formats") || s.Contains("requested format is not available")) return Loc.T("Тут нет видео (картинка или пост без медиа)");
        if (s.Contains("http error 404") || s.Contains("not found")) return Loc.T("Не найдено: пост удалён или ссылка неверная");
        if (s.Contains("unable to download") || s.Contains("getaddrinfo") || s.Contains("timed out")) return Loc.T("Нет связи с сайтом");
        var last = e.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("ERROR:"));
        return last != null ? last[6..].Trim() : Loc.T("Не получилось скачать");
    }

    // превью для окна: название, сайт, длительность, обложка (yt-dlp -J), без скачивания
    public sealed record Info(string Title, string Site, double Duration, string? Thumb);

    public static async Task<Info?> Probe(string url)
    {
        if (!HasYtDlp || IsYouTube(url) || HostOf(url) == null) return null;
        try
        {
            using var p = Start("-J", "--no-playlist", "--no-warnings", "--encoding", "utf-8", "--", url.Trim());
            p.ErrorDataReceived += (_, _) => { };
            p.BeginErrorReadLine();
            // таймер охватывает и чтение: раньше зависший сайт держал yt-dlp и это окно бесконечно
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            string json;
            try
            {
                json = await p.StandardOutput.ReadToEndAsync(cts.Token);
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException) { try { p.Kill(true); } catch { } return null; }
            if (p.ExitCode != 0 || json.Length == 0) return null;
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            double dur = r.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
            string thumb = S("thumbnail");
            return new Info(S("title"), S("extractor_key"), dur, thumb.Length > 0 ? thumb : null);
        }
        catch (Exception ex) { App.LogOnce("MediaDl.Probe", ex); return null; }
    }
}
