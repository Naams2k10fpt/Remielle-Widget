using System.IO;

namespace Remielle.Overlay;

internal sealed class AppLog(bool enabled)
{
    private readonly object _sync = new();

    public bool Enabled { get; set; } = enabled;

    public void Write(string message)
    {
        lock (_sync)
        {
            if (!Enabled)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(AppPaths.Directory);
                File.AppendAllText(
                    AppPaths.Log,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
