using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grimly.Models;
using Grimly.Services;

namespace Grimly.ViewModels;

/// <summary>
/// Backs the model browser modal. Two phases:
///   1. Browse — the full Foundry Local catalog with a search box and
///      "NPU only" toggle. Cached models are marked so users don't kick
///      off a redundant download.
///   2. Download — user picked a non-cached model. Progress lines stream
///      into the log and the download is cancellable.
/// On successful download the window closes and <see cref="DownloadedModelId"/>
/// carries the picked model back to whoever opened the browser.
/// </summary>
public partial class ModelBrowserViewModel : ObservableObject
{
    private readonly IFoundryManager _foundryManager;
    private readonly IExternalLlmProviderService? _externalProviders;
    private CancellationTokenSource? _downloadCts;

    /// <summary>Every model returned by <c>foundry model list --available</c>.</summary>
    public ObservableCollection<CatalogModelInfo> AllModels { get; } = new();

    /// <summary>Filtered view driving the ListBox. Rebuilt on search/filter change.</summary>
    public ObservableCollection<CatalogModelInfo> FilteredModels { get; } = new();

    [ObservableProperty]
    private string _searchText = "";

    /// <summary>
    /// Device filters. Both default off — the modal opens showing every
    /// model in the catalog, and the user ticks NPU or GPU (or both) to
    /// narrow the list. If nothing's ticked, no filter applies.
    /// </summary>
    [ObservableProperty]
    private bool _showNpu;

    [ObservableProperty]
    private bool _showGpu;

    [ObservableProperty]
    private CatalogModelInfo? _selectedModel;

    [ObservableProperty]
    private bool _isLoadingCatalog = true;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _downloadStatus = "";

    [ObservableProperty]
    private string _errorMessage = "";

    /// <summary>Set to the alias of the successfully downloaded model on close.</summary>
    public string? DownloadedModelId { get; private set; }

    public event Action? RequestClose;

    public ModelBrowserViewModel(
        IFoundryManager foundryManager,
        IExternalLlmProviderService? externalProviders = null)
    {
        _foundryManager = foundryManager;
        _externalProviders = externalProviders;

        // Only installed providers can pull; the Foundry catalog above
        // already covers Foundry downloads.
        if (externalProviders is not null)
        {
            foreach (var p in externalProviders.Providers)
            {
                if (p.PullArgsTemplate is not null && externalProviders.IsInstalled(p))
                    PullProviders.Add(p);
            }
            SelectedPullProvider = PullProviders.FirstOrDefault();
        }
    }

    /// <summary>Installed providers that support CLI pulls. Empty = the
    /// pull row hides.</summary>
    public ObservableCollection<ExternalProvider> PullProviders { get; } = new();

    [ObservableProperty]
    private ExternalProvider? _selectedPullProvider;

    [ObservableProperty]
    private string _pullModelName = "";

    public bool HasPullProviders => PullProviders.Count > 0;

    /// <summary>Open the selected provider's catalog site in the browser —
    /// none of these providers has an enumerable catalog API, so discovery
    /// happens on their site and the name comes back here to pull.</summary>
    [RelayCommand]
    private void OpenCatalogSite()
    {
        var url = SelectedPullProvider?.CatalogUrl;
        if (url is null) return;
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

    /// <summary>Pull a model by name via the selected provider's CLI,
    /// streaming its progress into the same status line the Foundry
    /// download uses.</summary>
    [RelayCommand]
    private async Task PullExternalAsync()
    {
        if (_externalProviders is null || SelectedPullProvider is null || IsDownloading) return;
        var provider = SelectedPullProvider;
        var name = PullModelName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        _downloadCts?.Cancel();
        _downloadCts = new CancellationTokenSource();
        IsDownloading = true;
        DownloadStatus = $"Pulling {name} via {provider.DisplayLabel}…";
        ErrorMessage = "";

        var progress = new Progress<string>(line => DownloadStatus = line);

        bool success;
        try
        {
            success = await _externalProviders.PullModelAsync(provider, name, progress, _downloadCts.Token);
        }
        catch (OperationCanceledException)
        {
            IsDownloading = false;
            DownloadStatus = "Cancelled.";
            return;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Pull failed: {ex.Message}";
            IsDownloading = false;
            return;
        }

        IsDownloading = false;
        if (!success)
        {
            ErrorMessage = string.IsNullOrEmpty(DownloadStatus)
                ? $"Pull failed. Check the model name against {provider.DisplayLabel}'s catalog."
                : $"Pull failed: {DownloadStatus}";
            return;
        }

        DownloadedModelId = $"{provider.Prefix}:{name}";
        RequestClose?.Invoke();
    }

    public async Task LoadCatalogAsync()
    {
        IsLoadingCatalog = true;
        ErrorMessage = "";
        try
        {
            var models = await _foundryManager.GetFullCatalogAsync();
            AllModels.Clear();
            foreach (var m in models.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase))
                AllModels.Add(m);
            RebuildFiltered();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't fetch the model catalog: {ex.Message}";
        }
        finally
        {
            IsLoadingCatalog = false;
        }
    }

    partial void OnSearchTextChanged(string value) => RebuildFiltered();
    partial void OnShowNpuChanged(bool value) => RebuildFiltered();
    partial void OnShowGpuChanged(bool value) => RebuildFiltered();

    private void RebuildFiltered()
    {
        IEnumerable<CatalogModelInfo> q = AllModels;

        // Device filter: OR across the checked device categories. Both off
        // = no filter (show everything, including CPU). Both on = show NPU
        // and GPU models but hide CPU-only models.
        if (ShowNpu || ShowGpu)
        {
            q = q.Where(m => (ShowNpu && m.IsNpu) || (ShowGpu && m.IsGpu));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
            q = q.Where(m => m.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        FilteredModels.Clear();
        foreach (var m in q) FilteredModels.Add(m);
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (SelectedModel == null || IsDownloading) return;
        var target = SelectedModel;

        // Already downloaded — treat as an instant success.
        if (target.IsCached)
        {
            DownloadedModelId = target.Id;
            RequestClose?.Invoke();
            return;
        }

        _downloadCts?.Cancel();
        _downloadCts = new CancellationTokenSource();
        IsDownloading = true;
        DownloadStatus = "Starting download…";
        ErrorMessage = "";

        var progress = new Progress<string>(line =>
        {
            // Marshal to UI thread — Progress<T> callbacks fire on the sync
            // context that constructed it (this is the UI thread here since
            // the command runs on it), so a plain assignment is fine.
            DownloadStatus = line;
        });

        bool success;
        try
        {
            success = await _foundryManager.DownloadModelWithProgressAsync(
                target.Id, progress, _downloadCts.Token);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Download failed: {ex.Message}";
            IsDownloading = false;
            return;
        }

        IsDownloading = false;
        if (_downloadCts.IsCancellationRequested)
        {
            DownloadStatus = "Cancelled.";
            return;
        }
        if (!success)
        {
            ErrorMessage = string.IsNullOrEmpty(DownloadStatus)
                ? "Download failed. Check your internet connection and Foundry service."
                : $"Download failed: {DownloadStatus}";
            return;
        }

        DownloadedModelId = target.Id;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        // If a download is in flight, cancel it and stay open so the user
        // sees "Cancelled" — otherwise dismiss the modal entirely.
        if (IsDownloading)
        {
            _downloadCts?.Cancel();
            return;
        }
        RequestClose?.Invoke();
    }
}
