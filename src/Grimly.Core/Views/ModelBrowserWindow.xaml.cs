using System.Windows;
using Grimly.ViewModels;

namespace Grimly.Views;

public partial class ModelBrowserWindow : Window
{
    private readonly ModelBrowserViewModel _vm;

    public ModelBrowserWindow(ModelBrowserViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
        _vm.RequestClose += OnRequestClose;
        Loaded += async (_, _) => await _vm.LoadCatalogAsync();
    }

    /// <summary>Model alias the user picked, or null if they cancelled.</summary>
    public string? DownloadedModelId => _vm.DownloadedModelId;

    private void OnRequestClose()
    {
        // DialogResult = true only when a real selection came through so
        // callers can distinguish "user picked something" from "user closed
        // the window."
        DialogResult = _vm.DownloadedModelId != null;
        Close();
    }
}
