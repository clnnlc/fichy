using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace VolumeMixer.Services;

/// <summary>An output device the user can switch to.</summary>
public sealed record OutputDevice(string Id, string Name, bool IsDefault);

/// <summary>
/// Lists the active output devices and switches the system default.
///
/// Windows exposes no public API for setting the default endpoint — the
/// Settings app uses the internal IPolicyConfig interface, which is what this
/// calls. It has been stable since Windows 7, but it is undocumented, so every
/// call is treated as fallible and reported rather than assumed to work.
/// </summary>
public static class DeviceSwitcher
{
    public static List<OutputDevice> GetOutputDevices()
    {
        var list = new List<OutputDevice>();
        try
        {
            using var en = new MMDeviceEnumerator();
            string? defaultId = null;
            try { defaultId = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; } catch { }

            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try { list.Add(new OutputDevice(d.ID, d.FriendlyName, d.ID == defaultId)); }
                catch { }
                finally { d.Dispose(); }
            }
        }
        catch { }
        return list;
    }

    /// <summary>
    /// Makes <paramref name="deviceId"/> the default output for every role, so
    /// media and communication apps both follow. Returns false if Windows
    /// refused the change.
    /// </summary>
    public static bool SetDefault(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return false;

        object? client = null;
        try
        {
            var type = Type.GetTypeFromCLSID(PolicyConfigClsid);
            if (type is null) return false;

            client = Activator.CreateInstance(type);
            if (client is not IPolicyConfig config) return false;

            // eConsole = 0, eMultimedia = 1, eCommunications = 2
            for (int role = 0; role <= 2; role++)
            {
                int hr = config.SetDefaultEndpoint(deviceId, role);
                if (hr != 0) return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (client is not null && Marshal.IsComObject(client))
            {
                try { Marshal.ReleaseComObject(client); } catch { }
            }
        }
    }

    private static readonly Guid PolicyConfigClsid = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        // Only SetDefaultEndpoint is called. The preceding methods still have to
        // be declared so its vtable slot lands in the right place; their exact
        // parameter types don't matter as long as the arity and widths match.
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool isDefault, out IntPtr format);
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool isDefault, out long defaultPeriod, out long minimumPeriod);
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref long period);
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr mode);
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, out IntPtr value);
        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool visible);
    }
}
