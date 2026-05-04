using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grimly.Models;
using Grimly.Services;

namespace Grimly.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IFoundryManager _foundryManager;

    [ObservableProperty] private string _hotkeyModifiers = "Ctrl+Alt";
    [ObservableProperty] private string _hotkeyKey = "G";
    [ObservableProperty] private string _foundryEndpoint = "http://127.0.0.1:51318";
    [ObservableProperty] private string _modelName = "qwen2.5-7b-instruct-qnn-npu:2";
    [ObservableProperty] private EditingMode _defaultMode = EditingMode.FixGrammar;
    [ObservableProperty] private double _creativity = 0.5;
    [ObservableProperty] private int _maxTokens = 2048;
    [ObservableProperty] private double _popupOpacity = 0.95;
    [ObservableProperty] private bool _isLoadingModels;
    [ObservableProperty] private string _foundryStatus = "Checking...";
    [ObservableProperty] private string _maxTokensInfo = "";

    async partial void OnModelNameChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var maxTokens = await _foundryManager.GetMaxOutputTokensAsync(value);
        if (maxTokens.HasValue)
        {
            MaxTokens = maxTokens.Value;
            MaxTokensInfo = $"(model max: {maxTokens.Value})";
        }
        else
        {
            MaxTokensInfo = "";
        }
    }

    [ObservableProperty]
    private ObservableCollection<string> _availableModels = [];

    public IReadOnlyList<EditingMode> AvailableModes { get; } = Enum.GetValues<EditingMode>();

    public event Action<bool>? RequestClose;

    public SettingsViewModel(ISettingsService settingsService, IFoundryManager foundryManager)
    {
        _settingsService = settingsService;
        _foundryManager = foundryManager;
        LoadFromSettings();

        // Seed the model list with the saved model so it shows immediately
        if (!string.IsNullOrEmpty(ModelName))
            AvailableModels.Add(ModelName);

        LoadModelsAsync();
    }

    private void LoadFromSettings()
    {
        var s = _settingsService.Load();
        HotkeyModifiers = s.HotkeyModifiers;
        HotkeyKey = s.HotkeyKey;
        FoundryEndpoint = s.FoundryEndpoint;
        ModelName = s.ModelName;
        DefaultMode = s.DefaultMode;
        Creativity =s.Creativity;
        MaxTokens = s.MaxTokens;
        PopupOpacity = s.PopupOpacity;
    }

    private async void LoadModelsAsync()
    {
        IsLoadingModels = true;
        FoundryStatus = "Checking Foundry Local...";

        var (running, endpoint) = await _foundryManager.CheckServiceStatusAsync();

        if (!running)
        {
            FoundryStatus = "Not running";
            IsLoadingModels = false;
            return;
        }

        if (endpoint != null && FoundryEndpoint != endpoint)
        {
            FoundryEndpoint = endpoint;
        }

        FoundryStatus = "Connected";

        var models = await _foundryManager.GetAvailableModelsAsync();

        var savedModel = ModelName;

        // Ensure current model is in the list
        if (!string.IsNullOrEmpty(savedModel) && !models.Contains(savedModel))
            models.Insert(0, savedModel);

        // Clear and repopulate instead of replacing the collection
        AvailableModels.Clear();
        foreach (var m in models)
            AvailableModels.Add(m);

        // Force ModelName refresh for UI binding
        _modelName = "";
        OnPropertyChanged(nameof(ModelName));
        ModelName = savedModel;

        IsLoadingModels = false;
    }

    [RelayCommand]
    private void RefreshModels() => LoadModelsAsync();

    [RelayCommand]
    private void Save()
    {
        var s = new AppSettings
        {
            HotkeyModifiers = HotkeyModifiers,
            HotkeyKey = HotkeyKey,
            FoundryEndpoint = FoundryEndpoint,
            ModelName = ModelName,
            DefaultMode = DefaultMode,
            Creativity = Creativity,
            MaxTokens = MaxTokens,
            PopupOpacity = PopupOpacity
        };
        _settingsService.Save(s);
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    [RelayCommand]
    private void ResetDefaults()
    {
        var defaults = new AppSettings();
        HotkeyModifiers = defaults.HotkeyModifiers;
        HotkeyKey = defaults.HotkeyKey;
        FoundryEndpoint = defaults.FoundryEndpoint;
        ModelName = defaults.ModelName;
        DefaultMode = defaults.DefaultMode;
        Creativity =defaults.Creativity;
        MaxTokens = defaults.MaxTokens;
        PopupOpacity = defaults.PopupOpacity;
    }
}
