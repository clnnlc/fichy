using System.Windows.Input;

namespace VolumeMixer.Model;

/// <summary>Persisted application configuration (stored as JSON in %AppData%).</summary>
public sealed class AppSettings
{
    /// <summary>Global hotkey that toggles the overlay. Triple-modifier by default
    /// to minimise the chance of colliding with another app's global hotkey.</summary>
    public HotkeyGesture ToggleOverlay { get; set; } =
        new(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.M);

    /// <summary>Per-program volume hotkey bindings.</summary>
    public List<VolumeBinding> Bindings { get; set; } = new();

    /// <summary>Default step used for new bindings and the overlay scroll.</summary>
    public float DefaultStep { get; set; } = 0.05f;

    /// <summary>Whether the app registered itself for autostart (mirror of registry).</summary>
    public bool Autostart { get; set; }

    /// <summary>Close the overlay automatically when it loses focus.</summary>
    public bool CloseOverlayOnFocusLost { get; set; } = true;

    /// <summary>
    /// Re-apply a program's volume to sessions it opens later. Players such as
    /// Spotify or Amazon Music create a fresh audio session per track, which
    /// always starts at 100%, so without this their volume resets every song.
    /// </summary>
    public bool RememberVolumes { get; set; } = true;

    /// <summary>Last volume fichy set per process name (lower-case, no extension).</summary>
    public Dictionary<string, RememberedLevel> RememberedVolumes { get; set; } = new();
}
