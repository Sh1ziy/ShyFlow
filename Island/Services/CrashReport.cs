using System.Diagnostics;
using System.IO;
using System.Text;

namespace Island;

// Шаг 79: если ShyFlow в прошлый раз упал, он сам это замечает при запуске и показывает, что случилось.
// Такие падения (в коде Windows, драйвера или WPF) не попадают в crash.log: процесс закрывается мгновенно.
// Откуда узнаём: дамп Windows (%LocalAppData%\CrashDumps\ShyFlow.exe.*.dmp, если сбор дампов включён) или запись
// «Application Error» в журнале Windows. Из дампа читаем код ошибки, модуль, где упало, и модули на стеке
// (тем же способом в шаге 79 нашли падение WPF на перекраске кистей). Отчёт можно скопировать или сразу открыть на GitHub.
internal sealed class CrashInfo
{
    public DateTime At;
    public string Code = "", Module = "", Version = "", DumpPath = "";
    public List<string> Stack = new();

    public string Human => Module.ToLowerInvariant() switch
    {
        var m when m.StartsWith("wpfgfx") || Stack.Any(s => s.StartsWith("wpfgfx", StringComparison.OrdinalIgnoreCase)) =>
            Loc.T("Сбой отрисовки окна (WPF)", "A window rendering failure (WPF)"),
        var m when m.StartsWith("nvwgf") || m.StartsWith("nvd3d") || m.StartsWith("atidxx") || m.StartsWith("igd") || m.StartsWith("amd") =>
            Loc.T("Сбой видеодрайвера", "A graphics driver failure"),
        var m when m.StartsWith("audioses") || m.StartsWith("mmdevapi") =>
            Loc.T("Сбой звука Windows", "A Windows audio failure"),
        var m when m.StartsWith("coreclr") || m.StartsWith("clrjit") =>
            Loc.T("Сбой .NET", "A .NET failure"),
        _ => Loc.T("Сбой внутри Windows", "A failure inside Windows")
    };

    public string Text()
    {
        var sb = new StringBuilder();
        sb.AppendLine("ShyFlow " + Version + " — " + Loc.T("вылет", "crash") + " " + At.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine(Human);
        if (Code.Length > 0) sb.AppendLine(Loc.T("Код: ", "Code: ") + Code);
        if (Module.Length > 0) sb.AppendLine(Loc.T("Где: ", "Where: ") + Module);
        if (Stack.Count > 0) sb.AppendLine(Loc.T("Стек: ", "Stack: ") + string.Join(" ← ", Stack));
        sb.AppendLine("Windows " + Environment.OSVersion.Version + ", .NET " + Environment.Version);
        return sb.ToString();
    }
}

internal static class CrashReport
{
    private static string DumpDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");

    // самый свежий вылет новее since; null — вылетов не было
    public static CrashInfo? FindSince(DateTime since)
    {
        try
        {
            if (Directory.Exists(DumpDir))
            {
                var dump = new DirectoryInfo(DumpDir).GetFiles("ShyFlow.exe.*.dmp")
                    .Where(f => f.LastWriteTime > since).OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (dump != null)
                {
                    var info = ParseDump(dump.FullName) ?? new CrashInfo();
                    info.At = dump.LastWriteTime;
                    info.DumpPath = dump.FullName;
                    if (info.Version.Length == 0) info.Version = AppInfo.CurrentVersion;
                    return info;
                }
            }
        }
        catch (Exception ex) { App.LogOnce("Crash.Dump", ex); }
        return FromEventLog(since);
    }

    // журнал Windows: «Application Error» по ShyFlow.exe (дампы собираются не у всех)
    private static CrashInfo? FromEventLog(DateTime since)
    {
        try
        {
            long ms = (long)Math.Max(1000, (DateTime.Now - since).TotalMilliseconds);
            // XML, а не текст: текст журнала переведён на язык Windows и в другой кодировке. В XML поля по порядку:
            // 0 программа, 1 её версия, 3 модуль, 6 код исключения, 7 смещение
            var psi = new ProcessStartInfo("wevtutil.exe",
                $"qe Application /q:\"*[System[Provider[@Name='Application Error'] and TimeCreated[timediff(@SystemTime) <= {ms}]]]\" /f:xml /c:20 /rd:true")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
            using var p = Process.Start(psi)!;
            string all = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            var doc = System.Xml.Linq.XDocument.Parse("<r>" + all + "</r>");
            foreach (var ev in doc.Root!.Elements().Where(e => e.Name.LocalName == "Event"))
            {
                var data = ev.Descendants().Where(e => e.Name.LocalName == "Data").Select(e => e.Value).ToList();
                if (data.Count < 8 || !data[0].Equals("ShyFlow.exe", StringComparison.OrdinalIgnoreCase)) continue;
                var info = new CrashInfo { Version = data[1], Module = data[3] + "+" + data[7].TrimStart('0', 'x'), Code = "0x" + data[6].ToUpperInvariant() };
                var tc = ev.Descendants().FirstOrDefault(e => e.Name.LocalName == "TimeCreated")?.Attribute("SystemTime")?.Value;
                info.At = DateTime.TryParse(tc, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var utc) ? utc.ToLocalTime() : DateTime.Now;
                return info;
            }
        }
        catch (Exception ex) { App.LogOnce("Crash.Events", ex); }
        return null;
    }

