using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Remielle.Core;

namespace Remielle.Overlay;

public partial class MainWindow : Window
{
    private const double MinimumSize = 60;
    private const double MaximumSize = 220;
    private const double SizeStep = 12;

    private readonly WidgetStateMachine _stateMachine;
    private readonly AppSettings _settings;
    private readonly AppLog _log;
    private WidgetState? _displayedState;
#if DEBUG
    private bool _simulating;
#endif

    internal MainWindow(
        WidgetStateMachine stateMachine,
        AppSettings settings,
        AppLog log)
    {
        InitializeComponent();
        _stateMachine = stateMachine;
        _settings = settings;
        _log = log;

        var size = Math.Clamp(double.IsFinite(settings.Size) ? settings.Size : 120, MinimumSize, MaximumSize);
        Width = size;
        Height = size;
        ContextMenu = BuildContextMenu();

        Loaded += OnLoaded;
        Closed += OnClosed;
        _stateMachine.StateChanged += OnStateChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        RestorePosition();
        ShowState(_stateMachine.CurrentState);
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        SaveSettings();
        _stateMachine.StateChanged -= OnStateChanged;
        Animation.Dispose();
    }

    private void OnStateChanged(WidgetState state)
    {
        _log.Write($"state_changed:{state}");
        Dispatcher.BeginInvoke(() =>
        {
#if DEBUG
            if (_simulating)
            {
                return;
            }
#endif
            ShowState(state);
        });
    }

    private void ShowState(WidgetState state)
    {
        if (_displayedState == state)
        {
            return;
        }

        var fileName = state switch
        {
            WidgetState.Waiting => "waiting_user_input.gif",
            WidgetState.UserTyping => "user_typing.gif",
            WidgetState.AiThinking => "ai_thingking.gif",
            WidgetState.AiTyping => "ai_typing.gif",
            WidgetState.AiComplete => "ai_complete_answer.gif",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

        try
        {
            Animation.Load(Path.Combine(AppContext.BaseDirectory, "Assets", fileName));
            _displayedState = state;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            _log.Write($"asset_load_error:{fileName}:{exception.GetType().Name}");
            if (state != WidgetState.Waiting)
            {
                _displayedState = null;
                ShowState(WidgetState.Waiting);
            }
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (args.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }

        ClampPosition();
        SaveSettings();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs args)
    {
        var size = Math.Clamp(Width + (args.Delta > 0 ? SizeStep : -SizeStep), MinimumSize, MaximumSize);
        Width = size;
        Height = size;
        ClampPosition();
        SaveSettings();
        args.Handled = true;
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var startup = new MenuItem
        {
            Header = "Auto-open with ChatGPT/Codex",
            IsCheckable = true,
            IsChecked = _settings.StartWithWindows
        };
        startup.Click += (_, _) =>
        {
            if (!StartupRegistration.SetEnabled(startup.IsChecked))
            {
                startup.IsChecked = !startup.IsChecked;
                _log.Write("startup_registration_error");
                return;
            }

            _settings.StartWithWindows = startup.IsChecked;
            SaveSettings();
        };
        menu.Items.Add(startup);
        menu.Items.Add(new Separator());

        var logging = new MenuItem
        {
            Header = "Diagnostic logging",
            IsCheckable = true,
            IsChecked = _log.Enabled
        };
        logging.Click += (_, _) =>
        {
            if (logging.IsChecked)
            {
                _log.Enabled = true;
                _log.Write("logging_enabled");
            }
            else
            {
                _log.Write("logging_disabled");
                _log.Enabled = false;
            }

            _settings.LoggingEnabled = logging.IsChecked;
            SaveSettings();
        };
        menu.Items.Add(logging);

        var reset = new MenuItem { Header = "Reset position" };
        reset.Click += (_, _) =>
        {
            MoveToDefaultPosition();
            SaveSettings();
        };
        menu.Items.Add(reset);

#if DEBUG
        var simulator = new MenuItem { Header = "Debug state simulator" };
        foreach (var state in Enum.GetValues<WidgetState>())
        {
            var item = new MenuItem { Header = state.ToString(), Tag = state };
            item.Click += (_, _) =>
            {
                _simulating = true;
                _displayedState = null;
                ShowState((WidgetState)item.Tag);
                _log.Write($"simulator_state:{item.Tag}");
            };
            simulator.Items.Add(item);
        }

        simulator.Items.Add(new Separator());
        var resume = new MenuItem { Header = "Resume observer" };
        resume.Click += (_, _) =>
        {
            _simulating = false;
            _displayedState = null;
            ShowState(_stateMachine.CurrentState);
        };
        simulator.Items.Add(resume);
        menu.Items.Add(simulator);
#endif

        menu.Items.Add(new Separator());
        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => Close();
        menu.Items.Add(exit);
        return menu;
    }

    private void RestorePosition()
    {
        if (_settings.Left is double left
            && _settings.Top is double top
            && double.IsFinite(left)
            && double.IsFinite(top))
        {
            Left = left;
            Top = top;
            ClampPosition();
            return;
        }

        MoveToDefaultPosition();
    }

    private void MoveToDefaultPosition()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - Height - 24;
        ClampPosition();
    }

    private void ClampPosition()
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;

        Left = Math.Clamp(double.IsFinite(Left) ? Left : left, left, Math.Max(left, right - Width));
        Top = Math.Clamp(double.IsFinite(Top) ? Top : top, top, Math.Max(top, bottom - Height));
    }

    private void SaveSettings()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Size = Width;
        if (!SettingsStore.Save(_settings))
        {
            _log.Write("settings_save_error");
        }
    }
}
