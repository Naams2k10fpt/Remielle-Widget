using System.IO;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;

namespace Remielle.Overlay;

internal sealed class AppSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Size { get; set; } = 120;
    public bool LoggingEnabled { get; set; }
    public bool StartWithWindows { get; set; } = true;
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

internal static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Remielle Widget";

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            using var startupKey = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (startupKey is null)
            {
                return false;
            }

            startupKey.SetValue(
                ValueName,
                CommandFor(executablePath),
                RegistryValueKind.String);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    internal static string CommandFor(string executablePath) =>
        $"\"{executablePath}\" --background";
}
