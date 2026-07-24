using System.Text;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace VolumeMixer.Model;

/// <summary>
/// Represents a global hotkey combination (modifiers + a single key).
/// Serialized to/from a human readable string like "Ctrl+Alt+Up".
/// </summary>
public sealed class HotkeyGesture : IEquatable<HotkeyGesture>
{
    public ModifierKeys Modifiers { get; init; }
    public Key Key { get; init; }

    [JsonIgnore]
    public bool IsEmpty => Key == Key.None;

    public HotkeyGesture() { }

    public HotkeyGesture(ModifierKeys modifiers, Key key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public static HotkeyGesture Empty => new(ModifierKeys.None, Key.None);

    /// <summary>Win32 virtual-key code for the main key.</summary>
    [JsonIgnore]
    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    /// <summary>MOD_* flags for RegisterHotKey (Alt=1, Ctrl=2, Shift=4, Win=8).</summary>
    [JsonIgnore]
    public uint Win32Modifiers
    {
        get
        {
            uint m = 0;
            if (Modifiers.HasFlag(ModifierKeys.Alt)) m |= 0x0001;
            if (Modifiers.HasFlag(ModifierKeys.Control)) m |= 0x0002;
            if (Modifiers.HasFlag(ModifierKeys.Shift)) m |= 0x0004;
            if (Modifiers.HasFlag(ModifierKeys.Windows)) m |= 0x0008;
            return m;
        }
    }

    public override string ToString()
    {
        if (IsEmpty) return "(none)";
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");
        sb.Append(Services.KeyNames.Describe(Key));
        return sb.ToString();
    }

    public bool Equals(HotkeyGesture? other)
        => other is not null && other.Modifiers == Modifiers && other.Key == Key;

    public override bool Equals(object? obj) => Equals(obj as HotkeyGesture);
    public override int GetHashCode() => HashCode.Combine(Modifiers, Key);
}
