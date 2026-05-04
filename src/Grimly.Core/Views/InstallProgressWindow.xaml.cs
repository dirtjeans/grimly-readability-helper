using System.Windows;
using System.Windows.Controls;
using Grimly.ViewModels;

namespace Grimly.Views;

public partial class InstallProgressWindow : Window
{
    public InstallProgressWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is InstallProgressViewModel oldVm)
            oldVm.RequestClose -= OnRequestClose;

        if (e.NewValue is InstallProgressViewModel newVm)
            newVm.RequestClose += OnRequestClose;
    }

    private void OnRequestClose() => Close();

    private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        LogScroller.ScrollToEnd();
    }
}
