using System.IO;
using System.Text.Json;

namespace Remielle.ChatGPTObserver;

public sealed class UiSelectors
{
    public string[] ProcessNames { get; init; } = [];
    public string[] WindowTitleContains { get; init; } = [];
    public UiMatcher[] Window { get; init; } = [];
    public UiMatcher[] Composer { get; init; } = [];
    public UiMatcher[] Send { get; init; } = [];
    public UiMatcher[] Stop { get; init; } = [];
    public UiMatcher[] Assistant { get; init; } = [];

    public static UiSelectors Load(string path, Action<string> log)
    {
        try
        {
            var selectors = JsonSerializer.Deserialize<UiSelectors>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (selectors is not null && selectors.IsValid())
            {
                return selectors;
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        log("selector_config_invalid_using_fallback");
        return Defaults();
    }

    private bool IsValid() =>
        ProcessNames.Any(name => !string.IsNullOrWhiteSpace(name))
        && Lists().All(list => list.Length > 0 && list.All(matcher => !matcher.IsEmpty));

    private IEnumerable<UiMatcher[]> Lists()
    {
        yield return Window;
        yield return Composer;
        yield return Send;
        yield return Stop;
        yield return Assistant;
    }

    private static UiSelectors Defaults() =>
        new()
        {
            ProcessNames = ["ChatGPT", "Codex"],
            WindowTitleContains = ["ChatGPT", "Codex"],
            Window =
            [
                new() { ControlType = "Window", ClassName = "Chrome_WidgetWin_1" },
                new() { ControlType = "Window" }
            ],
            Composer =
            [
                new() { AutomationId = "prompt-textarea" },
                new() { ClassNameContains = "ProseMirror", ControlType = "Edit" },
                new() { NameContains = "Message", ControlType = "Edit" },
                new() { NameContains = "Ask", ControlType = "Edit" },
                new() { NameContains = "Message", ControlType = "Document" },
                new() { NameContains = "Ask", ControlType = "Document" }
            ],
            Send =
            [
                new()
                {
                    NameContains = "Send",
                    ClassNameContains = "button-composer",
                    ControlType = "Button"
                }
            ],
            Stop =
            [
                new()
                {
                    NameContains = "Stop",
                    ClassNameContains = "button-composer",
                    ControlType = "Button"
                }
            ],
            Assistant =
            [
                new() { AutomationId = "conversation-turn" },
                new() { NameContains = "Assistant", ControlType = "Document" },
                new() { NameContains = "Assistant", ControlType = "Group" },
                new() { AutomationId = "RootWebArea", ControlType = "Document" }
            ]
        };
}

public sealed class UiMatcher
{
    public string? AutomationId { get; init; }
    public string? NameContains { get; init; }
    public string? ControlType { get; init; }
    public string? ClassName { get; init; }
    public string? ClassNameContains { get; init; }

    internal bool IsEmpty =>
        string.IsNullOrWhiteSpace(AutomationId)
        && string.IsNullOrWhiteSpace(NameContains)
        && string.IsNullOrWhiteSpace(ControlType)
        && string.IsNullOrWhiteSpace(ClassName)
        && string.IsNullOrWhiteSpace(ClassNameContains);
}
