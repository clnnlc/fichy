using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace VolumeMixer.Services;

public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl, string Notes);

/// <summary>
/// Checks GitHub releases for a newer build and, on request, replaces the
/// running executable with it.
///
/// Nothing is ever installed without the user asking: a check only reports what
/// is available.
/// </summary>
public static class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/clnnlc/fichy/releases/latest";
    private const string AssetName = "fichy.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub rejects requests without a User-Agent.
        c.DefaultRequestHeaders.Add("User-Agent", "fichy-updater");
        c.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        return c;
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>Returns the newer release, or null when up to date or unreachable.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken token = default)
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseApi, token);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (!TryParseTag(tag, out var version)) return null;

            // Compare on major/minor/build; the assembly revision is always 0.
            var current = CurrentVersion;
            if (version <= new Version(current.Major, current.Minor, current.Build)) return null;

            string? url = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (!string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    break;
                }
            }
            if (string.IsNullOrEmpty(url)) return null;

            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            return new UpdateInfo(version, tag, url, notes);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseTag(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var cleaned = tag.TrimStart('v', 'V');
        return Version.TryParse(cleaned, out version!);
    }

    /// <summary>
    /// Downloads the release and swaps it in for the running executable.
    /// Windows allows renaming a running image, so the old one is moved aside
    /// and deleted on the next start.
    /// </summary>
    public static async Task<string?> DownloadAndApplyAsync(UpdateInfo info, CancellationToken token = default)
    {
        string current = Environment.ProcessPath ?? "";
        if (string.IsNullOrEmpty(current) || !File.Exists(current))
            return "Cannot locate the running executable.";

        string dir = Path.GetDirectoryName(current)!;
        string staged = Path.Combine(Path.GetTempPath(), $"fichy_{info.Tag}.exe");

        try
        {
            using (var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                if (!response.IsSuccessStatusCode)
                    return $"Download failed ({(int)response.StatusCode}).";

                await using var src = await response.Content.ReadAsStreamAsync(token);
                await using var dst = File.Create(staged);
                await src.CopyToAsync(dst, token);
            }

            if (!LooksLikeExecutable(staged))
                return "The downloaded file is not a valid executable.";
        }
        catch (Exception ex)
        {
            TryDelete(staged);
            return $"Download failed: {ex.Message}";
        }

        string backup = Path.Combine(dir, $"{Path.GetFileName(current)}.old");
        try
        {
            TryDelete(backup);
            File.Move(current, backup);            // allowed even while running
            File.Move(staged, current);
        }
        catch (Exception ex)
        {
            // Put the original back if the swap only half happened.
            try { if (!File.Exists(current) && File.Exists(backup)) File.Move(backup, current); } catch { }
            TryDelete(staged);
            return $"Could not replace the program file: {ex.Message}";
        }

        try
        {
            Process.Start(new ProcessStartInfo(current) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            return $"Updated, but could not restart: {ex.Message}";
        }

        return null; // success — caller shuts down
    }

    /// <summary>Removes the previous executable left behind by an update.</summary>
    public static void CleanUpOldVersion()
    {
        try
        {
            string current = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(current)) return;
            TryDelete(current + ".old");
        }
        catch { }
    }

    private static bool LooksLikeExecutable(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 1_000_000) return false;

            using var fs = File.OpenRead(path);
            return fs.ReadByte() == 'M' && fs.ReadByte() == 'Z';
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
