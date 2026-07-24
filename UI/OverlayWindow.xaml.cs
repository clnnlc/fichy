using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VolumeMixer.Services;

namespace VolumeMixer.UI;

public partial class OverlayWindow : Window
{
    private SessionSnapshot? _snapshot;
    private List<SessionGroup> _groups = new();
    private readonly DispatcherTimer _meterTimer;
    private readonly DispatcherTimer _refreshTimer;
    private string _signature = "";

    public OverlayWindow()
    {
        InitializeComponent();

        _meterTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        _meterTimer.Tick += (_, _) => UpdateMeters();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500),
        };
        _refreshTimer.Tick += (_, _) => ReloadIfChanged();

        Loaded += OnLoaded;
        Closed += OnClosed;
        KeyDown += OnKeyDown;
        Deactivated += OnDeactivated;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadSessions();
        PositionBottomRight();
        _meterTimer.Start();
        _refreshTimer.Start();
    }

    private void PositionBottomRight()
    {
        var wa = SystemParameters.WorkArea;
        UpdateLayout();
        Left = wa.Right - ActualWidth;
        Top = wa.Bottom - ActualHeight;
    }

    private void LoadSessions()
    {
        _snapshot?.Dispose();
        _snapshot = App.Instance.Audio.GetSessions();
        _groups = _snapshot.BuildGroups();

        SessionList.ItemsSource = _groups;
        _signature = BuildSignature(_snapshot.Sessions);

        bool empty = _groups.Count == 0;
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        SessionList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string BuildSignature(IEnumerable<AudioSession> sessions)
        => string.Join("|", sessions.Select(s => $"{s.ProcessName}@{s.DeviceName}").OrderBy(x => x));

    private void ReloadIfChanged()
    {
        // Cheap probe: build a fresh snapshot, compare identity set, swap only if changed.
        var probe = App.Instance.Audio.GetSessions();
        var sig = BuildSignature(probe.Sessions);
        if (sig == _signature)
        {
            probe.Dispose();
            return;
        }

        _snapshot?.Dispose();
        _snapshot = probe;
        _groups = probe.BuildGroups();

        SessionList.ItemsSource = _groups;
        _signature = sig;

        bool empty = _groups.Count == 0;
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        SessionList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateMeters()
    {
        foreach (var g in _groups)
            g.RefreshMeter();
    }

    /// <summary>Called from App when a hotkey changed a program's volume/mute.</summary>
    public void RefreshLive()
    {
        foreach (var g in _groups)
            g.Refresh();
    }

    private void Row_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SessionGroup g)
        {
            double step = App.Instance.Settings.Current.DefaultStep * 100.0;
            g.VolumePercent = Math.Clamp(g.VolumePercent + (e.Delta > 0 ? step : -step), 0, 100);
            e.Handled = true;
        }
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is SessionGroup g)
            g.ToggleMute();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        App.Instance.OpenSettings();
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (App.Instance.Settings.Current.CloseOverlayOnFocusLost)
            Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _meterTimer.Stop();
        _refreshTimer.Stop();
        _snapshot?.Dispose();
        _snapshot = null;
    }
}
