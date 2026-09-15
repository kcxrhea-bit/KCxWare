using System.Windows;
using System.Windows.Threading;
using KCxWare.Core.Loading;
using KCxWare.Core.Models;
using KCxWare.ViewModels;
using KCxWare.Views;

namespace KCxWare;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        LoadingHost.DataContext = new LoadingViewModel(_viewModel.LoadingCoordinator);
        Loaded += async (_, _) => await _viewModel.RefreshAsync();
    }

    private async void GamingClick(object sender, RoutedEventArgs e) => await ConfirmAndRestartAsync(MachineMode.Gaming);
    private async void ProgrammingClick(object sender, RoutedEventArgs e) => await ConfirmAndRestartAsync(MachineMode.Programming);
    private async void NormalClick(object sender, RoutedEventArgs e) => await ConfirmAndRestartAsync(MachineMode.Normal);

    private async Task ConfirmAndRestartAsync(MachineMode mode)
    {
        // Reject duplicate transitions while one is already in flight.
        if (_viewModel.IsBusy) return;

        var details = mode switch
        {
            MachineMode.Gaming => "Windows will restart into a lean gaming session. Supported development, sync, indexing, overlay, and background workloads will be stopped for the session. Security, networking, audio, display, input, game services, and anti-cheat remain protected.",
            MachineMode.Programming => "Windows will restart into Programming Mode. Services previously stopped by KCxWare will be restored and a development-oriented installed power plan selected. Heavy tools are not launched automatically.",
            _ => "Windows will restart into Normal Mode. KCxWare-restored services and normal balanced power behavior will return; heavy development tools are not launched automatically."
        };

        var answer = MessageBox.Show($"{details}\n\nUnsaved work in other applications may be lost. Restart now?",
            $"Restart to {mode} Mode", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        // Set the busy/visible loading state before touching the helper, and let the dispatcher
        // actually paint the overlay before the UAC elevation prompt takes focus.
        using var op = _viewModel.LoadingCoordinator.Begin(
            $"{mode} Mode", $"Requesting Windows elevation for {mode} Mode…",
            indeterminate: true, isGamingRelated: mode == MachineMode.Gaming);
        await Dispatcher.Yield(DispatcherPriority.Render);

        await RunElevatedTransitionAsync($"arm {mode} --reboot", closeAfterSuccess: true, op);
    }

    private async void RecoverClick(object sender, RoutedEventArgs e) => await RunRecoveryAsync();

    private async void CancelClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;

        using var op = _viewModel.LoadingCoordinator.Begin("Cancel Transition", "Cancelling armed transition…");
        await Dispatcher.Yield(DispatcherPriority.Render);

        await RunElevatedTransitionAsync("cancel", closeAfterSuccess: false, op);
        if (!op.IsFailed) await _viewModel.RefreshAsync();
    }

    private async Task RunRecoveryAsync()
    {
        if (_viewModel.IsBusy) return;

        using var op = _viewModel.LoadingCoordinator.Begin(
            "Safe Recovery", "Verifying KCx Gaming environment and restoring services…");
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            var exitCode = await _viewModel.RunElevatedAsync("recover");
            if (exitCode != 0)
            {
                op.Fail($"Recovery helper exited with code {exitCode}. See helper.log for details.");
                MessageBox.Show(op.ErrorMessage, "KCxWare", MessageBoxButton.OK, MessageBoxImage.Error);
                await _viewModel.RefreshAsync();
                return;
            }

            op.Update("Refreshing KCxWare state…");
            await _viewModel.RefreshAsync();
            op.Complete("Recovery complete.");
        }
        catch (Exception exception)
        {
            op.Fail(exception.Message);
            MessageBox.Show(exception.Message, "KCxWare", MessageBoxButton.OK, MessageBoxImage.Error);
            await _viewModel.RefreshAsync();
        }
    }

    /// <summary>
    /// Launches the elevated helper and awaits its exit asynchronously (never blocking the UI
    /// thread), reporting truthful status/progress instead of assuming success. Only on a
    /// verified zero exit code does the operation complete and, for reboot transitions, the
    /// window close; any other outcome surfaces as a real failure with the helper's exit code.
    /// </summary>
    private async Task RunElevatedTransitionAsync(string command, bool closeAfterSuccess, LoadingHandle op)
    {
        try
        {
            var exitCode = await _viewModel.RunElevatedAsync(command);
            if (exitCode != 0)
            {
                op.Fail($"KCxWare.Helper exited with code {exitCode}. See helper.log for details.");
                MessageBox.Show(op.ErrorMessage, "KCxWare", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            op.Complete("Elevation granted.");
            if (closeAfterSuccess) Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            op.Fail(exception.Message);
            MessageBox.Show(exception.Message, "KCxWare", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