    // ---------- минидамп: заголовок, модули, исключение, стек потока, где упало ----------
    private static CrashInfo? ParseDump(string path)
    {
        byte[] d = File.ReadAllBytes(path);
        uint U32(long o) => BitConverter.ToUInt32(d, (int)o);
        ulong U64(long o) => BitConverter.ToUInt64(d, (int)o);
        if (d.Length < 32 || U32(0) != 0x504D444D) return null;   // «MDMP»
        uint n = U32(8), dir = U32(12);
        var streams = new Dictionary<uint, uint>();
        for (int i = 0; i < n; i++) streams[U32(dir + i * 12)] = U32(dir + i * 12 + 8);
        if (!streams.TryGetValue(6, out uint exRva) || !streams.TryGetValue(4, out uint modRva)) return null;

        var mods = new List<(ulong b, ulong s, string name)>();
        string ver = "";
        uint mc = U32(modRva);
        for (int i = 0; i < mc; i++)
        {
            long m = modRva + 4 + i * 108;
            uint nameRva = U32(m + 20);
            string name = Path.GetFileName(Encoding.Unicode.GetString(d, (int)nameRva + 4, (int)U32(nameRva)));
            mods.Add((U64(m), U32(m + 8), name));
            if (name.Equals("ShyFlow.exe", StringComparison.OrdinalIgnoreCase))
            {
                uint ms = U32(m + 32), ls = U32(m + 36);   // VS_FIXEDFILEINFO: FileVersionMS и LS
                ver = $"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}";
            }
        }
        string Where(ulong a)
        {
            foreach (var (b, s, nm) in mods) if (a >= b && a < b + s) return nm + "+0x" + (a - b).ToString("x");
            return "";
        }

        var info = new CrashInfo();
        info.Code = "0x" + U32(exRva + 8).ToString("X8");
        info.Module = Where(U64(exRva + 24));
        info.Version = ver;

        // память: Memory64List
        var ranges = new List<(ulong st, ulong sz, long fo)>();
        if (streams.TryGetValue(9, out uint m64))
        {
            ulong cnt = U64(m64);
            long off = (long)U64(m64 + 8);
            for (ulong i = 0; i < cnt; i++)
            {
                long e = m64 + 16 + (long)i * 16;
                ranges.Add((U64(e), U64(e + 8), off));
                off += (long)U64(e + 8);
            }
        }
        // и MemoryList: в нём лежат стеки потоков у обычных (не полных) дампов
        if (streams.TryGetValue(5, out uint m32))
        {
            uint cnt = U32(m32);
            for (int i = 0; i < cnt; i++)
            {
                long e = m32 + 4 + i * 16;
                ranges.Add((U64(e), U32(e + 8), U32(e + 12)));
            }
        }
        bool Read(ulong a, out ulong v)
        {
            foreach (var (st, sz, fo) in ranges)
                if (a >= st && a + 8 <= st + sz && fo + (long)(a - st) + 8 <= d.Length) { v = U64(fo + (long)(a - st)); return true; }
            v = 0; return false;
        }
        // стек потока, где упало: адреса, попадающие в модули (без символов — имена библиотек)
        uint tid = U32(exRva);
        uint ctxRva = U32(exRva + 8 + 152 + 4);
        if (streams.TryGetValue(3, out uint tl))
        {
            uint tc = U32(tl);
            for (int i = 0; i < tc; i++)
            {
                long t = tl + 4 + i * 48;
                if (U32(t) != tid) continue;
                ulong stEnd = U64(t + 24) + U32(t + 32);
                ulong rsp = U64(ctxRva + 0x98);
                for (ulong a = rsp; a < stEnd && info.Stack.Count < 8; a += 8)
                {
                    if (!Read(a, out ulong v)) continue;
                    string w = Where(v);
                    if (w.Length == 0) continue;
                    string mod = w.Split('+')[0];
                    if (info.Stack.Count == 0 || !info.Stack[^1].StartsWith(mod)) info.Stack.Add(w);
                }
            }
        }
        return info;
    }
}
