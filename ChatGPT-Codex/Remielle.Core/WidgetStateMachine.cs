using System.Diagnostics;

namespace Remielle.Core;

public sealed class WidgetStateMachine : IDisposable
{
    private readonly object _sync = new();
    private readonly TimeSpan _composerDebounce;
    private readonly TimeSpan _cooldown;
    private readonly TimeSpan _completeDuration;

    private WidgetState _currentState = WidgetState.Waiting;
    private CancellationTokenSource? _pendingTransition;
    private CancellationTokenSource? _completionReset;
    private long _transitionVersion;
    private long _resetVersion;
    private long _lastTransitionTimestamp;
    private bool _disposed;

    public WidgetStateMachine(
        TimeSpan? composerDebounce = null,
        TimeSpan? cooldown = null,
        TimeSpan? completeDuration = null)
    {
        _composerDebounce = Validate(composerDebounce ?? TimeSpan.FromMilliseconds(150));
        _cooldown = Validate(cooldown ?? TimeSpan.FromMilliseconds(100));
        _completeDuration = Validate(completeDuration ?? TimeSpan.FromSeconds(3));
    }

    public event Action<WidgetState>? StateChanged;

    public WidgetState CurrentState
    {
        get
        {
            lock (_sync)
            {
                return _currentState;
            }
        }
    }

    public void Handle(WidgetEvent widgetEvent)
    {
        WidgetState? target;
        WidgetState expected;
        TimeSpan delay;
        long version;
        CancellationTokenSource? timer = null;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (widgetEvent.Kind is WidgetEventKind.ComposerActivated
                or WidgetEventKind.PromptSubmitted
                or WidgetEventKind.ObserverDisconnected)
            {
                CancelResetLocked();
            }

            _pendingTransition?.Cancel();
            _pendingTransition = null;
            version = ++_transitionVersion;
            expected = _currentState;
            target = ResolveTarget(expected, widgetEvent.Kind);
            if (target is null || target == expected)
            {
                return;
            }

            delay = widgetEvent.Kind switch
            {
                WidgetEventKind.ComposerActivated or WidgetEventKind.ComposerCleared
                    => _composerDebounce,
                WidgetEventKind.AiOutputChanged or WidgetEventKind.AiPaused
                    => RemainingCooldown(),
                _ => TimeSpan.Zero
            };

            if (delay > TimeSpan.Zero)
            {
                timer = new CancellationTokenSource();
                _pendingTransition = timer;
            }
        }

        if (timer is null)
        {
            ChangeState(target.Value, expected, version);
        }
        else
        {
            _ = ChangeStateAfterAsync(target.Value, expected, delay, version, timer);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _transitionVersion++;
            _pendingTransition?.Cancel();
            _pendingTransition = null;
            CancelResetLocked();
        }
    }

    private static TimeSpan Validate(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return value;
    }

    private static WidgetState? ResolveTarget(WidgetState state, WidgetEventKind eventKind) =>
        (state, eventKind) switch
        {
            (WidgetState.Waiting or WidgetState.AiComplete, WidgetEventKind.ComposerActivated)
                => WidgetState.UserTyping,
            (WidgetState.UserTyping, WidgetEventKind.ComposerCleared)
                => WidgetState.Waiting,
            (WidgetState.Waiting or WidgetState.UserTyping or WidgetState.AiComplete,
                WidgetEventKind.PromptSubmitted)
                => WidgetState.AiThinking,
            (WidgetState.AiThinking, WidgetEventKind.AiOutputChanged)
                => WidgetState.AiTyping,
            (WidgetState.AiTyping, WidgetEventKind.AiPaused)
                => WidgetState.AiThinking,
            (WidgetState.AiThinking or WidgetState.AiTyping, WidgetEventKind.AiCompleted)
                => WidgetState.AiComplete,
            (_, WidgetEventKind.ObserverDisconnected)
                => WidgetState.Waiting,
            _ => null
        };

    private TimeSpan RemainingCooldown()
    {
        if (_lastTransitionTimestamp == 0)
        {
            return TimeSpan.Zero;
        }

        var elapsed = Stopwatch.GetElapsedTime(_lastTransitionTimestamp);
        return elapsed >= _cooldown ? TimeSpan.Zero : _cooldown - elapsed;
    }

    private async Task ChangeStateAfterAsync(
        WidgetState target,
        WidgetState expected,
        TimeSpan delay,
        long version,
        CancellationTokenSource timer)
    {
        try
        {
            await Task.Delay(delay, timer.Token).ConfigureAwait(false);
            ChangeState(target, expected, version);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_pendingTransition, timer))
                {
                    _pendingTransition = null;
                }
            }

            timer.Dispose();
        }
    }

    private void ChangeState(WidgetState target, WidgetState expected, long version)
    {
        Action<WidgetState>? changed;

        lock (_sync)
        {
            if (_disposed
                || version != _transitionVersion
                || _currentState != expected
                || _currentState == target)
            {
                return;
            }

            _currentState = target;
            _lastTransitionTimestamp = Stopwatch.GetTimestamp();

            if (target == WidgetState.AiComplete)
            {
                ScheduleResetLocked();
            }
            else
            {
                CancelResetLocked();
            }

            changed = StateChanged;
        }

        changed?.Invoke(target);
    }

    private void ScheduleResetLocked()
    {
        CancelResetLocked();
        var timer = new CancellationTokenSource();
        _completionReset = timer;
        var version = ++_resetVersion;
        _ = ResetAfterAsync(version, timer);
    }

    private async Task ResetAfterAsync(long version, CancellationTokenSource timer)
    {
        try
        {
            await Task.Delay(_completeDuration, timer.Token).ConfigureAwait(false);

            Action<WidgetState>? changed;
            lock (_sync)
            {
                if (_disposed
                    || version != _resetVersion
                    || _currentState != WidgetState.AiComplete)
                {
                    return;
                }

                _currentState = WidgetState.Waiting;
                _lastTransitionTimestamp = Stopwatch.GetTimestamp();
                changed = StateChanged;
            }

            changed?.Invoke(WidgetState.Waiting);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_completionReset, timer))
                {
                    _completionReset = null;
                }
            }

            timer.Dispose();
        }
    }

    private void CancelResetLocked()
    {
        _resetVersion++;
        _completionReset?.Cancel();
        _completionReset = null;
    }
}
