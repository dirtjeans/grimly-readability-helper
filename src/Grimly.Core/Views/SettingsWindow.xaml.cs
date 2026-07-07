using System.Windows;
using System.Windows.Controls;
using Grimly.ViewModels;

namespace Grimly.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void ModelListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModelListBox.SelectedItem is string model && DataContext is SettingsViewModel vm)
        {
            vm.ModelName = model;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SettingsViewModel oldVm)
            oldVm.RequestClose -= OnRequestClose;

        if (e.NewValue is SettingsViewModel newVm)
            newVm.RequestClose += OnRequestClose;
    }

    private void OnRequestClose(bool saved)
    {
        DialogResult = saved;
        Close();
    }

    /// <summary>
    /// Opens the model browser as a modal dialog. On successful download
    /// the alias comes back on the browser VM which is spliced into
    /// AvailableModels and selected.
    /// </summary>
    private void DownloadAnotherModel_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settingsVm) return;

        var browserVm = settingsVm.CreateModelBrowser();
        var window = new ModelBrowserWindow(browserVm) { Owner = this };
        var result = window.ShowDialog();
        if (result == true && !string.IsNullOrWhiteSpace(window.DownloadedModelId))
            settingsVm.OnModelDownloaded(window.DownloadedModelId);
    }
}
