using System.Runtime.InteropServices;
using System.Windows.Interop;
using VolumeMixer.Model;

namespace VolumeMixer.Services;

/// <summary>
/// Registers system-wide hotkeys via the Win32 RegisterHotKey API and routes
/// WM_HOTKEY messages to the callbacks that were registered for them.
/// Uses a hidden message-only window so no visible window is required.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HWND_MESSAGE = -3;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _callbacks = new();
    private int _nextId = 1;

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("VolumeMixerHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(HWND_MESSAGE), // message-only window
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>
    /// Registers a hotkey and its callback. Returns false if the gesture is
    /// empty or the combination is already taken by another app.
    /// </summary>
    public bool Register(HotkeyGesture gesture, Action callback)
    {
        if (gesture.IsEmpty) return false;

        int id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, gesture.Win32Modifiers, gesture.VirtualKey))
            return false;

        _callbacks[id] = callback;
        return true;
    }

    /// <summary>Unregisters every hotkey currently held.</summary>
    public void ClearAll()
    {
        foreach (var id in _callbacks.Keys)
            UnregisterHotKey(_source.Handle, id);
        _callbacks.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_callbacks.TryGetValue(id, out var cb))
            {
                handled = true;
                try { cb(); } catch { }
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        ClearAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
