using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;

namespace VolumeMixer.Services;

/// <summary>
/// View-model wrapper around a single WASAPI audio session (one application on
/// one output device). Exposes bindable Volume/Mute/Peak and applies changes
/// back to the underlying session in real time.
/// </summary>
public sealed class AudioSession : INotifyPropertyChanged
{
    private readonly AudioSessionControl _ctrl;
    private readonly SimpleAudioVolume _volume;

    public string DeviceName { get; }
    public bool IsDefaultDevice { get; }
    public string ProcessName { get; }
    public string DisplayName { get; }
    public ImageSource? Icon { get; }

    private AudioSession(AudioSessionControl ctrl, string deviceName, bool isDefault,
        string processName, string displayName, ImageSource? icon)
    {
        _ctrl = ctrl;
        _volume = ctrl.SimpleAudioVolume;
        DeviceName = deviceName;
        IsDefaultDevice = isDefault;
        ProcessName = processName;
        DisplayName = displayName;
        Icon = icon;
    }

    /// <summary>Volume in percent (0..100) for easy slider binding.</summary>
    public double VolumePercent
    {
        get { try { return Math.Round(_volume.Volume * 100.0); } catch { return 0; } }
        set
        {
            try
            {
                var v = (float)Math.Clamp(value / 100.0, 0.0, 1.0);
                _volume.Volume = v;
                if (v > 0f && _volume.Mute) _volume.Mute = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMuted));
            }
            catch { }
        }
    }

    public bool IsMuted
    {
        get { try { return _volume.Mute; } catch { return false; } }
        set { try { _volume.Mute = value; OnPropertyChanged(); } catch { } }
    }

    /// <summary>Current output peak level (0..1), used to draw a live meter.</summary>
    public float Peak
    {
        get { try { return _ctrl.AudioMeterInformation.MasterPeakValue; } catch { return 0f; } }
    }

    public void ToggleMute() => IsMuted = !IsMuted;

    /// <summary>Re-reads volume/mute from the system (e.g. after a hotkey change).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(Peak));
    }

    /// <summary>Raises only the peak meter change (called on the render timer).</summary>
    public void RefreshMeter() => OnPropertyChanged(nameof(Peak));

    internal static AudioSession? TryCreate(MMDevice device, AudioSessionControl ctrl, bool isDefault)
    {
        try
        {
            string procName = ResolveProcessName(ctrl);
            string display = ResolveDisplayName(ctrl, procName);
            ImageSource? icon = TryLoadIcon(ctrl);
            return new AudioSession(ctrl, device.FriendlyName, isDefault, procName, display, icon);
        }
        catch
        {
            return null;
        }
    }

    internal static string ResolveProcessName(AudioSessionControl ctrl)
    {
        try
        {
            uint pid = ctrl.GetProcessID;
            if (pid == 0) return "System";
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName; // no ".exe"
        }
        catch
        {
            return "";
        }
    }

    private static string ResolveDisplayName(AudioSessionControl ctrl, string procName)
    {
        try
        {
            var d = ctrl.DisplayName;
            if (!string.IsNullOrWhiteSpace(d) && !d.StartsWith("@")) return d;
        }
        catch { }

        if (ctrl.GetProcessID == 0) return "System sounds";

        // Fall back to the main window title, then the process name.
        try
        {
            using var p = Process.GetProcessById((int)ctrl.GetProcessID);
            if (!string.IsNullOrWhiteSpace(p.MainWindowTitle))
                return p.MainWindowTitle;
        }
        catch { }

        return string.IsNullOrWhiteSpace(procName) ? "Unknown" : procName;
    }

    private static ImageSource? TryLoadIcon(AudioSessionControl ctrl)
    {
        try
        {
            uint pid = ctrl.GetProcessID;
            if (pid == 0) return null;
            using var p = Process.GetProcessById((int)pid);
            string? exe = p.MainModule?.FileName;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;

            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (ico is null) return null;

            var img = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                ico.Handle,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            img.Freeze();
            return img;
        }
        catch
        {
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
