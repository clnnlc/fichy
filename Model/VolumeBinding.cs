namespace VolumeMixer.Model;

/// <summary>
/// A user-defined binding: two hotkeys that raise / lower the volume of a
/// target program. The target is matched against the audio session's process
/// name (case-insensitive, without the ".exe" suffix), e.g. "spotify".
/// </summary>
public sealed class VolumeBinding
{
    /// <summary>Process name to match, without extension, e.g. "chrome".</summary>
    public string TargetProcess { get; set; } = "";

    /// <summary>Friendly label shown in the UI (falls back to TargetProcess).</summary>
    public string DisplayName { get; set; } = "";

    public HotkeyGesture VolumeUp { get; set; } = HotkeyGesture.Empty;
    public HotkeyGesture VolumeDown { get; set; } = HotkeyGesture.Empty;
    public HotkeyGesture? Mute { get; set; }

    /// <summary>Volume step per key press, 0..1 (default 5%).</summary>
    public float Step { get; set; } = 0.05f;

    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? TargetProcess : DisplayName;
}
