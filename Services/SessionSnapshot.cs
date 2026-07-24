using NAudio.CoreAudioApi;

namespace VolumeMixer.Services;

/// <summary>
/// Owns a snapshot of audio sessions plus the underlying device COM handles.
/// Dispose it (e.g. when the overlay closes) to release the devices.
/// </summary>
public sealed class SessionSnapshot : IDisposable
{
    public IReadOnlyList<AudioSession> Sessions { get; }
    private readonly List<MMDevice> _devices;

    public SessionSnapshot(List<AudioSession> sessions, List<MMDevice> devices)
    {
        Sessions = sessions;
        _devices = devices;
    }

    /// <summary>Processes that are audio plumbing, not user programs — hidden from the mixer.</summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "audiodg", // Windows Audio Device Graph Isolation (APO/effects host)
    };

    /// <summary>
    /// Aggregates the raw sessions into one row per program (across all devices),
    /// dropping plumbing processes. "System" sessions collapse into one row.
    /// </summary>
    public List<SessionGroup> BuildGroups()
    {
        return Sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.ProcessName) && !Hidden.Contains(s.ProcessName))
            .GroupBy(s => s.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SessionGroup(g.Key, g.ToList()))
            .OrderByDescending(g => g.ContainsDefaultDevice)
            .ThenBy(g => g.ProcessName == "System") // push System sounds to the bottom
            .ThenBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void Dispose()
    {
        foreach (var d in _devices)
        {
            try { d.Dispose(); } catch { }
        }
        _devices.Clear();
    }
}
