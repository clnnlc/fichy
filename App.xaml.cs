using System.Windows;
using WinForms = System.Windows.Forms;
using VolumeMixer.Model;
using VolumeMixer.Services;
using VolumeMixer.UI;

namespace VolumeMixer;

public partial class App : Application
{
    public static App Instance => (App)Current;

    public SettingsService Settings { get; } = new();
    public AudioManager Audio { get; } = new();

    private HotkeyManager _hotkeys = null!;
    private VolumeWatcher _volumeWatcher = null!;
    private WinForms.NotifyIcon _tray = null!;
    private OverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private OsdWindow? _osd;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Remove the executable a previous update moved aside.
        UpdateService.CleanUpOldVersion();

        Settings.Load();
        // Keep the settings flag in sync with what the registry actually says.
        Settings.Current.Autostart = AutostartService.IsEnabled();

        VolumeMemory.Configure(Settings);

        _hotkeys = new HotkeyManager();
        var failed = RebuildHotkeys();

        // Restores levels for players that open a new session per track.
        _volumeWatcher = new VolumeWatcher();
        _volumeWatcher.Start();

        SetupTray();

        Logger.Log($"Startup args=[{string.Join(' ', e.Args)}] loadedFromDisk={Settings.WasLoadedFromDisk}");

        if (failed.Count > 0)
            WarnAboutFailedHotkeys(failed);

        if (Settings.Current.CheckForUpdates)
            _ = NotifyIfUpdateAvailableAsync();

        // Diagnostic / convenience: open the overlay immediately.
        if (e.Args.Contains("--overlay"))
        {
            Dispatcher.BeginInvoke(new Action(ToggleOverlay));
            return;
        }

        // Convenience: jump straight to the settings window.
        if (e.Args.Contains("--settings"))
        {
            Dispatcher.BeginInvoke(new Action(OpenSettings));
            return;
        }

        // First run (no config yet): open settings so the user can configure hotkeys.
        if (!Settings.WasLoadedFromDisk)
        {
            OpenSettings();
        }
        else
        {
            _tray.ShowBalloonTip(3000, "fichy is running",
                $"Open the overlay with {Settings.Current.ToggleOverlay}. Right-click the tray icon for settings.",
                WinForms.ToolTipIcon.Info);
        }
    }

    // ---- Tray icon ---------------------------------------------------------

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = IconFactory.CreateAppIcon(),
            Visible = true,
            Text = "fichy — finally I can hear you",
        };
        _tray.DoubleClick += (_, _) => ToggleOverlay();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open / close overlay", null, (_, _) => ToggleOverlay());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
    }

    // ---- Hotkeys -----------------------------------------------------------

    /// <summary>
    /// (Re)registers the overlay toggle and all per-program bindings.
    /// Returns a list of human-readable descriptions of hotkeys that could NOT
    /// be registered (already in use by another application).
    /// </summary>
    public List<string> RebuildHotkeys()
    {
        _hotkeys.ClearAll();
        var failed = new List<string>();

        void Try(HotkeyGesture g, string label, Action action)
        {
            if (g.IsEmpty) return;
            bool ok = _hotkeys.Register(g, action);
            Logger.Log($"Register {label} {g} -> {ok}");
            if (!ok) failed.Add($"{label} ({g})");
        }

        Try(Settings.Current.ToggleOverlay, "Overlay", () => Dispatcher.Invoke(ToggleOverlay));

        foreach (var binding in Settings.Current.Bindings)
        {
            var b = binding; // capture
            Try(b.VolumeUp, $"{b.Label} louder", () => Dispatcher.Invoke(() => ApplyVolume(b, +b.Step)));
            Try(b.VolumeDown, $"{b.Label} quieter", () => Dispatcher.Invoke(() => ApplyVolume(b, -b.Step)));
            if (b.Mute is not null)
                Try(b.Mute, $"{b.Label} mute", () => Dispatcher.Invoke(() => ApplyMute(b)));
        }

        foreach (var profile in Settings.Current.Profiles)
        {
            var p = profile; // capture
            Try(p.Hotkey, $"profile {p.Label}", () => Dispatcher.Invoke(() => ApplyProfile(p)));
        }

        return failed;
    }

    /// <summary>Applies a saved mix and reports what happened on screen.</summary>
    public void ApplyProfile(VolumeProfile profile)
    {
        int matched = Audio.ApplyLevels(profile.Levels);
        ShowOsd(profile.Label, -1, muted: false, notFound: false,
            detail: matched == 0
                ? "no matching app playing"
                : $"{matched} of {profile.Levels.Count} apps set");
        _overlay?.RefreshLive();
    }

    private void ApplyVolume(VolumeBinding b, float delta)
    {
        float v = Audio.AdjustVolume(b.TargetProcess, delta);
        if (v < 0f)
        {
            ShowOsd(b.Label, -1, muted: false, notFound: true);
            return;
        }
        ShowOsd(b.Label, (int)Math.Round(v * 100), muted: false);
        _overlay?.RefreshLive();
    }

    private void ApplyMute(VolumeBinding b)
    {
        bool? muted = Audio.ToggleMute(b.TargetProcess);
        if (muted is null)
        {
            ShowOsd(b.Label, -1, muted: false, notFound: true);
            return;
        }
        ShowOsd(b.Label, -1, muted.Value);
        _overlay?.RefreshLive();
    }

    // ---- Windows -----------------------------------------------------------

    public void ToggleOverlay()
    {
        Logger.Log("ToggleOverlay invoked");
        if (_overlay is { IsVisible: true })
        {
            _overlay.Close();
            return;
        }

        _overlay = new OverlayWindow();
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
        _overlay.Activate();
    }

    public void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>
    /// Reports a newer release in the tray. It only ever informs — installing
    /// stays a deliberate click in Settings.
    /// </summary>
    private async Task NotifyIfUpdateAvailableAsync()
    {
        try
        {
            var info = await UpdateService.CheckAsync();
            if (info is null) return;

            Dispatcher.Invoke(() => _tray.ShowBalloonTip(6000, $"fichy {info.Tag} is available",
                "Open Settings → Updates to install it.", WinForms.ToolTipIcon.Info));
        }
        catch { }
    }

    private void WarnAboutFailedHotkeys(List<string> failed)
    {
        var list = string.Join(", ", failed);
        _tray.ShowBalloonTip(6000, "Hotkey(s) already in use",
            $"Could not register: {list}. Please pick a different combination in Settings.",
            WinForms.ToolTipIcon.Warning);
    }

    private void ShowOsd(string label, int volumePercent, bool muted, bool notFound = false,
        string? detail = null)
    {
        _osd ??= new OsdWindow();
        _osd.ShowOverlay(label, volumePercent, muted, notFound, detail);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _volumeWatcher?.Dispose();
        VolumeMemory.Flush();
        _hotkeys?.Dispose();
        Audio.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }
}
