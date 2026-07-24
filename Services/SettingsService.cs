using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VolumeMixer.Model;

namespace VolumeMixer.Services;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON under %AppData%\VolumeMixer.</summary>
public sealed class SettingsService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fichy");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AppSettings Current { get; private set; } = new();

    /// <summary>True if a settings file was found on disk during Load (i.e. not a first run).</summary>
    public bool WasLoadedFromDisk { get; private set; }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                WasLoadedFromDisk = true;
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (loaded is not null)
                    Current = loaded;
            }
        }
        catch
        {
            // Corrupt config → fall back to defaults rather than crashing.
            Current = new AppSettings();
        }
        return Current;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(Current, Options);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best effort; ignore transient IO errors.
        }
    }
}
