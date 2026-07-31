using System.IO;
using System.Text.Json;

namespace Remielle.Overlay;

internal sealed class AppSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Size { get; set; } = 120;
    public bool LoggingEnabled { get; set; }
}

internal static class AppPaths
{
    public static readonly string Directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Remielle Widget");

    public static readonly string Settings = Path.Combine(Directory, "settings.json");
    public static readonly string Log = Path.Combine(Directory, "remielle.log");
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(AppPaths.Settings)
                ? JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(AppPaths.Settings),
                    JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public static bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Directory);
            File.WriteAllText(AppPaths.Settings, JsonSerializer.Serialize(settings, JsonOptions));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
