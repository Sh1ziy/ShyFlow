using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Island;

// Шаг 102: скорость интернета для пузыря (Настройки, «Пузырь и значки», «Что в нём»: «Интернет»).
// Считаем байты всех настоящих адаптеров со шлюзом (виртуальные и VPN пропускаем: их трафик идёт ещё и через настоящий).
// Опрос Windows занимает миллисекунды, поэтому он в фоне и только пока пузырь это показывает: Sample зовётся из пузыря раз в секунду.
internal static class NetSpeed
{
    public static double DownPerSec { get; private set; }
    public static double UpPerSec { get; private set; }
    public static bool Online { get; private set; } = true;
    public static bool Wifi { get; private set; }
    public static string Adapter { get; private set; } = "";

    private static long _down, _up;
    private static readonly Stopwatch Clock = new();
    private static int _busy;

    public static void Sample()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _ = Task.Run(() =>
        {
            try { Read(); }
            catch (Exception ex) { App.LogOnce("NetSpeed", ex); }
            finally { Volatile.Write(ref _busy, 0); }
        });
    }

    private static void Read()
    {
        long down = 0, up = 0;
        bool online = false, wifi = false;
        string name = "";
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            if (n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
                n.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase)) continue;
            if (!n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any))) continue;
            var s = n.GetIPStatistics();
            down += s.BytesReceived;
            up += s.BytesSent;
            if (!online || n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            {
                wifi = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                name = n.Name;
            }
            online = true;
        }
        double sec = Clock.Elapsed.TotalSeconds;
        // счётчики сбрасываются при смене адаптера: отрицательная разница — это не скорость
        if (Clock.IsRunning && sec > 0.2 && down >= _down && up >= _up)
        {
            DownPerSec = (down - _down) / sec;
            UpPerSec = (up - _up) / sec;
        }
        else if (Clock.IsRunning && sec > 0.2) DownPerSec = UpPerSec = 0;
        _down = down; _up = up;
        Clock.Restart();
        Online = online; Wifi = wifi; Adapter = name;
    }

    // «840 КБ/с», «2.4 МБ/с»
    public static string Format(double bytesPerSec)
    {
        double kb = bytesPerSec / 1024;
        if (kb < 1000) return Math.Round(kb) + " " + Loc.T("КБ/с", "KB/s");
        double mb = kb / 1024;
        return mb.ToString(mb < 100 ? "0.0" : "0") + " " + Loc.T("МБ/с", "MB/s");
    }
}
