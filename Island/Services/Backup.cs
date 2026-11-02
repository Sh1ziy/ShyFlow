using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Island;

// Шаг 79: резервная копия всего ShyFlow одним файлом (Настройки, «Система», «Данные»).
// Внутри zip — папка %AppData%\Island: настройки, коллекция карточек, статистика с обложками, блокнот, заметки, карман,
// календарь, свой фон и стикер. Кэш текстов песен и журналы не берём (они большие и восстанавливаются сами).
// Восстановление: файл копии запоминается (restore.pending), ShyFlow перезапускается, и новый процесс разворачивает копию
// в самом начале, до загрузки настроек (App.OnStartup). Иначе старый процесс при выходе записал бы поверх свои файлы.
// Перед восстановлением текущие данные сами сохраняются в backups\перед-восстановлением-….zip.
internal static class Backup
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island");
    private static string Pending => Path.Combine(Dir, "restore.pending");
    private static readonly string[] SkipDirs = { "lyrics", "backups" };
    private static readonly string[] SkipFiles = { "crash.log", "crash.old.log", "uia.log", "restore.pending" };

    public static string DefaultName => "ShyFlow-копия-" + DateTime.Now.ToString("yyyy-MM-dd") + ".zip";

    public static void Create(string zipPath)
    {
        string tmp = zipPath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var f in Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(Dir, f);
                string top = rel.Split(Path.DirectorySeparatorChar)[0];
                if (SkipDirs.Contains(top, StringComparer.OrdinalIgnoreCase) || SkipFiles.Contains(rel, StringComparer.OrdinalIgnoreCase)) continue;
                if (rel.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    // файл может быть открыт островом (статистика пишется раз в минуту): читаем с общим доступом
                    using var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var e = zip.CreateEntry(rel.Replace('\\', '/'), CompressionLevel.Optimal);
                    e.LastWriteTime = File.GetLastWriteTime(f);
                    using var dst = e.Open();
                    src.CopyTo(dst);
                }
                catch (Exception ex) { App.LogOnce("Backup.File", ex); }
            }
            var info = zip.CreateEntry("shyflow-backup.json");
            using var w = new StreamWriter(info.Open());
            w.Write(JsonSerializer.Serialize(new { App = "ShyFlow", Version = AppInfo.CurrentVersion, Date = DateTime.Now }));
        }
        if (File.Exists(zipPath)) File.Delete(zipPath);
        File.Move(tmp, zipPath);
    }

    // проверка, что это копия ShyFlow, а не любой zip
    public static bool IsBackup(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return zip.GetEntry("shyflow-backup.json") != null || zip.GetEntry("settings.json") != null;
        }
        catch { return false; }
    }

    // запомнить и перезапустить ShyFlow; сама копия разворачивается в ApplyPending нового процесса
    public static void RestoreAndRestart(string zipPath)
    {
        File.WriteAllText(Pending, zipPath);
        // --restart: новая копия ждёт, пока эта закроется, дольше обычного (App.OnStartup); иначе могла тихо выйти
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = false }); }
        catch (Exception ex) { App.Log("Backup.Restart", ex); }
        System.Windows.Application.Current.Shutdown();
    }

    // вызывается из App.OnStartup, когда старый процесс уже вышел (одна копия ShyFlow)
    public static void ApplyPending()
    {
        try
        {
            if (!File.Exists(Pending)) return;
            string zipPath = File.ReadAllText(Pending).Trim();
            File.Delete(Pending);
            if (!File.Exists(zipPath) || !IsBackup(zipPath)) return;
            // сначала спасаем текущее
            string safe = Path.Combine(Dir, "backups");
            Directory.CreateDirectory(safe);
            Create(Path.Combine(safe, "перед-восстановлением-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".zip"));
            string root = Path.GetFullPath(Dir) + Path.DirectorySeparatorChar;
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var e in zip.Entries)
            {
                if (e.FullName.EndsWith("/") || e.Name == "shyflow-backup.json" || e.Name == "restore.pending") continue;   // чужой restore.pending запустил бы ещё одно восстановление
                string target = Path.GetFullPath(Path.Combine(Dir, e.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // путь из архива не выходит за папку ShyFlow
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, true);
            }
        }
        catch (Exception ex) { App.Log("Backup.Restore", ex); }
    }
}
