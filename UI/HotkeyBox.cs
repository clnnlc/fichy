using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VolumeMixer.Model;
using VolumeMixer.Services;

namespace VolumeMixer.UI;

/// <summary>
/// A button that records a global-hotkey combination. Click it, then press the
/// desired combination — any number of modifiers plus one key, e.g.
/// Ctrl+Alt+Num+ or Ctrl+Shift+F9. Backspace/Delete clears it, Escape cancels.
///
/// Recording runs through a low-level keyboard hook rather than WPF key events,
/// because WPF swallows Alt (menu mode) and Windows (Start menu) combinations
/// before they ever reach a control.
/// </summary>
public sealed class HotkeyBox : Button
{
    private readonly CaptureHook _hook = new();

    public static readonly DependencyProperty GestureProperty =
        DependencyProperty.Register(nameof(Gesture), typeof(HotkeyGesture), typeof(HotkeyBox),
            new FrameworkPropertyMetadata(HotkeyGesture.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnGestureChanged));

    public HotkeyGesture Gesture
    {
        get => (HotkeyGesture)GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    public event EventHandler? GestureCommitted;

    public HotkeyBox()
    {
        Focusable = true;
        MinWidth = 150;
        UpdateText();

        _hook.Captured += OnHookCaptured;
        Click += (_, _) => BeginCapture();
        LostFocus += (_, _) => CancelCapture();
        Unloaded += (_, _) => _hook.Dispose();
    }

    private static void OnGestureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HotkeyBox box && !box._hook.IsActive) box.UpdateText();
    }

    private void BeginCapture()
    {
        if (_hook.IsActive) return;
        Content = "› Press a key …";
        _hook.Start();
    }

    private void CancelCapture()
    {
        if (!_hook.IsActive) return;
        _hook.Stop();
        UpdateText();
    }

    /// <summary>Called from the hook thread's message pump; marshal to the UI thread.</summary>
    private void OnHookCaptured(Key key, ModifierKeys modifiers)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_hook.IsActive) return;

            if (key == Key.Escape)
            {
                CancelCapture();
                return;
            }

            if (key is Key.Back or Key.Delete)
            {
                _hook.Stop();
                Gesture = HotkeyGesture.Empty;
                UpdateText();
                GestureCommitted?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (key == Key.None) return;

            _hook.Stop();
            Gesture = new HotkeyGesture(modifiers, key);
            UpdateText();
            GestureCommitted?.Invoke(this, EventArgs.Empty);
        }));
    }

    private void UpdateText() => Content = Gesture is null || Gesture.IsEmpty ? "(none)" : Gesture.ToString();
}
