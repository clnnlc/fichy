using System.Runtime.InteropServices;
using System.Windows.Input;

namespace VolumeMixer.Services;

/// <summary>
/// A low-level keyboard hook (WH_KEYBOARD_LL) used while recording a hotkey.
///
/// WPF never sees the keys while this is active, which is essential: Alt would
/// otherwise put the window into menu/access-key mode and the Windows key would
/// open the Start menu, so those combinations could never be recorded through
/// normal WPF key events.
/// </summary>
public sealed class CaptureHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private readonly HookProc _proc;   // kept alive: the OS holds a raw pointer to it
    private IntPtr _hook = IntPtr.Zero;

    /// <summary>
    /// Modifier keys currently held down, tracked from the raw events we see.
    /// We cannot ask Windows (GetAsyncKeyState) once capturing starts: because
    /// the hook swallows the key events, the system never records those keys as
    /// pressed.
    /// </summary>
    private readonly HashSet<int> _heldModifiers = new();

    /// <summary>Raised on the first non-modifier key, with the modifiers held at that moment.</summary>
    public event Action<Key, ModifierKeys>? Captured;

    public CaptureHook() => _proc = HookCallback;

    public bool IsActive => _hook != IntPtr.Zero;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;

        // Seed with whatever is already held — this is still accurate right now,
        // before the hook starts swallowing events.
        _heldModifiers.Clear();
        foreach (var vk in new[] { VK_CONTROL, VK_MENU, VK_SHIFT, VK_LWIN, VK_RWIN })
            if (IsDown(vk)) _heldModifiers.Add(vk);

        // A null module handle is valid for a managed low-level hook.
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _heldModifiers.Clear();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        int msg = wParam.ToInt32();
        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        int vk = (int)data.vkCode;
        bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;

        if (NormalizeModifier(vk) is int mod)
        {
            if (isDown) _heldModifiers.Add(mod);
            else _heldModifiers.Remove(mod);
        }
        else if (isDown)
        {
            var key = KeyInterop.KeyFromVirtualKey(vk);
            Captured?.Invoke(key, HeldModifiers());
        }

        // Swallow everything (down *and* up) while capturing so no keystroke
        // leaks into this or any other application.
        return new IntPtr(1);
    }

    /// <summary>Maps left/right modifier variants onto their generic VK, or null if not a modifier.</summary>
    private static int? NormalizeModifier(int vk) => vk switch
    {
        VK_SHIFT or 0xA0 or 0xA1 => VK_SHIFT,
        VK_CONTROL or 0xA2 or 0xA3 => VK_CONTROL,
        VK_MENU or 0xA4 or 0xA5 => VK_MENU,
        VK_LWIN => VK_LWIN,
        VK_RWIN => VK_RWIN,
        _ => null,
    };

    private ModifierKeys HeldModifiers()
    {
        var m = ModifierKeys.None;
        if (_heldModifiers.Contains(VK_CONTROL)) m |= ModifierKeys.Control;
        if (_heldModifiers.Contains(VK_MENU)) m |= ModifierKeys.Alt;
        if (_heldModifiers.Contains(VK_SHIFT)) m |= ModifierKeys.Shift;
        if (_heldModifiers.Contains(VK_LWIN) || _heldModifiers.Contains(VK_RWIN)) m |= ModifierKeys.Windows;
        return m;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose() => Stop();
}
