using System.ComponentModel;
using System.IO;
using System.Windows;
using Remielle.ChatGPTObserver;
using Remielle.Core;

namespace Remielle.Overlay;

public partial class App : Application
{
    private readonly CancellationTokenSource _shutdown = new();
    private WidgetStateMachine? _stateMachine;
    private ChatGptObserver? _observer;
    private AppLog? _log;
    private bool _shutdownInProgress;
    private bool _shutdownReady;

    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);

        var settings = SettingsStore.Load();
        _log = new AppLog(settings.LoggingEnabled);
        _log.Write("application_started");

        _stateMachine = new WidgetStateMachine();
        _observer = new ChatGptObserver(
            Path.Combine(AppContext.BaseDirectory, "UiSelectors.json"),
            _log.Write);
        _observer.EventObserved += _stateMachine.Handle;

        MainWindow = new MainWindow(_stateMachine, settings, _log);
        MainWindow.Closing += OnMainWindowClosing;
        MainWindow.Show();

        _ = _observer.RunAsync(_shutdown.Token).ContinueWith(
            task => _log.Write($"observer_failed:{task.Exception?.GetBaseException().GetType().Name}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs args)
    {
        if (_shutdownReady)
        {
            return;
        }

        args.Cancel = true;
        if (_shutdownInProgress)
        {
            return;
        }

        _shutdownInProgress = true;
        if (_observer is not null && _stateMachine is not null)
        {
            _observer.EventObserved -= _stateMachine.Handle;
        }

        _shutdown.Cancel();
        try
        {
            if (_observer is not null)
            {
                await _observer.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            _log?.Write($"observer_shutdown_error:{exception.GetType().Name}");
        }

        _stateMachine?.Dispose();
        _log?.Write("application_closed");
        _shutdownReady = true;
        MainWindow?.Close();
    }

    protected override void OnExit(ExitEventArgs args)
    {
        if (!_shutdown.IsCancellationRequested)
        {
            _shutdown.Cancel();
        }

        _shutdown.Dispose();
        base.OnExit(args);
    }
}
