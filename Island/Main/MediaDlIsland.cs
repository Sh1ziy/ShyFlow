using System.Windows;
using System.Windows.Media;

namespace Island;

// Шаг 90: загрузчик медиа в острове (сам загрузчик — Services\MediaDl.cs, окно — Ui\DownloaderWindow.cs).
//   Пока качается — активность в пузыре: кольцо с процентами, в карточке название, скорость и «Отменить».
//   Готово — плашка «Скачано», файл сразу в кармане; не вышло — плашка с причиной.
//   Скопировал ссылку на видео (TikTok, Pinterest...) — на плашке «клик — скачать».
//   Ctrl+Alt+J — скачать выделенную ссылку; ссылку можно бросить на остров.
public partial class MainWindow
{
    private static readonly Color MediaC = Color.FromRgb(0xFF, 0x37, 0x5F);
    private readonly HashSet<MediaDl.Job> _mediaSeen = new();

    private void InitMediaDl()
    {
        MediaDl.Changed += () => Dispatcher.BeginInvoke(new Action(OnMediaChanged));
    }

    private void OnMediaChanged()
    {
        foreach (var j in MediaDl.Jobs.ToList())
        {
            if (!(j.Done || j.Failed) || !_mediaSeen.Add(j)) continue;
            if (j.Done && j.File != null)
            {
                PocketAddFile(j.File);
                ShowToast("check", Loc.T("Скачано") + ": " + j.Title, null, "dl");
                var f = j.File;
                _osdClick = () => { ApplyMode(Rest()); try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + f + "\"") { UseShellExecute = true }); } catch { } };
            }
            else if (j.Failed)
            {
                ShowToast("triangle-alert", Loc.T("Не скачалось") + ": " + j.Status, null, "key");
                _osdClick = () => { ApplyMode(Rest()); DownloaderWindow.ShowDownloader(); };
            }
        }
        UpdateBubble();
    }

    // активность в пузыре
    private (string kind, string text, double frac, Color col, string icon, bool paused)? MediaDlState()
    {
        if (MediaDl.Active is not { } j) return null;
        return ("media", (int)Math.Round(j.Frac * 100) + "%", j.Frac, MediaC, j.Audio ? "music" : "download", false);
    }

    // скачать ссылку: yt-dlp уже стоит — сразу в очередь, иначе окно (там кнопка установки)
    private void StartMediaDl(string url)
    {
        if (!MediaDl.HasYtDlp) { DownloaderWindow.ShowDownloader(url); return; }
        string? err = MediaDl.Enqueue(url, false);
        if (err != null) { ShowToast("triangle-alert", err, null, "key"); return; }
        ShowToast("download", Loc.T("Скачиваю") + ": " + MediaDl.SiteName(url), null, "dl");
        _osdClick = () => { ApplyMode(Rest()); DownloaderWindow.ShowDownloader(); };
    }

    // Ctrl+Alt+J: выделенная ссылка (как Ctrl+Alt+Q у переводчика, Translator.cs)
    private async void QuickMediaDl()
    {
        try
        {
            var fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out uint pid);
            if (pid == OwnPid) { DownloaderWindow.ShowDownloader(); return; }
            for (int i = 0; i < 40 && ((GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0x11) & 0x8000) != 0 || (GetAsyncKeyState(0x4A) & 0x8000) != 0); i++)
                await Task.Delay(25);
            string before = SafeClipText();
            uint seq = GetClipboardSequenceNumber();
            _ignoreClipUntil = DateTime.UtcNow.AddSeconds(2);
            SendCombo(0x43);   // Ctrl+C
            string? sel = null;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(25);
                if (GetClipboardSequenceNumber() != seq) { await Task.Delay(30); sel = SafeClipText(); break; }
            }
            if (sel != null && before.Length > 0) { _ignoreClipUntil = DateTime.UtcNow.AddSeconds(1); CopyBack(before); }
            sel = sel?.Trim();
            if (string.IsNullOrEmpty(sel) || !Uri.TryCreate(sel, UriKind.Absolute, out _)) { DownloaderWindow.ShowDownloader(); return; }
            StartMediaDl(sel);
        }
        catch (Exception ex) { App.LogOnce("QuickMediaDl", ex); DownloaderWindow.ShowDownloader(); }
    }

    private void Downloader_Click(object sender, RoutedEventArgs e) => DownloaderWindow.ShowDownloader();
}
