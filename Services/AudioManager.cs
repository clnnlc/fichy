using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using VolumeMixer.Model;

namespace VolumeMixer.Services;

/// <summary>
/// Wraps the Windows Core Audio API (WASAPI) via NAudio to enumerate every
/// active audio *output* device and the per-application sessions playing on it,
/// and to change the volume of a session by process name.
/// </summary>
public sealed class AudioManager : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    /// <summary>Returns a fresh snapshot of all active render devices.</summary>
    public IReadOnlyList<MMDevice> GetRenderDevices()
    {
        var list = new List<MMDevice>();
        try
        {
            foreach (var d in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                list.Add(d);
        }
        catch { /* audio service may be restarting */ }
        return list;
    }

    public MMDevice? GetDefaultRenderDevice()
    {
        try { return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
        catch { return null; }
    }

    /// <summary>
    /// Builds a snapshot of all sessions across all active output devices.
    /// Dispose the returned <see cref="SessionSnapshot"/> to release the devices.
    /// </summary>
    public SessionSnapshot GetSessions()
    {
        var result = new List<AudioSession>();
        var keptDevices = new List<MMDevice>();
        var defaultId = GetDefaultRenderDevice()?.ID;

        foreach (var device in GetRenderDevices())
        {
            try
            {
                var sessions = device.AudioSessionManager.Sessions;
                bool anySession = false;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var ctrl = sessions[i];
                    if (ctrl.State == AudioSessionState.AudioSessionStateExpired)
                        continue;

                    var session = AudioSession.TryCreate(device, ctrl, device.ID == defaultId);
                    if (session is not null)
                    {
                        result.Add(session);
                        anySession = true;
                    }
                }

                if (anySession)
                    keptDevices.Add(device); // keep alive; snapshot owns it
                else
                    device.Dispose();
            }
            catch
            {
                device.Dispose();
            }
        }

        return new SessionSnapshot(result, keptDevices);
    }

    /// <summary>
    /// Adjusts the volume of every session whose process name matches
    /// <paramref name="processName"/> (case-insensitive, no extension) by
    /// <paramref name="delta"/> across all output devices. Returns the new
    /// average volume (0..1) or -1 if no matching session was found.
    /// </summary>
    public float AdjustVolume(string processName, float delta)
    {
        if (string.IsNullOrWhiteSpace(processName)) return -1f;
        var target = Normalize(processName);
        float newVolume = -1f;
        bool found = false;
        bool muted = false;

        foreach (var device in GetRenderDevices())
        {
            try
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var ctrl = sessions[i];
                    if (ctrl.State == AudioSessionState.AudioSessionStateExpired) continue;
                    if (!MatchesProcess(ctrl, target)) continue;

                    var vol = ctrl.SimpleAudioVolume;
                    float v = Math.Clamp(vol.Volume + delta, 0f, 1f);
                    vol.Volume = v;
                    if (v > 0f) vol.Mute = false;
                    newVolume = v;
                    found = true;
                    muted = vol.Mute;
                }
            }
            catch { /* device gone */ }
            finally { device.Dispose(); }
        }

        if (found)
            VolumeMemory.Remember(processName, newVolume, muted);

        return found ? newVolume : -1f;
    }

    /// <summary>Toggles mute for all sessions of the given process. Returns new mute state or null.</summary>
    public bool? ToggleMute(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        var target = Normalize(processName);
        bool? state = null;

        foreach (var device in GetRenderDevices())
        {
            try
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var ctrl = sessions[i];
                    if (ctrl.State == AudioSessionState.AudioSessionStateExpired) continue;
                    if (!MatchesProcess(ctrl, target)) continue;

                    var vol = ctrl.SimpleAudioVolume;
                    state ??= !vol.Mute;
                    vol.Mute = state.Value;
                }
            }
            catch { }
            finally { device.Dispose(); }
        }

        if (state is not null)
            VolumeMemory.RememberMute(processName, state.Value);

        return state;
    }

    /// <summary>
    /// Applies a whole set of process→level entries in one enumeration pass and
    /// records them, so the levels also survive the players that recreate their
    /// session per track. Returns how many programs were actually matched.
    /// </summary>
    public int ApplyLevels(IReadOnlyDictionary<string, RememberedLevel> levels)
    {
        if (levels.Count == 0) return 0;

        var wanted = levels.ToDictionary(kv => Normalize(kv.Key), kv => kv.Value);
        var matched = new HashSet<string>(StringComparer.Ordinal);

        foreach (var device in GetRenderDevices())
        {
            try
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var ctrl = sessions[i];
                    if (ctrl.State == AudioSessionState.AudioSessionStateExpired) continue;

                    var name = AudioSession.ResolveProcessName(ctrl);
                    if (string.IsNullOrEmpty(name)) continue;

                    var key = Normalize(name);
                    if (!wanted.TryGetValue(key, out var level)) continue;

                    var vol = ctrl.SimpleAudioVolume;
                    vol.Volume = Math.Clamp(level.Volume, 0f, 1f);
                    vol.Mute = level.Muted;
                    matched.Add(key);
                }
            }
            catch { }
            finally { device.Dispose(); }
        }

        // Remember every entry, not just the matched ones: a program that is
        // closed right now should still come up at the profile's level later.
        foreach (var (key, level) in wanted)
            VolumeMemory.Remember(key, level.Volume, level.Muted);

        return matched.Count;
    }

    private static bool MatchesProcess(AudioSessionControl ctrl, string normalizedTarget)
    {
        var name = AudioSession.ResolveProcessName(ctrl);
        return !string.IsNullOrEmpty(name) && Normalize(name) == normalizedTarget;
    }

    internal static string Normalize(string s)
    {
        s = s.Trim();
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            s = s[..^4];
        return s.ToLowerInvariant();
    }

    public void Dispose() => _enumerator.Dispose();
}
