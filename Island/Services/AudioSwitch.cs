using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Island;

// Переключение устройства звука и микрофона по умолчанию. У Windows для этого нет открытого интерфейса,
// поэтому используется тот же недокументированный вызов, что и в системных настройках звука.
// Шаг 80: список всех устройств (раньше только первые 4 — нужный микрофон мог не влезть), понятное имя и подпись,
// какие виртуальные (Steam Streaming, Oculus, Virtual Desktop, VB-Cable — шина ROOT) и какие Bluetooth.
internal static class AudioSwitch
{
    // Name — само устройство («F9», «Realtek(R) Audio»), Sub — что это («Микрофон гарнитуры», «Microphone Array»).
    // IsDefault — по умолчанию для программ, CommDefault — для звонков (у Windows они бывают разными).
    public sealed record Dev(string Id, string Name, string Sub, bool IsDefault, bool CommDefault, bool Virtual, bool Bluetooth);

    private static readonly Guid DeviceKeys = new("a45c254e-df1c-4efd-8020-67d146a850e0");   // PKEY_Device_*
    private static readonly Guid DriverKeys = new("b3f8fa53-0004-438e-9003-51a46e139bfc");   // PKEY_Device_* (драйвер, родитель)
    private static readonly string[] VirtualNames = { "virtual", "cable", "steam streaming", "voicemeeter", "oculus", "nvidia broadcast", "mixed reality" };

    public static List<Dev> List(DataFlow flow)
    {
        var res = new List<Dev>();
        try
        {
            using var en = new MMDeviceEnumerator();
            string def = "", comm = "";
            try { def = en.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID; } catch { }
            try { comm = en.GetDefaultAudioEndpoint(flow, Role.Communications).ID; } catch { }
            foreach (var d in en.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                try
                {
                    string full = d.FriendlyName, iface = "";
                    try { iface = d.DeviceFriendlyName; } catch { }
                    // «Headset Microphone (F9)» -> имя «F9», подпись «Headset Microphone»
                    string sub = iface.Length > 0 && full.EndsWith(" (" + iface + ")") ? full[..^(iface.Length + 3)] : "";
                    string name = iface.Length > 0 && sub.Length > 0 ? iface : full;
                    name = System.Text.RegularExpressions.Regex.Replace(name, @"^\d+-\s*", "");   // «2- VB-Audio…»: номер копии от Windows
                    string bus = Prop(d, DeviceKeys, 24);   // шина: ROOT у программных (виртуальных) устройств
                    string lower = full.ToLowerInvariant();
                    bool virt = bus.Equals("ROOT", StringComparison.OrdinalIgnoreCase) || VirtualNames.Any(lower.Contains);
                    string parent = Prop(d, DriverKeys, 2) + "|" + Prop(d, DriverKeys, 39) + "|" + Prop(d, DriverKeys, 6);
                    bool bt = parent.Contains("BTH", StringComparison.OrdinalIgnoreCase) || parent.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);
                    res.Add(new Dev(d.ID, name, sub, d.ID == def, d.ID == comm, virt, bt));
                }
                catch { /* устройство пропало посреди перечисления */ }
            }
        }
        catch (Exception ex) { App.LogOnce("AudioSwitch.List", ex); }
        // настоящие сверху, виртуальные внизу; в каждой группе выбранное первым, дальше как у Windows
        return res.Select((d, i) => (d, i)).OrderBy(x => x.d.Virtual).ThenBy(x => !x.d.IsDefault).ThenBy(x => x.i).Select(x => x.d).ToList();
    }

    private static string Prop(MMDevice d, Guid fmt, int pid)
    {
        try
        {
            var key = new PropertyKey(fmt, pid);
            var ps = d.Properties;
            return ps.Contains(key) ? ps[key].Value?.ToString() ?? "" : "";
        }
        catch { return ""; }
    }

    public static bool SetDefault(string id)
    {
        try
        {
            var pc = (IPolicyConfig)new PolicyConfigClient();
            // для обычных программ, мультимедиа и связи (звонки)
            int a = pc.SetDefaultEndpoint(id, 0);
            int b = pc.SetDefaultEndpoint(id, 1);
            int c = pc.SetDefaultEndpoint(id, 2);
            return a == 0 || b == 0 || c == 0;
        }
        catch { return false; }
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Bool)] bool def, IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Bool)] bool def, IntPtr defPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Bool)] bool fx, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Bool)] bool fx, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string name, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Bool)] bool visible);
    }
}
