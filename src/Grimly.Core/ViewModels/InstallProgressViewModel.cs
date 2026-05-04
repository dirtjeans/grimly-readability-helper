using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grimly.Services;

namespace Grimly.ViewModels;

public partial class InstallProgressViewModel : ObservableObject
{
    private readonly IFoundryInstallerService _installer;
    private readonly CancellationTokenSource _cts = new();
    private readonly StringBuilder _log = new();

    [ObservableProperty]
    private string _title = "Install Foundry Local";

    [ObservableProperty]
    private string _stepText = "Preparing...";

    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    private bool _isInstalling = true;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private bool _detailsExpanded;

    public event Action<bool>? InstallCompleted; // bool = success
    public event Action? RequestClose;

    public InstallProgressViewModel(IFoundryInstallerService installer)
    {
        _installer = installer;
        _installer.StepChanged += OnStepChanged;
        _installer.LogLine += OnLogLine;
        _installer.StageChanged += OnStageChanged;
    }

    public async Task StartAsync()
    {
        await _installer.InstallAsync(_cts.Token);
    }

    private void OnStepChanged(string step)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => StepText = step);
    }

    private void OnLogLine(string line)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _log.AppendLine(line);
            LogText = _log.ToString();
        });
    }

    private void OnStageChanged(InstallStage stage)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (stage == InstallStage.Completed)
            {
                IsInstalling = false;
                IsSuccess = true;
                StepText = "Installation complete! Grimly is ready to use.";
                InstallCompleted?.Invoke(true);
            }
            else if (stage == InstallStage.Failed)
            {
                IsInstalling = false;
                IsFailed = true;
                DetailsExpanded = true;
                StepText = "Installation failed. See details below.";
                InstallCompleted?.Invoke(false);
            }
        });
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts.Cancel();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    [RelayCommand]
    private void ToggleDetails() => DetailsExpanded = !DetailsExpanded;
}
