using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace VolumeMixer.Services;

/// <summary>
/// Raises an event whenever the set of audio endpoints changes — a headset
/// plugged in, a device disabled, the default output switched.
///
/// Without this, anything bound to the device list at startup silently stops
/// covering devices that appear later.
/// </summary>
public sealed class DeviceNotifier : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private bool _registered;

    /// <summary>Fired (on a system thread) when the endpoint layout changed.</summary>
    public event Action? Changed;

    public void Start()
    {
        if (_registered) return;
        try
        {
            _enumerator.RegisterEndpointNotificationCallback(this);
            _registered = true;
        }
        catch { }
    }

    private void Raise()
    {
        try { Changed?.Invoke(); } catch { }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Raise();
    public void OnDeviceAdded(string pwstrDeviceId) => Raise();
    public void OnDeviceRemoved(string deviceId) => Raise();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Raise();

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Property churn is noisy (volume, format) and not a layout change.
    }

    public void Dispose()
    {
        if (_registered)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
            _registered = false;
        }
        try { _enumerator.Dispose(); } catch { }
    }
}
