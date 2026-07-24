using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace VolumeMixer.Services;

/// <summary>
/// Produces human-readable key labels that match the user's *current Windows
/// keyboard layout*. Character-producing keys (letters, digits, punctuation)
/// are resolved through the active layout via ToUnicodeEx so, e.g., a German
/// QWERTZ keyboard shows "Z"/"Ü"/"ß" correctly instead of raw VK names.
/// </summary>
public static class KeyNames
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

    private const uint MAPVK_VK_TO_VSC = 0;

    /// <summary>Friendly, layout-aware label for a WPF key.</summary>
    public static string Describe(Key key)
    {
        // Named keys that don't map to a printable character.
        string? special = key switch
        {
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Escape => "Esc",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.PrintScreen => "PrintScreen",
            Key.Pause => "Pause",
            Key.CapsLock => "CapsLock",
            Key.NumLock => "NumLock",
            Key.Scroll => "ScrollLock",
            Key.Apps => "Menu",
            >= Key.F1 and <= Key.F24 => key.ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
            Key.Multiply => "Num*",
            Key.Add => "Num+",
            Key.Subtract => "Num-",
            Key.Divide => "Num/",
            Key.Decimal => "Num.",
            _ => null,
        };
        if (special is not null) return special;

        // Character keys: resolve through the active keyboard layout.
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var ch = CharForVk(vk);
        if (ch is not null) return ch.ToUpperInvariant();

        // Fallback: raw enum name.
        return key.ToString();
    }

    private static string? CharForVk(uint vk)
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            uint tid = GetWindowThreadProcessId(fg, IntPtr.Zero);
            IntPtr hkl = GetKeyboardLayout(tid);

            uint sc = MapVirtualKeyEx(vk, MAPVK_VK_TO_VSC, hkl);
            var keyState = new byte[256];
            var sb = new StringBuilder(8);
            // wFlags bit 2 (=4): do not change keyboard state — safe for querying.
            int rc = ToUnicodeEx(vk, sc, keyState, sb, sb.Capacity, 4, hkl);
            if (rc >= 1)
            {
                var s = sb.ToString();
                if (s.Length > 0 && !char.IsControl(s[0]) && !char.IsWhiteSpace(s[0]))
                    return s.Substring(0, 1);
            }
        }
        catch { }
        return null;
    }
}
