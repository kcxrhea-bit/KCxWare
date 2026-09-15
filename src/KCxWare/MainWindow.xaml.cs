using System.Windows;
using KCxWare.Core.Models;
using KCxWare.ViewModels;

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
        Loaded += async (_, _) => await _viewModel.RefreshAsync();
    }

    private void GamingClick(object sender, RoutedEventArgs e) => ConfirmAndRestart(MachineMode.Gaming);
    private void ProgrammingClick(object sender, RoutedEventArgs e) => ConfirmAndRestart(MachineMode.Programming);
    private void NormalClick(object sender, RoutedEventArgs e) => ConfirmAndRestart(MachineMode.Normal);

    private void ConfirmAndRestart(MachineMode mode)
    {
        var details = mode switch
        {
            MachineMode.Gaming => "Windows will restart into a lean gaming session. Supported development, sync, indexing, overlay, and background workloads will be stopped for the session. Security, networking, audio, display, input, game services, and anti-cheat remain protected.",
            MachineMode.Programming => "Windows will restart into Programming Mode. Services previously stopped by KCxWare will be restored and a development-oriented installed power plan selected. Heavy tools are not launched automatically.",
            _ => "Windows will restart into Normal Mode. KCxWare-restored services and normal balanced power behavior will return; heavy development tools are not launched automatically."
        };

        var answer = MessageBox.Show($"{details}\n\nUnsaved work in other applications may be lost. Restart now?",
            $"Restart to {mode} Mode", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        RunHelper($"arm {mode} --reboot", true);
    }

    private void RecoverClick(object sender, RoutedEventArgs e) => RunHelper("recover", false);
    private void CancelClick(object sender, RoutedEventArgs e) => RunHelper("cancel", false);

    private void RunHelper(string command, bool closeAfterStart)
    {
        try
        {
            _viewModel.StartElevated(command);
            if (closeAfterStart) Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "KCxWare", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
