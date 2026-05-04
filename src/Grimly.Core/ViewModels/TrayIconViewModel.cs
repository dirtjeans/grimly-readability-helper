using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Grimly.ViewModels;

public partial class TrayIconViewModel : ObservableObject
{
    [ObservableProperty]
    private string _toolTipText = "Grimly (Ctrl+Alt+G)";

    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke();
}
