using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace VolumeMixer.Services;

/// <summary>
/// Watches for *newly created* audio sessions and restores the volume fichy
/// remembers for that program.
///
/// Only sessions whose instance identifier hasn't been seen before are touched,
/// so changing a volume elsewhere (the Windows mixer, the app's own slider) is
/// never fought over — only a fresh session, which always starts at 100%, gets
/// corrected.
/// </summary>
public sealed class VolumeWatcher : IDisposable
{
    private readonly System.Timers.Timer _timer;

    /// <summary>Session instance id → the volume and mute we last observed on it.</summary>
    private readonly Dictionary<string, (float Volume, bool Muted)> _seen = new(StringComparer.Ordinal);
    private bool _primed;

    /// <summary>Devices we hold open purely to receive their session-created events.</summary>
    private readonly List<MMDevice> _subscribed = new();
    private readonly object _subLock = new();

    private volatile bool _stopped;

    public VolumeWatcher()
    {
        _timer = new System.Timers.Timer(700) { AutoReset = false };
        _timer.Elapsed += (_, _) =>
        {
            try { Poll(); } catch { /* audio stack transient */ }
            // Re-arm only if we are still running: a poll in flight during
            // Dispose must not resurrect the timer.
            finally { if (!_stopped) { try { _timer.Start(); } catch { } } }
        };
    }

    public void Start()
    {
        SubscribeToDevices();
        _timer.Start();
    }

    /// <summary>
    /// Subscribes to each device's session-created notification so a new track's
    /// session is corrected immediately, instead of playing at 100% until the
    /// next poll.
    /// </summary>
    private void SubscribeToDevices()
    {
        lock (_subLock)
        {
            foreach (var d in _subscribed)
            {
                try { d.Dispose(); } catch { }
            }
            _subscribed.Clear();

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    try
                    {
                        device.AudioSessionManager.OnSessionCreated += OnSessionCreated;
                        _subscribed.Add(device); // keep alive; the callback dies with it
                    }
                    catch { device.Dispose(); }
                }
            }
            catch { }
        }
    }

    private void OnSessionCreated(object sender, IAudioSessionControl newSession)
    {
        try
        {
            if (!VolumeMemory.Enabled) return;

            var ctrl = new AudioSessionControl(newSession);
            var process = AudioSession.ResolveProcessName(ctrl);
            if (string.IsNullOrEmpty(process)) return;

            var level = VolumeMemory.Get(process);
            if (level is null) return;

            var vol = ctrl.SimpleAudioVolume;
            vol.Volume = level.Volume;
            vol.Mute = level.Muted;

            var id = SafeInstanceId(ctrl);
            if (id.Length > 0)
            {
                lock (_seen) { _seen[id] = (level.Volume, level.Muted); }
            }
        }
        catch { }
    }

    private void Poll()
    {
        if (!VolumeMemory.Enabled) return;

        using var enumerator = new MMDeviceEnumerator();
        var seenThisPass = new HashSet<string>(StringComparer.Ordinal);

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var ctrl = sessions[i];
                    if (ctrl.State == AudioSessionState.AudioSessionStateExpired) continue;

                    string id = SafeInstanceId(ctrl);
                    if (id.Length == 0) continue;
                    seenThisPass.Add(id);

                    var vol = ctrl.SimpleAudioVolume;
                    float current = vol.Volume;

                    bool currentMute = vol.Mute;

                    bool known;
                    (float Volume, bool Muted) previous;
                    lock (_seen) { known = _seen.TryGetValue(id, out previous); }

                    if (known)
                    {
                        // Known session. If its level or mute moved since we last
                        // looked, something else changed it (the Windows mixer, the
                        // app's own slider) — follow that instead of overruling it,
                        // so the next session inherits the state the user expects.
                        if (Math.Abs(current - previous.Volume) > 0.005f || currentMute != previous.Muted)
                        {
                            var owner = AudioSession.ResolveProcessName(ctrl);
                            if (!string.IsNullOrEmpty(owner) && VolumeMemory.Get(owner) is not null)
                                VolumeMemory.Remember(owner, current, currentMute);
                            lock (_seen) { _seen[id] = (current, currentMute); }
                        }
                        continue;
                    }

                    // Brand new session.
                    lock (_seen) { _seen[id] = (current, currentMute); }

                    // The first pass only records what already exists; it must not
                    // override levels that were set before fichy started.
                    if (!_primed) continue;

                    var process = AudioSession.ResolveProcessName(ctrl);
                    if (string.IsNullOrEmpty(process)) continue;

                    var level = VolumeMemory.Get(process);
                    if (level is null) continue;

                    if (Math.Abs(current - level.Volume) > 0.001f)
                        vol.Volume = level.Volume;
                    if (currentMute != level.Muted)
                        vol.Mute = level.Muted;

                    lock (_seen) { _seen[id] = (level.Volume, level.Muted); }
                }
            }
            catch { }
            finally { device.Dispose(); }
        }

        // Drop identifiers of sessions that are gone, so the map can't grow forever.
        lock (_seen)
        {
            foreach (var stale in _seen.Keys.Where(k => !seenThisPass.Contains(k)).ToList())
                _seen.Remove(stale);
        }

        _primed = true;
    }

    private static string SafeInstanceId(AudioSessionControl ctrl)
    {
        try { return ctrl.GetSessionInstanceIdentifier ?? ""; }
        catch { return ""; }
    }

    public void Dispose()
    {
        _stopped = true;
        _timer.Stop();
        _timer.Dispose();

        lock (_subLock)
        {
            foreach (var d in _subscribed)
            {
                try { d.Dispose(); } catch { }
            }
            _subscribed.Clear();
        }
    }
}
