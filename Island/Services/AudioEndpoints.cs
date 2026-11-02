using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Island;

// Смена устройства по умолчанию снаружи ShyFlow (подключили наушники, выбрали выход в панели Windows).
// Раньше остров продолжал слушать громкость и звук старого устройства: плашки громкости и эквалайзер замирали.
// Колбэки приходят из потока COM; здесь только передаём событие дальше, тяжёлое делает остров у себя.
internal sealed class AudioEndpoints : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _en = new();
    private readonly Action<DataFlow> _changed;
    private bool _on;

    public AudioEndpoints(Action<DataFlow> changed)
    {
        _changed = changed;
        try { _on = _en.RegisterEndpointNotificationCallback(this) == 0; }
        catch (Exception ex) { App.Log("AudioEndpoints", ex); }
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (role == Role.Multimedia) _changed(flow);
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        if (_on) { try { _en.UnregisterEndpointNotificationCallback(this); } catch { } _on = false; }
        try { _en.Dispose(); } catch { }
    }
}
