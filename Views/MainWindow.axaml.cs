using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GreedyDownloader.ViewModels;
using System;
using System.Threading.Tasks;

namespace GreedyDownloader.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.RequestDirectoryPick += OpenFolderDialogAsync;
        }
    }

    private async Task<string?> OpenFolderDialogAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Download Directory",
            AllowMultiple = false
        });

        return result is { Count: > 0 } ? result[0].TryGetLocalPath() : null;
    }
}
