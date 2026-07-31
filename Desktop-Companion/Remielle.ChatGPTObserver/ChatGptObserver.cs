using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Remielle.Core;

namespace Remielle.ChatGPTObserver;

public sealed class ChatGptObserver : IWidgetObserver
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OutputIdleDuration = TimeSpan.FromMilliseconds(1200);

    private readonly object _runSync = new();
    private readonly UiSelectors _selectors;
    private readonly Action<string> _log;
    private readonly AutomationFocusChangedEventHandler _focusHandler;
    private readonly AutomationEventHandler _composerTextHandler;
    private readonly AutomationPropertyChangedEventHandler _composerPropertyHandler;
    private readonly AutomationEventHandler _assistantTextHandler;
    private readonly StructureChangedEventHandler _assistantStructureHandler;

    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private AutomationElement? _window;
    private AutomationElement? _composer;
    private AutomationElement? _assistant;
    private bool _connected;
    private bool? _targetAvailable;
    private bool _busy;
    private bool _outputSeen;
    private bool _pauseEmitted;
    private bool _contentUnavailableLogged;
    private bool _sendLogged;
    private bool _stopLogged;
    private bool? _composerActive;
    private bool _pendingDisconnect;
    private DateTimeOffset _lastOutputAt;
    private DateTimeOffset? _busyMissingSince;
    private bool _disposed;

    public ChatGptObserver(string selectorPath, Action<string>? log = null)
    {
        _log = log ?? (_ => { });
        _selectors = UiSelectors.Load(selectorPath, _log);
        _focusHandler = OnFocusChanged;
        _composerTextHandler = OnComposerTextChanged;
        _composerPropertyHandler = OnComposerPropertyChanged;
        _assistantTextHandler = OnAssistantTextChanged;
        _assistantStructureHandler = OnAssistantStructureChanged;
    }

    public event Action<WidgetEvent>? EventObserved;
    public event Action<bool>? TargetAvailabilityChanged;

    public Task RunAsync(CancellationToken cancellationToken)
    {
        lock (_runSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is not null)
            {
                throw new InvalidOperationException("Observer is already running.");
            }

            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runTask = Task.Run(() => ObserveLoopAsync(_runCancellation.Token));
            return _runTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? task;
        lock (_runSync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _runCancellation?.Cancel();
            task = _runTask;
        }

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        Disconnect(emitEvent: false);
        _runCancellation?.Dispose();
    }

    private async Task ObserveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_window is null)
                {
                    Connect();
                    if (_window is null)
                    {
                        if (_targetAvailable.HasValue || _pendingDisconnect)
                        {
                            SetTargetAvailability(false);
                            if (_pendingDisconnect)
                            {
                                _pendingDisconnect = false;
                                _log("observer_disconnected");
                                Emit(WidgetEventKind.ObserverDisconnected);
                            }
                        }

                        await Task.Delay(ReconnectInterval, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                if (_pendingDisconnect)
                {
                    _pendingDisconnect = false;
                    _log("observer_reconnected");
                    Emit(WidgetEventKind.ObserverDisconnected);
                }

                Poll();
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (IsUiaFailure(exception))
            {
                _log($"uia_error:{exception.GetType().Name}");
                Disconnect(emitEvent: true);
                await Task.Delay(ReconnectInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void Connect()
    {
        var processIds = GetTargetProcessIds();
        if (processIds.Count == 0)
        {
            return;
        }

        var candidates = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            Condition.TrueCondition);

        AutomationElement? first = null;
        AutomationElement? preferred = null;

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!processIds.Contains(candidate.Current.ProcessId)
                || !MatchesAny(candidate, _selectors.Window, out _))
            {
                continue;
            }

            first ??= candidate;
            if (_selectors.WindowTitleContains.Any(
                    text => candidate.Current.Name.Contains(text, StringComparison.OrdinalIgnoreCase)))
            {
                preferred = candidate;
                break;
            }
        }

        // ponytail: observe one ChatGPT/Codex window; add per-window observers if multi-window use matters.
        _window = preferred ?? first;
        if (_window is null)
        {
            return;
        }

        _connected = true;
        Automation.AddAutomationFocusChangedEventHandler(_focusHandler);
        _log("chatgpt_window_detected");
        SetTargetAvailability(IsTargetVisible(_window));
    }

    private HashSet<int> GetTargetProcessIds()
    {
        var result = new HashSet<int>();
        foreach (var configuredName in _selectors.ProcessNames)
        {
            var processName = Path.GetFileNameWithoutExtension(configuredName.Trim());
            foreach (var process in Process.GetProcessesByName(processName))
            {
                result.Add(process.Id);
                process.Dispose();
            }
        }

        return result;
    }

    private void Poll()
    {
        if (_window is null)
        {
            return;
        }

        _ = _window.Current.ProcessId;
        var targetVisible = IsTargetVisible(_window);
        SetTargetAvailability(targetVisible);
        if (!targetVisible)
        {
            return;
        }

        var descendants = _window.FindAll(TreeScope.Descendants, Condition.TrueCondition);

        var composer = FindElement(descendants, _selectors.Composer, out var composerIndex);
        var assistant = FindElement(descendants, _selectors.Assistant, out var assistantIndex);
        var stop = FindElement(descendants, _selectors.Stop, out var stopIndex);
        var send = FindElement(descendants, _selectors.Send, out var sendIndex);

        UpdateComposer(composer, composerIndex);
        UpdateAssistant(assistant, assistantIndex);

        if (stop is not null && !_stopLogged)
        {
            _stopLogged = true;
            _log($"stop_detected:selector_{stopIndex}");
        }

        if (send is not null && !_sendLogged)
        {
            _sendLogged = true;
            _log($"send_detected:selector_{sendIndex}");
        }

        var anyContent = composer is not null || assistant is not null || stop is not null || send is not null;
        if (!anyContent && !_contentUnavailableLogged)
        {
            _contentUnavailableLogged = true;
            _log("uia_content_tree_unavailable");
        }

        if (composer is not null)
        {
            ObserveComposer(composer);
        }

        var busyNow = stop is not null;
        ObserveBusyState(busyNow);
    }

    private void ObserveBusyState(bool busyNow)
    {
        var now = DateTimeOffset.UtcNow;

        if (busyNow)
        {
            _busyMissingSince = null;
            if (!_busy)
            {
                _busy = true;
                _outputSeen = false;
                _pauseEmitted = false;
                Emit(WidgetEventKind.PromptSubmitted);
            }
            else if (_outputSeen
                     && !_pauseEmitted
                     && now - _lastOutputAt >= OutputIdleDuration)
            {
                _pauseEmitted = true;
                Emit(WidgetEventKind.AiPaused);
            }

            return;
        }

        if (!_busy)
        {
            _busyMissingSince = null;
            return;
        }

        _busyMissingSince ??= now;
        if (now - _busyMissingSince < PollInterval)
        {
            return;
        }

        _busy = false;
        _busyMissingSince = null;
        _outputSeen = false;
        _pauseEmitted = false;
        Emit(WidgetEventKind.AiCompleted);
    }

    private void UpdateComposer(AutomationElement? next, int selectorIndex)
    {
        if (SameElement(_composer, next))
        {
            return;
        }

        RemoveComposerHandlers();
        _composer = next;
        _composerActive = null;
        if (next is null)
        {
            return;
        }

        Automation.AddAutomationEventHandler(
            TextPattern.TextChangedEvent,
            next,
            TreeScope.Element,
            _composerTextHandler);
        Automation.AddAutomationPropertyChangedEventHandler(
            next,
            TreeScope.Element,
            _composerPropertyHandler,
            ValuePattern.ValueProperty,
            AutomationElement.HasKeyboardFocusProperty);
        _log($"composer_detected:selector_{selectorIndex}");
    }

    private void UpdateAssistant(AutomationElement? next, int selectorIndex)
    {
        if (SameElement(_assistant, next))
        {
            return;
        }

        RemoveAssistantHandlers();
        _assistant = next;
        if (next is null)
        {
            return;
        }

        Automation.AddAutomationEventHandler(
            TextPattern.TextChangedEvent,
            next,
            TreeScope.Subtree,
            _assistantTextHandler);
        Automation.AddStructureChangedEventHandler(
            next,
            TreeScope.Subtree,
            _assistantStructureHandler);
        _log($"assistant_detected:selector_{selectorIndex}");

        if (_busy)
        {
            RecordOutputChange();
        }
    }

    private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs args)
    {
        try
        {
            if (sender is AutomationElement element
                && _window is not null
                && element.Current.ProcessId == _window.Current.ProcessId
                && MatchesAny(element, _selectors.Composer, out _))
            {
                ObserveComposer(element);
            }
            else if (_composer is not null)
            {
                ObserveComposer(_composer);
            }
        }
        catch (Exception exception) when (IsUiaFailure(exception))
        {
            _log($"uia_focus_error:{exception.GetType().Name}");
        }
    }

    private void OnComposerTextChanged(object sender, AutomationEventArgs args)
    {
        if (sender is AutomationElement element)
        {
            ObserveComposerSafely(element);
        }
    }

    private void OnComposerPropertyChanged(object sender, AutomationPropertyChangedEventArgs args)
    {
        if (sender is AutomationElement element)
        {
            ObserveComposerSafely(element);
        }
    }

    private void ObserveComposerSafely(AutomationElement element)
    {
        try
        {
            ObserveComposer(element);
        }
        catch (Exception exception) when (IsUiaFailure(exception))
        {
            _log($"uia_composer_error:{exception.GetType().Name}");
        }
    }

    private void ObserveComposer(AutomationElement element)
    {
        var active = element.Current.HasKeyboardFocus && HasText(element);
        if (_composerActive == active)
        {
            return;
        }

        _composerActive = active;
        Emit(active ? WidgetEventKind.ComposerActivated : WidgetEventKind.ComposerCleared);
    }

    private static bool HasText(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPattern))
        {
            var sample = ((TextPattern)textPattern).DocumentRange.GetText(1);
            return !string.IsNullOrWhiteSpace(sample);
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern))
        {
            return !string.IsNullOrWhiteSpace(((ValuePattern)valuePattern).Current.Value);
        }

        return false;
    }

    private void OnAssistantTextChanged(object sender, AutomationEventArgs args) =>
        RecordOutputChangeSafely();

    private void OnAssistantStructureChanged(object sender, StructureChangedEventArgs args) =>
        RecordOutputChangeSafely();

    private void RecordOutputChangeSafely()
    {
        try
        {
            RecordOutputChange();
        }
        catch (Exception exception) when (IsUiaFailure(exception))
        {
            _log($"uia_assistant_error:{exception.GetType().Name}");
        }
    }

    private void RecordOutputChange()
    {
        if (!_busy)
        {
            return;
        }

        _outputSeen = true;
        _pauseEmitted = false;
        _lastOutputAt = DateTimeOffset.UtcNow;
        Emit(WidgetEventKind.AiOutputChanged);
    }

    private void Disconnect(bool emitEvent)
    {
        if (!_connected && _window is null)
        {
            return;
        }

        try
        {
            Automation.RemoveAutomationFocusChangedEventHandler(_focusHandler);
            RemoveComposerHandlers();
            RemoveAssistantHandlers();
        }
        catch (Exception exception) when (IsUiaFailure(exception))
        {
            _log($"uia_disconnect_error:{exception.GetType().Name}");
        }

        _window = null;
        _composer = null;
        _assistant = null;
        _connected = false;
        _busy = false;
        _outputSeen = false;
        _pauseEmitted = false;
        _busyMissingSince = null;
        _composerActive = null;
        _contentUnavailableLogged = false;
        _sendLogged = false;
        _stopLogged = false;

        if (emitEvent)
        {
            _pendingDisconnect = true;
        }
    }

    private void RemoveComposerHandlers()
    {
        if (_composer is null)
        {
            return;
        }

        Automation.RemoveAutomationEventHandler(
            TextPattern.TextChangedEvent,
            _composer,
            _composerTextHandler);
        Automation.RemoveAutomationPropertyChangedEventHandler(
            _composer,
            _composerPropertyHandler);
    }

    private void RemoveAssistantHandlers()
    {
        if (_assistant is null)
        {
            return;
        }

        Automation.RemoveAutomationEventHandler(
            TextPattern.TextChangedEvent,
            _assistant,
            _assistantTextHandler);
        Automation.RemoveStructureChangedEventHandler(
            _assistant,
            _assistantStructureHandler);
    }

    private void Emit(WidgetEventKind kind) =>
        EventObserved?.Invoke(new WidgetEvent(kind));

    private void SetTargetAvailability(bool available)
    {
        if (_targetAvailable == available)
        {
            return;
        }

        _targetAvailable = available;
        TargetAvailabilityChanged?.Invoke(available);
    }

    private static bool IsTargetVisible(AutomationElement window)
    {
        var visualState = WindowVisualState.Normal;
        if (window.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern))
        {
            visualState = ((WindowPattern)pattern).Current.WindowVisualState;
        }

        return ShouldShowTarget(window.Current.IsOffscreen, visualState);
    }

    internal static bool ShouldShowTarget(bool isOffscreen, WindowVisualState visualState) =>
        !isOffscreen && visualState != WindowVisualState.Minimized;

    private static AutomationElement? FindElement(
        AutomationElementCollection elements,
        UiMatcher[] matchers,
        out int selectorIndex)
    {
        for (var matcherIndex = 0; matcherIndex < matchers.Length; matcherIndex++)
        {
            for (var elementIndex = 0; elementIndex < elements.Count; elementIndex++)
            {
                if (Matches(elements[elementIndex], matchers[matcherIndex]))
                {
                    selectorIndex = matcherIndex;
                    return elements[elementIndex];
                }
            }
        }

        selectorIndex = -1;
        return null;
    }

    private static bool MatchesAny(
        AutomationElement element,
        UiMatcher[] matchers,
        out int selectorIndex)
    {
        for (var i = 0; i < matchers.Length; i++)
        {
            if (Matches(element, matchers[i]))
            {
                selectorIndex = i;
                return true;
            }
        }

        selectorIndex = -1;
        return false;
    }

    private static bool Matches(AutomationElement element, UiMatcher matcher)
    {
        var current = element.Current;
        if (!string.IsNullOrWhiteSpace(matcher.AutomationId)
            && !string.Equals(current.AutomationId, matcher.AutomationId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(matcher.NameContains)
            && !current.Name.Contains(matcher.NameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(matcher.ClassName)
            && !string.Equals(current.ClassName, matcher.ClassName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(matcher.ClassNameContains)
            && !current.ClassName.Contains(matcher.ClassNameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(matcher.ControlType))
        {
            var controlType = current.ControlType.ProgrammaticName["ControlType.".Length..];
            if (!string.Equals(controlType, matcher.ControlType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameElement(AutomationElement? left, AutomationElement? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.GetRuntimeId().SequenceEqual(right.GetRuntimeId());
    }

    private static bool IsUiaFailure(Exception exception) =>
        exception is ElementNotAvailableException
            or InvalidOperationException
            or COMException
            or ArgumentException;
}
