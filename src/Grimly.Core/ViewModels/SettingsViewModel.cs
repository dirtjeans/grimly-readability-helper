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
    private readonly IProviderInstallService? _providerInstaller;
    private readonly IExternalLlmProviderService? _externalProviders;

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

    /// <summary>True while a provider winget install is running — disables
    /// the install links and shows the status line.</summary>
    [ObservableProperty] private bool _isInstallingProvider;

    /// <summary>Latest line of winget output during a provider install.</summary>
    [ObservableProperty] private string _providerInstallStatus = "";

    // Per-provider installed flags. Installed providers show a Start link;
    // missing ones show an Install link. Refreshed after installs complete.
    [ObservableProperty] private bool _isOllamaInstalled;
    [ObservableProperty] private bool _isLmStudioInstalled;
    [ObservableProperty] private bool _isGenieXInstalled;
    [ObservableProperty] private bool _showInstallRow;
    [ObservableProperty] private bool _showStartRow;

    private void RefreshInstalledFlags()
    {
        if (_externalProviders is null) return;
        bool Installed(string prefix) =>
            _externalProviders.Providers
                .Where(p => string.Equals(p.Prefix, prefix, StringComparison.OrdinalIgnoreCase))
                .Any(_externalProviders.IsInstalled);
        IsOllamaInstalled = Installed("ollama");
        IsLmStudioInstalled = Installed("lmstudio");
        IsGenieXInstalled = Installed("geniex");
        ShowInstallRow = !IsOllamaInstalled || !IsLmStudioInstalled || !IsGenieXInstalled;
        ShowStartRow = IsOllamaInstalled || IsLmStudioInstalled || IsGenieXInstalled;
    }

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

    /// <summary>
    /// Factory for the model browser dialog. The code-behind opens the
    /// window; on success we splice the downloaded model into the local
    /// list and select it. Kept as a method so the VM stays view-agnostic.
    /// </summary>
    public ModelBrowserViewModel CreateModelBrowser() => new(_foundryManager);

    /// <summary>
    /// Called by the code-behind when the browser closes with a picked
    /// model. Idempotent — safe to call with a model already in the list.
    /// </summary>
    public void OnModelDownloaded(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return;
        if (!AvailableModels.Contains(modelId, StringComparer.OrdinalIgnoreCase))
            AvailableModels.Insert(0, modelId);
        ModelName = modelId;
    }

    public event Action<bool>? RequestClose;

    public SettingsViewModel(
        ISettingsService settingsService,
        IFoundryManager foundryManager,
        IProviderInstallService? providerInstaller = null,
        IExternalLlmProviderService? externalProviders = null)
    {
        _settingsService = settingsService;
        _foundryManager = foundryManager;
        _providerInstaller = providerInstaller;
        _externalProviders = externalProviders;
        LoadFromSettings();
        RefreshInstalledFlags();

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

    /// <summary>
    /// One-click winget install for an external provider. Parameter is the
    /// winget package id ("Ollama.Ollama", "ElementLabs.LMStudio"). After a
    /// successful install the model list refreshes — though most providers
    /// need the user to start the app and download a model before anything
    /// shows up, which the status line spells out.
    /// </summary>
    [RelayCommand]
    private async Task InstallProviderAsync(string wingetId)
    {
        if (_providerInstaller is null || IsInstallingProvider) return;

        IsInstallingProvider = true;
        ProviderInstallStatus = $"Installing {wingetId} via winget…";
        try
        {
            var progress = new Progress<string>(line => ProviderInstallStatus = line);
            var ok = await _providerInstaller.InstallWingetPackageAsync(wingetId, progress);
            ProviderInstallStatus = ok
                ? "Installed. Start the app and download a model, then click Refresh."
                : "Install failed — try installing manually from the vendor's site.";
            if (ok)
            {
                RefreshInstalledFlags();
                LoadModelsAsync();
            }
        }
        catch (Exception ex)
        {
            ProviderInstallStatus = $"Install error: {ex.Message}";
        }
        finally
        {
            IsInstallingProvider = false;
        }
    }

    /// <summary>
    /// Explicit "start this provider's server" action. Works when the app
    /// is installed but idle: launches its CLI detached, waits for the
    /// server to answer, then refreshes the model list. Reports through
    /// the same status line as installs.
    /// </summary>
    [RelayCommand]
    private async Task StartProviderAsync(string prefix)
    {
        var provider = _externalProviders?.Providers
            .FirstOrDefault(p => string.Equals(p.Prefix, prefix, StringComparison.OrdinalIgnoreCase));
        if (provider is null || IsInstallingProvider) return;

        IsInstallingProvider = true;
        ProviderInstallStatus = $"Starting {provider.DisplayLabel}…";
        try
        {
            var ok = await _externalProviders!.EnsureRunningAsync(provider);
            ProviderInstallStatus = ok
                ? $"{provider.DisplayLabel} is running."
                : $"Couldn't start {provider.DisplayLabel} — is it installed?";
            if (ok) LoadModelsAsync();
        }
        catch (Exception ex)
        {
            ProviderInstallStatus = $"Start error: {ex.Message}";
        }
        finally
        {
            IsInstallingProvider = false;
        }
    }

    /// <summary>Open a provider's website (used for GenieX, which has no
    /// winget package — it ships through Qualcomm AI Hub).</summary>
    [RelayCommand]
    private void OpenProviderSite(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch { }
    }

    [RelayCommand]
    private void Save()
    {
        // Mutate the loaded settings rather than constructing a fresh
        // AppSettings: fields this dialog doesn't edit (WindowsAiDefaulted,
        // ShowFloatingIcon, and anything added later) must survive a save.
        // Building a new object silently reset them to defaults — which,
        // for WindowsAiDefaulted, re-armed the one-time windows-ai flip and
        // overwrote the user's model choice on the next launch.
        var s = _settingsService.Load();
        s.HotkeyModifiers = HotkeyModifiers;
        s.HotkeyKey = HotkeyKey;
        s.FoundryEndpoint = FoundryEndpoint;
        s.ModelName = ModelName;
        s.DefaultMode = DefaultMode;
        s.Creativity = Creativity;
        s.MaxTokens = MaxTokens;
        s.PopupOpacity = PopupOpacity;
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
