namespace Remielle.Core;

public enum WidgetEventKind
{
    ComposerActivated,
    ComposerCleared,
    PromptSubmitted,
    AiOutputChanged,
    AiPaused,
    AiCompleted,
    ObserverDisconnected
}

public readonly record struct WidgetEvent(WidgetEventKind Kind);
