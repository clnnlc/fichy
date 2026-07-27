using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace VolumeMixer.Services;

/// <summary>
/// Aggregates all <see cref="AudioSession"/>s of one program (matched by
/// process name) across every output device into a single mixer row — the same
/// per-application model the Windows volume mixer presents. Volume/mute changes
/// are applied to every underlying session at once.
/// </summary>
public sealed class SessionGroup : INotifyPropertyChanged
{
    private readonly List<AudioSession> _sessions;

    public string ProcessName { get; }
    public string DisplayName { get; }
    public ImageSource? Icon { get; }
    public bool ContainsDefaultDevice { get; }

    public SessionGroup(string processName, List<AudioSession> sessions)
    {
        ProcessName = processName;
        _sessions = sessions;
        var first = sessions[0];
        DisplayName = first.DisplayName;
        Icon = sessions.FirstOrDefault(s => s.Icon is not null)?.Icon;
        ContainsDefaultDevice = sessions.Any(s => s.IsDefaultDevice);
    }

    /// <summary>Subtitle describing which output device(s) this program plays on.</summary>
    public string DeviceSummary
    {
        get
        {
            var devices = _sessions.Select(s => s.DeviceName).Distinct().ToList();
            return devices.Count == 1 ? devices[0] : $"{devices.Count} output devices";
        }
    }

    /// <summary>Displayed volume = loudest of the grouped sessions (0..100).</summary>
    public double VolumePercent
    {
        get => _sessions.Max(s => s.VolumePercent);
        set
        {
            foreach (var s in _sessions)
                s.VolumePercent = value;
            VolumeMemory.Remember(ProcessName, (float)(value / 100.0), IsMuted);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMuted));
        }
    }

    /// <summary>Muted only when every underlying session is muted.</summary>
    public bool IsMuted
    {
        get => _sessions.All(s => s.IsMuted);
        set
        {
            foreach (var s in _sessions)
                s.IsMuted = value;
            VolumeMemory.RememberMute(ProcessName, value);
            OnPropertyChanged();
        }
    }

    /// <summary>Loudest current peak across the grouped sessions (0..1).</summary>
    public float Peak => _sessions.Count == 0 ? 0f : _sessions.Max(s => s.Peak);

    public void ToggleMute() => IsMuted = !IsMuted;

    public void RefreshMeter() => OnPropertyChanged(nameof(Peak));

    public void Refresh()
    {
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(Peak));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
