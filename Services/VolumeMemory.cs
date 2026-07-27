using VolumeMixer.Model;

namespace VolumeMixer.Services;

/// <summary>
/// Remembers the volume/mute fichy last applied to each program, so it can be
/// restored when that program opens a new audio session.
///
/// This is what makes the setting stick for players like Spotify or Amazon
/// Music: they create a brand new session for every track (Amazon Music even
/// runs several at once, which is why Windows shows it twice), and every new
/// session starts at 100%.
/// </summary>
public static class VolumeMemory
{
    private static SettingsService? _settings;
    private static readonly object Gate = new();
    private static DateTime _lastSave = DateTime.MinValue;

    public static void Configure(SettingsService settings) => _settings = settings;

    public static bool Enabled => _settings?.Current.RememberVolumes ?? false;

    /// <summary>Records the level fichy just applied to <paramref name="processName"/>.</summary>
    public static void Remember(string processName, float volume, bool muted)
    {
        if (_settings is null || string.IsNullOrWhiteSpace(processName)) return;
        if (!_settings.Current.RememberVolumes) return;

        var key = AudioManager.Normalize(processName);
        if (key is "" or "system") return;

        lock (Gate)
        {
            var map = _settings.Current.RememberedVolumes;
            if (!map.TryGetValue(key, out var level))
                map[key] = level = new RememberedLevel();

            level.Volume = Math.Clamp(volume, 0f, 1f);
            level.Muted = muted;
        }

        SaveThrottled();
    }

    /// <summary>Records only a mute change, keeping the stored volume.</summary>
    public static void RememberMute(string processName, bool muted)
    {
        if (_settings is null || string.IsNullOrWhiteSpace(processName)) return;
        if (!_settings.Current.RememberVolumes) return;

        var key = AudioManager.Normalize(processName);
        if (key is "" or "system") return;

        lock (Gate)
        {
            if (_settings.Current.RememberedVolumes.TryGetValue(key, out var level))
                level.Muted = muted;
            else
                _settings.Current.RememberedVolumes[key] = new RememberedLevel { Volume = 1f, Muted = muted };
        }

        SaveThrottled();
    }

    public static RememberedLevel? Get(string processName)
    {
        if (_settings is null || !_settings.Current.RememberVolumes) return null;
        var key = AudioManager.Normalize(processName);
        lock (Gate)
        {
            return _settings.Current.RememberedVolumes.TryGetValue(key, out var level) ? level : null;
        }
    }

    public static void Clear()
    {
        if (_settings is null) return;
        lock (Gate) { _settings.Current.RememberedVolumes.Clear(); }
        _settings.Save();
    }

    /// <summary>Persist at most once every few seconds — dragging a slider fires constantly.</summary>
    private static void SaveThrottled()
    {
        if (_settings is null) return;
        if ((DateTime.UtcNow - _lastSave).TotalSeconds < 3) return;
        _lastSave = DateTime.UtcNow;
        _settings.Save();
    }

    /// <summary>Flush pending changes (called on exit).</summary>
    public static void Flush() => _settings?.Save();
}
