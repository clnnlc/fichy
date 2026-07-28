using System.Globalization;
using System.Windows;
using System.Windows.Input;
using VolumeMixer.Model;

namespace VolumeMixer.UI;

/// <summary>
/// Assigns the volume hotkeys for one program, opened straight from its row in
/// the overlay so the process name never has to be typed by hand.
/// </summary>
public partial class AppHotkeyWindow : Window
{
    private readonly string _process;

    public AppHotkeyWindow(string processName, string displayName)
    {
        InitializeComponent();

        _process = processName;
        HeaderText.Text = $"Hotkeys for {displayName}";
        SubText.Text = $"Matched by process name: {processName}";

        var existing = FindBinding();
        if (existing is not null)
        {
            UpBox.Gesture = existing.VolumeUp;
            DownBox.Gesture = existing.VolumeDown;
            MuteBox.Gesture = existing.Mute ?? HotkeyGesture.Empty;
            StepBox.Text = Math.Round(existing.Step * 100).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            StepBox.Text = Math.Round(App.Instance.Settings.Current.DefaultStep * 100)
                .ToString(CultureInfo.InvariantCulture);
            RemoveButton.Visibility = Visibility.Collapsed;
        }

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private VolumeBinding? FindBinding() => App.Instance.Settings.Current.Bindings
        .FirstOrDefault(b => string.Equals(b.TargetProcess, _process, StringComparison.OrdinalIgnoreCase));

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        bool anyKey = !UpBox.Gesture.IsEmpty || !DownBox.Gesture.IsEmpty || !MuteBox.Gesture.IsEmpty;
        if (!anyKey)
        {
            StatusText.Text = "Assign at least one key.";
            return;
        }

        float step = int.TryParse(StepBox.Text, out var pct)
            ? Math.Clamp(pct, 1, 100) / 100f
            : App.Instance.Settings.Current.DefaultStep;

        var binding = FindBinding();
        if (binding is null)
        {
            binding = new VolumeBinding { TargetProcess = _process };
            App.Instance.Settings.Current.Bindings.Add(binding);
        }

        binding.VolumeUp = UpBox.Gesture;
        binding.VolumeDown = DownBox.Gesture;
        binding.Mute = MuteBox.Gesture.IsEmpty ? null : MuteBox.Gesture;
        binding.Step = step;

        App.Instance.Settings.Save();
        var failed = App.Instance.RebuildHotkeys();

        // Only complain about this program's keys — other bindings may already
        // have been in conflict before this window was opened.
        var mine = failed.Where(f => f.Contains(binding.Label, StringComparison.OrdinalIgnoreCase)).ToList();
        if (mine.Count > 0)
        {
            StatusText.Text = $"Already in use: {string.Join(", ", mine)}";
            return;
        }

        Close();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var binding = FindBinding();
        if (binding is not null)
        {
            App.Instance.Settings.Current.Bindings.Remove(binding);
            App.Instance.Settings.Save();
            App.Instance.RebuildHotkeys();
        }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
