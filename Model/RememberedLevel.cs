namespace VolumeMixer.Model;

/// <summary>The volume/mute state fichy restores for a program's new audio sessions.</summary>
public sealed class RememberedLevel
{
    /// <summary>Volume in 0..1.</summary>
    public float Volume { get; set; } = 1f;
    public bool Muted { get; set; }
}
