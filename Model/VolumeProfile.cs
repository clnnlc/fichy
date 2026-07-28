namespace VolumeMixer.Model;

/// <summary>
/// A named mix — a set of per-program levels applied together, optionally by
/// hotkey. Handy for switching between e.g. gaming and listening setups.
/// </summary>
public sealed class VolumeProfile
{
    public string Name { get; set; } = "";

    /// <summary>Optional global hotkey that applies this profile.</summary>
    public HotkeyGesture Hotkey { get; set; } = HotkeyGesture.Empty;

    /// <summary>Process name (lower-case, no extension) → level to apply.</summary>
    public Dictionary<string, RememberedLevel> Levels { get; set; } = new();

    public string Label => string.IsNullOrWhiteSpace(Name) ? "(unnamed)" : Name;
}
