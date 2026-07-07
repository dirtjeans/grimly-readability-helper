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
    private CancellationTokenSource? _downloadCts;

    /// <summary>Every model returned by <c>foundry model list --available</c>.</summary>
    public ObservableCollection<CatalogModelInfo> AllModels { get; } = new();

    /// <summary>Filtered view driving the ListBox. Rebuilt on search/filter change.</summary>
    public ObservableCollection<CatalogModelInfo> FilteredModels { get; } = new();

    [ObservableProperty]
    private string _searchText = "";

    /// <summary>
    /// Device filters. Both default on so a first-time user sees the
    /// relevant models for their hardware without having to toggle
    /// anything. Both off = no filter (show everything, including CPU).
    /// </summary>
    [ObservableProperty]
    private bool _showNpu = true;

    [ObservableProperty]
    private bool _showGpu = true;

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

    public ModelBrowserViewModel(IFoundryManager foundryManager)
    {
        _foundryManager = foundryManager;
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
