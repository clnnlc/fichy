using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VolumeMixer.Model;

namespace VolumeMixer.UI;

/// <summary>
/// A button that captures a global-hotkey gesture. Click it, then press the
/// desired combination (e.g. Ctrl+Alt+Up). Backspace/Delete clears it, Escape
/// cancels capture.
/// </summary>
public sealed class HotkeyBox : Button
{
    private bool _capturing;

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
        Click += (_, _) => BeginCapture();
        LostFocus += (_, _) => CancelCapture();
    }

    private static void OnGestureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HotkeyBox box && !box._capturing) box.UpdateText();
    }

    private void BeginCapture()
    {
        _capturing = true;
        Content = "› Press a key …";
    }

    private void CancelCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        UpdateText();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturing)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            CancelCapture();
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            _capturing = false;
            Gesture = HotkeyGesture.Empty;
            UpdateText();
            GestureCommitted?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Ignore standalone modifier presses; wait for a real key.
        if (IsModifier(key)) return;

        _capturing = false;
        Gesture = new HotkeyGesture(Keyboard.Modifiers, key);
        UpdateText();
        GestureCommitted?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsModifier(Key k) => k is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    private void UpdateText() => Content = Gesture is null || Gesture.IsEmpty ? "(none)" : Gesture.ToString();
}
