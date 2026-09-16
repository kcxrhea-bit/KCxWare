using System.Windows;
using System.Windows.Threading;
using System.IO.Pipes;
using System.IO;
using System.Text.Json;
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
        Loaded += async (_, _) =>
        {
            await _viewModel.RefreshAsync();
            if (_viewModel.NeedsBootNormalization)
            {
                await _viewModel.NormalizeAfterBootAsync();
                await _viewModel.RefreshAsync();
            }
        };
    }

    private async void GamingClick(object sender, RoutedEventArgs e) => await ConfirmAndApplyAsync(MachineMode.Gaming);
    private async void ProgrammingClick(object sender, RoutedEventArgs e) => await ConfirmAndApplyAsync(MachineMode.Programming);
    private async void NormalClick(object sender, RoutedEventArgs e) => await ConfirmAndApplyAsync(MachineMode.Normal);

    private async Task ConfirmAndApplyAsync(MachineMode mode)
    {
        // Reject duplicate transitions while one is already in flight.
        if (_viewModel.IsBusy) return;

        var details = mode switch
        {
            MachineMode.Gaming => "KCxWare will apply a temporary Gaming session now. Supported development, sync, indexing, overlay, and background workloads will be stopped. Security, networking, audio, display, input, game services, and anti-cheat remain protected.",
            MachineMode.Programming => "KCxWare will apply Programming Mode now. Services previously stopped by KCxWare will be restored and a development-oriented installed power plan selected. Heavy tools are not launched automatically.",
            _ => "KCxWare will restore Normal Mode now. KCxWare-recorded baseline services and power behavior will return; heavy development tools are not launched automatically."
        };

        var answer = MessageBox.Show($"{details}\n\nApply now?",
            $"Apply {mode} Mode", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        // Set the busy/visible loading state before touching the helper, and let the dispatcher
        // actually paint the overlay before the UAC elevation prompt takes focus.
        using var op = _viewModel.LoadingCoordinator.Begin(
            $"{mode} Mode", $"Requesting Windows elevation for {mode} Mode…",
            indeterminate: true, isGamingRelated: mode == MachineMode.Gaming);
        await Dispatcher.Yield(DispatcherPriority.Render);

        using var progressServer = StartProgressServer(op);
        await RunElevatedTransitionAsync($"apply-live {mode} --progress {progressServer.PipeName}", closeAfterSuccess: false, op);
        if (!op.IsFailed) await _viewModel.RefreshAsync();
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

    private ProgressServer StartProgressServer(LoadingHandle operation)
    {
        var server = new ProgressServer(operation);
        server.Start();
        return server;
    }

    private sealed class ProgressServer(LoadingHandle operation) : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);
        public string PipeName { get; } = $"KCxWare.Progress.{Guid.NewGuid():N}";

        public void Start() => _ = ListenAsync();

        private async Task ListenAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    using var reader = new StreamReader(pipe);
                    var line = await reader.ReadLineAsync(_stop.Token);
                    var progress = line is null ? null : JsonSerializer.Deserialize<TransitionProgress>(line, _options);
                    if (progress is { Percent: >= 0 and <= 100 } && !string.IsNullOrWhiteSpace(progress.Status))
                    {
                        operation.Update(progress.Status, progress.Percent, false);
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
                catch (IOException) when (!_stop.IsCancellationRequested) { }
                catch (JsonException) { }
            }
        }

        public void Dispose() => _stop.Cancel();
    }
}
