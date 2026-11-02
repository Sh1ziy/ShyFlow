using System.IO;

namespace Island;

// Запись файла «целиком или никак». Сначала пишем во временный файл рядом, потом подменяем им старый.
// Если программа упадёт или выключат свет посреди записи, старый файл останется целым
// (раньше settings.json мог оказаться обрезанным, и все настройки молча сбрасывались).
internal static class SafeFile
{
    public static void WriteAllText(string path, string text)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        // два потока могли писать один .tmp одновременно: файл портился или запись падала
        lock (Gate)
        {
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(f))
            {
                w.Write(text);
                w.Flush();
                f.Flush(true);   // на диск, а не в кэш: иначе при отключении света после подмены файл оказывался пустым
            }
            if (!File.Exists(path)) { File.Move(tmp, path); return; }
            try { File.Replace(tmp, path, null, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Replace не везде работает (сетевые и облачные папки, антивирус держит файл): тогда обычная перезапись
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
        }
    }

    private static readonly object Gate = new();
}
